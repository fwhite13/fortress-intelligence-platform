# Deploy Report: ADO#3236 + ADO#3242 — Phase 1

**Date:** 2026-05-10 (21:33–21:41 EDT)
**Deployed by:** Rhodey (devops subagent)
**Commit:** `d2f10857`

---

## Deployment Type

AWS ECS (Fargate) — Task definition update + ECR image push

---

## What Was Deployed

### ADO#3236 — In-App Feedback (Report a Bug / Suggest a Feature)
- New `feedback_submissions` DB table (EF migration `20260510230000_AddFeedbackSubmissions`)
- `FeedbackSubmission.cs` model
- `FeedbackModal.razor` component
- "Report a Bug" button in chat header
- `POST /api/feedback` + `POST /api/feedback/{id}/status` endpoints
- `FeedbackDispatcher` DI service
- Two new env vars: `FEEDBACK_INTERNAL_TOKEN`, `FEEDBACK_JARVIS_WEBHOOK_URL`

### ADO#3242 — Harness `create_document` Description Fix
- Updated `create_document` tool description in `fait-v2/agent-harness/harness-server.js`

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Previous fred-dev task def | `fred-dev:177` |
| Previous Blazor image | `fred-chat:f1af77a8` |
| Previous harness task def | `fait-v2-agent-harness:18` |
| Previous harness image | `fait-v2-agent-harness:f1af77a8` |

---

## Steps Completed

1. ✅ Confirmed `d2f10857` at HEAD — `git log --oneline -3`
2. ✅ ECR login — authenticated `742932328420.dkr.ecr.us-east-1.amazonaws.com`
3. ✅ Blazor build — `docker build -f fait/Dockerfile.debian -t fred-chat:d2f10857 .` — SUCCESS
4. ✅ Blazor pushed to ECR — `fred-chat:d2f10857`
5. ✅ Harness build — `docker build -t fait-v2-agent-harness:d2f10857 .` — SUCCESS (cached layers)
6. ✅ Harness pushed to ECR — `fait-v2-agent-harness:d2f10857`
7. ✅ Harness task def registered — `fait-v2-agent-harness:19`
8. ✅ fred-dev task def registered — `fred-dev:178`
   - Updated image → `fred-chat:d2f10857`
   - Updated `Fargate__TaskDefinition` → `fait-v2-agent-harness:19`
   - Added `FEEDBACK_INTERNAL_TOKEN`
   - Added `FEEDBACK_JARVIS_WEBHOOK_URL`
9. ✅ ECS service updated — `fred-dev` → `fred-dev:178`
10. ✅ Service stable — RUNNING 1/1, pending 0
11. ✅ CloudWatch startup verified — `Database initialization complete`, `Now listening on: http://[::]:8080`
12. ✅ DB migration — `feedback_submissions` table created via EF Core `CreateTablesAsync` on startup
13. ✅ ADO#3236 — comment added, state → Closed
14. ✅ ADO#3242 — comment added, state → Closed

---

## ECR Digests

| Image | Tag | Digest |
|-------|-----|--------|
| `fred-chat` | `d2f10857` | `sha256:f1fcb740421a4a1b2fe433d706b90fa47532e0ad2da23884ce47faaa267fdd51` |
| `fait-v2-agent-harness` | `d2f10857` | `sha256:8618b7de5699f049a99bf519e53f742a45bb5cee4238c8167f5a1848dfe23e2d` |

---

## Task Definition Revisions

| Task Definition | Revision |
|-----------------|----------|
| `fred-dev` | `:178` |
| `fait-v2-agent-harness` | `:19` |

---

## ECS Status

```json
{
  "status": "ACTIVE",
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:178",
  "running": 1,
  "desired": 1,
  "pending": 0
}
```

---

## Migration Confirmation

`feedback_submissions` table created via EF Core `RelationalDatabaseCreator.CreateTablesAsync()` during `DatabaseInitializationService` startup.

Log confirmation: `Database initialization complete` — no errors related to feedback table creation.

EF migration file: `20260510230000_AddFeedbackSubmissions.cs` — included in build.

---

## Rollback Plan

### Pre-Deploy State
- fred-dev task def: `fred-dev:177`
- Blazor image: `fred-chat:f1af77a8`
- Harness task def: `fait-v2-agent-harness:18`

### Rollback Commands

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:177 \
  --region us-east-1
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1
```

### Rollback SLA: < 5 minutes

---

## ADO Status

| Item | Comment | State |
|------|---------|-------|
| ADO#3236 | ✅ Added (comment ID 792273) | ✅ Closed |
| ADO#3242 | ✅ Added (comment ID 792274) | ✅ Closed |
