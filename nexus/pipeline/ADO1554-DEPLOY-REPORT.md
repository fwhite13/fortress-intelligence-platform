# ADO#1554 — Deploy Report: nexus-web:8 (FIP Cookie Auth)

**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-04-02  
**ADO Work Item:** [#1554](https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/1554)  
**Final State:** Closed ✅

---

## Summary

Deployed nexus-web:8 — MSAL/Azure AD authentication removed, replaced with FIP shared cookie auth. Full CodeBuild + ECS deployment pipeline executed without issues.

---

## Deployment Timeline

| Time (EDT)   | Event                                      |
|--------------|--------------------------------------------|
| 12:27        | ADO#1554 comment posted — deploy start     |
| 12:27        | CodeBuild `fip-nexus-build` started        |
| 12:28        | CodeBuild SUCCEEDED (~90s)                 |
| 12:29        | nexus-web:8 task definition registered     |
| 12:29        | ECS update-service initiated               |
| 12:33        | ECS rollout COMPLETED                      |
| 12:33        | Health check → 200 ✅                      |
| 12:33        | ADO#1554 closed                            |

---

## Build

- **CodeBuild Project:** `fip-nexus-build`
- **Build ID:** `fip-nexus-build:f962ff2e-7b7b-422b-85c5-b798a91bc64d`
- **Source Version:** `f948387`
- **Full Commit SHA:** `f9483873c9ae629e349d87d6baec304b95bdf15f`
- **Result:** SUCCEEDED

---

## Container Image

```
742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:f9483873c9ae629e349d87d6baec304b95bdf15f
```

---

## Task Definition

- **Baseline:** `nexus-web:7` (commit `16acb3f`)
- **Registered:** `nexus-web:8`
- **ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:8`

### AzureAd Vars (preserved — not used, but kept for future re-enable path)
- `AzureAd__ClientId` ✅
- `AzureAd__ClientSecret` ✅
- `AzureAd__TenantId` ✅

---

## ECS Deployment

- **Cluster:** `fortress-tools-cluster`
- **Service:** `nexus-web`
- **Task Definition:** `nexus-web:8`
- **Rollout State:** COMPLETED
- **Running Count:** 1

---

## Health Check

```
GET https://nexus.fortressam.ai/health → 200 OK
```

---

## Rollback

If rollback needed:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
export AWS_DEFAULT_REGION=us-east-1
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:7 \
  --force-new-deployment \
  --region us-east-1
```

---

## Notes

- MSAL dependency fully removed from nexus-web code at commit f948387
- FIP shared cookie auth is now the sole auth mechanism
- AzureAd environment variables retained in task definition for forward compatibility
- No database migrations required
- No rollback was needed
