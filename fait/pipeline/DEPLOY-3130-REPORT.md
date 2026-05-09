# DEPLOY-3130 Report — Fargate Config Wiring (fred-dev)

**Date:** 2026-05-09
**Agent:** Rhodey (DevOps)
**ADO WI:** ADO#3130

---

## Summary

Fixes 1, 2, and 4 from ADO#3130 applied to fred-dev environment.

---

## Fix 2 — Harness Task Def FAIT_BASE_URL Correction

**Problem:** `fait-v2-agent-harness:6` had `FAIT_BASE_URL=https://fait-v2.dev.fortressam.ai` (wrong — should point to v1)

**Action:**
- Pulled `fait-v2-agent-harness:6`
- Changed `FAIT_BASE_URL` → `https://fait.dev.fortressam.ai`
- Registered new revision

**Result:** `fait-v2-agent-harness:7` registered
- ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:7`

---

## Fix 1 — fred-dev Fargate Env Vars

**Problem:** fred-dev task def missing all `Fargate__*` env vars needed for `FargateUserAgentRuntime`

**Action:**
- Added 6 new env vars to `fred-dev` container definition:

| Variable | Value |
|---|---|
| `Fargate__ClusterArn` | `arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster` |
| `Fargate__TaskDefinition` | `fait-v2-agent-harness:7` |
| `Fargate__SubnetIds` | `subnet-08e1d4f1b5530f39e,subnet-051bfcf5b07661809` |
| `Fargate__SecurityGroupIds` | `sg-0fb53615b1eb4a175` |
| `Fargate__ContainerName` | `harness` |
| `Fargate__HarnessPort` | `3000` |

- Registered `fred-dev:131`
- Deployed with `--force-new-deployment`

**Result:**
- `fred-dev:131` active
- Service: RUNNING=1, PENDING=0, DESIRED=1 ✅
- App started cleanly — no `InvalidOperationException` about Fargate config ✅
- Logs show `Application started` in Development mode ✅

**Note:** `ecs-register-task-def.sh` wrapper script referenced in spec does not exist at `/home/fredw/projects/fip/fait/scripts/`. Registered directly via `aws ecs register-task-definition`. Functionally equivalent.

---

## Fix 4 — IAM Permissions Verification on fait-v2-task-role

**Status: BLOCKED — AccessDeniedException**

`fortress-tools-deployer` does not have `iam:ListAttachedRolePolicies` permission:

```
User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized to perform: 
iam:ListAttachedRolePolicies on resource: role fait-v2-task-role
```

**Cannot verify** whether `fait-v2-task-role` has the required ECS permissions:
- `ecs:RunTask` on `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:*`
- `ecs:DescribeTasks` on `arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster`
- `ecs:StopTask` on `arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster`
- `iam:PassRole` for the harness task role

**Action required:** Fred or an IAM-privileged user must verify `fait-v2-task-role` manually in the AWS Console or via a user with `iam:ListAttachedRolePolicies` / `iam:GetRolePolicy` permissions.

If any permissions are missing, the following policy document should be attached:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["ecs:RunTask"],
      "Resource": "arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:*"
    },
    {
      "Effect": "Allow",
      "Action": ["ecs:DescribeTasks", "ecs:StopTask"],
      "Resource": "*",
      "Condition": {
        "ArnEquals": {
          "ecs:cluster": "arn:aws:ecs:us-east-1:742932328420:cluster/fortress-tools-cluster"
        }
      }
    },
    {
      "Effect": "Allow",
      "Action": ["iam:PassRole"],
      "Resource": "arn:aws:iam::742932328420:role/*",
      "Condition": {
        "StringEquals": {
          "iam:PassedToService": "ecs-tasks.amazonaws.com"
        }
      }
    }
  ]
}
```

---

## Post-Deploy Verification

| Check | Result |
|---|---|
| fred-dev RUNNING=1, PENDING=0 | ✅ |
| App started cleanly (no Fargate config exceptions) | ✅ |
| No `InvalidOperationException` in logs | ✅ |
| Fargate env vars present in fred-dev:131 | ✅ |
| Harness pointing to correct FAIT URL | ✅ |

---

## Open Items

1. **Fix 4 IAM verification** — Requires IAM-privileged user to check `fait-v2-task-role` policies
2. **`ecs-register-task-def.sh` script** — Script referenced in spec is missing from `/home/fredw/projects/fip/fait/scripts/`. Should be created if standardized wrapper is desired.

---

## Deploy: ADO#3130 Fix 3 — IUserAgentRuntime DI (fred-dev:132)

**Date:** 2026-05-09 15:57–16:00 EDT  
**Deployed by:** Rhodey (devops subagent)  
**Commit:** `173138d3`  
**Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:173138d3`  
**Digest:** `sha256:0b3d677ca5df93492e7e835791a6fe91ee82e93d1aae68d9f35b2b084be3b7af`  
**Task def:** `fred-dev:132` (from `fred-dev:131`, updated image only)  
**Service:** `fred-dev` on `fortress-tools-cluster`

### Steps Completed

1. ✅ Docker build from monorepo root (`--no-cache`): success
2. ✅ ECR push: `fred-chat:173138d3` pushed (4 new layers, 8 reused)
3. ✅ Task definition registered: `fred-dev:132` (all env vars preserved from :131 including 6 Fargate__ vars)
4. ✅ ECS update-service deployed with force-new-deployment
5. ✅ ECS stable: running=1, pending=0
6. ✅ CloudWatch logs: clean startup, DB init complete, MCP tools listing 200
7. ✅ `/api/agent/status` → HTTP 403 (auth required, not 500/404)
8. ✅ ADO#3130 marked Resolved

### Fixes Deployed in 173138d3

- **Fix 3:** IUserAgentRuntime DI in AssistantLoadingState.razor — HTTP poll replaced with direct DI injection; no more 403s on agent status check
- **Fix 4:** IAM permissions for fait-v2-task-role — confirmed clear by Fred; no ECS role changes needed

### All 4 Fixes in ADO#3130

| Fix | Description | Status |
|-----|-------------|--------|
| Fix 1 | Fargate env vars in fred-dev task def | ✅ Done (fred-dev:131) |
| Fix 2 | FAIT_BASE_URL in harness task def | ✅ Done (fait-v2-agent-harness:7) |
| Fix 3 | IUserAgentRuntime DI in AssistantLoadingState | ✅ Done (fred-dev:132) |
| Fix 4 | IAM ECS permissions confirmed | ✅ Confirmed by Fred |

### Notes

- Base image: `fred-dev:131` env vars fully preserved — no env var changes in this rev
- taskRoleArn: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` preserved
