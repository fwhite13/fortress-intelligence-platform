# Deploy Report: FAIT M365 MCP Adapter
**Task:** FAIT-M365-MCP  
**Commit:** `64aebd1` — M365McpAdapter + seed + McpToolService gating  
**Date:** 2026-03-13  
**Deployed by:** War Machine (Rhodey) — devops subagent  
**Requested by:** Maria Hill — Pipeline Manager  

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Previous task definition | `fred-dev:69` |
| Previous image digest | `sha256:6bc291b5…` |
| Service | `fred-dev` |
| Cluster | `fortress-tools-cluster` |
| Environment | `dev` |
| Health endpoint | `https://fait.dev.fortressam.ai/health` |

---

## Deploy Steps

| # | Step | Status | Time | Notes |
|---|------|--------|------|-------|
| 1 | Load `.env.deployer` | ✅ PASS | 11:24 | Account: `742932328420` |
| 2 | Start CodeBuild `fip-fait-build` | ✅ PASS | 11:24 | Build ID: `fip-fait-build:2944e34f-9f0a-4f3b-b486-cbf48ed572d9` |
| 3 | CodeBuild polling | ✅ SUCCEEDED | 11:24–11:26 | ~2 min build time |
| 4 | ECR digest captured | ✅ PASS | 11:26 | `sha256:ff66dea8ee3bed3f59f6cb6a964ccfe40a90858e9b13cacabbc42a7030cdfedc` |
| 5 | ECS `update-service --force-new-deployment` | ✅ PASS | 11:26 | Rollout initiated |
| 6 | ECS rollout polling | ✅ COMPLETED | 11:26–11:29 | ~3 min rollout time |
| 7 | Digest verification | ✅ MATCH | 11:29 | Task digest == ECR digest |
| 8 | Health check | ✅ HEALTHY | 11:29 | `{"status":"healthy","service":"fred"}` |

---

## Post-Deploy Verification

| Check | Result | Detail |
|-------|--------|--------|
| CodeBuild | ✅ SUCCEEDED | `fip-fait-build:2944e34f-9f0a-4f3b-b486-cbf48ed572d9` |
| ECS rollout state | ✅ COMPLETED | Running count: 1 |
| Task definition | `fred-dev:70` (new) | Forced new deployment |
| Image digest (ECR) | `sha256:ff66dea8…` | `ff66dea8ee3bed3f59f6cb6a964ccfe40a90858e9b13cacabbc42a7030cdfedc` |
| Image digest (task) | `sha256:ff66dea8…` | ✅ MATCH |
| Task ARN | `fortress-tools-cluster/ddc54571f1b54141b228dd4a32450d6b` | |
| Health endpoint | ✅ HEALTHY | `https://fait.dev.fortressam.ai/health` |
| Health response | `{"status":"healthy","service":"fred","timestamp":"2026-03-13T15:29:50.0936808Z"}` | |

---

## Rollback Plan

If rollback is required, execute:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:69 \
  --region us-east-1 \
  --profile fortress-tools-deployer \
  --force-new-deployment

# Monitor rollback
for i in $(seq 1 18); do
  STATE=$(aws ecs describe-services --cluster fortress-tools-cluster --services fred-dev \
    --region us-east-1 --profile fortress-tools-deployer \
    --query 'services[0].deployments[0].rolloutState' --output text)
  RUNNING=$(aws ecs describe-services --cluster fortress-tools-cluster --services fred-dev \
    --region us-east-1 --profile fortress-tools-deployer \
    --query 'services[0].deployments[0].runningCount' --output text)
  echo "$(date +%H:%M:%S) state=$STATE running=$RUNNING"
  [ "$STATE" = "COMPLETED" ] && echo "✅ ROLLBACK COMPLETE" && break
  [ "$STATE" = "FAILED" ] && echo "❌ ROLLBACK FAILED" && break
  sleep 20
done
```

**Rollback target:** `fred-dev:69` (previous known-good)  
**Previous digest:** `sha256:6bc291b5…`

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 11:24:35 | CodeBuild started |
| 11:26:38 | CodeBuild SUCCEEDED |
| 11:26:50 | ECS update-service initiated |
| 11:29:39 | ECS rollout COMPLETED |
| 11:29:xx | Digest verified ✅ |
| 11:29:50 | Health check ✅ HEALTHY |

**Total pipeline time:** ~5 minutes

---

## Outcome

**✅ DEPLOY SUCCESSFUL**

Commit `64aebd1` (M365McpAdapter + seed + McpToolService gating) is live in `fred-dev`.  
Service is healthy, digest verified, rollout complete.  
Review score: 30/30 (PASS).
