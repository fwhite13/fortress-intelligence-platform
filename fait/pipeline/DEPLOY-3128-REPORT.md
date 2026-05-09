# Deploy Report — ADO#3128: Assistant Setup Detection + /assistant-setup

**Date:** 2026-05-09  
**Deployed by:** Rhodey (DevOps subagent)  
**Commit:** `a01103a1`  
**Service:** `fred-dev` on cluster `fortress-tools-cluster`

---

## What Was Deployed

- `feat(fait#3128): assistant setup detection + /assistant-setup onboarding page`
- `fix(fait#3128): replace hardcoded 2px in setup-spinner with CSS variable`

Adds `onboarding_completed_at` column detection in `/chat` — users with NULL are redirected to `/assistant-setup`. New `/assistant-setup` page collects display name and role/description, sets the column on submit, and redirects to `/chat`.

---

## Resources Updated

| Resource | Before | After |
|----------|--------|-------|
| ECR Image | `fred-chat:8bf9078b` | `fred-chat:a01103a1` |
| Task Definition | `fred-dev:129` | `fred-dev:130` |
| ECS Service | `fred-dev` | `fred-dev` (redeployed) |

---

## ECR Push

- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:a01103a1`
- **Digest:** `sha256:b19afdd51db550650f9c802150ca001709b9de817333a97e950d71d26fd909be`
- **Repo:** `fred-chat`

---

## Deployment Outcome

- ECS service stabilized: **RUNNING=1, PENDING=0, DESIRED=1**
- Task definition: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:130`
- App startup: **Clean** — no errors related to `onboarding_completed_at` column or `AppUser` model
- `/api/agent/status`: **HTTP 403** (auth-gated, not 404/500 — healthy)

---

## CloudWatch Logs Highlights

- Database initialization complete ✅
- All MCP transports healthy (devops, brave, m365) ✅
- `Now listening on: http://[::]:8080` ✅
- No migration errors (S3Key dedup constraint was pre-existing idempotent failure, unrelated to this WI)

---

## ADO Update

- **Work Item:** ADO#3128  
- **State:** Resolved  
- **Comment:** Deployed fred-chat:a01103a1, fred-dev:130. ECS stable. Onboarding gate + /assistant-setup live.

---

## Notes

- Pre-flight script reported ECR repo `fortress-ai-chat` not found — this is a script misconfiguration for `fred-dev` (actual repo is `fred-chat`). AWS creds verified OK separately.
- Build used `--no-cache` per SOUL.md policy for UI-facing services.
