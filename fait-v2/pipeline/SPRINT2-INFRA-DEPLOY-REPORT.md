# SPRINT 2 INFRA DEPLOY REPORT
**Agent:** Rhodey (War Machine) — DevOps  
**Date:** 2026-05-07  
**ADO Epic:** #2835  
**Status:** 🔴 BLOCKED — AccessDenied on Step 3

---

## Pre-Deploy Snapshot

New service — no previous ECS service state. This is a net-new infrastructure setup for FAIT v2.

---

## Step Results

| Step | Action | Status | Notes |
|------|--------|--------|-------|
| 1 | Route53 CNAME `fait-v2.dev.fortressam.ai` | ✅ DONE | Change ID: `C00358051P4OHBJNA5KNT`, Status: PENDING (DNS propagating) |
| 2 | Target group health check path → `/health` | ✅ DONE | Interval 30s, timeout 5s, thresholds 2/3 |
| 3 | Register ECS task definition `fait-v2` | 🔴 BLOCKED | `AccessDeniedException: iam:PassRole` on `fait-v2-task-role` |
| 4 | Create CloudWatch log group `/ecs/fait-v2` | ⏭️ SKIPPED | Depends on Step 3 |
| 5 | Create ECS service `fait-v2` | ⏭️ SKIPPED | Depends on Step 3 |
| 6 | Service stability / health check | ⏭️ SKIPPED | Depends on Steps 3–5 |

---

## Step 1 — Route53 CNAME ✅

**Command:** `aws route53 change-resource-record-sets`  
**Record:** `fait-v2.dev.fortressam.ai` → `fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`  
**TTL:** 300  
**Result:**
```json
{
    "ChangeInfo": {
        "Id": "/change/C00358051P4OHBJNA5KNT",
        "Status": "PENDING",
        "SubmittedAt": "2026-05-07T12:59:52.477000+00:00",
        "Comment": "FAIT v2 dev CNAME"
    }
}
```

---

## Step 2 — Target Group Health Check ✅

**Target Group ARN:** `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c`  
**Health check path:** `/health` (was `/`)  
**Confirmed settings:** interval=30s, timeout=5s, healthy=2, unhealthy=3  
**Result:** HTTP 200 response from `modify-target-group`, target group confirmed updated.

---

## Step 3 — ECS Task Definition 🔴 BLOCKED

**Exact error:**
```
An error occurred (AccessDeniedException) when calling the RegisterTaskDefinition operation: 
User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized to perform: 
iam:PassRole on resource: arn:aws:iam::742932328420:role/fait-v2-task-role 
because no identity-based policy allows the iam:PassRole action
```

**Root cause:** `fortress-tools-deployer` IAM user is missing an `iam:PassRole` policy allowing it to pass `fait-v2-task-role` when registering a task definition.

**Fix required (Fred must apply):**

Option A — Add inline policy to `fortress-tools-deployer`:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "iam:PassRole",
      "Resource": [
        "arn:aws:iam::742932328420:role/fait-v2-task-role",
        "arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role"
      ],
      "Condition": {
        "StringEquals": {
          "iam:PassedToService": "ecs-tasks.amazonaws.com"
        }
      }
    }
  ]
}
```

Option B — Attach the existing `fortress-tools-deployer` PassRole policy (if it covers ECS execution role already, just add `fait-v2-task-role` to its resource list).

---

## Task Definition ARN

**Not registered** — blocked by AccessDenied.

---

## ECS Service ARN

**Not created** — depends on task definition registration.

---

## Target Health Status

**Not available** — service not yet created.

---

## Health Check Result

**Not available** — service not yet created.

Route53 CNAME is live (pending DNS TTL propagation). ALB routing rule for `fait-v2.dev.fortressam.ai` was confirmed to already exist (pre-existing per brief).

---

## Rollback Plan

Steps 1 and 2 are safe — no rollback needed for DNS/target group changes.

If needed, DNS rollback:
```bash
aws route53 change-resource-record-sets \
  --hosted-zone-id Z003394436J64H3UMZ756 \
  --profile fortress-tools-deployer \
  --change-batch '{
    "Changes": [{
      "Action": "DELETE",
      "ResourceRecordSet": {
        "Name": "fait-v2.dev.fortressam.ai",
        "Type": "CNAME",
        "TTL": 300,
        "ResourceRecords": [{"Value": "fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com"}]
      }
    }]
  }'
```

Target group health check path rollback (revert to `/`):
```bash
aws elbv2 modify-target-group \
  --target-group-arn "arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c" \
  --health-check-path "/" \
  --profile fortress-tools-deployer --region us-east-1
```

Once `iam:PassRole` is granted, resume from Step 3:
```bash
# Step 3: Register task def (re-run the register-task-definition command from the brief)
# Step 4: Create log group
aws logs create-log-group --log-group-name "/ecs/fait-v2" --profile fortress-tools-deployer --region us-east-1
# Step 5: Create ECS service (re-run create-service command from the brief)
# Step 6: Wait for stability and health check
```

---

## Summary

**2 of 6 steps complete. Blocked on IAM permissions.**

Fred needs to grant `fortress-tools-deployer` the `iam:PassRole` permission for `fait-v2-task-role` (and optionally `fortress-tools-ecs-execution-role`). Once that's in place, Steps 3–6 can run immediately — no other blockers anticipated.
