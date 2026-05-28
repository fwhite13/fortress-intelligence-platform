# ADO#4246 — CloudFront Signed URLs — IAM Fix Redeploy Report

**Date:** 2026-05-28  
**Operator:** DevOps Agent (rhodey-ado4246-iam-redeploy)  
**Service:** fred-dev (ECS Fargate, fortress-tools-cluster)

---

## IAM Policy Confirmed

**Role:** `fortress-tools-ecs-task-role`  
**Policy:** `AllowCloudFrontSigningKeyRead` (inline)  
**Effect:** Allow  
**Action:** `secretsmanager:GetSecretValue`  
**Resource:** `arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/cloudfront-signing-key*`  

Policy verified present on role before deployment proceeded. ✅

**Note:** A pre-existing policy `fait-cloudfront-secrets-policy` covers `arn:aws:secretsmanager:us-east-1:742932328420:secret:fait/cloudfront/*` — different ARN prefix, not the signing key. The new policy correctly targets `fortress-tools/cloudfront-signing-key*`.

---

## Deployment

**Operation:** Force-new-deployment (no code change, no new task definition)  
**Task Definition (rollback ref):** `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:291`  
**Force-deploy issued:** ~2026-05-28 01:52 UTC  
**New task startup:** 2026-05-28 01:53:13 UTC  
**ECS stable confirmed:** 2026-05-28 01:54:50 UTC ✅

---

## CloudWatch Log Evidence

### Previous task (OLD — before IAM fix propagated)
- **Timestamp:** 2026-05-28 01:35:49 UTC (before our force-deploy)
- **Result:** ❌ FAILED
  ```
  Failed to load CloudFront private key from Secrets Manager secret 'fortress-tools/cloudfront-signing-key'
  Amazon.SecretsManager.AmazonSecretsManagerException: User: arn:aws:sts::742932328420:assumed-role/fortress-tools-ecs-task-role/fd1ff02d42774aba95bae81158ebb892
  is not authorized to perform: secretsmanager:GetSecretValue on resource: fortress-tools/cloudfront-signing-key
  because no identity-based policy allows the secretsmanager:GetSecretValue action
  ```

### New task (CURRENT — after IAM fix, started 01:53:13 UTC)
- **CloudFront key error:** ❌ Not present in startup logs
- **CloudFront key success:** ⚠️ Not explicitly logged at startup (key load appears to be lazy/deferred — triggered on first signed-URL request, not at application startup)
- **App status:** ✅ Started successfully — `Application started. Press Ctrl+C to shut down.`
- **DB init:** ✅ Clean — `Database initialization complete`

**Assessment:** No error = positive signal. The IAM policy is active on the running task. The expected success log `CloudFront private key loaded from Secrets Manager secret 'fortress-tools/cloudfront-signing-key'` will appear on first actual use of a signed URL. Functional verification via browser/API is recommended to confirm end-to-end.

---

## Rollback

If needed:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster --service fred-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:291 \
  --region us-east-1 --profile fortress-tools-deployer
```
(Same task def — rollback would just be another force-new-deploy; IAM fix was applied separately.)

---

## Summary

| Check | Result |
|-------|--------|
| IAM policy `AllowCloudFrontSigningKeyRead` on role | ✅ Confirmed |
| Covers correct secret ARN | ✅ `fortress-tools/cloudfront-signing-key*` |
| Force-new-deployment issued | ✅ |
| ECS service stable | ✅ 01:54:50 UTC |
| Old task error gone in new task logs | ✅ No error in new task |
| Success log present | ⚠️ Deferred — not logged at startup |
| Functional test needed | ✅ Recommended |

