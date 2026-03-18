# WI844 Deploy Report — FIRM v1: Fix 5 Blocking Gaps
**Date:** 2026-03-17  
**Agent:** War Machine (James Rhodes) — `devops`  
**Status:** ✅ DEPLOY COMPLETE

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Task Definition | `firm-web:26` |
| Running Count | 1 |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:be9034b` |
| Service Status | ACTIVE |

---

## Rollback Command

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster \
  --service firm-web --task-definition firm-web:26 \
  --force-new-deployment --region us-east-1
```

**Rollback target:** `firm-web:26` (image: `firm-web:be9034b`)

---

## Firm__SharedSecret Verification

| Task Definition | Value |
|----------------|-------|
| `firm-web:26` (pre-deploy) | `5bb8ff8088fdac0f8fb19319723fae663124db1ffee73e5e6de0b64e67ac606e` |
| `firm-web:27` (new deploy) | `5bb8ff8088fdac0f8fb19319723fae663124db1ffee73e5e6de0b64e67ac606e` (inherited from rev 26) |
| `fait-prod:32` | **NOT PRESENT** |

**Assessment:** `Firm__SharedSecret` is correctly set in firm-web:27. It is absent from fait-prod:32 across all checked revisions (28–32) — FIRM is still in dev-only state and the VpCallback integration between FAIT and FIRM has not been wired in FAIT prod yet. This is expected for FIRM v1. A follow-up WI should add `Firm__SharedSecret` to fait-prod task def when FIRM/FAIT VpCallback integration is activated.

**⚠️ Follow-up required:** When FAIT prod is wired to call FIRM's VpCallback endpoint, `Firm__SharedSecret` must be added to fait-prod task def with matching value `5bb8ff8088fdac0f8fb19319723fae663124db1ffee73e5e6de0b64e67ac606e`.

---

## New Image + Task Definition

| Field | Value |
|-------|-------|
| Commit | `dff2e61` |
| Image Tag | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:dff2e61` |
| Image Digest | `sha256:a4b8b9d17c493c9cf4e42f264c0da06fdfa77cf20489a582321539c2168ccc62` |
| New Task Def | `firm-web:27` |
| Build Source | `~/projects/fip/` (monorepo root) + `firm/Dockerfile.debian` |

---

## fip-tokens.css Verification

### Docker Image Inspect
```
docker run --rm --entrypoint="" firm-web:dff2e61 find /app/wwwroot -name "fip-tokens*"
/app/wwwroot/css/fip-tokens.css
/app/wwwroot/_content/FipShared/css/fip-tokens.css
```
✅ `fip-tokens.css` **PRESENT** at `/app/wwwroot/_content/FipShared/css/fip-tokens.css`

> Note: The static files are served from `/app/wwwroot/`, not `/app/`. Initial check used wrong path.

### Live URL
```
curl -sk -o /dev/null -w "%{http_code}" https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css
```
✅ **200 OK**

---

## Target Group — Stale Target Deregistration

**TG Used:** `meetings-web-dev-tg` (routes `firm.dev.fortressam.ai` host header)  
**TG ARN:** `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/meetings-web-dev-tg/7a7e9af531f05a53`

> Note: TG was originally named for the meetings app; it now routes FIRM traffic. No `firm-web-tg` exists yet.

| IP | Port | Action |
|----|------|--------|
| `172.31.42.72` | 8080 | Deregistered (was draining — old firm-web:26 task) |
| `172.31.64.244` | 8080 | **KEPT** — new firm-web:27 task (healthy) |

**Deregistration delay:** 300s (default). Old target shows "draining" for up to 5min after deregister command — this is expected ELB behavior. New traffic routes exclusively to `172.31.64.244` immediately upon deregistration.

**Final TG state (after drain completes):** 1 target — `172.31.64.244:8080` (healthy)

---

## Health Checks

| Endpoint | Status |
|----------|--------|
| `https://firm.dev.fortressam.ai/health` | ✅ 200 |
| `https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css` | ✅ 200 |
| `https://fait.dev.fortressam.ai/health` | ✅ 200 |
| `https://fait.fortressam.ai/health` | ✅ 200 |

All health checks green. FAIT regression clean.

---

## Pipeline Steps Summary

| Step | Action | Result |
|------|--------|--------|
| 1 | ADO comment — deploy starting | ✅ Comment ID 724661 |
| 2 | Pre-deploy snapshot | ✅ firm-web:26, running=1 |
| 3 | Firm__SharedSecret verification | ✅ Present in firm-web:26/27; absent in fait-prod:32 (expected — see note) |
| 4 | Docker build (monorepo root) | ✅ firm-web:dff2e61 built |
| 5 | fip-tokens.css in image | ✅ Present at /app/wwwroot/_content/FipShared/css/fip-tokens.css |
| 6 | ECR push | ✅ Pushed (digest sha256:a4b8b9d1...) |
| 7 | Register firm-web:27 | ✅ Registered |
| 8 | Update service to firm-web:27 | ✅ Service stable, running=1 |
| 9 | Stale TG target deregistration | ✅ 172.31.42.72 deregistered, 172.31.64.244 kept |
| 10 | Health checks | ✅ All 200 |
| 11 | ADO comment — deploy complete | ✅ Comment ID 724670 |

---

## ADO Comments
- **Start:** Comment ID 724661 — "DEPLOY STARTING. War Machine deploying WI844..."
- **Complete:** Comment ID 724670 — "DEPLOY COMPLETE. firm-web:27 running..."

---

*Deployed by War Machine (James Rhodes) — devops subagent*  
*Natasha verifying next.*
