# ADO#1344 — Deploy Report v2
## FIRM Standalone Microsoft Token Management

**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-03-29  
**Status:** ✅ HEALTHY  

---

## What Was Deployed

**Commits:** `33d1104` + `0f6d962`  
**Code Review:** PASS (Hawkeye, 2 cycles)

**Changes:**
- `FirmMicrosoftTokenService` + `IFirmMicrosoftTokenService` — FIRM's own token service using `FirmDbContext`
- `UserMicrosoftTokens` DbSet wired in `FirmDbContext` pointing at `firm_dev.user_microsoft_tokens`
- `/auth/ms-callback` OAuth consent endpoint
- `CalendarService` updated to use `IFirmMicrosoftTokenService` with `firmUser.Id`
- `FaitSharedDbContext` deleted

---

## Pre-Deploy State (Rollback Target)

| Item | Value |
|------|-------|
| Previous task def | `firm-web:53` |
| Previous running image | `firm-web:52` |
| Rollback command | `aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:53` |

---

## Build

| Item | Value |
|------|-------|
| Build host | SteamServer (WSL2) |
| Dockerfile | `firm/Dockerfile.debian` |
| Build context | `~/projects/fip` (monorepo root) |
| `--no-cache` | ✅ Yes |
| ECR image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:53` |
| Image digest | `sha256:42e721fbcacbd571020bffca85fdcac1719b13e0730551e876a3f64951d9ce70` |
| Build result | ✅ SUCCESS (warnings only, no errors) |

---

## ECS Deploy

| Item | Value |
|------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `firm-web` |
| New task def | `firm-web:54` |
| Running image | `firm-web:53` ✅ confirmed |
| `Firm__MsCallbackUrl` | ✅ Added: `https://firm.dev.fortressam.ai/auth/ms-callback` |
| Service health | STABLE / HEALTHY |

---

## Env Var Changes

`Firm__MsCallbackUrl` was **not previously set** and was **added** during task def registration:
- Value: `https://firm.dev.fortressam.ai/auth/ms-callback`
- Matches Azure app registration redirect URI for FIRM dev

---

## Preflight Notes

Pre-flight script (`/home/fredw/.openclaw/workspace/scripts/preflight/deploy.sh`) has a stale ECR repo mapping for `firm` → `meeting-assistant-aws`. Actual repo is `firm-web`. Script needs updating (non-blocking; manually verified `firm-web` exists before proceeding).

---

## Rollback Plan

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:53
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```
