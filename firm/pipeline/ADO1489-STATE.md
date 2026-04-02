# Pipeline State: ADO#1489

## Current Stage: DEPLOYED ✅
## Risk Level: medium (Dockerfile change + full vpbot image rebuild)
## Pipeline Path: full
## Review Cycles: 0

### Root Cause
firm-vpbot:4 is 3.79GB (Whisper large-v3 pre-baked = ~1.5GB model layer).
Fargate cold start pulls full image from ECR every time — 4+ minutes.
Target: bot in Teams within 60 seconds of Join Now.

### Approach
Switch pre-baked model from large-v3 → medium for dev.
Image drops ~1.8-2.0GB. Pull time target: <90s.
WHISPER_MODEL env var allows override to large-v3 per-task when accuracy matters.
large-v3 remains available as a named ECR tag for future prod promotion.

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Maria | 14:12 | 14:14 | medium model for dev, large-v3 via env var |
| BUILD | ✅ DONE | Tony | 14:14 | 14:16 | Commit 449dc60. Dockerfile + transcribe.ts. |
| REVIEW | ✅ PASS | Clint | 14:16 | 14:18 | PASS 10/10. Dead-code WARN on L34 noted, pre-existing. |
| DEPLOY | ✅ DONE | Rhodey | 14:18 | 14:22 | firm-vpbot:5. 2.20GB (was 3.79GB, -38%). |
| VERIFY | ✅ PASS | Natasha | 14:22 | 14:23 | PASS 6/6. 2.20GB confirmed. All env vars intact. |
| CONFIRM | ✅ DONE | Maria | 14:23 | 14:23 | Pipeline complete. |
