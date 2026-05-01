# ADO#2627 — fip-mcp Deploy Report
**Date:** 2026-05-01  
**Agent:** War Machine (Rhodey)  
**Service:** fip-mcp — FORGE KB MCP Server Phase 0  
**Status:** ✅ DEPLOYED — ⚠️ IAM task role pending Fred action

---

## Resources Created

| Resource | ARN / Value |
|----------|-------------|
| ECR image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp:76ec38f` |
| ECR digest | `sha256:82b9391b8ef48fba867780f35a589295c1eb43b34e20ffbf3ba7d8544ab6fd44` |
| ECR tag (latest) | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp:latest` |
| Task definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/fip-mcp:1` |
| Target group | `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fip-mcp-tg/2bd099cec13fac47` |
| ALB rule | `arn:aws:elasticloadbalancing:us-east-1:742932328420:listener-rule/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1/13bffab92578167e` |
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fortress-tools-cluster/fip-mcp` |
| CloudWatch log group | `/ecs/fip-mcp` |
| Execution role | `arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role` |

---

## ALB Configuration

- **Listener:** `arn:aws:elasticloadbalancing:us-east-1:742932328420:listener/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1`
- **Priority:** 18
- **Conditions:** `Host=api.fortressam.ai` AND `Path=/mcp OR /mcp/*`
- **Action:** Forward → `fip-mcp-tg`

---

## ECS Service Configuration

- **Launch type:** FARGATE
- **CPU:** 256 (0.25 vCPU)
- **Memory:** 512 MB
- **Subnets:** `subnet-08e1d4f1b5530f39e`, `subnet-051bfcf5b07661809`
- **Security group:** `sg-0fb53615b1eb4a175`
- **Desired count:** 1
- **Running count:** 1 (stable at ~45s)

---

## Task Definition (fip-mcp:1) — Environment Variables

| Variable | Value |
|----------|-------|
| `NODE_ENV` | `production` |
| `PORT` | `3000` |
| `LOG_LEVEL` | `info` |
| `BEDROCK_REGION` | `us-east-1` |
| `ENTRA_TENANT_ID` | `7152ea12-c930-44b0-bb52-069152161c5b` |
| `ENTRA_CLIENT_ID` | `eda4d502-8c93-422e-b7fb-bb922a2a472e` |
| `FALLBACK_ENTITLEMENTS_CONFIG` | `/app/src/config/entitlements.json` |

---

## ⚠️ IAM Blocker — Fred Action Required

`fortress-tools-deployer` lacks `iam:CreateRole` and `iam:PassRole` scoped to `fip-mcp-task-role`.  
The task definition was registered **without a taskRoleArn** as a result.

**Fred must run (from any admin profile):**
```bash
aws iam create-role \
  --role-name fip-mcp-task-role \
  --assume-role-policy-document '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"Service":"ecs-tasks.amazonaws.com"},"Action":"sts:AssumeRole"}]}'

aws iam attach-role-policy \
  --role-name fip-mcp-task-role \
  --policy-arn arn:aws:iam::742932328420:policy/FipMcpBedrockAccess
```

**Then Rhodey registers task def :2 and updates the service:**
```bash
# After Fred confirms role is created:
# 1. Edit /tmp/fip-mcp-task-def.json to add taskRoleArn
# 2. aws ecs register-task-definition --cli-input-json file:///tmp/fip-mcp-task-def.json --profile fortress-tools-deployer --region us-east-1
# 3. aws ecs update-service --cluster fortress-tools-cluster --service fip-mcp --task-definition fip-mcp:2 --profile fortress-tools-deployer --region us-east-1
```

**Impact:** `/health` returns 200 ✅. Bedrock tools (`search_kb`, `list_kbs`, `add_to_kb`, `get_kb_metadata`, `get_job_status`) will return 500 until the task role is attached.

---

## Health Check

**CloudWatch logs (startup):**
```
[fip-mcp] FORGE KB MCP Server v1.0.0 listening on port 3000
[fip-mcp] Entra tenant: 7152ea12-c930-44b0-bb52-069152161c5b
[fip-mcp] Entra client: eda4d502-8c93-422e-b7fb-bb922a2a472e
[fip-mcp] Bedrock region: us-east-1
[fip-mcp] Entitlements config: /app/src/config/entitlements.json
```

**Service stabilization:** 1/1 running in ~45 seconds. No crash restarts.

---

## Rollback Procedure

New service — no previous version. If rollback needed:
```bash
# Scale down to 0 (stops tasks, preserves service config)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fip-mcp \
  --desired-count 0 \
  --profile fortress-tools-deployer \
  --region us-east-1

# Full teardown (if needed)
aws ecs delete-service --cluster fortress-tools-cluster --service fip-mcp --force --profile fortress-tools-deployer --region us-east-1
aws elbv2 delete-rule --rule-arn arn:aws:elasticloadbalancing:us-east-1:742932328420:listener-rule/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1/13bffab92578167e --profile fortress-tools-deployer --region us-east-1
aws elbv2 delete-target-group --target-group-arn arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fip-mcp-tg/2bd099cec13fac47 --profile fortress-tools-deployer --region us-east-1
```

---

## Commits Deployed

| SHA | Message |
|-----|---------|
| `76ec38f` | fix(ADO#2627): non-root user in Dockerfile + engines field in package.json |
| `11aab1f` | feat(ADO#2627): fip-mcp FORGE KB MCP Server Phase 0 — 5 tools, Entra auth, fallback entitlements |

---

## Build Note

Dockerfile context must be `services/fip-mcp/` (not monorepo root).  
The Dockerfile copies `src/` with no monorepo-level prefix — service-scoped build context only.
