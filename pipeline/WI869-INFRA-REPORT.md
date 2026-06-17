# Infra Report: WI869 — FAM OS Sprint 1
## Status: PARTIAL

**Provisioned by:** War Machine (Rhodey) — `devops` agent  
**Date:** 2026-03-18  
**Parallel with:** Tony Stark BUILD (WI869)

---

| Item | Status | Details |
|------|--------|---------|
| ECR repo `famos-web` | ✅ | URI: `742932328420.dkr.ecr.us-east-1.amazonaws.com/famos-web` (scan-on-push enabled) |
| Aurora DB `famos_dev` | ✅ | Created on `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com` — utf8mb4 |
| CloudWatch log group `/famos/tasks` | ✅ | Created, retention: 90 days |
| ECS task def `famos-dev:1` | ✅ | ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/famos-dev:1` — 512 CPU / 1024 MB, FORTRESS_DB_PASS via Secrets Manager ref |
| Target group `famos-dev-tg` | ✅ | ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/famos-dev-tg/606bcf5a16f383d0` |
| ECS service `famos-dev` | ✅ | Status: ACTIVE — cluster `fortress-tools-cluster`, desired count 1, task def `famos-dev:1` |
| ALB listener rule (`famos.dev.fortressam.ai`) | ✅ | Priority: 9 — Rule ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:listener-rule/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1/df50e62f302d42e9` |
| Route53 CNAME | ✅ | `famos.dev.fortressam.ai` → `fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com` (Status: PENDING propagation) |
| CodeBuild project `fip-famos-build` | ❌ | **BLOCKED**: `fortress-tools-deployer` IAM user lacks `codebuild:CreateProject` and `codebuild:BatchGetProjects` permissions. Needs admin to either (a) grant CodeBuild perms to deployer, or (b) manually create the project. |

---

## Notes

### Task Definition — Secrets Approach
FORTRESS_DB_PASS is injected via Secrets Manager reference (not plaintext), matching the pattern used by firm-web. Secret ARN:  
`arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/dev-db-password-9ZKFmr`

### ECS Service — Placeholder State
The ECS service is ACTIVE but the placeholder image (`famos-web:latest`) doesn't exist in ECR yet — tasks will fail to start until Tony's build completes and CodeBuild pushes the first image. This is expected for parallel infra provisioning.

### CodeBuild Blocker
`fortress-tools-deployer` has no CodeBuild permissions at all. Options:
1. Fred/admin adds `codebuild:CreateProject`, `codebuild:BatchGetProjects`, `codebuild:StartBuild` to the deployer policy
2. Admin manually creates `fip-famos-build` project following the same pattern as `fip-forms-build` (once perms on that are confirmed)

### Infrastructure Ready For
Once Tony commits and CodeBuild project is created:
1. Run first build: `aws codebuild start-build --project-name fip-famos-build`
2. First successful build will push `famos-web:latest` to ECR
3. ECS service will stabilize and tasks will start
4. `https://famos.dev.fortressam.ai` will be live

---

## Infrastructure Summary
- **VPC:** `vpc-0783a9844741980ff`
- **Subnets:** `subnet-08e1d4f1b5530f39e`, `subnet-051bfcf5b07661809`
- **Security Group:** `sg-0fb53615b1eb4a175`
- **ALB:** `fortress-tools-alb` (`fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com`)
- **Execution Role:** `arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role`
- **DB Host:** `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com`
- **DB Name:** `famos_dev` (charset utf8mb4)
