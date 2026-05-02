# DEPLOY Assignment: ADO#2631
## Proposal Generator: NBAIS WC Template Fidelity Pass

---

## Context
- **ADO WI:** #2631 (Legacy Work project)
- **Commit:** `de138c5` (latest — cycle 2 fix on top of `35e25ca`)
- **Service:** `proposal-generator-dev` on ECS cluster `fortress-tools-cluster`
- **ECR repo:** `fip-proposal-generator`
- **AWS profile:** `fortress-tools-deployer`
- **Region:** `us-east-1`
- **Monorepo root:** `/home/fredw/projects/fip/`
- **Dockerfile:** `services/proposal-generator/Dockerfile` — build context is monorepo root

---

## Pre-Deploy Snapshot (capture BEFORE doing anything)

```bash
# Current task def revision
aws ecs describe-services --cluster fortress-tools-cluster \
  --services proposal-generator-dev \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'services[0].taskDefinition'

# Current running image
aws ecs describe-task-definition \
  --task-definition $(aws ecs describe-services --cluster fortress-tools-cluster \
    --services proposal-generator-dev --profile fortress-tools-deployer --region us-east-1 \
    --query 'services[0].taskDefinition' --output text) \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition.containerDefinitions[0].image'
```

Document the current task def revision and image tag before proceeding.

---

## ADO Comment — Pre-Deploy (POST THIS BEFORE DEPLOYING)

```bash
mcporter call devops.add_comment project="Legacy Work" id=2631 text="**[War Machine — DEPLOY pre-flight]**
Pre-deploy snapshot: task def {current_revision}, image {current_image_tag}. Starting Docker build for commit de138c5."
```

---

## Deploy Steps

### Step 1 — Docker Build & Push
```bash
cd /home/fredw/projects/fip

# Get commit SHA for image tag
COMMIT_SHA=$(git -C /home/fredw/projects/fip rev-parse --short HEAD)
echo "Commit SHA: $COMMIT_SHA"

# ECR login
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin \
  742932328420.dkr.ecr.us-east-1.amazonaws.com

# Build -- ALWAYS --no-cache (stale ECR layer incident in ADO#2593)
docker build --no-cache \
  -t 742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:${COMMIT_SHA} \
  -t 742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:latest \
  -f services/proposal-generator/Dockerfile .

# Push both tags
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:${COMMIT_SHA}
docker push 742932328020.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:latest
```

### Step 2 — Register New Task Definition
```bash
# Get current task def as baseline (use revision :3+ — never :1)
CURRENT_TD=$(aws ecs describe-services --cluster fortress-tools-cluster \
  --services proposal-generator-dev --profile fortress-tools-deployer --region us-east-1 \
  --query 'services[0].taskDefinition' --output text)

aws ecs describe-task-definition \
  --task-definition $CURRENT_TD \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition' > /tmp/pg-td.json

# Update image to new SHA tag (NOT :latest)
COMMIT_SHA=$(git -C /home/fredw/projects/fip rev-parse --short HEAD)
python3 -c "
import json, sys
with open('/tmp/pg-td.json') as f:
    td = json.load(f)
td['containerDefinitions'][0]['image'] = f'742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:{sys.argv[1]}'
for key in ['taskDefinitionArn','revision','status','requiresAttributes','compatibilities','registeredAt','registeredBy']:
    td.pop(key, None)
with open('/tmp/pg-td-new.json', 'w') as f:
    json.dump(td, f, indent=2)
print('Updated image to:', td['containerDefinitions'][0]['image'])
" $COMMIT_SHA

# Register new revision
aws ecs register-task-definition \
  --cli-input-json file:///tmp/pg-td-new.json \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition.taskDefinitionArn'
```

### Step 3 — Update ECS Service
```bash
NEW_TD_ARN=$(aws ecs register-task-definition \
  --cli-input-json file:///tmp/pg-td-new.json \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'taskDefinition.taskDefinitionArn' --output text)

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition $NEW_TD_ARN \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

### Step 4 — Health Check
Wait for the new task to reach RUNNING, then verify:
```bash
# Check service desired/running/pending counts
aws ecs describe-services --cluster fortress-tools-cluster \
  --services proposal-generator-dev \
  --profile fortress-tools-deployer --region us-east-1 \
  --query 'services[0].{desired:desiredCount,running:runningCount,pending:pendingCount,taskDef:taskDefinition}'

# Health check via ALB (DNS unreliable from WSL2 — use ALB direct with Host header)
curl -s -o /dev/null -w "%{http_code}" \
  -H "Host: proposal-generator.dev.fortressam.ai" \
  https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/health \
  --insecure
# Expected: 200
```

---

## Rollback Plan (document BEFORE deploying)

```bash
# Rollback = update service back to previous task def revision
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition {PREVIOUS_TASK_DEF_ARN} \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

Fill in `{PREVIOUS_TASK_DEF_ARN}` from your pre-deploy snapshot.

---

## S3 Note
Template already synced to S3 by Tony (both cycles). No additional S3 step needed.

---

## ADO Comment — Post-Deploy (POST AFTER HEALTH CONFIRMED)

```bash
mcporter call devops.add_comment project="Legacy Work" id=2631 text="**[War Machine — DEPLOY complete]**
Docker build: SUCCEEDED. Image: fip-proposal-generator:{commit_sha}. Task def: {new_revision}. ECS health: RUNNING {running}/{desired}. /health: 200. Rollback target: {previous_revision}."
```

---

## Deliverables
1. Deploy Report saved to `services/proposal-generator/pipeline/ADO2631-DEPLOY-REPORT.md`
2. Both ADO comments posted (pre-deploy snapshot + post-deploy health)
3. Service healthy at `/health` → 200

### Deploy Report Format
```markdown
# Deploy Report: ADO#2631
## Status: SUCCEEDED / FAILED
## Pre-Deploy Snapshot
- Task def: {revision}
- Image: {tag}
## Deployment
- New image: fip-proposal-generator:{commit_sha}
- New task def: {arn}
- ECS running/desired: {n}/{n}
## Health Check
- /health: 200 ✅
## Rollback Plan
aws ecs update-service --cluster fortress-tools-cluster --service proposal-generator-dev --task-definition {prev_arn} --force-new-deployment --profile fortress-tools-deployer --region us-east-1
## Notes
```
