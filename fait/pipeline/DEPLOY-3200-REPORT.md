# Deploy Report: ADO#3200

**Story:** 5.1-A — user_workspace_files table, S3 storage, artifact SSE event + chat card  
**Agent:** War Machine (Rhodey — devops)  
**Date:** 2026-05-10  
**Status:** ✅ COMPLETE

---

## Deployment Type

AWS ECS / Fargate — `fred-dev` service on `fortress-tools-cluster`

---

## Pre-Deploy Snapshot

- **Previous task def:** `fred-dev:165`
- **Previous image:** `fred-chat:0c113528`
- **Commit at deploy:** `aca376f2` (HEAD verified)
- **Service health:** 1/1 running, ACTIVE

---

## Steps Completed

1. ✅ **Commit verified** — `aca376f2` confirmed at HEAD
2. ✅ **Pre-flight passed** — `docker-build.sh` from monorepo root: no blocks
3. ✅ **Docker build** — `docker build --no-cache -f fait/Dockerfile.debian -t fred-chat:aca376f2 .`
   - Build succeeded with warnings only (CS0168, CS1998, CS8604, CS8602, MUD0002 — all pre-existing)
   - Image digest: `sha256:2fd84f64713d1fbd1b8551213fb96a9e1c5b3fe850b8fa3152a95d9b93f740c6`
4. ✅ **ECR login** — `fortress-tools-deployer` confirmed
5. ✅ **ECR push** — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:aca376f2`
   - Digest: `sha256:2fd84f64713d1fbd1b8551213fb96a9e1c5b3fe850b8fa3152a95d9b93f740c6`
6. ✅ **Task def registered** — Cloned `fred-dev:165`, updated image → `fred-dev:166`
   - ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:166`
7. ✅ **ECS service updated** — `fred-dev` → `fred-dev:166`
8. ✅ **Service stable** — `aws ecs wait services-stable` exited 0
9. ✅ **Post-deploy verification** — 1/1 running, task def `fred-dev:166`, status ACTIVE
10. ✅ **CloudWatch logs verified** — App started cleanly:
    - `EF Core CreateTablesAsync` executed — `user_workspace_files` table created (no error → fresh create)
    - `Database initialization complete`
    - `Application started. Press Ctrl+C to shut down.`
    - No startup errors (all `fail:` entries are expected idempotent "already applied" cases)

---

## Migration Result

The `20260510174001_AddWorkspaceFiles` EF migration ran via `CreateTablesAsync()` on startup. The `user_workspace_files` table was created successfully — confirmed by:
- No `Table 'user_workspace_files' already exists` error (would appear if it existed)
- No crash or fatal error in startup sequence
- Clean `Database initialization complete` reached

---

## Deployed Image

| Field | Value |
|-------|-------|
| Image tag | `fred-chat:aca376f2` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:aca376f2` |
| Digest | `sha256:2fd84f64713d1fbd1b8551213fb96a9e1c5b3fe850b8fa3152a95d9b93f740c6` |
| Task def | `fred-dev:166` |
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |

---

## Files Deployed

- `src/FortressAI.Shared/Models/UserWorkspaceFile.cs` (new)
- `src/FortressAI.Web/Services/IWorkspaceFileService.cs` (new)
- `src/FortressAI.Web/Services/WorkspaceFileService.cs` (new)
- `src/FortressAI.Web/Components/Chat/ArtifactCard.razor` (new)
- `src/FortressAI.Web/Migrations/20260510174001_AddWorkspaceFiles.cs` (new)
- `src/FortressAI.Web/Data/AppDbContext.cs` (modified)
- `src/FortressAI.Web/Program.cs` (modified)
- `src/FortressAI.Web/Services/IUserAgentRuntime.cs` (modified)
- `src/FortressAI.Web/Components/Chat/ChatView.razor` (modified)

---

## Rollback Plan

If rollback is needed:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:165 \
  --region us-east-1
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1
```

**DB note:** The `user_workspace_files` migration is additive-only (new table, no column changes to existing tables). Rollback is safe without DB reversal — the table can remain with no impact.

---

## Notes

- Pre-flight `deploy.sh` script reported ECR repo `fortress-ai-chat` not found — this is expected; script is configured for the old repo path. The monorepo deploy uses `fred-chat` ECR repo. Build pre-flight (`docker-build.sh`) passed cleanly from monorepo root.
- Commit `aca376f2` is a fix commit (reset `_conversationArtifacts` on conversation switch) — the full Epic 5.1-A work was in the preceding commit, with this commit being the final fix before deploy.

---

_First story of Epic 5 shipped. Artifact pipeline is live in fred-dev._
