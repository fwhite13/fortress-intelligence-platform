# DEPLOY Brief: ADO#2899 — fip-mcp routing refactor

**ADO WI:** #2899 (Fortress project)
**Commit:** `8f247b9d668141a108b2dd5ac94cd1bd6b9cdd92`
**Review:** PASS (Hawkeye C1) ✅
**Deploy target:** fip-mcp on ECS (fortress-tools-cluster)
**Current version:** fip-mcp:8
**New version:** fip-mcp:9

---

## Deploy Type

Docker image rebuild + ECS task def update. This is a pure routing refactor — no new env vars, no schema changes, no IAM changes needed. Straightforward image push.

---

## Pre-Deploy

Capture current state:
```bash
aws ecs describe-services \
  --cluster fortress-tools-cluster \
  --services fip-mcp \
  --profile fortress-tools-deployer \
  --region us-east-1 \
  --query 'services[0].{taskDef:taskDefinition,running:runningCount,desired:desiredCount}'
```

---

## Build & Push

**fip-mcp builds from its subdirectory (NOT monorepo root):**
```bash
cd /home/fredw/projects/fip/services/fip-mcp

# Login to ECR
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com

# Build
docker build -t fip-mcp:latest .

# Tag and push
docker tag fip-mcp:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp:latest
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp:latest
```

---

## Register Task Def

```bash
bash /home/fredw/projects/fip/scripts/ecs-register-task-def.sh fip-mcp
```

This script auto-inherits taskRoleArn — do NOT use raw `aws ecs register-task-definition`.

---

## Deploy

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fip-mcp \
  --task-definition fip-mcp \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

Wait for service to stabilize (running count = desired count).

---

## Post-Deploy Health Checks

```bash
# Health check — /mcp/health (existing)
curl -s https://api.fortressam.ai/mcp/health

# New path health checks
curl -s https://api.fortressam.ai/mcp/ms365/health
curl -s https://api.fortressam.ai/mcp/ado/health
curl -s https://api.fortressam.ai/mcp/web/health
```

All four should return `{"status":"ok","version":"1.0.0"}` (or similar with server name).

---

## Rollback Plan

```bash
# Get previous task def revision
PREV_TASK_DEF=$(aws ecs describe-services \
  --cluster fortress-tools-cluster \
  --services fip-mcp \
  --profile fortress-tools-deployer \
  --region us-east-1 \
  --query 'services[0].taskDefinition' --output text)

# Rollback to fip-mcp:8
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fip-mcp \
  --task-definition fip-mcp:8 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## ADO Tracking (MANDATORY)

After deploy complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2899,
  "text": "**[War Machine — DEPLOY]**\nfip-mcp:{revision} deployed. Commit 8f247b9. Health checks: /mcp/health ✅, /mcp/ms365/health ✅, /mcp/ado/health ✅, /mcp/web/health ✅. ECS service healthy."
}'
```

---

## Deliverables

1. Deploy Report: `/home/fredw/projects/fip/services/fip-mcp/pipeline/ADO2899-DEPLOY-REPORT.md`
2. Report back with: task def revision, health check results, rollback commands
