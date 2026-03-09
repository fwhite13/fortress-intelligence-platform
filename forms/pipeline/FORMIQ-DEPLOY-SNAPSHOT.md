# FormIQ Pre-Deploy Snapshot

**Date:** 2026-02-26 22:28 EST  
**Engineer:** War Machine (devops)  
**Purpose:** Baseline before FormIQ AWS deployment

---

## AWS Identity Verified
- ARN: `arn:aws:iam::742932328420:user/fortress-tools-deployer` ✅

---

## ALB Info
- ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:loadbalancer/app/fortress-tools-alb/fe0b167b2404ae04`
- DNS: `fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`
- Canonical Hosted Zone: `Z35SXDOTRQ7X7K`
- HTTPS Listener ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:listener/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1`

---

## ALB Listener Rules (Pre-Deploy)

| Priority | Condition | Actions |
|----------|-----------|---------|
| 1 | path `/api/*`, `/health*` | forward → fortress-tools-tg |
| 2 | host `tools.dev.fortressam.ai` | cognito-auth → forward → fortress-portal-dev |
| 3 | host `tools.dev.fortressam.ai` | cognito-auth + forward → fortress-portal-dev (duplicate) |
| default | (all) | cognito-auth → forward → fortress-tools-tg |

**Next available priority: 4** (will be used for formiq.dev.fortressam.ai)

### Existing Target Groups
- `fortress-tools-tg`: `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fortress-tools-tg/5eec7257679ebb85`
- `fortress-portal-dev`: `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fortress-portal-dev/25b78d2eec87064f`

---

## ECS Services (Pre-Deploy)
- `arn:aws:ecs:us-east-1:742932328420:service/fortress-tools-cluster/fortress-tools-portal`
- `arn:aws:ecs:us-east-1:742932328420:service/fortress-tools-cluster/fortress-portal-dev`

---

## Route53 Records for *.dev.fortressam.ai (Pre-Deploy)
- `_961625368103160e55183bdd965b9669.dev.fortressam.ai.` → CNAME (ACM validation)
- `tools.dev.fortressam.ai.` → A (ALIAS → fortress-tools-alb, Z35SXDOTRQ7X7K)

**formiq.dev.fortressam.ai does NOT exist yet** — will be created.

---

## ACM Certificate
- ARN: `arn:aws:acm:us-east-1:742932328420:certificate/6b8e0857-1cb2-4320-b93d-0513c7b79c5c`
- Domain: `*.dev.fortressam.ai` ✅ (covers formiq.dev.fortressam.ai)

---

## Cognito Client (Pre-Deploy)
- User Pool: `us-east-1_CloTcONs1`
- Client: `e3ra6bg1oqji3i1mn2e7g1o1g`
- Domain: `fortress-tools`

---

## App State (Pre-Deploy)
- App: `/home/fredw/.openclaw/workspace/fortress-form-tools/`
- DB: SQLite (`formtools.db`)
- Packages: `Microsoft.EntityFrameworkCore.Sqlite` (in both Web + Data projects)
- Builds clean, runs on port 5200

---

## Rollback Commands
```bash
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer

# Scale ECS service to 0
aws ecs update-service --cluster fortress-tools-cluster --service formiq-dev --desired-count 0

# Delete ALB rule (get ARN first)
# aws elbv2 delete-rule --rule-arn <formiq-rule-arn>

# Delete target group
# aws elbv2 delete-target-group --target-group-arn <formiq-dev-tg-arn>

# Delete Route53 record
# aws route53 change-resource-record-sets --hosted-zone-id Z003394436J64H3UMZ756 \
#   --change-batch '{"Changes":[{"Action":"DELETE","ResourceRecordSet":{"Name":"formiq.dev.fortressam.ai","Type":"A","AliasTarget":{"HostedZoneId":"Z35SXDOTRQ7X7K","DNSName":"fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com","EvaluateTargetHealth":true}}}]}'

# Remove Cognito callback URLs (restore original list)
```
