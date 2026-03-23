# WI909 Deploy Report — War Machine (Rhodey)

**Date:** 2026-03-20  
**Deployer:** War Machine (Rhodey) / devops subagent  
**Target:** `firm-web` on `fortress-tools-cluster` (dev)  
**URL:** https://firm.dev.fortressam.ai  

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Task Definition | `firm-web:27` |
| Image Tag | `dff2e61` |
| Image Digest | `sha256:a4b8b9d17c493c9cf4e42f264c0da06fdfa77cf20489a582321539c2168ccc62` |
| Service Status | ACTIVE, runningCount=1, pendingCount=0 |
| Image Pushed At | 2026-03-17T16:04:11 EDT |

## Rollback Plan

```bash
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition firm-web:27 --force-new-deployment --region us-east-1
```

---

## Deploy Steps

| # | Step | Status | Notes |
|---|------|--------|-------|
| 1 | Source AWS creds | ✅ | `fortress-tools-deployer` |
| 2 | ADO pre-deploy comment | ✅ | Comment ID 726549 |
| 3 | Trigger CodeBuild #13 | ❌ FAILED | Docker Hub 429 + wrong build context (`firm/` instead of `.`) |
| 4 | Fix buildspec context | ✅ | Changed `firm/` → `.` (monorepo root); committed `de9c9ce` |
| 5 | Trigger CodeBuild #14 | ✅ SUCCEEDED | Build #14 `fip-firm-build:47a5bb53` — ~90s |
| 6 | Verify ECR image push | ✅ | Tag `de9c9ce1...` + `latest`, 99.9 MB, digest `sha256:6048e50a...` |
| 7 | Register `firm-web:28` | ✅ | New task def pointing to `de9c9ce1...` image |
| 8 | ECS update-service | ✅ | `firm-web:28` deployed with `--force-new-deployment` |
| 9 | ECS stabilization | ✅ | runningCount=1, pendingCount=0, single PRIMARY deployment |
| 10 | Health check: root | ✅ | HTTP 302 |
| 11 | Health check: /health | ✅ | HTTP 200 |
| 12 | Health check: fip-tokens.css | ✅ | HTTP 200 — FipShared confirmed in image |

---

## Deployment Result

| Item | Value |
|------|-------|
| Task Definition | `firm-web:28` |
| Image Tag | `de9c9ce1ca026400cd79d69f3c47c4006a066f58` |
| Image Digest | `sha256:6048e50a0bf6ae14a4f276b5d1c901efa539d27c781c7ee7840e7dcf396e926d` |
| Image Size | 99.9 MB (was 98.9 MB — FipShared now included) |
| CodeBuild Build | `fip-firm-build:47a5bb53-3fd6-42e5-916f-a6bd5743bd35` (build #14) |
| WI909 Commits | `dff2e61` (code changes) / `4a79693` (build report) / `de9c9ce` (buildspec fix) |
| Deployment Time | ~10 minutes total |

---

## Issues Encountered

### CodeBuild #13 Failed — Two Issues

**Issue 1: Wrong build context**
- Buildspec had `docker build -f firm/Dockerfile.debian -t firm-web:$IMAGE_TAG firm/`
- Should be `.` (monorepo root) — the Dockerfile requires `COPY shared/FipShared/` which only exists at root
- This was the **FipShared bug root cause** — every prior build with this buildspec would have failed to include FipShared
- **Fix:** Changed `firm/` → `.` in buildspec; committed and pushed as `de9c9ce`

**Issue 2: Docker Hub rate limit (429)**  
- CodeBuild pulling `debian:bookworm-slim` from Docker Hub unauthenticated hit rate limit
- Transient — cleared by the time build #14 ran (~2 minute gap)
- No credentials change needed; may recur on rapid successive builds

**Resolution:** Both issues resolved. Build #14 succeeded cleanly.

---

## Health Check Results

| Endpoint | Expected | Actual | Status |
|----------|----------|--------|--------|
| `https://firm.dev.fortressam.ai/` | 200 or 302 | **302** | ✅ PASS |
| `https://firm.dev.fortressam.ai/health` | 200 | **200** | ✅ PASS |
| `https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css` | 200 | **200** | ✅ PASS |

**fip-tokens.css is 200 — FipShared is confirmed present in the deployed image.**

---

## Status: ✅ DEPLOY COMPLETE

Handing to Natasha (Black Widow) for QA verification.
