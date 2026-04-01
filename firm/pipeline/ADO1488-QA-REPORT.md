# QA Report: ADO#1488 — Whisper large-v3 Pre-baked into vpbot Image

### Verdict: ✅ PASS

**Analyst:** Natasha Romanoff (Black Widow)  
**Date:** 2026-04-01  
**Task Def:** `firm-vpbot:4`  
**Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:4a9b7807704183a2bbaf2cdf87e4640ca583d2a3`

---

### Test Results

| TC | Description | Result | Details |
|----|-------------|--------|---------|
| TC1 | firm-vpbot:4 is latest active task def | ✅ PASS | Revision: `4` ✓ |
| TC2 | Image is commit-tagged (not just :latest) | ✅ PASS | URI ends in `:4a9b7807704183a2bbaf2cdf87e4640ca583d2a3` ✓ |
| TC3 | Image size confirms Whisper model layer | ✅ PASS | 3,794,011,812 bytes (~3.79 GB) > 3.5 GB threshold; digest `sha256:a4e1361630afc0de6522cf1192d2ffba419e71bec15da1cb646af196b5e2561d` matches ✓ |
| TC4 | HF_HOME and pre-bake present in Dockerfile | ✅ PASS | Line 72: `ENV HF_HOME=/app/.cache/huggingface`; Line 73–74: `RUN python3 -c "from faster_whisper import WhisperModel; WhisperModel('large-v3', device='cpu', compute_type='int8')"` ✓ |
| TC5 | All env vars from :3 retained in :4 | ✅ PASS | `FIRM_API_URL`, `BOT_CALLBACK_SECRET`, `S3_BUCKET`, `AWS_REGION`, `FIRM_MAX_MEETING_HOURS` — all 5 present ✓ |
| TC6 | transcribe.ts model params match pre-baked model | ✅ PASS | `large-v3` (line 34/64), `device="cpu"` (line 37), `compute_type="int8"` (line 37) — exact match ✓ |

---

### Detail Notes

**TC3 — Image Size & Digest**
```json
{
  "tags": ["latest", "4a9b7807704183a2bbaf2cdf87e4640ca583d2a3"],
  "sizeBytes": 3794011812,
  "digest": "sha256:a4e1361630afc0de6522cf1192d2ffba419e71bec15da1cb646af196b5e2561d"
}
```
Size increase from ~2.3 GB → ~3.79 GB (+1.49 GB) is consistent with the Whisper large-v3 model layer.

**TC4 — Dockerfile Pre-bake Lines**
```
72: ENV HF_HOME=/app/.cache/huggingface
73: RUN python3 -c "from faster_whisper import WhisperModel; WhisperModel('large-v3', device='cpu', compute_type='int8')" \
74:     && echo "Whisper large-v3 pre-baked successfully"
```

**TC5 — Environment Variables**
```json
[
  { "name": "S3_BUCKET",              "value": "firm-recordings-dev" },
  { "name": "BOT_CALLBACK_SECRET",    "value": "bd9b766..." },
  { "name": "AWS_REGION",             "value": "us-east-1" },
  { "name": "FIRM_MAX_MEETING_HOURS", "value": "4" },
  { "name": "FIRM_API_URL",           "value": "http://firm.fip.internal:8080" }
]
```

**TC6 — transcribe.ts Model Parameters**
```
13:  modelSize?: string;   // default: large-v3
15:  device?: string;      // default: cpu
34:  model_size = sys.argv[2] if len(sys.argv) > 2 else "large-v3"
37:  model = WhisperModel(model_size, device="cpu", compute_type="int8")
64:  this.modelSize = process.env.WHISPER_MODEL || config.modelSize || 'large-v3'
```
Runtime params (`large-v3`, `device="cpu"`, `compute_type="int8"`) are an exact match to the pre-bake invocation in the Dockerfile.

---

### Test Summary
- **Total:** 6
- **Passed:** 6
- **Failed:** 0
- **Warnings:** 0

---

### Recommendation

✅ **Ready for transcription test.** Image integrity confirmed, model layer verified, env vars complete, source/runtime params aligned. No issues found.
