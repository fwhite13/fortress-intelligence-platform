# Deploy Report: FAIT Prod Cherry-pick — WIs #1667 + #1669 + #1670

**Date:** 2026-04-08  
**Time:** 17:49–17:57 EDT  
**Deployer:** War Machine (Rhodey / devops subagent)  
**Requested by:** Maria Hill  
**Branch:** `fait-prod` (cherry-pick from `main` onto `ab122c7`)

---

## Summary

Cherry-picked 4 commits from `main` onto a new `fait-prod` branch (base: `ab122c7` / task def `:41`), built via CodeBuild from the branch, and registered + deployed `fait-prod:42`.

---

## Commits Cherry-Picked

| Source SHA | Dest SHA | WI | Description |
|-----------|---------|-----|-------------|
| `163f4c3` | `6def164` | #1667 | feat(fait#1667): ForgeService S3 upload fix (KB notes sync to S3 on create/update/delete) |
| `c4971f8` | `50c475c` | #1669 | fix(ai#1669): inject user email into system prompt; add anti-fabrication guard for m365 email addresses |
| `270f61f` | `be75c1a` | #1669 | fix(fait#1669): gate own-email bullet on non-null email in m365 guidance |
| `63d0212` | `55b9111` | #1670 | feat(fait#1670): mandatory pre-send email confirmation step in m365 system prompt |

**Conflict resolved:** `nexus/src/FortressNexus.Web/pipeline/P3-STATE.md` was deleted in HEAD (fait-prod base) but modified in `163f4c3`. Took HEAD deletion — pipeline doc only, no FAIT code impact.

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild project | `fip-fait-build` |
| Build ID | `fip-fait-build:b18e1e51-2d7e-45be-923c-fc3ef0bc0cb8` |
| Build # | 189 |
| Source version | `fait-prod` branch |
| Status | ✅ SUCCEEDED |
| Duration | ~7 minutes |

---

## Image

| Field | Value |
|-------|-------|
| ECR repo | `fred-chat` |
| Tag | `55b911194cf45ebcc652a76b3813a2efc484601f` (full SHA) |
| Also tagged | `kb-latest` |
| Digest | `sha256:ab934b4a0e340fda176dbea3e2c6c3756a33f24526a93d04d2f84e953e026a79` |
| Pushed at | 2026-04-08T17:52:46 EDT |
| Full URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:55b911194cf45ebcc652a76b3813a2efc484601f` |

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Previous task def | `fait-prod:41` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:ab122c76970647c14f561a8466718cb099ef6d00` |
| Previous commit | `ab122c7` |

---

## Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fait-prod` |
| Task def registered | `fait-prod:42` |
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/6babfbf3bda34479b6fb1cc22d30c576` |
| Deployed at | 2026-04-08T17:54:40 EDT |
| Service stability | ✅ STABLE |
| Container health | ✅ HEALTHY |

---

## Steps Completed

1. ✅ Read MEMORY.md + deploy pipeline reference
2. ✅ ADO comment posted — PROD DEPLOY start (WI #1667)
3. ✅ `fait-prod` branch created from `ab122c7`
4. ✅ Git stash applied for clean checkout
5. ✅ 4 commits cherry-picked cleanly (1 minor conflict resolved — nexus pipeline doc, not FAIT code)
6. ✅ `fait-prod` pushed to `origin`
7. ✅ CodeBuild triggered with `--source-version fait-prod` (build #189)
8. ✅ Build SUCCEEDED (~7 min)
9. ✅ Image verified in ECR (`fred-chat:55b911194cf45ebcc652a76b3813a2efc484601f`)
10. ✅ `fait-prod:42` task definition registered
11. ✅ ECS service updated: `fait-prod` → `fait-prod:42`
12. ✅ `aws ecs wait services-stable` — passed
13. ✅ Task health: HEALTHY
14. ✅ CloudWatch logs: clean startup (DB init expected non-fatal warnings only)
15. ✅ ADO comment posted — PROD DEPLOY complete

---

## CloudWatch Logs (startup summary)

Log group: `/ecs/fred-dev`  
Stream: `ecs/fred/6babfbf3bda34479b6fb1cc22d30c576`

- DB initialization: all tables ensured ✅
- `DataProtectionKeys` already exists — non-fatal, expected ✅
- MCP transports healthy (brave: 200, m365: 200) ✅
- No error-level logs on startup ✅

---

## Rollback Plan

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-prod \
  --task-definition fait-prod:41 \
  --force-new-deployment \
  --region us-east-1

aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fait-prod \
  --region us-east-1
```

Rollback to: `fait-prod:41` (`ab122c7`) — `fred-chat:ab122c76970647c14f561a8466718cb099ef6d00`  
Rollback SLA: < 5 minutes

---

## Notes

- CodeBuild `fip-fait-build` auto-deployed to `fred-dev` as part of its normal buildspec — this is expected and was not interfered with
- `kb-latest` tag was also updated as part of CodeBuild push — this is normal behavior
- The `fait-prod` branch is now live at `origin/fait-prod` for future cherry-picks

---

_Deployed by War Machine (Rhodey). fait-prod is production-ready._
