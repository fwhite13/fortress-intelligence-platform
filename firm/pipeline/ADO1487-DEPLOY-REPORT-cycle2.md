# ADO#1487 — DEPLOY REPORT (cycle 2)
**War Machine — firm-web:76**
**Date:** 2026-04-01
**Engineer:** War Machine (James Rhodes)

---

## Summary

Deployed `firm-web:76` with `[AllowAnonymous]` on the `VpCallback` action in `MeetingsApiController.cs`.
The FallbackPolicy was requiring auth on the VP bot callback endpoint, breaking the meeting recording flow.

---

## What Changed

- **Commit:** `8342d8e` (`8342d8eb5dcc397e191f54afcd7ab6630cba4dc9`)
- **File:** `firm/FIRM.Web/Controllers/MeetingsApiController.cs`
- **Change:** `[AllowAnonymous]` added to `VpCallback` action
- **Branch:** `main`

---

## Pre-Deploy State

| Resource | Value |
|---|---|
| Task Definition | `firm-web:75` |
| Image SHA | `b23cfb9ccdc1ad7022b3c98f38e9e6f0fad269cc` |
| Service Status | ACTIVE, rolloutState=COMPLETED, runningCount=1 |

---

## Deployment Steps

### Step 1 — Git Push
- Pushed 3 commits to `origin/main`
- HEAD: `8342d8eb5dcc397e191f54afcd7ab6630cba4dc9`

### Step 2 — CodeBuild
- **Build ID:** `fip-firm-build:4fa3eb42-a139-4b30-9981-82493c34700b`
- **Result:** SUCCEEDED (~90s)

### Step 3 — Task Definition Registration
- **New Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:8342d8eb5dcc397e191f54afcd7ab6630cba4dc9`
- **Registered:** `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:76`
- Cloned from `firm-web:75`, image updated, all env vars/secrets/roles preserved

### Step 4 — ECS Service Update
- Service updated with `--force-new-deployment`
- **Stabilization:** rolloutState=COMPLETED, runningCount=1, failedTasks=0 (~210s)

### Step 5 — Stale TG Cleanup
- Target group: `meetings-web-dev-tg`
- Deregistered draining target: `172.31.73.3:8080`

### Step 6 — FipShared Check
- URL: `https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css`
- Response: **302** — PASS ✅

---

## Post-Deploy State

| Resource | Value |
|---|---|
| Task Definition | `firm-web:76` |
| Image SHA | `8342d8eb5dcc397e191f54afcd7ab6630cba4dc9` |
| Service Status | ACTIVE, rolloutState=COMPLETED, runningCount=1, failedTasks=0 |

---

## Rollback Plan

If `:76` fails: update service to `firm-web:75` with `--force-new-deployment`.

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:75 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## Result: ✅ DEPLOY SUCCEEDED
