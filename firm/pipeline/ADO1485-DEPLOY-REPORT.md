# ADO#1485 Deploy Report — Fix vpbot Callback URL

**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-04-01  
**Type:** Config-only (no code change, no CodeBuild)  
**ADO Work Item:** FAIT#1485

---

## Root Cause

vpbot ECS tasks (running inside VPC) were POSTing callbacks to `Firm__ApiUrl = https://firm.dev.fortressam.ai`. Cloudflare Turnstile's managed challenge blocks all non-browser VPC traffic to this URL. Callbacks never reached firm-web → meeting status stuck at **Joining**.

## Fix Applied

Changed `Firm__ApiUrl` in the firm-web ECS task definition:

| | Value |
|---|---|
| **Before** | `https://firm.dev.fortressam.ai` |
| **After** | `http://firm.fip.internal:8080` |

Routes callbacks through VPC internal DNS (Cloud Map `firm.fip.internal`) — same pattern as `FIP__FaitApiUrl = http://fait.fip.internal:8080`.

---

## Pre-Deploy Snapshot

| Field | Value |
|---|---|
| Previous task def revision | firm-web:73 |
| Running task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/66f0d053197c4b7a9dc815c675797d96` |
| Image digest | `sha256:3cd1f0722a943832ec82bfdb411ce7356e242cb7953e65f480555bd753971c6a` |

---

## Steps Executed

### Step 1 — Killed stuck bot task
- Task: `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/369243ef1fad44e597743ee3f25c90d8`
- Reason: "Stuck bot from ADO#1485 investigation — killing before fix deploy"
- Status at stop: RUNNING → stopped

### Step 2 — Registered new task definition
- Source: firm-web:73
- Change: `Firm__ApiUrl` only (1 env var)
- **New revision: firm-web:74**

### Step 3 — Updated ECS service
- Service: `firm-web` on cluster `fortress-tools-cluster`
- Task definition: `firm-web:74`
- Force new deployment: yes

### Step 4 — Stabilization
- Achieved rolloutState=COMPLETED in ~4.5 minutes (10 polls × 15s)
- runningCount=1, failedTasks=0

### Step 5 — Stale target cleanup
- New task private IP: `172.31.72.50`
- Old task `172.31.75.0:8080` deregistered from `meetings-web-dev-tg`
- Final TG target count: **1 (healthy)**
- Drain complete at ~08:26:58 EDT

### Step 6 — FipShared check
- URL: `https://firm.dev.fortressam.ai/_content/FipShared/css/fip-tokens.css`
- HTTP response: **302** (not 404) — ✅ PASS

### Step 7 — Internal URL verification (Cloud Map)
- Namespace: `fip.internal`, Service: `firm`
- Resolved IP: **172.31.72.50** (matches healthy TG target) — ✅ PASS

---

## Outcome

| Check | Result |
|---|---|
| Stuck bot task stopped | ✅ |
| firm-web:74 deployed | ✅ |
| Firm__ApiUrl corrected | ✅ |
| ECS service stabilized | ✅ |
| TG targets = 1 (healthy) | ✅ |
| FipShared HTTP 302 | ✅ |
| firm.fip.internal resolves to 172.31.72.50 | ✅ |

---

## Rollback Plan

If issues arise: `aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:73 --force-new-deployment --region us-east-1 --profile fortress-tools-deployer`

---

## Notes

- Image URI unchanged — same digest `sha256:3cd1f0722a943832ec82bfdb411ce7356e242cb7953e65f480555bd753971c6a`
- No code change, no git push, no CodeBuild triggered
- `fortress-tools-deployer` profile used exclusively throughout
- Pattern matches fait: `FIP__FaitApiUrl = http://fait.fip.internal:8080`
