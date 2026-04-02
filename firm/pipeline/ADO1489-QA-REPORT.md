# QA Report: ADO#1489 — Whisper medium pre-baked vpbot image

### Verdict: ✅ PASS (6/6)

### Environment
- **Target:** AWS ECS / ECR — `firm-vpbot` task definition
- **Region:** us-east-1
- **Profile:** fortress-tools-deployer
- **Commit:** `449dc600d0fde4fa058cc1c318172ef147916409`
- **Test Date:** 2026-04-01
- **Analyst:** Natasha Romanoff (Black Widow — QA)

---

### Test Results

| TC | Test | Result | Details |
|----|------|--------|---------|
| TC1 | firm-vpbot latest revision is :5 | ✅ PASS | `revision = 5` confirmed |
| TC2 | Image size confirms medium model (not large-v3) | ✅ PASS | `2,358,256,350 bytes (~2.20GB)` — within range [1.8GB–2.5GB]. Previous was 3,794,011,812 bytes (3.79GB). Delta: **-37.8%** |
| TC3 | Dockerfile pre-bakes medium | ✅ PASS | Line 76: `WhisperModel('medium', device='cpu', compute_type='int8')` — no `large-v3` in pre-bake RUN |
| TC4 | transcribe.ts constructor default is medium | ✅ PASS | Line 64: `process.env.WHISPER_MODEL \|\| config.modelSize \|\| 'medium'` — effective default is `medium` |
| TC5 | All env vars from :4 retained in :5 | ✅ PASS | All 5 required vars present: `FIRM_API_URL`, `BOT_CALLBACK_SECRET`, `S3_BUCKET`, `AWS_REGION`, `FIRM_MAX_MEETING_HOURS` |
| TC6 | firm-vpbot:5 points to commit-tagged image (not :latest) | ✅ PASS | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:449dc600d0fde4fa058cc1c318172ef147916409` |

---

### Detail Notes

**TC2 — Image Size**
- Actual: `2,358,256,350 bytes` (2.20 GB)
- Previous (:4): `3,794,011,812 bytes` (3.79 GB)
- Reduction: **1,435,755,462 bytes (-37.8%)**
- Confirms Whisper medium model baked (medium ~750MB vs large-v3 ~1.5GB)

**TC3 — Dockerfile**
```
Line 71: # Pre-bake Whisper medium model (dev default — faster cold start, ~750MB vs 1.5GB for large-v3)
Line 76: RUN python3 -c "from faster_whisper import WhisperModel; WhisperModel('medium', device='cpu', compute_type='int8')" \
Line 77:     && echo "Whisper medium pre-baked successfully"
```
No `large-v3` in the pre-bake RUN line. Comments note large-v3 requires HF egress (not available in Fargate) — the guard rails are documented in-source.

**TC4 — transcribe.ts**
```
Line 64:  this.modelSize = process.env.WHISPER_MODEL || config.modelSize || 'medium';
```
Note: The Python helper script (`transcribe.py`) at line 34 has `"large-v3"` as its own positional argv fallback — but this path is unreachable in normal operation. TypeScript always passes `this.modelSize` as argv[2], which resolves to `'medium'` by default. Not a defect.

**TC5 — Environment Variables (firm-vpbot:5)**
```json
FIRM_API_URL          = http://firm.fip.internal:8080
BOT_CALLBACK_SECRET   = bd9b7660...  (secret present)
S3_BUCKET             = firm-recordings-dev
AWS_REGION            = us-east-1
FIRM_MAX_MEETING_HOURS = 4
```
All 5 required env vars confirmed present and populated.

**TC6 — Image URI**
```
742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:449dc600d0fde4fa058cc1c318172ef147916409
```
Full 40-char commit SHA tag confirmed. `:latest` also present as secondary tag on same digest — task def pinned to SHA, not `:latest`. ✅

---

### Issues Found

None. Clean pass.

---

### Test Summary
- **Total tests:** 6
- **Passed:** 6
- **Failed:** 0
- **Warnings:** 0

---

### Readiness Assessment

firm-vpbot:5 is confirmed deployable. Image size reduction (-38%) directly targets Fargate cold start time. Target cold start <90s is plausible — recommend Fred live test with a real Teams meeting to validate end-to-end transcription pipeline with the medium model.

---

_Trust nothing. Verify everything. — N. Romanoff_
