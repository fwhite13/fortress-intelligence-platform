# Deploy Report: FIRM Login Fix
**Date:** 2026-03-11  
**Time:** 11:44–11:53 EDT  
**Agent:** War Machine (Rhodey) — devops  
**Commit:** `7f7dc32`  
**Deployed by:** Maria Hill (pipeline-manager)

---

## Pre-Deploy Snapshots

### FAIT (fred-dev service)
| Field | Value |
|-------|-------|
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/ea8f3c46967a40938c9b9ccf70518773` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:62` |
| Image Digest | `sha256:83b67bb89167e327d0c0ea41726bd844ec318357e2cfad60c4fda092f569be20` |

### FIRM (firm-web service)
| Field | Value |
|-------|-------|
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/3aaf466548ff4f8f94036668c66c107f` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:2` |
| Image Digest | `sha256:78f92b8c3b2afabdcf89ceb5dd17b63249a0f2989b52c90a4ffd451bb5d7e010` |

---

## Task Definition Updates

### Step 1: FAIT Task Definition
| Field | Value |
|-------|-------|
| Old Revision | `fred-dev:62` |
| New ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:63` |
| Env vars added | `Auth__CookieDomain=.dev.fortressam.ai`, `Firm__SharedSecret=<redacted>` |
| Total env count | 25 (was 23) |

### Step 2: FIRM Task Definition
| Field | Value |
|-------|-------|
| Old Revision | `firm-web:2` |
| New ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:3` |
| Env vars added | `Auth__CookieDomain=.dev.fortressam.ai`, `Firm__SharedSecret=<redacted>`, `FIP__FaitApiUrl=https://fait.dev.fortressam.ai`, `FIP__FirmCallbackUrl=https://meetings.dev.fortressam.ai/auth/firm-session` |
| Note | Pre-existing `Auth__CookieDomain` removed and replaced with new value |
| Total env count | 18 (was 15) |

---

## CodeBuild Results

### FAIT Build
| Field | Value |
|-------|-------|
| Build ID | `fip-fait-build:77eccba0-0acd-4705-a1dc-0b770356dc15` |
| Status | ✅ **SUCCEEDED** |
| Duration | ~1.5 minutes |

### FIRM Build
| Field | Value |
|-------|-------|
| Build ID | `fip-firm-build:83419b74-9184-43dd-982c-df73a42d34b9` |
| Status | ⚠️ **FAILED** (expected — known IAM issue) |
| Failure Phase | `POST_BUILD` |
| Failure Reason | `aws ecs update-service` — AccessDeniedException in post_build (IAM permission missing for CodeBuild role on ECS) |
| Build Phase | ✅ SUCCEEDED — image built and pushed successfully |
| Resolution | Force-deployed manually in Step 6 |

---

## Service Deployments

### Step 5: FAIT Service Force Deploy
- Service: `fred-dev`
- Task definition: `fred-dev:63`
- Deployment triggered: 11:48 EDT
- Force new deployment: yes

### Step 6: FIRM Service Force Deploy
- Service: `firm-web`
- Task definition: `firm-web:3`
- Deployment triggered: 11:50 EDT
- Force new deployment: yes

---

## Post-Deploy Health Checks

### FAIT — ✅ HEALTHY
| Field | Value |
|-------|-------|
| Health Status | `HEALTHY` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:63` |
| Image Digest | `sha256:9716a4478ef1c09ee8c3d747abc2db5ab7f98be101d2cdbeb62caa3774c9c776` |
| Confirmed at | 11:50:51 EDT |

### FIRM — ✅ HEALTHY
| Field | Value |
|-------|-------|
| Health Status | `HEALTHY` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:3` |
| Image Digest | `sha256:e357d2cf76a24d923418f200108397b87f22efcd26084c18c27edb1b34a5d729` |
| Confirmed at | 11:52:45 EDT |

---

## Rollback Commands

### FAIT Rollback (to fred-dev:62)
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:62 --force-new-deployment --region us-east-1
```

### FIRM Rollback (to firm-web:2)
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition firm-web:2 --force-new-deployment --region us-east-1
```

---

## Summary

All steps completed successfully. Both FAIT and FIRM are running on new task definitions with the shared secret and cookie domain env vars injected. The FIRM CodeBuild POST_BUILD ECS failure was expected (known IAM issue) — the image built and pushed successfully, and the ECS service was force-deployed manually.

**Total deploy time:** ~9 minutes (11:44–11:53 EDT)
