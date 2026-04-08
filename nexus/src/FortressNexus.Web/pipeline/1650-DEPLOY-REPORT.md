# Deploy Report — NEXUS Phase 3: SubmissionDetail Draft UI
## WIs: #1650, #1651, #1652, #1658

**Date:** 2026-04-08  
**Deployed by:** War Machine (devops subagent)  
**App:** nexus-web  
**Cluster:** fortress-tools-cluster  

---

## Summary

Deployed the SubmissionDetail Draft UI batch to nexus-web successfully. No schema changes.

---

## What Was Deployed

- `SubmissionDetail.razor` — Continue CTA, Delete button + server-side guard, Version History accordion, Discovery history toggle
- `SubmissionService.cs` — `DeleteSubmissionAsync` with ownership guard + 3-phase delete
- `ISubmissionService.cs` — updated interface
- `DiscoveryService.cs` — `GetAllSessionsAsync`
- `IDiscoveryService.cs` — updated interface

---

## Steps

### 1. Pre-Deploy State
- **Service:** `nexus-web` — ACTIVE, 1/1 running
- **Task Def:** `nexus-web:23` — single PRIMARY deployment (no in-flight)

### 2. CodeBuild
- **Project:** `fip-nexus-build`
- **Build ID:** `fip-nexus-build:714c5248-c4ad-43e1-b6a7-70c504a3693d`
- **Build #:** 23
- **Started:** 2026-04-08 15:49:24 EDT
- **Completed:** 2026-04-08 ~15:51 EDT (~1m 30s)
- **Status:** ✅ SUCCEEDED

### 3. New Image
- **ECR Repo:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web`
- **Commit Tag:** `01934922d60406c6fa1dbd3290c2e391fcef68dc`
- **Also tagged:** `latest`
- **Digest:** `sha256:c3d99a9fc25aef1af3ab7f01308fd9f0ab2f5cae40356bcfef2d1fcb0ae8bb39`
- **Pushed at:** 2026-04-08 15:50:44 EDT

### 4. Task Definition
- **Previous:** `nexus-web:23`
- **New:** `nexus-web:24`
- **Registered by:** fortress-tools-deployer

### 5. ECS Deploy
- **Command:** `update-service --task-definition nexus-web:24 --force-new-deployment`
- **Steady state:** ✅ Reached (aws ecs wait services-stable exit 0)
- **Running:** 1/1

### 6. Health Check
- **URL:** `https://nexus.fortressam.ai/`
- **HTTP Status:** ✅ 403 (auth-gated — expected, confirms app is live)

### 7. CloudWatch Logs (`/ecs/nexus-web`)
- **Stream:** `ecs/nexus-web/cd062e800a8e46f9b80ed31ff1bb6876`
- EF Core migrations ran and completed ✅
- No exceptions, no errors ✅
- WRN `Overriding HTTP_PORTS` — pre-existing cosmetic warning, not a regression

---

## Rollback

If rollback required:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:23 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Result

| Step | Status |
|------|--------|
| Pre-deploy check | ✅ Clean |
| CodeBuild | ✅ SUCCEEDED |
| ECR image push | ✅ `nexus-web:01934922` |
| Task def registration | ✅ `nexus-web:24` |
| ECS deployment | ✅ Stable |
| Health check | ✅ 403 |
| CloudWatch | ✅ Clean |

**Deploy: COMPLETE ✅**
