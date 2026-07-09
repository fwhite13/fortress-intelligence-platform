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
"""

import os
import re
import sys
import json
import tempfile
import boto3
from botocore.config import Config
import requests
from faster_whisper import WhisperModel
from pyannote.audio import Pipeline
from json_repair import repair_json


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

    s3 = boto3.client("s3", region_name=aws_region)

    print(f"[Transcriber] Starting job for meeting {meeting_id}, audio: {audio_s3_key}")

    # Download audio
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        audio_path = f.name
    print(f"[Transcriber] Downloading audio to {audio_path}")
    s3.download_file(s3_bucket, audio_s3_key, audio_path)
    print(f"[Transcriber] Download complete")

    try:
        # Whisper transcription (GPU)
        print(f"[Transcriber] Loading Whisper model (large-v3-turbo, GPU)...")
        model = WhisperModel("large-v3-turbo", device="cuda", compute_type="float16")
        print(f"[Transcriber] Running transcription...")
        initial_prompt = ", ".join([e.get("Term", "") or e.get("term", "") for e in org_wiki_entries if (e.get("Term", "") or e.get("term", ""))])
        if initial_prompt:
            print(f"[Transcriber] Whisper initial_prompt: {initial_prompt}")
        segments_raw, info = model.transcribe(
            audio_path,
            language="en",
            beam_size=5,
            word_timestamps=True,
            vad_filter=True,
            initial_prompt=initial_prompt if initial_prompt else None,
        )
        whisper_segments = [
            {"text": seg.text.strip(), "start": seg.start, "end": seg.end}
            for seg in segments_raw
        ]
        print(f"[Transcriber] Whisper complete: {len(whisper_segments)} segments, duration={info.duration:.1f}s")

        # Pyannote diarization (GPU)
        diarization_map = {}
        if hf_token:
            try:
                print(f"[Transcriber] Loading pyannote diarization pipeline...")
                # Set offline mode only for pyannote — Whisper downloads at runtime
                os.environ["HF_HUB_OFFLINE"] = "1"
                try:
                    pipeline = Pipeline.from_pretrained(
                        "pyannote/speaker-diarization-3.1",
                        use_auth_token=hf_token
                    )
                finally:
                    del os.environ["HF_HUB_OFFLINE"]
                import torch
                pipeline = pipeline.to(torch.device("cuda"))
                print(f"[Transcriber] Running diarization...")
                diarization = pipeline(audio_path)
                turns = [(turn.start, turn.end, speaker)
                         for turn, _, speaker in diarization.itertracks(yield_label=True)]
                for seg in whisper_segments:
                    mid = (seg["start"] + seg["end"]) / 2
                    speaker = "SPEAKER_00"
                    for (t_start, t_end, t_speaker) in turns:
                        if t_start <= mid <= t_end:
                            speaker = t_speaker
                            break
                    diarization_map[seg["start"]] = speaker
                print(f"[Transcriber] Diarization complete: {len(turns)} turns, {len(set(s for _,_,s in turns))} speakers")
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

        # Build transcript
        result = []
        for seg in whisper_segments:
            speaker = diarization_map.get(seg["start"], "SPEAKER_00")
            result.append({
                "speakerLabel": speaker,
                "speakerName": speaker,
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
            full_text = "\n".join([f"{s['speakerLabel']}: {s['text']}" for s in result])
            org_context_block = ""
            if org_wiki_entries:
                terms = "\n".join([f"- {e.get('Term', '') or e.get('term', '')}: {e.get('Description', '') or e.get('description', '')}" for e in org_wiki_entries if (e.get('Term', '') or e.get('term', ''))])
                print(f"[Transcriber] Org wiki terms block:\n{terms}")
                org_context_block = f"\n[ORG WIKI — AUTHORITATIVE DEFINITIONS. READ BEFORE INTERPRETING TRANSCRIPT.]\n{terms}\n[END ORG WIKI]\n\n"
            summary_prompt = f"""Analyze this meeting transcript and provide a rich, structured summary.
{org_context_block}
Meeting date: {meeting_date if meeting_date else "unknown"}

IMPORTANT — Org Context Usage:
1. TERMINOLOGY — MANDATORY LOOKUP: Before interpreting ANY acronym, abbreviation, or proper noun in the transcript, check it against the ORG WIKI above. The Org Wiki is the authoritative source — do NOT guess or use general knowledge if a term could match a wiki entry. Examples: "NBA" MUST be looked up before assuming it means the basketball association. "FAM", "FAIT", "FIRM", "FORMS", "FIP" are all defined in the wiki. If a term appears in the wiki, use the wiki definition. Period. Only fall back to general knowledge if the term has NO plausible wiki match.

2. SPEAKER IDENTITY RESOLUTION: The transcript uses diarization labels (SPEAKER_00, SPEAKER_01, etc.) which are assigned by an AI and may be imperfect. Use BOTH conversational context AND diarization consistency to identify who is speaking:
   - Read the content of what each speaker says to identify them (e.g., if someone explains they are the AI lead, that is likely Fred White)
   - How people address each other by name is a strong signal
   - Once you identify a speaker label as a person, treat ALL segments with that label as that person
   - The SAME person may appear as MULTIPLE speaker numbers (e.g., diarization may split one person into SPEAKER_02 and SPEAKER_07) — this is expected; use context to merge them
   - Build an internal speaker-to-name map before writing the summary and use it consistently throughout

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
