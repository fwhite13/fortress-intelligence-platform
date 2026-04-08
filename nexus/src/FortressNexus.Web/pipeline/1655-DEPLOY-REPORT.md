# Deploy Report — WI #1655 + WI #1661
**War Machine (Rhodey / devops) — Phase 3 Deploy**
**Date:** 2026-04-08
**Deployer:** fortress-tools-deployer

---

## What Deployed

### WI #1655 — File Delete + Narrative Persist
- `SubmissionService.UpdateNarrativeAsync` — persists narrative on resume submit
- `SubmissionService.DeleteUploadedFileAsync` — soft-deletes files from S3 + DB

### WI #1661 — Progress Indicator
- `NewSpecWizard.razor` — `ApplyResumeChangesAsync`, `_regenInProgress` flag
- Live `MudProgressLinear` indicator on Confirm step during spec regen
- Pass 2 progress wiring

---

## Build

| Build | # | SHA | Status | Duration |
|-------|---|-----|--------|----------|
| `fip-nexus-build:4c6bc715-6464-4c65-b89c-ec8dc09fec73` | 22 | `b5d0a14e74a36217c33c33af2e0a07c75e721cc9` | ✅ SUCCEEDED | ~1m 15s |

**Note:** Build #21 (`fip-nexus-build:897d6d78`) also ran and succeeded but resolved to `f2924ec` (WI #1655 only) because `b5d0a14` (WI #1661) had not yet been pushed to remote `main`. Build #22 was fired after pushing, and resolved to the correct HEAD (`b5d0a14`).

---

## Rollback Baseline

| Task Def | Image | Notes |
|----------|-------|-------|
| `nexus-web:22` | `nexus-web:f2924ec94c26e78704804b642ed1f158be81d67a` | WI #1659 baseline (pre-this-deploy) |

---

## Deployment

| Task Definition | Image | ECS Deployment | Rollout |
|----------------|-------|----------------|---------|
| `nexus-web:23` | `nexus-web:b5d0a14e74a36217c33c33af2e0a07c75e721cc9` | `ecs-svc/7758552306558007149` | ✅ COMPLETED |

---

## Health Check

| Check | Result |
|-------|--------|
| `curl https://nexus.fortressam.ai/` | **HTTP 403** ✅ (expected — auth-gated) |
| Running container image | `nexus-web:b5d0a14e74a36217c33c33af2e0a07c75e721cc9` ✅ |
| EF Core migrations | COMPLETED (no schema changes) ✅ |
| Startup errors | None ✅ |
| CloudWatch log stream | `ecs/nexus-web/cfeae40e8ea442a48252a3dd49aac5f5` |

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 15:17 | Confirmed WI #1659 deploy COMPLETED (nexus-web:22) |
| 15:17 | Build #21 started — resolved f2924ec (b5d0a14 not yet pushed) |
| 15:19 | Build #21 SUCCEEDED — image f2924ec pushed |
| 15:20 | Pushed b5d0a14 to origin/main |
| 15:20 | Build #22 started |
| 15:21 | Build #22 SUCCEEDED — image b5d0a14 pushed, ECS force-deploy triggered |
| 15:24 | Task def nexus-web:23 registered (b5d0a14) |
| 15:24 | ECS update-service --force-new-deployment with nexus-web:23 |
| 15:27 | ECS steady state — nexus-web:23 PRIMARY/COMPLETED |
| 15:27 | Health check HTTP 403 ✅ |

---

## Rollback Procedure

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:22 \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Schema Changes
None.

---

_Deployed by War Machine (Rhodey) — devops subagent_
