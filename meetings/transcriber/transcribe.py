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
  ACTIVE_SPEAKER_S3_KEY - (optional) S3 key (in S3_BUCKET) of a JSON array of {name, startMs, endMs} from the
                         meeting platform's speaking indicator. Epoch ms or ms from recording start.
                         Preferred over ACTIVE_SPEAKER_JSON — Batch caps container overrides at 8192 bytes.
  ACTIVE_SPEAKER_JSON  - (optional, legacy) same array passed inline; used only if ACTIVE_SPEAKER_S3_KEY is absent.
  SUMMARIZE_ONLY     - (optional) "true" skips audio/Whisper/diarization and re-summarizes the existing transcript
  TRANSCRIPT_S3_KEY  - (summarize-only) S3 key of the existing transcript.json; defaults to firm-transcripts/{id}/transcript.json
  SPEAKER_CORRECTIONS_S3_KEY - (summarize-only, optional) S3 key of the speaker-corrections overlay JSON array
"""

import gc
import os
import re
import sys
import json
import tempfile
import uuid
import io
import boto3
import numpy as np
import pymysql
from botocore.config import Config
import requests
import torch
import whisperx
from whisperx.diarize import DiarizationPipeline  # no longer re-exported from the whisperx package in 3.8
from json_repair import repair_json
from pyannote.audio import Model, Inference
import torch.nn.functional as F

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

SUGGESTED_EVENT_MIN_CONFIDENCE = 0.65
SUGGESTED_EVENT_MAX = 3
SUGGESTED_EVENT_DEFAULT_TZ = "Central Standard Time"


def normalize_suggested_events(raw) -> list:
    """
    WI #8294: sanitize the model's suggested_events array. Enforces the prompt's
    contract defensively (confidence >= 0.65, at least one attendee, max 3) so a
    sloppy model response can never surface junk suggestions in the UI.
    """
    if not isinstance(raw, list):
        return []
    events = []
    for ev in raw:
        if not isinstance(ev, dict):
            continue
        try:
            confidence = float(ev.get("confidence") or 0)
        except (TypeError, ValueError):
            continue
        if confidence < SUGGESTED_EVENT_MIN_CONFIDENCE:
            continue
        attendees = [str(a).strip() for a in (ev.get("attendees") or []) if str(a).strip()]
        if not attendees:
            continue
        try:
            duration = int(ev.get("proposed_duration_minutes") or 30)
        except (TypeError, ValueError):
            duration = 30
        events.append({
            "subject": str(ev.get("subject") or "").strip()[:120] or None,
            "source_quote": str(ev.get("source_quote") or "").strip(),
            "attendees": attendees,
            "attendees_inferred": bool(ev.get("attendees_inferred", False)),
            "proposed_datetime": ev.get("proposed_datetime") or None,
            "proposed_duration_minutes": duration if 5 <= duration <= 480 else 30,
            "confidence": round(confidence, 2),
            "timezone": ev.get("timezone") or SUGGESTED_EVENT_DEFAULT_TZ,
        })
    events.sort(key=lambda e: e["confidence"], reverse=True)
    return events[:SUGGESTED_EVENT_MAX]


def apply_speaker_corrections(segments: list, corrections: list) -> list:
    """
    WI #8335: overlay the user's speaker corrections onto transcript segments, in list order
    (later corrections win). Returns corrected copies — the input segments are not mutated.
      assign:   {"type":"assign","speaker":"SPEAKER_07","resolved_name":"..."}        — every segment with that label
      override: {"type":"override","segment_index":42,"resolved_name":"..."}          — one segment
      merge:    {"type":"merge","speakers":["SPEAKER_03","SPEAKER_07"],"resolved_name":"..."} — every segment with any label
    Segments match a label on speakerLabel (raw diarization label) or speakerName. Corrections
    flagged "archived" (made against a previous transcript, before a retranscribe) are skipped.
    segment_index is the position in startTimeMs order — the order firm-web renders the transcript in.
    """
    corrected = sorted((dict(s) for s in segments), key=lambda s: s.get("startTimeMs") or 0)

    def matches(seg, labels):
        return seg.get("speakerLabel") in labels or seg.get("speakerName") in labels

    for c in corrections or []:
        if not isinstance(c, dict) or c.get("archived"):
            continue
        ctype = c.get("type")
        name = (c.get("resolved_name") or "").strip()
        if not name:
            continue
        if ctype == "assign":
            labels = {c.get("speaker")}
        elif ctype == "merge":
            labels = set(c.get("speakers") or [])
        elif ctype == "override":
            idx = c.get("segment_index")
            if not isinstance(idx, int) or not (0 <= idx < len(corrected)):
                print(f"[Transcriber] Skipped override correction: segment_index {idx} out of range")
                continue
            corrected[idx]["speakerName"] = name
            print(f"[Transcriber] Applied correction: {ctype} → {name}")
            continue
        else:
            print(f"[Transcriber] Skipped unknown correction type: {ctype}")
            continue
        labels.discard(None)
        for seg in corrected:
            if matches(seg, labels):
                seg["speakerName"] = name
        print(f"[Transcriber] Applied correction: {ctype} → {name}")
    return corrected


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

EMBEDDING_MODEL_ID = "pyannote/embedding"
EMBEDDING_SAMPLE_RATE = 16000  # whisperx.load_audio resamples to 16 kHz mono
REFERENCE_PREFERRED_WINDOW_S = 10.0  # prefer reference windows at least this long
REFERENCE_MIN_WINDOW_S = 2.0  # shortest window usable as a reference when no long one exists
REFERENCE_MAX_WINDOW_S = 120.0  # ADO#8334: cap individual windows — a 75-min log entry averaged into a garbage embedding
REFERENCE_MAX_WINDOWS = 3  # averaged per person
REFERENCE_MERGE_GAP_MS = 1000  # adjacent log entries for the same person closer than this are one window
CLUSTER_MIN_SEGMENT_S = 1.0  # shorter diarization turns give unstable embeddings
CLUSTER_MAX_SEGMENTS = 30  # longest turns per cluster, bounds GPU time on long meetings
VOICE_MATCH_THRESHOLD = 0.80  # best similarity must exceed this to assign a name
VOICE_MATCH_DOMINANCE = 1.2  # ADO#8334 (was 1.3): best must be >= this multiple of runner-up
VOICE_MATCH_ABSOLUTE_MIN = 0.95  # ADO#8334: score this high assigned regardless of ratio ...
VOICE_MATCH_ABSOLUTE_MAX_RUNNER_UP = 0.90  # ... provided runner-up stays below this
VOICEPRINT_PROMOTE_MIN_CONFIDENCE = 0.95  # ADO#8334 (was 0.90): tighter — wrong voiceprints poison later meetings
VOICEPRINT_PROMOTE_MAX_RUNNER_UP = 0.80  # ADO#8334: runner-up must be clearly separated
VOICEPRINT_PROMOTE_MIN_DURATION_S = 300.0  # ... with at least 5 minutes of attributed audio
VOICEPRINT_PROMOTE_COOLDOWN_DAYS = 7  # at most one meeting_auto voiceprint per person per week
ACTIVE_SPEAKER_OVERRIDE_MAX_WINDOW_S = 30.0  # ADO#8673: only a per-turn log window this short may override a voice match

_embedding_cache = {}

def _get_embedding_inference(hf_token: str):
    """Load pyannote/embedding once per job (shared by reference and cluster extraction)."""
    if "inference" not in _embedding_cache:
        try:
            model = Model.from_pretrained(EMBEDDING_MODEL_ID, token=hf_token)  # pyannote.audio >= 4
        except TypeError:
            model = Model.from_pretrained(EMBEDDING_MODEL_ID, use_auth_token=hf_token)
        if model is None:
            raise RuntimeError(f"{EMBEDDING_MODEL_ID} could not be loaded (gated repo terms not accepted for HF_TOKEN?)")
        inference = Inference(model, window="whole")
        if torch.cuda.is_available():
            inference.to(torch.device("cuda"))
        _embedding_cache["inference"] = inference
    return _embedding_cache["inference"]

def _get_waveform(audio_path: str):
    if _embedding_cache.get("audio_path") != audio_path:
        _embedding_cache["waveform"] = whisperx.load_audio(audio_path)
        _embedding_cache["audio_path"] = audio_path
    return _embedding_cache["waveform"]

def release_embedding_model():
    _embedding_cache.clear()
    free_gpu_memory()

def _embed_window(inference, waveform, start_s: float, end_s: float):
    """Embedding for one audio window as a 1-D float tensor, or None if the window is unusable."""
    lo = max(0, int(start_s * EMBEDDING_SAMPLE_RATE))
    hi = min(len(waveform), int(end_s * EMBEDDING_SAMPLE_RATE))
    if hi - lo < int(CLUSTER_MIN_SEGMENT_S * EMBEDDING_SAMPLE_RATE):
        return None
    chunk = torch.from_numpy(waveform[lo:hi]).float().unsqueeze(0)
    emb = torch.as_tensor(inference({"waveform": chunk, "sample_rate": EMBEDDING_SAMPLE_RATE})).float().flatten()
    if not torch.isfinite(emb).all():
        return None
    return emb

def _average_embeddings(embeddings: list, weights: list):
    """Duration-weighted mean of L2-normalized embeddings."""
    stacked = F.normalize(torch.stack(embeddings), dim=1)
    w = torch.tensor(weights, dtype=stacked.dtype).unsqueeze(1)
    return (stacked * w).sum(dim=0) / w.sum()

def active_log_windows(active_speaker_log: list, recording_start_ms: int) -> dict:
    """
    Merge the active speaker log into per-person windows in seconds from recording start.
    Returns name -> list of (start_s, end_s, possibly_multi_voice).
    """
    entries = []
    for e in active_speaker_log:
        name = (e.get("name") or "").strip()
        start_ms = e.get("startMs")
        if not name or start_ms is None:
            continue
        entries.append((name, start_ms, e.get("endMs") or start_ms, bool(e.get("possiblyMultiVoice"))))
    entries.sort(key=lambda x: x[1])
    windows = {}
    for name, start_ms, end_ms, multi in entries:
        person = windows.setdefault(name, [])
        if person and start_ms - person[-1][1] <= REFERENCE_MERGE_GAP_MS:
            prev = person[-1]
            person[-1] = (prev[0], max(prev[1], end_ms), prev[2] or multi)
        else:
            person.append((start_ms, end_ms, multi))
    return {name: [((s - recording_start_ms) / 1000.0, (e - recording_start_ms) / 1000.0, m) for s, e, m in wins]
            for name, wins in windows.items()}

def active_speakers_at(active_speaker_log: list, recording_start_ms: int, at_ms: float) -> set:
    """
    Names the active speaker log shows speaking at at_ms (ms from recording start).
    possiblyMultiVoice entries are skipped — a shared room device doesn't say which voice is talking.
    """
    names = set()
    for e in active_speaker_log:
        name = (e.get("name") or "").strip()
        start_ms = e.get("startMs")
        if not name or start_ms is None or e.get("possiblyMultiVoice"):
            continue
        if start_ms - recording_start_ms <= at_ms < (e.get("endMs") or start_ms) - recording_start_ms:
            names.add(name)
    return names

def _active_window_duration_s(active_speaker_log: list, recording_start_ms: int, at_ms: float, name: str) -> float:
    """
    Length in seconds of the longest log entry for name covering at_ms (ms from recording start), using
    the same matching as active_speakers_at. inf when no entry matches, so callers treat it as too long.
    """
    durations = []
    for e in active_speaker_log:
        start_ms = e.get("startMs")
        if (e.get("name") or "").strip().casefold() != name.casefold() or start_ms is None or e.get("possiblyMultiVoice"):
            continue
        end_ms = e.get("endMs") or start_ms
        if start_ms - recording_start_ms <= at_ms < end_ms - recording_start_ms:
            durations.append((end_ms - start_ms) / 1000.0)
    return max(durations, default=float("inf"))

def extract_reference_embeddings(active_speaker_log, audio_path, hf_token, recording_start_ms):
    """
    For each named attendee in active_speaker_log, find their longest clean speaking window(s)
    (prefer >= 10s). Extract audio and run through pyannote/embedding model.
    Skip possiblyMultiVoice=True entries and the notetaker bot (callers filter the notetaker out
    of active_speaker_log before calling). A clean window does not overlap anyone else's window.
    If a person has multiple windows, average the embeddings.
    Returns dict: { name -> embedding tensor }
    """
    windows = active_log_windows(active_speaker_log, recording_start_ms)
    multi_voice_names = {name for name, wins in windows.items() if any(m for _, _, m in wins)}
    for name in sorted(multi_voice_names):
        print(f"[Transcriber] Reference embedding skipped for {name} (possiblyMultiVoice)")

    all_windows = [(name, s, e) for name, wins in windows.items() for s, e, _ in wins]
    inference = _get_embedding_inference(hf_token)
    waveform = _get_waveform(audio_path)
    audio_end_s = len(waveform) / EMBEDDING_SAMPLE_RATE

    references = {}
    for name, wins in windows.items():
        if name in multi_voice_names:
            continue
        clean = []
        for s, e, _ in wins:
            s, e = max(0.0, s), min(audio_end_s, e)
            if e - s < REFERENCE_MIN_WINDOW_S:
                continue
            if any(other != name and os_ < e and oe > s for other, os_, oe in all_windows):
                continue
            clean.append((s, e))
        clean.sort(key=lambda w: w[1] - w[0], reverse=True)
        long_windows = [w for w in clean if w[1] - w[0] >= REFERENCE_PREFERRED_WINDOW_S]
        chosen = (long_windows or clean)[:REFERENCE_MAX_WINDOWS]
        embs, weights = [], []
        for s, e in chosen:
            if e - s > REFERENCE_MAX_WINDOW_S:
                print(f"[Transcriber] Reference window for {name} trimmed from {e - s:.1f}s to {REFERENCE_MAX_WINDOW_S:.0f}s")
                e = s + REFERENCE_MAX_WINDOW_S
            emb = _embed_window(inference, waveform, s, e)
            if emb is not None:
                embs.append(emb)
                weights.append(e - s)
        if embs:
            references[name] = _average_embeddings(embs, weights)
            print(f"[Transcriber] Reference embedding for {name}: {len(embs)} window(s), {sum(weights):.1f}s "
                  f"(longest {max(weights):.1f}s)")
        else:
            print(f"[Transcriber] No clean speaking window >= {REFERENCE_MIN_WINDOW_S:.0f}s for {name} — no reference embedding")
    return references

def extract_cluster_embeddings(turns, audio_path, hf_token):
    """
    For each SPEAKER_XX cluster in turns (list of (start_s, end_s, speaker_label) tuples),
    collect all segments and extract an aggregate embedding using pyannote/embedding.
    Uses the longest CLUSTER_MAX_SEGMENTS turns of at least CLUSTER_MIN_SEGMENT_S, weighted by duration.
    Returns dict: { speaker_label -> averaged embedding tensor }
    """
    segments_by_label = {}
    for start_s, end_s, label in turns:
        if end_s - start_s >= CLUSTER_MIN_SEGMENT_S:
            segments_by_label.setdefault(label, []).append((float(start_s), float(end_s)))

    inference = _get_embedding_inference(hf_token)
    waveform = _get_waveform(audio_path)
    clusters = {}
    for label, segs in segments_by_label.items():
        segs.sort(key=lambda w: w[1] - w[0], reverse=True)
        embs, weights = [], []
        for s, e in segs[:CLUSTER_MAX_SEGMENTS]:
            emb = _embed_window(inference, waveform, s, e)
            if emb is not None:
                embs.append(emb)
                weights.append(e - s)
        if embs:
            clusters[label] = _average_embeddings(embs, weights)
            print(f"[Transcriber] Cluster embedding for {label}: {len(embs)} segment(s), {sum(weights):.1f}s")
    return clusters

def assign_names_by_embedding(cluster_embeddings, reference_embeddings, threshold=VOICE_MATCH_THRESHOLD, dominance=VOICE_MATCH_DOMINANCE, confidences=None, runner_ups=None):
    """
    For each cluster embedding, compute cosine similarity against every reference embedding.
    Assign the closest reference name IF:
      - Best similarity > threshold (default 0.80), AND
      - Best similarity >= dominance * second-best similarity (default 1.2x)
    OR (absolute confidence override): best >= 0.95 and second < 0.90.
    Log full similarity scores for every assignment/non-assignment (needed for threshold tuning).
    Returns dict: { speaker_label -> name or None }
    If confidences is a dict, it is filled with { speaker_label -> best similarity } for assigned labels.
    """
    name_map = {}
    for label, cluster_emb in sorted(cluster_embeddings.items()):
        if not reference_embeddings:
            name_map[label] = None
            continue
        scores = {name: F.cosine_similarity(cluster_emb, ref_emb, dim=0).item()
                  for name, ref_emb in reference_embeddings.items()}
        ranked = sorted(scores.items(), key=lambda kv: kv[1], reverse=True)
        best_name, best = ranked[0]
        second = ranked[1][1] if len(ranked) > 1 else 0.0
        scores_str = ", ".join(f"{n}={v:.3f}" for n, v in ranked)
        if runner_ups is not None:
            runner_ups[label] = second
        if (best > threshold and best >= dominance * second) or \
                (best >= VOICE_MATCH_ABSOLUTE_MIN and second < VOICE_MATCH_ABSOLUTE_MAX_RUNNER_UP):
            source = 'voice_embedding_absolute' if not (best >= dominance * second) else 'voice_embedding'
            name_map[label] = best_name
            if confidences is not None:
                confidences[label] = best
            print(f"[Transcriber] {label} → {best_name} via voice embedding (best {best:.3f}, runner-up {second:.3f}, source={source}) "
                  f"scores: {scores_str}")
        else:
            name_map[label] = None
            reason = f"best {best:.3f} <= threshold {threshold}" if best <= threshold else \
                f"best {best:.3f} < {dominance}x runner-up {second:.3f} (and {best:.3f} < absolute min {VOICE_MATCH_ABSOLUTE_MIN} or {second:.3f} >= {VOICE_MATCH_ABSOLUTE_MAX_RUNNER_UP})"
            print(f"[Transcriber] {label} unresolved by voice embedding ({reason}) scores: {scores_str}")
    return name_map

def voiceprint_db_config() -> dict | None:
    """MySQL connection settings for firm_speaker_embeddings (same env vars as firm-web), or None if unset."""
    host = os.environ.get("FORTRESS_DB_HOST", "")
    if not host:
        return None
    return {
        "host": host,
        "port": int(os.environ.get("FORTRESS_DB_PORT", "3306")),
        "user": os.environ.get("FORTRESS_DB_USER", "fortress_mysql"),
        "password": os.environ.get("FORTRESS_DB_PASS", ""),
        "database": os.environ.get("FIRM_DB_NAME", "firm_dev"),
        "connect_timeout": 10,
        "charset": "utf8mb4",
    }

def load_org_voiceprints(tenant_id, db_config, s3_client, bucket):
    """
    ADO#8295: load the tenant's active voiceprints from firm_speaker_embeddings.
    Each row points at an .npy embedding in S3; multiple rows for the same name are averaged
    (uniform weights, L2-normalized first). Unreadable rows are skipped.
    Returns dict: { name -> embedding tensor }
    """
    if not tenant_id or not db_config:
        return {}
    conn = pymysql.connect(**db_config)
    try:
        with conn.cursor() as cur:
            cur.execute("SELECT name, s3_key FROM firm_speaker_embeddings WHERE tenant_id = %s AND is_active = 1",
                        (tenant_id,))
            rows = cur.fetchall()
    finally:
        conn.close()

    by_name = {}
    for name, s3_key in rows:
        try:
            body = s3_client.get_object(Bucket=bucket, Key=s3_key)["Body"].read()
            emb = torch.from_numpy(np.load(io.BytesIO(body), allow_pickle=False)).float().flatten()
            if emb.numel() == 0 or not torch.isfinite(emb).all():
                print(f"[Transcriber] Voiceprint {s3_key} for {name} is empty/non-finite — skipped")
                continue
            by_name.setdefault(name, []).append(emb)
        except Exception as e:
            print(f"[Transcriber] Failed to load voiceprint {s3_key} for {name} (skipped): {e}", file=sys.stderr)

    voiceprints = {}
    for name, embs in by_name.items():
        if len({e.numel() for e in embs}) > 1:
            print(f"[Transcriber] Voiceprints for {name} have mismatched dimensions — skipped")
            continue
        voiceprints[name] = _average_embeddings(embs, [1.0] * len(embs))
    print(f"[Transcriber] Loaded {len(voiceprints)} org voiceprint(s) from {len(rows)} row(s) for tenant {tenant_id}")
    return voiceprints

def compute_cluster_durations(turns) -> dict:
    """Total diarized seconds per speaker label: { speaker_label -> total_seconds }."""
    durations = {}
    for start_s, end_s, label in turns:
        durations[label] = durations.get(label, 0.0) + max(0.0, float(end_s) - float(start_s))
    return durations

def auto_promote_voiceprints(name_map, cluster_embeddings, cluster_durations, confidences,
                             tenant_id, meeting_id, db_config, s3_client, bucket):
    """
    ADO#8295: persist high-confidence cluster embeddings as meeting_auto voiceprints so later meetings
    can identify the speaker. A label is promoted when its voice match is > 0.90, it has >= 5 minutes of
    diarized audio, and the person has no meeting_auto voiceprint from the last 7 days.
    Best-effort: logs and swallows every error — must never block transcript delivery.
    """
    try:
        if not tenant_id or not db_config:
            print("[Transcriber] Voiceprint auto-promotion skipped (no TENANT_ID or DB config)")
            return
        candidates = []
        for label, name in sorted(name_map.items()):
            if not name or label not in cluster_embeddings:
                continue
            confidence = confidences.get(label, 0.0)
            duration = cluster_durations.get(label, 0.0)
            runner_up = runner_ups.get(label, 0.0) if runner_ups else 0.0
            if confidence > VOICEPRINT_PROMOTE_MIN_CONFIDENCE and runner_up < VOICEPRINT_PROMOTE_MAX_RUNNER_UP and duration >= VOICEPRINT_PROMOTE_MIN_DURATION_S:
                candidates.append((label, name, confidence, duration))
            else:
                print(f"[Transcriber] Voiceprint not promoted for {name} ({label}): confidence {confidence:.3f}, "
                      f"{duration:.0f}s attributed")
        if not candidates:
            return

        conn = pymysql.connect(**db_config)
        try:
            for label, name, confidence, duration in candidates:
                try:
                    with conn.cursor() as cur:
                        cur.execute(
                            "SELECT 1 FROM firm_speaker_embeddings WHERE tenant_id = %s AND name = %s "
                            "AND source = 'meeting_auto' AND created_at > UTC_TIMESTAMP() - INTERVAL %s DAY LIMIT 1",
                            (tenant_id, name, VOICEPRINT_PROMOTE_COOLDOWN_DAYS))
                        if cur.fetchone():
                            print(f"[Transcriber] Voiceprint not promoted for {name}: meeting_auto voiceprint exists "
                                  f"within {VOICEPRINT_PROMOTE_COOLDOWN_DAYS} days")
                            continue

                    buf = io.BytesIO()
                    np.save(buf, cluster_embeddings[label].detach().cpu().numpy().astype(np.float32), allow_pickle=False)
                    s3_key = f"voiceprints/{tenant_id}/{uuid.uuid4()}.npy"
                    s3_client.put_object(Bucket=bucket, Key=s3_key, Body=buf.getvalue(),
                                         ContentType="application/octet-stream")

                    with conn.cursor() as cur:
                        cur.execute(
                            "INSERT INTO firm_speaker_embeddings "
                            "(tenant_id, name, s3_key, source, meeting_id, confidence, is_active, created_at, updated_at) "
                            "VALUES (%s, %s, %s, 'meeting_auto', %s, %s, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                            (tenant_id, name, s3_key, meeting_id, float(confidence)))
                    conn.commit()
                    print(f"[Transcriber] Voiceprint auto-promoted for {name} ({label}): confidence {confidence:.3f}, "
                          f"{duration:.0f}s attributed → s3://{bucket}/{s3_key}")
                except Exception as e:
                    print(f"[Transcriber] Voiceprint auto-promotion failed for {name} (non-fatal): {e}", file=sys.stderr)
        finally:
            conn.close()
    except Exception as e:
        print(f"[Transcriber] Voiceprint auto-promotion failed (non-fatal): {e}", file=sys.stderr)

def post_callback(url: str, secret: str, payload: dict):
    try:
        resp = requests.post(url, json=payload,
            headers={"Content-Type": "application/json", "X-Bot-Secret": secret},
            timeout=30,
            allow_redirects=False)
        print(f"[Callback] POST {url} → {resp.status_code}")
    except Exception as e:
        print(f"[Callback] Failed: {e}", file=sys.stderr)

def process_voice_sample():
    """
    ADO#8298: JOB_MODE=voice_sample — a user's enrollment recording (one speaker, Rainbow Passage).
    Embeds the whole file (no diarization), stores the .npy under voiceprints/{tenant}/, inserts a
    voice_sample row and deletes the user's previous voice_sample rows (re-record replaces).
    """
    sample_s3_key = os.environ["VOICE_SAMPLE_S3_KEY"]
    entra_oid = os.environ["ENTRA_OID"]
    tenant_id = os.environ["TENANT_ID"]
    name = os.environ.get("FIRM_SPEAKER_EMBEDDING_NAME", "").strip() or entra_oid
    s3_bucket = os.environ.get("S3_BUCKET", "firm-recordings-dev")
    hf_token = os.environ.get("HF_TOKEN", "")
    aws_region = os.environ.get("AWS_REGION", "us-east-1")

    db_config = voiceprint_db_config()
    if not tenant_id or not db_config:
        print("[VoiceSample] TENANT_ID or FORTRESS_DB_HOST not set — cannot store voiceprint", file=sys.stderr)
        sys.exit(1)

    s3 = boto3.client("s3", region_name=aws_region)
    suffix = os.path.splitext(sample_s3_key)[1] or ".webm"
    with tempfile.NamedTemporaryFile(suffix=suffix, delete=False) as f:
        audio_path = f.name
    try:
        print(f"[VoiceSample] Downloading s3://{s3_bucket}/{sample_s3_key} for {entra_oid}")
        s3.download_file(s3_bucket, sample_s3_key, audio_path)

        waveform = whisperx.load_audio(audio_path)  # ffmpeg decode → 16 kHz mono float32
        duration_s = len(waveform) / EMBEDDING_SAMPLE_RATE
        print(f"[VoiceSample] Decoded {duration_s:.1f}s of audio")
        inference = _get_embedding_inference(hf_token)
        emb = _embed_window(inference, waveform, 0.0, duration_s)
        if emb is None:
            raise RuntimeError(f"voice sample too short or unusable ({duration_s:.1f}s)")
        emb = F.normalize(emb, dim=0)

        buf = io.BytesIO()
        np.save(buf, emb.detach().cpu().numpy().astype(np.float32), allow_pickle=False)
        voiceprint_key = f"voiceprints/{tenant_id}/{uuid.uuid4()}.npy"
        s3.put_object(Bucket=s3_bucket, Key=voiceprint_key, Body=buf.getvalue(),
                      ContentType="application/octet-stream")

        conn = pymysql.connect(**db_config)
        try:
            with conn.cursor() as cur:
                cur.execute(
                    "INSERT INTO firm_speaker_embeddings "
                    "(tenant_id, name, entra_oid, s3_key, source, meeting_id, confidence, is_active, created_at, updated_at) "
                    "VALUES (%s, %s, %s, %s, 'voice_sample', NULL, NULL, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
                    (tenant_id, name, entra_oid, voiceprint_key))
                new_id = cur.lastrowid
                cur.execute(
                    "DELETE FROM firm_speaker_embeddings "
                    "WHERE tenant_id = %s AND entra_oid = %s AND source = 'voice_sample' AND id <> %s",
                    (tenant_id, entra_oid, new_id))
                replaced = cur.rowcount
            conn.commit()
        finally:
            conn.close()
        print(f"[VoiceSample] Voiceprint {new_id} stored for {name} ({entra_oid}) → s3://{s3_bucket}/{voiceprint_key}; "
              f"replaced {replaced} previous voice_sample row(s)")
    finally:
        release_embedding_model()
        try:
            os.unlink(audio_path)
        except OSError:
            pass

def main():
    if os.environ.get("JOB_MODE") == "voice_sample":
        process_voice_sample()
        return

    meeting_id = int(os.environ["MEETING_ID"])
    # WI #8335: summarize-only re-runs just the summarization pass on the existing transcript
    # (after a user saves speaker corrections) — no audio download, Whisper, or diarization.
    summarize_only = os.environ.get("SUMMARIZE_ONLY", "").strip().lower() == "true"
    transcript_s3_key = os.environ.get("TRANSCRIPT_S3_KEY", "")
    speaker_corrections_s3_key = os.environ.get("SPEAKER_CORRECTIONS_S3_KEY", "")
    if summarize_only and not transcript_s3_key:
        transcript_s3_key = f"firm-transcripts/{meeting_id}/transcript.json"
    audio_s3_key = os.environ.get("AUDIO_S3_KEY", "") if summarize_only else os.environ["AUDIO_S3_KEY"]
    s3_bucket = os.environ.get("S3_BUCKET", "firm-recordings-dev")
    callback_url = os.environ["FIRM_CALLBACK_URL"]
    bot_secret = os.environ.get("BOT_CALLBACK_SECRET", "")
    hf_token = os.environ.get("HF_TOKEN", "")
    aws_region = os.environ.get("AWS_REGION", "us-east-1")
    bedrock_model_id = os.environ.get("BEDROCK_MODEL_ID", "us.anthropic.claude-sonnet-4-6")
    meeting_date = os.environ.get("MEETING_DATE", "")
    # WI #8294: UTC ISO-8601 meeting start — anchor for relative dates in suggested_events
    meeting_start_datetime = os.environ.get("MEETING_START_DATETIME", "")
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

    s3 =boto3.client("s3", region_name=aws_region)

    # ADO#8295: org voiceprints from prior meetings — best-effort, transcription proceeds without them
    tenant_id = os.environ.get("TENANT_ID", "")
    db_config = voiceprint_db_config()
    org_voiceprints = {}
    if summarize_only:
        pass  # voiceprints only feed diarization, which summarize-only skips
    elif tenant_id and db_config:
        try:
            org_voiceprints = load_org_voiceprints(tenant_id, db_config, s3, s3_bucket)
        except Exception as e:
            print(f"[Transcriber] Failed to load org voiceprints (non-fatal): {e}", file=sys.stderr)
    else:
        print(f"[Transcriber] Org voiceprints disabled (TENANT_ID {'set' if tenant_id else 'unset'}, "
              f"FORTRESS_DB_HOST {'set' if db_config else 'unset'})")

    # ADO#8230: prefer the S3-staged timeline (avoids Batch's 8192-byte override limit);
    # fall back to the legacy inline env var for jobs submitted by older firm-web builds.
    active_speaker_s3_key = os.environ.get("ACTIVE_SPEAKER_S3_KEY", "")
    active_speaker_json = ""
    active_speaker_source = "ACTIVE_SPEAKER_JSON"
    if active_speaker_s3_key:
        active_speaker_source = f"s3://{s3_bucket}/{active_speaker_s3_key}"
        try:
            obj = s3.get_object(Bucket=s3_bucket, Key=active_speaker_s3_key)
            active_speaker_json = obj["Body"].read().decode("utf-8")
        except Exception as e:
            print(f"[Transcriber] Failed to download active speaker log from {active_speaker_source}: {e}")
    else:
        active_speaker_json = os.environ.get("ACTIVE_SPEAKER_JSON", "")
    active_speaker_log = []  # list of dicts with name, startMs, endMs
    if active_speaker_json:
        try:
            parsed = json.loads(active_speaker_json) or []
            active_speaker_log = [e for e in parsed if isinstance(e, dict)] if isinstance(parsed, list) else []
            print(f"[Transcriber] Active speaker log loaded from {active_speaker_source}: {len(active_speaker_log)} entries")
        except Exception as e:
            print(f"[Transcriber] Failed to parse active speaker log from {active_speaker_source}: {e}")

    audio_path = None
    has_speaker_corrections = False
    if summarize_only:
        print(f"[Transcriber] Starting summarize-only job for meeting {meeting_id}, transcript: {transcript_s3_key}")
    else:
        print(f"[Transcriber] Starting job for meeting {meeting_id}, audio: {audio_s3_key}")

        # Download audio
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            audio_path = f.name
        print(f"[Transcriber] Downloading audio to {audio_path}")
        s3.download_file(s3_bucket, audio_s3_key, audio_path)
        print(f"[Transcriber] Download complete")

    try:
        if summarize_only:
            # WI #8335: reuse the existing transcript (no Whisper/diarization), overlay the user's
            # speaker corrections, and rerun only the summarization pass below. The corrected
            # transcript is never written back — the base transcript stays untouched.
            obj = s3.get_object(Bucket=s3_bucket, Key=transcript_s3_key)
            result = json.loads(obj["Body"].read().decode("utf-8")) or []
            print(f"[Transcriber] Loaded transcript from s3://{s3_bucket}/{transcript_s3_key}: {len(result)} segments")
            if speaker_corrections_s3_key:
                obj = s3.get_object(Bucket=s3_bucket, Key=speaker_corrections_s3_key)
                corrections = json.loads(obj["Body"].read().decode("utf-8")) or []
                print(f"[Transcriber] Loaded {len(corrections)} speaker correction(s) from s3://{s3_bucket}/{speaker_corrections_s3_key}")
                result = apply_speaker_corrections(result, corrections)
                has_speaker_corrections = any(isinstance(c, dict) and not c.get("archived") for c in corrections)
        else:
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
            speaker_log = []  # active speaker log minus the notetaker, possiblyMultiVoice flagged
            log_base_ms = None  # recording start in the log's timebase; None when there's no usable log
            if hf_token:
                try:
                    print(f"[Transcriber] Loading pyannote diarization pipeline...")
                    # Set offline mode only for pyannote — weights are pre-baked, Whisper downloads at runtime
                    os.environ["HF_HUB_OFFLINE"] = "1"
                    try:
                        diarize_model = DiarizationPipeline(
                            model_name="pyannote/speaker-diarization-community-1",
                            token=hf_token,
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

                    # Build speaker label → name assignment: voice embeddings first, then roster timing overlap
                    if (roster_attendees or active_speaker_log or org_voiceprints) and turns:
                        speaker_segments = {}  # speaker_label -> list of (start, end) tuples
                        for (t_start, t_end, t_speaker) in turns:
                            speaker_segments.setdefault(t_speaker, []).append((t_start, t_end))

                        # ADO#8232: timestamp overlap can't tell apart people whose speaking/presence windows
                        # coincide, so compare voices instead — reference embeddings from each person's clean
                        # active-speaker windows vs. aggregate embeddings of each diarization cluster.
                        # ADO#8295: org voiceprints from prior meetings join those references.
                        embedding_turns = list(turns)
                        if active_speaker_log:
                            multi_voice_roster = {e["name"] for e in roster_attendees if e.get("possiblyMultiVoice")}
                            speaker_log = [dict(e, possiblyMultiVoice=bool(e.get("possiblyMultiVoice")) or (e.get("name") or "").strip() in multi_voice_roster)
                                           for e in active_speaker_log if (e.get("name") or "").strip() != notetaker_name]
                            log_starts = [e["startMs"] for e in speaker_log if e.get("startMs") is not None]
                            if log_starts:
                                # Same rebasing as build_active_speaker_block: epoch-ms logs use the roster's
                                # approximate recording start (earliest join); relative logs are already 0-based
                                if min(log_starts) > 1_000_000_000_000:
                                    log_base_ms = min(e["joinedAtMs"] for e in roster_attendees) if roster_attendees else min(log_starts)
                                else:
                                    log_base_ms = 0

                                # Diarization turns inside a possiblyMultiVoice attendee's speaking window may mix
                                # several voices — keep them out of the cluster embeddings, and leave labels that
                                # speak mostly inside those windows as SPEAKER_NN.
                                multi_windows = [(s, e) for wins in active_log_windows(speaker_log, log_base_ms).values()
                                                 for s, e, m in wins if m]
                                def in_multi_window(t_start, t_end):
                                    return any(s < t_end and e > t_start for s, e in multi_windows)
                                for label, segs in speaker_segments.items():
                                    total = sum(e - s for s, e in segs)
                                    in_multi = sum(e - s for s, e in segs if in_multi_window(s, e))
                                    if multi_windows and total > 0 and in_multi >= 0.5 * total:
                                        speaker_name_map[label] = label
                                        print(f"[Transcriber] {label} left as-is ({in_multi:.1f}s of {total:.1f}s inside possiblyMultiVoice windows)")
                                embedding_turns = [t for t in turns if t[2] not in speaker_name_map and not in_multi_window(t[0], t[1])]

                        if log_base_ms is not None or org_voiceprints:
                            try:
                                meeting_refs = (extract_reference_embeddings(speaker_log, audio_path, hf_token, log_base_ms)
                                                if log_base_ms is not None else {})
                                # Org voiceprints take priority; this meeting's active-speaker references fill in the rest
                                ref_embeddings = dict(org_voiceprints)
                                ref_sources = {name: "voiceprint_db" for name in org_voiceprints}
                                for name, emb in meeting_refs.items():
                                    if name not in ref_embeddings:
                                        ref_embeddings[name] = emb
                                        ref_sources[name] = "active_speaker_log"
                                if ref_embeddings:
                                    cluster_embeddings = extract_cluster_embeddings(embedding_turns, audio_path, hf_token)
                                    dims = {e.numel() for e in cluster_embeddings.values()}
                                    for name in [n for n, e in ref_embeddings.items() if dims and e.numel() not in dims]:
                                        print(f"[Transcriber] Reference for {name} ({ref_sources[name]}) has wrong embedding size — dropped")
                                        del ref_embeddings[name]
                                    confidences = {}
                                    name_map = assign_names_by_embedding(cluster_embeddings, ref_embeddings, confidences=confidences)
                                    for label, name in sorted(name_map.items()):
                                        if name:
                                            print(f"[Transcriber] {label} → {name} source={ref_sources[name]}")
                                    speaker_name_map.update({label: name for label, name in name_map.items() if name})
                                    auto_promote_voiceprints(name_map, cluster_embeddings, compute_cluster_durations(turns),
                                                             confidences, tenant_id, meeting_id, db_config, s3, s3_bucket)
                                else:
                                    print(f"[Transcriber] No reference voice embeddings — falling back to roster timing")
                            except Exception as e:
                                print(f"[Transcriber] Voice embedding resolution failed (non-fatal, falling back to roster timing): {e}", file=sys.stderr)
                            finally:
                                release_embedding_model()

                        # Roster timestamps are epoch ms; approximate recording start as earliest join
                        recording_start_ms = min(e["joinedAtMs"] for e in roster_attendees) if roster_attendees else None

                        for speaker_label, segs in speaker_segments.items():
                            if speaker_label in speaker_name_map:
                                continue  # resolved via voice embedding (or held as multi-voice)
                            if not roster_attendees:
                                speaker_name_map[speaker_label] = speaker_label
                                print(f"[Transcriber] {speaker_label} left unresolved (no voice match, no roster)")
                                continue
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
                print(f"[Transcriber] WARNING: HF_TOKEN not set — skipping diarization and voice-embedding speaker resolution")

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
                # ADO#8479: names are assigned per cluster, but pyannote can fold another person's speech into a
                # matched cluster. The active speaker log is the better per-moment signal, so on conflict it wins
                # — but only when its window is short enough to be a per-turn signal (ADO#8673).
                # Unresolved SPEAKER_NN labels are left alone, as are moments where several people are logged.
                if log_base_ms is not None and speaker_name != speaker_label:
                    midpoint_ms = (seg["start"] + seg["end"]) * 500
                    log_names = active_speakers_at(speaker_log, log_base_ms, midpoint_ms)
                    if len(log_names) == 1 and speaker_name.casefold() not in {n.casefold() for n in log_names}:
                        log_name = next(iter(log_names))
                        # ADO#8673: a long log window is a presence signal spanning back-and-forth, not a per-turn
                        # one — it can't attribute individual segments inside it, so the voice match stands.
                        window_s = _active_window_duration_s(speaker_log, log_base_ms, midpoint_ms, log_name)
                        if window_s <= ACTIVE_SPEAKER_OVERRIDE_MAX_WINDOW_S:
                            print(f"[Transcriber] Speaker conflict at {midpoint_ms:.0f}ms: cluster assigned '{speaker_name}', "
                                  f"active speaker log says '{log_name}' ({window_s:.1f}s window) — using log name")
                            speaker_name = log_name
                        else:
                            print(f"[Transcriber] Speaker conflict at {midpoint_ms:.0f}ms: cluster assigned '{speaker_name}', "
                                  f"active speaker log says '{log_name}' ({window_s:.1f}s window > "
                                  f"{ACTIVE_SPEAKER_OVERRIDE_MAX_WINDOW_S:.0f}s) — keeping cluster name")
                # ADO#8517: once a segment's name is resolved, label it by that name so a raw cluster label
                # never maps to several people (the #8479 override splits clusters per segment).
                if not re.fullmatch(r"SPEAKER_\d+", speaker_name):
                    speaker_label = speaker_name
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
            corrections_block = ""
            if has_speaker_corrections:
                corrections_block = "\n[SPEAKER CORRECTIONS — the meeting organizer manually corrected speaker attribution in this transcript. Treat the speaker names shown as authoritative; do not reassign lines to a different person.]\n\n"
            summary_prompt = f"""Analyze this meeting transcript and provide a rich, structured summary.
{org_context_block}{attendees_block}{active_speaker_block}{corrections_block}
Meeting date: {meeting_date if meeting_date else "unknown"}
Meeting start (UTC): {meeting_start_datetime if meeting_start_datetime else "unknown"}

IMPORTANT — Org Context Usage:
1. TERMINOLOGY — MANDATORY LOOKUP: Before interpreting ANY acronym, abbreviation, or proper noun in the transcript, check it against the ORG WIKI above. The Org Wiki is the authoritative source — do NOT guess or use general knowledge if a term could match a wiki entry. Examples: "NBA" MUST be looked up before assuming it means the basketball association. "FAM", "FAIT", "FIRM", "FORMS", "FIP" are all defined in the wiki. If a term appears in the wiki, use the wiki definition. Period. Only fall back to general knowledge if the term has NO plausible wiki match.

2. SPEAKER IDENTITY RESOLUTION: The transcript uses diarization labels (SPEAKER_00, SPEAKER_01, etc.) which are assigned by an AI and may be imperfect. Use BOTH conversational context AND diarization consistency to identify who is speaking:
   - Read the content of what each speaker says to identify them (e.g., if someone explains they are the AI lead, that is likely Fred White)
   - How people address each other by name is a strong signal
   - Once you identify a speaker label as a person, treat ALL segments with that label as that person
   - The SAME person may appear as MULTIPLE speaker numbers (e.g., diarization may split one person into SPEAKER_02 and SPEAKER_07) — this is expected; use context to merge them
   - Build an internal speaker-to-name map before writing the summary and use it consistently throughout
   - If MEETING ATTENDEES are listed above, they are the people actually in the meeting — prefer mapping speaker labels to those names. Some labels may already be replaced with attendee names based on join/leave timing
   - Speaker labels that appear as real names (e.g. 'Fred White:') in the transcript were identified via acoustic voice matching and are high-confidence — treat them as ground truth. Apply contextual resolution only to remaining SPEAKER_XX labels.
   - If an ACTIVE SPEAKER TIMELINE is given above, use it to break ties: a speaker label whose lines fall inside a person's speaking window is likely that person

Transcript:
{full_text[:50000]}

Return ONLY a valid JSON object with exactly these fields. No commentary before or after.

{{
  "summaryText": "<rich markdown — see format below>",
  "keyDecisionsJson": ["decision 1", "decision 2"],
  "actionItemsJson": [{{"owner": "Name", "deadline": "YYYY-MM-DD or TBD", "description": "task"}}],
  "followUpsJson": ["follow-up 1", "follow-up 2"],
  "openQuestionsJson": ["question 1", "question 2"],
  "suggested_events": [
    {{
      "subject": "short calendar subject, e.g. Pricing review with Acme",
      "source_quote": "verbatim transcript snippet",
      "attendees": ["name or email"],
      "attendees_inferred": false,
      "proposed_datetime": "YYYY-MM-DDTHH:MM:SS or null",
      "proposed_duration_minutes": 30,
      "confidence": 0.85,
      "timezone": "Central Standard Time"
    }}
  ]
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
- openQuestionsJson: unresolved questions only

SCHEDULING DETECTION (suggested_events):
Identify NEW meetings the participants agreed or proposed to schedule during this conversation. Return an empty array if there are none.
- Include ONLY new scheduling intent. Do NOT include references to meetings that already exist or are already on the calendar.
- Each suggestion MUST have: clear intent to meet + at least one identifiable attendee + some timeframe (even "next week" counts).
- DROP conditional intent ("if the deal closes, let's meet..."), recurring cadences ("every two weeks", "weekly sync"), and vague intent with no identifiable attendee.
- subject: a short calendar-ready title for the new meeting (under 80 characters).
- source_quote: the verbatim transcript snippet expressing the intent (escape any double quotes as \\").
- attendees: use the people explicitly named first. If the speaker says "everyone", "the team", "all of us", etc., fall back to the meeting participants (MEETING ATTENDEES / Key People) and set attendees_inferred to true; otherwise attendees_inferred is false.
- proposed_datetime: resolve relative dates ("tomorrow", "next Tuesday", "next week") against the MEETING START above — NOT against today's date or when this transcript was processed. Output local wall-clock time in the suggestion's timezone as YYYY-MM-DDTHH:MM:SS with no UTC offset. If only a day or week is known, pick a reasonable business-hours time (e.g. 10:00) on the earliest plausible day. Use null only if the timeframe is too vague to pick any date.
- proposed_duration_minutes: as stated, otherwise 30.
- timezone: a Windows time zone name (e.g. "Eastern Standard Time", "Pacific Standard Time") inferred from context; default "Central Standard Time".
- confidence: 0.0-1.0 that this is a real, new meeting the participants intend to schedule. Include ONLY suggestions with confidence >= 0.65.
- Return 0-3 suggestions maximum. Prefer quality over quantity."""
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
                    for key in ("keyDecisionsJson", "actionItemsJson", "followUpsJson", "openQuestionsJson", "suggested_events"):
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

            suggested_events = normalize_suggested_events(summary_data.get("suggested_events"))
            summary_data["suggested_events"] = suggested_events
            print(f"[Transcriber] Scheduling detection: {len(suggested_events)} suggested event(s)")

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
                    "OpenQuestionsJson": json.dumps(summary_data.get("openQuestionsJson", [])),
                    # WI #8335: summarize-only omits this (null = leave stored value alone) so a
                    # correction re-summarize can't reset suggestions the organizer already
                    # created/dismissed back to pending.
                    "SuggestedEventsJson": None if summarize_only else json.dumps(suggested_events),
                    "ModelUsed": bedrock_model_id
                }
            })
        except Exception as e:
            print(f"[Transcriber] Bedrock summarization failed (non-fatal): {e}", file=sys.stderr)

        print(f"[Transcriber] Job complete for meeting {meeting_id}")

    except Exception as e:
        print(f"[Transcriber] FATAL: {e}", file=sys.stderr)
        # WI #8335: a summarize-only failure leaves the existing transcript + summary intact —
        # don't flip a Complete meeting to Failed.
        if not summarize_only:
            post_callback(callback_url, bot_secret, {
                "meetingId": meeting_id,
                "status": "failed",
                "error": str(e)
            })
        sys.exit(1)
    finally:
        try:
            if audio_path:
                os.unlink(audio_path)
        except:
            pass

if __name__ == "__main__":
    main()
