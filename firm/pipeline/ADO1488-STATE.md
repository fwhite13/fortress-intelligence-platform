# Pipeline State: ADO#1488

## Current Stage: DEPLOYED ✅
## Risk Level: medium (Docker image change — larger image, build time increase)
## Pipeline Path: full
## Review Cycles: 0

### Root Cause
faster-whisper downloads large-v3 model from HuggingFace Hub at runtime (~1.5GB).
Fargate task has no egress to HuggingFace Hub from VPC. Download fails → Whisper crashes.
HF_TOKEN is a red herring — public model, no auth needed. Root cause is network isolation.

### Fix
Pre-bake large-v3 model into Docker image during build (internet available at build time).
Set HF_HOME env var so faster-whisper uses baked path at runtime, no download attempted.

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Maria | 13:33 | 13:35 | Pre-bake model in Dockerfile |
| BUILD | ✅ DONE | Tony | 13:35 | 13:37 | Commit 4a9b780. Dockerfile only. Syntax clean. |
| REVIEW | ✅ PASS | Clint | 13:37 | 13:39 | PASS 7/7. Two non-blocking obs (WHISPER_MODEL override, root cache dir) |
| DEPLOY | ✅ DONE | Rhodey | 13:39 | 13:46 | firm-vpbot:4. 3.79GB image. sha256:a4e13616. |
| VERIFY | ✅ PASS | Natasha | 13:46 | 13:47 | PASS 6/6. 3.79GB image, all env vars confirmed. |
| CONFIRM | ✅ DONE | Maria | 13:47 | 13:47 | Pipeline complete. |
