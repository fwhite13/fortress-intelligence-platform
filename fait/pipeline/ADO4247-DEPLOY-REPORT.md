# ADO#4247 — DEPLOY REPORT
## Grant fait-v2-task-role permission to read pgvector connection secret

**Date:** 2026-05-27  
**Deployer:** devops subagent (rhodey-ado4247)  
**Status:** ⛔ BLOCKED — IAM gap on fortress-tools-deployer

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| fred-dev task definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:288` |
| fred-dev status | ACTIVE |
| fred-dev running count | 1 |
| fred-dev desired count | 1 |
| fred-dev deployment state | PRIMARY |
| fait-v2-agent-harness service | Not found (on-demand tasks only, expected) |

---

## Step 2 — fait-v2-task-role Policy Check

**Result: AccessDenied**

`fortress-tools-deployer` does not have `iam:ListRolePolicies` permission:

```
An error occurred (AccessDenied) when calling the ListRolePolicies operation:
User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized to perform:
iam:ListRolePolicies on resource: role fait-v2-task-role
because no identity-based policy allows the iam:ListRolePolicies action
```

---

## Step 3 — Apply IAM Policy

**Result: BLOCKED — AccessDenied on all IAM mutation operations**

### Attempt 1: `iam:PutRolePolicy` (inline policy — preferred)
```
An error occurred (AccessDenied) when calling the PutRolePolicy operation:
User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized to perform:
iam:PutRolePolicy on resource: role fait-v2-task-role
because no identity-based policy allows the iam:PutRolePolicy action
```

### Attempt 2: `iam:AttachRolePolicy` (managed policy — fallback)
```
An error occurred (AccessDenied) when calling the AttachRolePolicy operation:
User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized to perform:
iam:AttachRolePolicy on resource: role fait-v2-task-role
because no identity-based policy allows the iam:AttachRolePolicy action
```

### Summary
`fortress-tools-deployer` has **no IAM permissions** — cannot list, read, or modify any role policies.

---

## Step 4 — Force New Deployment

**Skipped** — IAM policy was not applied; redeploying now would not fix the issue and would just cycle the service unnecessarily.

---

## Step 5 — Verification

**Skipped** — IAM policy not applied.

---

## ⛔ IAM Gap — Action Required

**What needs to happen:**

Rob Nethery (or whoever manages IAM for the fortress AWS account) needs to apply the following inline policy to `fait-v2-task-role`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowPgVectorSecretRead",
      "Effect": "Allow",
      "Action": "secretsmanager:GetSecretValue",
      "Resource": "arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/pgvector-connection-wx0f9F"
    }
  ]
}
```

**Policy name:** `fait-pgvector-secret-policy`  
**Role:** `fait-v2-task-role`

**AWS CLI command to apply (requires IAM admin):**
```bash
aws iam put-role-policy \
  --role-name fait-v2-task-role \
  --policy-name fait-pgvector-secret-policy \
  --policy-document '{
    "Version": "2012-10-17",
    "Statement": [
      {
        "Sid": "AllowPgVectorSecretRead",
        "Effect": "Allow",
        "Action": "secretsmanager:GetSecretValue",
        "Resource": "arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/pgvector-connection-wx0f9F"
      }
    ]
  }' \
  --profile <admin-profile>
```

**After applying, run:**
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1 \
  --profile fortress-tools-deployer
echo "fred-dev stable"
```

---

## Rollback Plan

The IAM policy change is additive and non-breaking. No rollback needed unless the force-new-deployment causes instability.

If ECS rollback is needed (after the policy IS applied and force-deploy is run):
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:288 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

To remove the IAM policy (if needed):
```bash
aws iam delete-role-policy \
  --role-name fait-v2-task-role \
  --policy-name fait-pgvector-secret-policy \
  --profile <admin-profile>
```

---

## Acceptance Criteria Status

| # | Criteria | Status |
|---|----------|--------|
| AC1 | fait-v2-task-role has secretsmanager:GetSecretValue on pgvector secret | ❌ Not applied — IAM blocked |
| AC2 | Force-new-deployment on fred-dev completed, service stable | ⏸ Skipped — pending IAM fix |
| AC3 | Harness log showing [pgvector] connected | ⏸ Pending — for Natasha in VERIFY |

---

## Recommended Next Step

Escalate to **Maria / Rob Nethery** to grant IAM permissions to `fortress-tools-deployer` OR have an IAM admin apply the policy directly via the AWS Console or with admin credentials.

Once IAM is fixed: force-new-deployment on fred-dev can be done by the devops agent with existing permissions.
