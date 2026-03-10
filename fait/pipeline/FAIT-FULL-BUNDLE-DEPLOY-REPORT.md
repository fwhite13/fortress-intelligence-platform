# FAIT Full Bundle Deploy Report

**Date:** 2026-03-10  
**Time:** ~00:06–00:09 EDT  
**Deployed by:** devops subagent (on behalf of Maria Hill)  
**Branch:** `main` @ `0c2eaa0`

---

## Summary

✅ **Deploy SUCCEEDED** — all checks passed.

---

## What Was Deployed

- KB redesign Phase 2a+2b (4 separate KBs, `KbProject`→`KbTeam` rename)
- DB connection fix (EF Core `GetDbConnection` lifecycle)
- Dockerfile healthcheck fix (`/health` endpoint + `curl` installed in container)
- Processing chip fix (`ListDocumentsAsync` DB lookup)
- Tool-call-limit system prompt directive
- Dictation button (Web Speech API)

---

## Deploy Steps

### Step 1 — New Task Definition Registered
- Source: `fred-dev:54`
- New revision: **`fred-dev:55`**
- Method: Cloned task def, stripped immutable fields, re-registered via `file://` input

### Step 2 — CodeBuild
- Project: `fip-fait-build`
- Build ID: `fip-fait-build:506b01ef-2b40-42e1-b05c-49869800f9cd`
- Result: **SUCCEEDED** (~1.5 min)

### Step 3 — ECS Service Update
- Cluster: `fortress-tools-cluster`
- Service: `fred-dev`
- Task definition: `fred-dev:55`
- Force new deployment: yes
- ECS confirmed: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:55`

### Step 4 — Task Health Poll
| Time     | Status      | Health  |
|----------|-------------|---------|
| 00:08:04 | No tasks yet | —      |
| 00:08:25 | PROVISIONING | UNKNOWN |
| 00:08:46 | ACTIVATING   | UNKNOWN |
| 00:09:07 | RUNNING      | **HEALTHY** ✅ |

Task reached HEALTHY + RUNNING in **~65 seconds** after provisioning began.

### Step 5 — Verification
| Check | Result |
|-------|--------|
| Running digest | `sha256:ca9c0cb7d6582a7ee1b06cd1682800df9e4f20b1c0362f647201799ca282c838` |
| ECR digest (`kb-latest`) | `sha256:ca9c0cb7d6582a7ee1b06cd1682800df9e4f20b1c0362f647201799ca282c838` |
| Digest match | ✅ Match |
| Health endpoint (`/health`) | ✅ `{"status":"healthy","service":"fred","timestamp":"2026-03-10T04:09:15.5736223Z"}` |

---

## Previous State
- Was running: `fred-dev:54`, image `sha256:637d46ce` — UNHEALTHY (curl missing in container)
- Rollback target (available if needed): `fred-dev:53`, image `sha256:e8ac602f`

---

## Notes

- The `--cli-input-json /dev/stdin` approach failed (AWS CLI JSON parsing issue with piped stdin); resolved by writing task def to `/tmp/new-task-def.json` and using `file://` reference.
- Build was exceptionally fast (~1.5 min vs typical 5-8 min) — likely warm CodeBuild environment.
- Container transitioned ACTIVATING→RUNNING→HEALTHY in one poll cycle (20s), confirming the `curl`+healthcheck fix is working.
- No ECS events of concern observed.

---

## Final Status: ✅ DEPLOY COMPLETE
