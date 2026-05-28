# Deploy Report — ADO#4545
## FIRM: JWT Bearer auth for mobile API endpoints

**Date:** 2026-05-27  
**Deployed by:** Rhodey (DevOps subagent)  
**Risk:** Medium — Auth change (JWT Bearer added). Single-image deploy.

---

## Pre-Deploy Snapshot

| Resource | Value |
|---|---|
| Pre-deploy firm-web task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:134` |
| Git HEAD before deploy | `d6f1442d` (already at expected commit) |

---

## What Was Built

**Commit:** `d6f1442d`

Changes included in this deploy:
- `Program.cs`: `AddJwtBearer("Bearer")` with Entra v2.0 authority + `api://{ClientId}` audience; `CookieOrBearer` named policy; `DefaultScheme` stays cookie
- `MeetingsApiController.cs`: 4 mobile endpoints swapped to `[Authorize(Policy = "CookieOrBearer")]`
- `FortressIntelligenceRM.Web.csproj`: `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.*` added
- Build: 0 errors (pre-existing warnings only)

---

## Docker Build

| | |
|---|---|
| Dockerfile | `firm/Dockerfile.debian` |
| Image tag | `firm-web:d6f1442d` |
| Build flags | `--no-cache` |
| Build result | **SUCCESS** (exit 0) |
| Build log | `/tmp/ado4545-firm-build.log` |
| Image digest | `sha256:f310b9f775614db4f9fc50961495b9052816f95fad32970991e814016d3cc60e` |

---

## ECR Push

| | |
|---|---|
| Repository | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web` |
| Tag pushed | `d6f1442d` |
| Push result | **SUCCESS** |
| Digest | `sha256:f310b9f775614db4f9fc50961495b9052816f95fad32970991e814016d3cc60e` |

---

## ECS Deployment

| | |
|---|---|
| Cluster | `fortress-tools-cluster` |
| Service | `firm-web` |
| Pre-deploy task def | `firm-web:134` |
| New task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:135` |
| Force new deployment | Yes |
| ECS stable | **YES** (`aws ecs wait services-stable` exited 0) |

---

## Rollback Command

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:134 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## ADO Status

ADO#4545 → **Resolved** with deploy comment.

---

## Notes

- No infra changes. No new env vars required.
- `AzureAd:TenantId` + `AzureAd:ClientId` were already present in the FIRM ECS task def.
- Existing Blazor UI cookie auth routes unaffected.
- Mobile app can now authenticate with Entra Bearer tokens against 4 MeetingsApiController endpoints.
