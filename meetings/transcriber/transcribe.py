#!/usr/bin/env python3
"""
firm-transcriber: AWS Batch GPU transcription job
Environment variables (injected by Batch):
  MEETING_ID         - FIRM meeting ID
  AUDIO_S3_KEY       - S3 key of audio file (firm-audio/{id}/recording.wav)
  S3_BUCKET          - firm-recordings-dev
  FIRM_CALLBACK_URL  - https://firm.dev.fortressam.ai/api/vp/callback
  BOT_CALLBACK_SECRET - secret for callback auth
  HF_TOKEN           - HuggingFace token for pyannote
  AWS_REGION         - us-east-1
  BEDROCK_MODEL_ID   - us.anthropic.claude-sonnet-4-6
  ROSTER_TIMELINE_JSON - (optional) JSON array of {name, joinedAtMs, leftAtMs, possiblyMultiVoice}
  ACTIVE_SPEAKER_JSON  - (optional) JSON array of {name, startMs, endMs} from the meeting platform's
                         speaking indicator. Epoch ms or ms from recording start; may be empty/absent.
"""

import gc
import os
import re
import sys
import json
import tempfile
import boto3
from botocore.config import Config
import requests
import torch
import whisperx
from json_repair import repair_json

ACTIVE_SPEAKER_PROMPT_LIMIT = 50  # cap timeline entries in the summary prompt to bound token usage


def extract_summary_text_field(raw_text: str) -> str | None:
    """
    Last-resort extraction of just the summaryText value from a malformed JSON
    blob, without needing the whole document to parse. Handles the common
    failure mode where the model emits an unescaped literal double-quote
    inside a string value (e.g. quoting a phrase like \"Fortress Notetaker\"
    verbatim from the transcript) which breaks json.loads/json_repair alike.

    Strategy: find the "summaryText": " marker, then scan forward taking the
    text up to the next occurrence of an end-of-field pattern
    (\",\n  \"<nextKey>\":) or a trailing \"\n} at the very end of the document.
    This does not require every quote inside the value to be escaped correctly.
    """
    marker = '"summaryText"'
    idx = raw_text.find(marker)
    if idx == -1:
        return None
    # Move past `"summaryText": "`
    colon_idx = raw_text.find(':', idx)
    if colon_idx == -1:
        return None
    quote_idx = raw_text.find('"', colon_idx)
    if quote_idx == -1:
        return None
    start = quote_idx + 1

    # End boundary: the next `",\n  "someKey":` or `",\n"someKey":` pattern,
    # or the end of the string near a trailing `"\n}` if this is the last field.
    end_pattern = re.compile(r'",\s*"(keyDecisionsJson|actionItemsJson|followUpsJson|openQuestionsJson|KeyDecisionsJson|ActionItemsJson|FollowUpsJson|OpenQuestionsJson)"\s*:')
    match = end_pattern.search(raw_text, start)
    if match:
        end = match.start()
    else:
        # Fall back to the last `"` before a closing `}` at the end of the doc
        tail_match = re.search(r'"\s*\}\s*$', raw_text.rstrip())
        end = tail_match.start() if tail_match else len(raw_text)

    value = raw_text[start:end]
    # Unescape the standard JSON escapes we expect the model to have used
    # for the parts that WERE escaped correctly.
    value = value.replace('\\n', '\n').replace('\\"', '"').replace('\\\\', '\\')
    return value.strip() if value.strip() else None

def free_gpu_memory():
    gc.collect()
    if torch.cuda.is_available():
        torch.cuda.empty_cache()

def split_segments_by_speaker(segments: list) -> list:
    """
    Turn WhisperX segments (after assign_word_speakers) into {text, start, end, speaker}
    segments, splitting a segment wherever the word-level speaker changes. This is the
    point of word-level diarization: a Whisper segment that spans a speaker handoff no
    longer gets attributed wholesale to whoever owned its midpoint.

    Words WhisperX could not align (numerals, symbols) have no timestamps and therefore
    no speaker; they inherit the speaker of the preceding word (or the following word at
    the start of a segment).
    """
    out = []
    for seg in segments:
        text = (seg.get("text") or "").strip()
        if not text:
            continue
        seg_speaker = seg.get("speaker", "SPEAKER_00")
        words = [w for w in (seg.get("words") or []) if (w.get("word") or "").strip()]

        word_speakers = [w.get("speaker") for w in words]
        for i in range(1, len(word_speakers)):  # forward-fill
            if word_speakers[i] is None:
                word_speakers[i] = word_speakers[i - 1]
        first_known = next((spk for spk in word_speakers if spk is not None), seg_speaker)
        word_speakers = [spk if spk is not None else first_known for spk in word_speakers]

        runs = []  # [speaker, [words]]
        for w, spk in zip(words, word_speakers):
            if runs and runs[-1][0] == spk:
                runs[-1][1].append(w)
            else:
                runs.append([spk, [w]])

        if len(runs) <= 1:
            out.append({"text": text, "start": seg["start"], "end": seg["end"],
                        "speaker": runs[0][0] if runs else seg_speaker})
            continue

        for i, (spk, run_words) in enumerate(runs):
            # Every run after the first begins with a speaker-attributed (hence timestamped) word
            timed = [w for w in run_words if "start" in w]
            start = seg["start"] if i == 0 else timed[0]["start"]
            end = seg["end"] if i == len(runs) - 1 else timed[-1]["end"]
            out.append({"text": " ".join(w["word"].strip() for w in run_words),
                        "start": start, "end": end, "speaker": spk})
    return out

def build_active_speaker_block(active_speaker: list, recording_start_ms: int | None, notetaker_name: str) -> str:
    """
    Render the platform active-speaker log as a prompt block. Adjacent entries for the same
    person are merged before capping so the cap covers as much of the meeting as possible.
    """
    entries = []
    for e in active_speaker:
        name = (e.get("name") or "").strip()
        start_ms = e.get("startMs")
        if not name or name == notetaker_name or start_ms is None:
            continue
        end_ms = e.get("endMs") or start_ms
        if entries and entries[-1]["name"] == name:
            entries[-1]["endMs"] = max(entries[-1]["endMs"], end_ms)
        else:
            entries.append({"name": name, "startMs": start_ms, "endMs": end_ms})
    if not entries:
        return ""

    # Epoch-ms timelines are rebased onto the recording (approximated like the roster: earliest known timestamp)
    if entries[0]["startMs"] > 1_000_000_000_000:
        base_ms = recording_start_ms if recording_start_ms is not None else min(e["startMs"] for e in entries)
    else:
        base_ms = 0

    def fmt(ms):
        sec = max(0, int((ms - base_ms) // 1000))
        return f"{sec // 60}:{sec % 60:02d}"

    lines = [f"- {e['name']}: {fmt(e['startMs'])}–{fmt(e['endMs'])}" for e in entries[:ACTIVE_SPEAKER_PROMPT_LIMIT]]
    if len(entries) > ACTIVE_SPEAKER_PROMPT_LIMIT:
        lines.append(f"- … {len(entries) - ACTIVE_SPEAKER_PROMPT_LIMIT} more entries omitted")
    return ("\n[ACTIVE SPEAKER TIMELINE — meeting platform's speaking indicator, mm:ss from recording start. "
            "Approximate; use it to resolve ambiguous speaker labels, not as ground truth]\n"
            + "\n".join(lines) + "\n[END ACTIVE SPEAKER TIMELINE]\n\n")

def post_callback(url: str, secret: str, payload: dict):
    try:
        resp = requests.post(url, json=payload,
            headers={"Content-Type": "application/json", "X-Bot-Secret": secret},
            timeout=30,
            allow_redirects=False)
        print(f"[Callback] POST {url} → {resp.status_code}")
    except Exception as e:
        print(f"[Callback] Failed: {e}", file=sys.stderr)

def main():
    meeting_id = int(os.environ["MEETING_ID"])
    audio_s3_key = os.environ["AUDIO_S3_KEY"]
    s3_bucket = os.environ.get("S3_BUCKET", "firm-recordings-dev")
    callback_url = os.environ["FIRM_CALLBACK_URL"]
    bot_secret = os.environ.get("BOT_CALLBACK_SECRET", "")
    hf_token = os.environ.get("HF_TOKEN", "")
    aws_region = os.environ.get("AWS_REGION", "us-east-1")
    bedrock_model_id = os.environ.get("BEDROCK_MODEL_ID", "us.anthropic.claude-sonnet-4-6")
    meeting_date = os.environ.get("MEETING_DATE", "")
    notetaker_name = os.environ.get("NOTETAKER_NAME", "Fortress Notetaker")

    org_wiki_json = os.environ.get("ORG_WIKI_JSON", "")
    org_wiki_entries = []
    if org_wiki_json:
        try:
            org_wiki_entries = json.loads(org_wiki_json)
            print(f"[Transcriber] Org wiki loaded: {len(org_wiki_entries)} entries")
        except Exception as e:
            print(f"[Transcriber] Failed to parse ORG_WIKI_JSON: {e}")

    roster_timeline_json = os.environ.get("ROSTER_TIMELINE_JSON", "")
    roster_attendees = []  # list of dicts with name, joinedAtMs, leftAtMs, possiblyMultiVoice
    if roster_timeline_json:
        try:
            raw_entries = json.loads(roster_timeline_json) or []
            # Deduplicate by name, keeping the widest time window (covers leave/rejoin)
            seen = {}
            for entry in raw_entries:
                name = (entry.get("name") or "").strip()
                if not name or name == notetaker_name or entry.get("joinedAtMs") is None:
                    continue
                if name not in seen:
                    seen[name] = dict(entry, name=name)
                else:
                    seen[name]["joinedAtMs"] = min(seen[name]["joinedAtMs"], entry["joinedAtMs"])
                    if entry.get("leftAtMs") is not None:
                        seen[name]["leftAtMs"] = max(seen[name].get("leftAtMs") or 0, entry["leftAtMs"])
                    seen[name]["possiblyMultiVoice"] = bool(seen[name].get("possiblyMultiVoice")) or bool(entry.get("possiblyMultiVoice"))
            roster_attendees = list(seen.values())
            # leftAtMs is optional in the vpbot RosterEntry — treat missing as "present until the end"
            if roster_attendees:
                roster_end_ms = max(max(e["joinedAtMs"], e.get("leftAtMs") or 0) for e in roster_attendees)
                for e in roster_attendees:
                    if e.get("leftAtMs") is None:
                        e["leftAtMs"] = roster_end_ms
            print(f"[Transcriber] Roster loaded: {len(roster_attendees)} attendees: {[e['name'] for e in roster_attendees]}")
        except Exception as e:
            roster_attendees = []
            print(f"[Transcriber] Failed to parse ROSTER_TIMELINE_JSON: {e}")

    active_speaker_json = os.environ.get("ACTIVE_SPEAKER_JSON", "")
    active_speaker_log = []  # list of dicts with name, startMs, endMs
    if active_speaker_json:
        try:
            parsed = json.loads(active_speaker_json) or []
            active_speaker_log = [e for e in parsed if isinstance(e, dict)] if isinstance(parsed, list) else []
            print(f"[Transcriber] Active speaker log loaded: {len(active_speaker_log)} entries")
        except Exception as e:
            print(f"[Transcriber] Failed to parse ACTIVE_SPEAKER_JSON: {e}")

    s3 =boto3.client("s3", region_name=aws_region)

    print(f"[Transcriber] Starting job for meeting {meeting_id}, audio: {audio_s3_key}")

    # Download audio
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        audio_path = f.name
    print(f"[Transcriber] Downloading audio to {audio_path}")
    s3.download_file(s3_bucket, audio_s3_key, audio_path)
    print(f"[Transcriber] Download complete")

    try:
        # WhisperX transcription (GPU) — faster-whisper backend with batched inference
        device = "cuda"
        print(f"[Transcriber] Loading WhisperX model (large-v3-turbo, GPU)...")
        initial_prompt = ", ".join([e.get("Term", "") or e.get("term", "") for e in org_wiki_entries if (e.get("Term", "") or e.get("term", ""))])
        if initial_prompt:
            print(f"[Transcriber] Whisper initial_prompt: {initial_prompt}")
        model = whisperx.load_model(
            "large-v3-turbo",
            device,
            compute_type="float16",
            language="en",
            asr_options={"initial_prompt": initial_prompt} if initial_prompt else None,
        )
        audio = whisperx.load_audio(audio_path)
        print(f"[Transcriber] Running transcription...")
        transcription = model.transcribe(audio, batch_size=16, language="en")
        whisper_segments = transcription["segments"]
        print(f"[Transcriber] Whisper complete: {len(whisper_segments)} segments, duration={len(audio) / whisperx.audio.SAMPLE_RATE:.1f}s")
        del model
        free_gpu_memory()

        # Word-level alignment (wav2vec2) — prerequisite for word-level speaker assignment
        try:
            print(f"[Transcriber] Running word-level alignment...")
            align_model, align_metadata = whisperx.load_align_model(language_code=transcription["language"], device=device)
            aligned = whisperx.align(whisper_segments, align_model, align_metadata, audio, device, return_char_alignments=False)
            whisper_segments = aligned["segments"]
            print(f"[Transcriber] Alignment complete: {len(aligned['word_segments'])} words")
            del align_model
            free_gpu_memory()
        except Exception as e:
            print(f"[Transcriber] Alignment failed (non-fatal, falling back to segment-level speakers): {e}", file=sys.stderr)

        # Pyannote diarization (GPU) via WhisperX, speakers assigned per word
        speaker_name_map = {}  # SPEAKER_NN -> actual name (from roster timing overlap)
        if hf_token:
            try:
                print(f"[Transcriber] Loading pyannote diarization pipeline...")
                # Set offline mode only for pyannote — weights are pre-baked, Whisper downloads at runtime
                os.environ["HF_HUB_OFFLINE"] = "1"
                try:
                    diarize_model = whisperx.DiarizationPipeline(
                        model_name="pyannote/speaker-diarization-3.1",
                        use_auth_token=hf_token,
                        device=device,
                    )
                finally:
                    del os.environ["HF_HUB_OFFLINE"]
                print(f"[Transcriber] Running diarization...")
                diarization_kwargs = {}
                if roster_attendees:
                    n = len(roster_attendees)
                    multi_voice = sum(1 for e in roster_attendees if e.get("possiblyMultiVoice", False))
                    if multi_voice == 0:
                        diarization_kwargs["num_speakers"] = n
                        print(f"[Transcriber] Pyannote num_speakers={n} (from roster, no multi-voice entries)")
                    else:
                        min_s = n
                        max_s = n + (multi_voice * 2)
                        diarization_kwargs["min_speakers"] = min_s
                        diarization_kwargs["max_speakers"] = max_s
                        print(f"[Transcriber] Pyannote min_speakers={min_s}, max_speakers={max_s} (roster has {multi_voice} multi-voice entries)")
                else:
                    print(f"[Transcriber] No roster data — pyannote in automatic speaker detection mode")
                diarize_df = diarize_model(audio, **diarization_kwargs)
                del diarize_model
                free_gpu_memory()
                turns = list(zip(diarize_df["start"], diarize_df["end"], diarize_df["speaker"]))
                whisper_segments = whisperx.assign_word_speakers(diarize_df, {"segments": whisper_segments})["segments"]
                print(f"[Transcriber] Diarization complete: {len(turns)} turns, {len(set(s for _,_,s in turns))} speakers")

                # Build speaker label → name assignment using roster timing overlap
                if roster_attendees and turns:
                    speaker_segments = {}  # speaker_label -> list of (start, end) tuples
                    for (t_start, t_end, t_speaker) in turns:
                        speaker_segments.setdefault(t_speaker, []).append((t_start, t_end))

                    # Roster timestamps are epoch ms; approximate recording start as earliest join
                    recording_start_ms = min(e["joinedAtMs"] for e in roster_attendees)

                    for speaker_label, segs in speaker_segments.items():
                        best_name = None
                        best_overlap = 0.0
                        second_overlap = 0.0
                        for attendee in roster_attendees:
                            att_start = (attendee["joinedAtMs"] - recording_start_ms) / 1000.0
                            att_end = (attendee["leftAtMs"] - recording_start_ms) / 1000.0
                            total_overlap = sum(
                                max(0, min(seg_end, att_end) - max(seg_start, att_start))
                                for seg_start, seg_end in segs
                            )
                            if total_overlap > best_overlap:
                                second_overlap = best_overlap
                                best_overlap = total_overlap
                                best_name = attendee["name"]
                            elif total_overlap > second_overlap:
                                second_overlap = total_overlap
                        # Only attribute when one attendee clearly dominates — when presence windows
                        # largely coincide, overlap can't distinguish people, and assigning the
                        # earliest joiner to every label would be worse than leaving SPEAKER_NN
                        # for the summary model to resolve via the attendee list.
                        if best_name and best_overlap > 0 and second_overlap <= 0.5 * best_overlap:
                            speaker_name_map[speaker_label] = best_name
                            print(f"[Transcriber] {speaker_label} → {best_name} (overlap {best_overlap:.1f}s, runner-up {second_overlap:.1f}s)")
                        else:
                            speaker_name_map[speaker_label] = speaker_label  # ambiguous or no overlap — keep label
                            print(f"[Transcriber] {speaker_label} left unresolved (best {best_name} {best_overlap:.1f}s, runner-up {second_overlap:.1f}s)")
            except Exception as e:
                print(f"[Transcriber] Diarization failed (non-fatal): {e}", file=sys.stderr)
        else:
            print(f"[Transcriber] HF_TOKEN not set — skipping diarization")

        # Build speaker name map from org wiki people entries
        wiki_people = {}
        for e in org_wiki_entries:
            term = e.get('Term', '') or e.get('term', '')
            if term and any(w[0].isupper() for w in term.split() if w):
                wiki_people[term.lower()] = term
        if wiki_people:
            print(f"[Transcriber] Wiki people available for name resolution: {list(wiki_people.values())}")

        # Build transcript — segments are split at word-level speaker changes
        result = []
        for seg in split_segments_by_speaker(whisper_segments):
            speaker_label = seg["speaker"]
            speaker_name = speaker_name_map.get(speaker_label, speaker_label) if speaker_name_map else speaker_label
            result.append({
                "speakerLabel": speaker_label,
                "speakerName": speaker_name,  # actual name if resolved, else SPEAKER_NN
                "text": seg["text"],
                "startTimeMs": int(seg["start"] * 1000),
                "endTimeMs": int(seg["end"] * 1000)
            })

        # Upload transcript to S3
        transcript_key = f"firm-transcripts/{meeting_id}/transcript.json"
        transcript_json = json.dumps(result)
        s3.put_object(
            Bucket=s3_bucket,
            Key=transcript_key,
            Body=transcript_json,
            ContentType="application/json"
        )
        print(f"[Transcriber] Transcript uploaded to s3://{s3_bucket}/{transcript_key}")

        # Post transcription_complete callback with transcript S3 key and segments
        post_callback(callback_url, bot_secret, {
            "meetingId": meeting_id,
            "status": "transcription_complete",
            "transcriptS3Key": transcript_key,
            "segments": result
        })

        # Guard: skip summarization if no speech was detected
        if not result:
            print(f"[Transcriber] No speech segments detected — skipping Bedrock summarization")
            post_callback(callback_url, bot_secret, {
                "meetingId": meeting_id,
                "status": "summary_complete",
                "summary": {
                    "summaryText": "# No Speech Detected\n\nNo transcribable audio was found in this recording. The meeting may have been silent, very short, or recorded with no active microphone.",
                    "KeyDecisionsJson": "[]",
                    "ActionItemsJson": "[]",
                    "FollowUpsJson": "[]",
                    "ModelUsed": bedrock_model_id
                }
            })
            return

        # Bedrock summarization
        try:
            print(f"[Transcriber] Running Bedrock summarization...")
            bedrock = boto3.client(
                "bedrock-runtime",
                region_name=aws_region,
                config=Config(read_timeout=300, retries={"max_attempts": 2})
            )
            full_text = "\n".join([f"{s['speakerName']}: {s['text']}" for s in result])
            org_context_block = ""
            if org_wiki_entries:
                terms = "\n".join([f"- {e.get('Term', '') or e.get('term', '')}: {e.get('Description', '') or e.get('description', '')}" for e in org_wiki_entries if (e.get('Term', '') or e.get('term', ''))])
                print(f"[Transcriber] Org wiki terms block:\n{terms}")
                org_context_block = f"\n[ORG WIKI — AUTHORITATIVE DEFINITIONS. READ BEFORE INTERPRETING TRANSCRIPT.]\n{terms}\n[END ORG WIKI]\n\n"
            attendees_block = ""
            if roster_attendees:
                recording_start_ms = min(e["joinedAtMs"] for e in roster_attendees)
                att_lines = []
                for e in roster_attendees:
                    rel_sec = (e["joinedAtMs"] - recording_start_ms) / 1000
                    mins = int(rel_sec // 60)
                    secs = int(rel_sec % 60)
                    mv_note = " [⚠ may contain multiple voices]" if e.get("possiblyMultiVoice") else ""
                    att_lines.append(f"- {e['name']} (joined {mins}:{secs:02d}){mv_note}")
                attendees_block = "\n[MEETING ATTENDEES — confirmed by bot roster polling. Some entries may represent multiple people in the same room]\n" + "\n".join(att_lines) + "\n[END ATTENDEES]\n\n"
            active_speaker_block = ""
            if active_speaker_log:
                roster_start_ms = min(e["joinedAtMs"] for e in roster_attendees) if roster_attendees else None
                active_speaker_block = build_active_speaker_block(active_speaker_log, roster_start_ms, notetaker_name)
            summary_prompt = f"""Analyze this meeting transcript and provide a rich, structured summary.
{org_context_block}{attendees_block}{active_speaker_block}
Meeting date: {meeting_date if meeting_date else "unknown"}

IMPORTANT — Org Context Usage:
1. TERMINOLOGY — MANDATORY LOOKUP: Before interpreting ANY acronym, abbreviation, or proper noun in the transcript, check it against the ORG WIKI above. The Org Wiki is the authoritative source — do NOT guess or use general knowledge if a term could match a wiki entry. Examples: "NBA" MUST be looked up before assuming it means the basketball association. "FAM", "FAIT", "FIRM", "FORMS", "FIP" are all defined in the wiki. If a term appears in the wiki, use the wiki definition. Period. Only fall back to general knowledge if the term has NO plausible wiki match.

2. SPEAKER IDENTITY RESOLUTION: The transcript uses diarization labels (SPEAKER_00, SPEAKER_01, etc.) which are assigned by an AI and may be imperfect. Use BOTH conversational context AND diarization consistency to identify who is speaking:
   - Read the content of what each speaker says to identify them (e.g., if someone explains they are the AI lead, that is likely Fred White)
   - How people address each other by name is a strong signal
   - Once you identify a speaker label as a person, treat ALL segments with that label as that person
   - The SAME person may appear as MULTIPLE speaker numbers (e.g., diarization may split one person into SPEAKER_02 and SPEAKER_07) — this is expected; use context to merge them
   - Build an internal speaker-to-name map before writing the summary and use it consistently throughout
   - If MEETING ATTENDEES are listed above, they are the people actually in the meeting — prefer mapping speaker labels to those names. Some labels may already be replaced with attendee names based on join/leave timing
   - If an ACTIVE SPEAKER TIMELINE is given above, use it to break ties: a speaker label whose lines fall inside a person's speaking window is likely that person

Transcript:
{full_text[:50000]}

Return ONLY a valid JSON object with exactly these fields. No commentary before or after.

{{
  "summaryText": "<rich markdown — see format below>",
  "keyDecisionsJson": ["decision 1", "decision 2"],
  "actionItemsJson": [{{"owner": "Name", "deadline": "YYYY-MM-DD or TBD", "description": "task"}}],
  "followUpsJson": ["follow-up 1", "follow-up 2"],
  "openQuestionsJson": ["question 1", "question 2"]
}}

For summaryText, produce rich markdown in this exact format:

# Meeting Summary: <descriptive title>

**Date:** <YYYY-MM-DD inferred from context if possible> | **Recorded by:** {notetaker_name}

---

## Overview
2-4 sentence description of the meeting purpose, key themes, and outcomes.

## Key People

| Name | Role | Present/Speaking |
|------|------|-----------------|
| <name> | <role from org context or inferred> | Active participant / Mentioned only |

Notes:
- List everyone identified from the transcript, whether they spoke or were just mentioned
- If MEETING ATTENDEES are listed above, Key People MUST include every attendee who spoke or was addressed by name — do not leave this table empty when attendees are known
- "Active participant" = spoke in the meeting
- "Mentioned only" = referenced but did not speak
- Do NOT include SPEAKER_XX labels in this table
- The same person may map to multiple speaker numbers — that is fine, just list the person once
- WIKI LOOKUP IS MANDATORY: Before assigning any name or role, search the ORG WIKI for a matching entry. If found: use the wiki `term` as the canonical name and wiki `description` as the role — verbatim, no paraphrasing, no blending with context. If NOT found in wiki: use the name as spoken and infer role from context.
- Speaker label → name resolution: use conversational cues (how people address each other, what they say about themselves) to map speaker labels to wiki names. Once mapped, use the wiki canonical name for that person throughout.

## Key Topics Discussed

### <Topic 1>
- Bullet points covering what was said, decisions considered, and context
- Use resolved names (e.g. "Fred suggested..." not "SPEAKER_00 suggested...")

### <Topic 2>
- Continue for all major topics

## Decisions Made

| Decision | Details |
|----------|---------|
| <decision> | <context and rationale> |

## Action Items

| Action Item | Owner | Due |
|------------|-------|-----|
| <task> | <owner> | <date or TBD> |

## Notable Quotes

> "<verbatim or close quote>" — <speaker name>

## Open Questions

- Unresolved items, questions raised but not answered, follow-ups needed

---
*Generated by {notetaker_name} — {meeting_date if meeting_date else "date unknown"}*

Rules:
- Use resolved names throughout — never use SPEAKER_XX labels in the summary body
- Use `##` for section headers, `###` for sub-topics, `- ` for bullets, `>` for quotes
- Tables must have proper markdown pipe formatting
- summaryText must be the complete markdown document as a single JSON string (escape newlines as \\n)
- keyDecisionsJson: same decisions as the Decisions table, as plain strings
- actionItemsJson: same as Action Items table, as objects with owner/deadline/description
- followUpsJson: same as Open Questions, as plain strings
- openQuestionsJson: unresolved questions only"""
            print(f"[Transcriber] Summary prompt (first 2000 chars):\n{summary_prompt[:2000]}")

            response = bedrock.invoke_model(
                modelId=bedrock_model_id,
                body=json.dumps({
                    "anthropic_version": "bedrock-2023-05-31",
                    "max_tokens": 8192,
                    "messages": [{"role": "user", "content": summary_prompt}]
                }),
                contentType="application/json",
                accept="application/json"
            )
            response_body = json.loads(response["body"].read())
            summary_text = response_body["content"][0]["text"]

            # Try to parse structured JSON from response. Fallback chain:
            # 1. Strict json.loads on the {...} block
            # 2. On failure: manually extract just the summaryText field via regex
            #    FIRST -- this is the most reliable recovery for the dominant
            #    failure mode (the model quotes a phrase verbatim from the
            #    transcript inside summaryText without escaping the inner
            #    quotes, e.g. showing it as "Fortress Notetaker"). json_repair
            #    was tested against this exact failure and silently truncated
            #    summaryText at the first unescaped quote, dumping the
            #    remainder into a spurious extra key -- it "succeeds" without
            #    erroring but returns an incomplete summary, which is worse
            #    than a clean failure. Manual extraction recovered the full
            #    10.7KB summary vs json_repair's truncated 2.5KB.
            # 3. Use json_repair only as a secondary source for the *array*
            #    fields (keyDecisions/actionItems/etc.) which are much less
            #    likely to contain embedded quotes than the long-form
            #    markdown summaryText.
            # 4. If even manual extraction finds nothing, use a clean generic
            #    fallback message -- NEVER dump the raw model response
            #    (including the outer JSON wrapper) into summaryText, since
            #    that renders as literal JSON text in the UI instead of
            #    formatted markdown.
            json_start = summary_text.find("{")
            json_end = summary_text.rfind("}") + 1
            json_block = summary_text[json_start:json_end] if (json_start >= 0 and json_end > json_start) else summary_text

            summary_data = None
            try:
                summary_data = json.loads(json_block)
                print("[Transcriber] Summary JSON parsed on first attempt")
            except Exception as e1:
                print(f"[Transcriber] Strict JSON parse failed ({e1}), attempting recovery...")

                extracted_summary = extract_summary_text_field(json_block)

                recovered_arrays = {}
                try:
                    repaired = repair_json(json_block)
                    repaired_data = json.loads(repaired)
                    for key in ("keyDecisionsJson", "actionItemsJson", "followUpsJson", "openQuestionsJson"):
                        if key in repaired_data:
                            recovered_arrays[key] = repaired_data[key]
                    print(f"[Transcriber] json_repair recovered array fields: {list(recovered_arrays.keys())}")
                except Exception as e2:
                    print(f"[Transcriber] json_repair array-field recovery also failed ({e2})")

                if extracted_summary:
                    print("[Transcriber] Recovered summaryText via manual field extraction")
                    summary_data = {"summaryText": extracted_summary, **recovered_arrays}
                else:
                    print("[Transcriber] All recovery attempts failed -- using generic fallback (not raw JSON)")
                    summary_data = {
                        "summaryText": "# Summary Unavailable\n\nThe AI summary could not be generated in a readable format for this meeting. The transcript is still available in full under the Transcript tab.",
                        **recovered_arrays
                    }

            # Upload summary to S3
            summary_key = f"firm-transcripts/{meeting_id}/summary.json"
            s3.put_object(
                Bucket=s3_bucket,
                Key=summary_key,
                Body=json.dumps(summary_data),
                ContentType="application/json"
            )
            print(f"[Transcriber] Summary uploaded to s3://{s3_bucket}/{summary_key}")

            # Post summary callback — nested under 'summary' key, C# property name casing
            post_callback(callback_url, bot_secret, {
                "meetingId": meeting_id,
                "status": "summary_complete",
                "summary": {
                    "summaryText": summary_data.get("summaryText", ""),
                    "KeyDecisionsJson": json.dumps(summary_data.get("keyDecisionsJson", [])),
                    "ActionItemsJson": json.dumps(summary_data.get("actionItemsJson", [])),
                    "FollowUpsJson": json.dumps(summary_data.get("followUpsJson", [])),
                    "ModelUsed": bedrock_model_id
                }
            })
        except Exception as e:
            print(f"[Transcriber] Bedrock summarization failed (non-fatal): {e}", file=sys.stderr)

        print(f"[Transcriber] Job complete for meeting {meeting_id}")

    except Exception as e:
        print(f"[Transcriber] FATAL: {e}", file=sys.stderr)
        post_callback(callback_url, bot_secret, {
            "meetingId": meeting_id,
            "status": "failed",
            "error": str(e)
        })
        sys.exit(1)
    finally:
        try:
            os.unlink(audio_path)
        except:
            pass

if __name__ == "__main__":
    main()
