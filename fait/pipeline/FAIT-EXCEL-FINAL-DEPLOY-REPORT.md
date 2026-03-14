# Deploy Report: FAIT for Excel — Env Var + Static Hosting

**Date:** 2026-03-14  
**Time:** 00:22–00:30 EDT  
**Deployed by:** War Machine (Rhodey) — devops subagent  
**Authorized by:** Maria Hill (Pipeline Manager)  
**Commits deployed:** `97fe948` (static hosting), `022da21` (multi-key auth)

---

## Summary

Two changes deployed together:
1. ✅ New ECS env var `AppKeys__ExcelAddin` added to task definition
2. ⚠️ Static files at `wwwroot/excel-addin/` — container running but `/excel-addin/` path returns 404

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild Project | `fip-fait-build` |
| Build ID | `fip-fait-build:82580b98-0bc5-4d37-ac17-24ed5a715819` |
| Build Status | ✅ SUCCEEDED |
| Build Duration | ~2 minutes (00:23:00 → 00:25:06) |

---

## Task Definition

| Field | Value |
|-------|-------|
| Previous task def | `fred-dev:70` |
| New task def | `fred-dev:71` |
| Change | Added `AppKeys__ExcelAddin` env var to `fred` container |
| Env var name | `AppKeys__ExcelAddin` |
| Env var value | `Ozv2CSVTw4pOY7LJaoJwbJrRemIqTWOmJKYA_6zZUTk` |

---

## ECR Image

| Field | Value |
|-------|-------|
| Repository | `fred-chat` |
| Tag | `kb-latest` |
| Pre-deploy digest | `sha256:d7120a2c…` |
| New ECR digest | `sha256:e942c85af6978e0feeac774282b7d4a3bea0c7fe8719cd902da82f175c46a06f` |

---

## Rollout

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |
| Rollout state | ✅ COMPLETED |
| Running count | 1 |
| Duration | ~4 minutes (00:25:31 → 00:29:24) |

---

## Verification

### Digest Match
| Field | Value |
|-------|-------|
| Running task | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/6f920ddef4084876a0eeeaa57799b406` |
| Task image digest | `sha256:e942c85af6978e0feeac774282b7d4a3bea0c7fe8719cd902da82f175c46a06f` |
| ECR digest | `sha256:e942c85af6978e0feeac774282b7d4a3bea0c7fe8719cd902da82f175c46a06f` |
| Match | ✅ DIGEST MATCH |

### Health Check
| Endpoint | Result |
|----------|--------|
| `https://fait.dev.fortressam.ai/health` | ✅ HEALTHY — `{"status":"healthy","service":"fred","timestamp":"2026-03-14T04:29:33.2272343Z"}` |

### Excel Addin Path
| Endpoint | HTTP Status | Result |
|----------|-------------|--------|
| `https://fait.dev.fortressam.ai/excel-addin/` | `404` | ⚠️ NOT SERVING |

---

## ⚠️ Issue: `/excel-addin/` returns 404

The container deployed successfully with the new image (digest verified), but the `/excel-addin/` path returns HTTP 404. Possible causes:

1. **Static files not present in image** — `wwwroot/excel-addin/` may not have been committed to the repo or not copied into the Docker image during the CodeBuild
2. **Static file middleware not configured** — The ASP.NET Core app may not be serving files from that path
3. **Route conflict** — A controller or middleware may be intercepting the request before static files

**Recommended follow-up:**
- Verify `wwwroot/excel-addin/` exists in commit `97fe948`
- Check CodeBuild logs for file copy steps
- Check ASP.NET Core `UseStaticFiles()` configuration and wwwroot path

---

## Rollback

If rollback is needed, execute:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:70 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

**Rollback target:** `fred-dev:70` (pre-deploy baseline, digest `sha256:d7120a2c…`)

---

## Stage Outcomes

| Stage | Result | Notes |
|-------|--------|-------|
| CodeBuild | ✅ SUCCEEDED | Picked up commit `97fe948` |
| Task Def Registration | ✅ `fred-dev:71` | `AppKeys__ExcelAddin` env var confirmed present |
| ECS Service Update | ✅ COMPLETED | `fred-dev:71` deployed to `fortress-tools-cluster` |
| Digest Verification | ✅ MATCH | `sha256:e942c85a…` confirmed on running task |
| Health Check | ✅ HEALTHY | `/health` returns 200 |
| Excel Addin Path | ⚠️ 404 | `/excel-addin/` not serving — needs investigation |

---

*Deployed by War Machine. Rollback ready at fred-dev:70.*
