# FAIT v2 — Sprint 5 Deploy Brief

## What to Deploy
Sprint 5 code — HEAD commit `987a94f` on `main` branch.
Repo: `/home/fredw/projects/fip/fait-v2/`

## Pre-Deploy Snapshot
- Current task def: `fait-v2:6`
- Current image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:7dbe42b`
- ECS service: `fait-v2` on cluster `fortress-tools-cluster`
- **Document rollback commands before deploying**

## Build Steps

### 1. Build Docker image (Dockerfile.debian — MANDATORY)
```bash
cd /home/fredw/projects/fip/fait-v2
docker build -f src/FortressAI.V2.Web/Dockerfile.debian \
  -t fait-v2:987a94f \
  -t fait-v2:sprint5 \
  src/FortressAI.V2.Web/
```

### 2. Tag and push to ECR
```bash
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com

docker tag fait-v2:987a94f 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:987a94f
docker tag fait-v2:987a94f 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:sprint5

docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:987a94f
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:sprint5
```

### 3. Run EF Core migrations (NEW TABLES — MANDATORY before ECS update)

Sprint 5 adds 3 new migrations:
- `AddScheduledTasks` (#2877) — `scheduled_tasks`, `scheduled_task_runs`
- `AddAgentPlugins` (#2879) — `agent_plugins`
- `SeedInitialAgentPlugins` (#2880) — seeds Marketing, Finance, Legal rows

Get DB password:
```bash
DB_PASS=$(aws secretsmanager get-secret-value \
  --secret-id fortress-tools/dev-db-password \
  --query SecretString --output text \
  --profile fortress-tools-deployer --region us-east-1 | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("password", list(d.values())[0]))')
```

Apply migrations via mysql client (same pattern as Sprint 4):
```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web

# Option A: dotnet ef (if password has no special chars that break shell)
dotnet ef database update \
  --connection "Server=fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com;Database=fait_v2_dev;User=admin;Password=$DB_PASS;GuidFormat=MySqlGuidFormat.None;AllowPublicKeyRetrieval=true;SslMode=Required;"

# Option B: if dotnet ef fails, apply migration SQL directly via mysql client
# Generate scripts first:
dotnet ef migrations script --idempotent --output /tmp/sprint5-migrations.sql
mysql -h fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com \
  -u admin -p"$DB_PASS" fait_v2_dev < /tmp/sprint5-migrations.sql
```

Confirm tables created: `scheduled_tasks`, `scheduled_task_runs`, `agent_plugins`
Confirm seed rows: 3 rows in `agent_plugins` (Marketing, Finance, Legal)

### 4. Register new ECS task definition (MANDATORY: use wrapper script)
```bash
cd /home/fredw/projects/fip/fait-v2
./scripts/ecs-register-task-def.sh \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --image 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:987a94f
```

### 5. Update ECS service
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:<new-revision> \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

### 6. Health check
Wait for 1/1 running, then verify:
```bash
# Direct via ALB (DNS may not resolve from this host)
ALB="fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com"
curl -sk -H "Host: fait-v2.dev.fortressam.ai" http://$ALB/health
```
Expected: 200 OK

## Rollback Plan (document before deploying)
```bash
# Rollback to fait-v2:6
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:6 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

## Sprint 5 Changes Summary
- #2877: ScheduledTask + ScheduledTaskRun models, EF migration, IScheduledTaskService, ScheduledTaskBackgroundService (60s poll, distributed CAS lock, Cronos)
- #2878: Tasks.razor (/tasks route, 3-tab), TaskEditDialog, ConfirmDialog, Dashboard widget, sidebar nav
- #2879: AgentPlugin model, IPluginAgentService, plugin-aware ContextEnvelopeService, plugin selector in ChatView
- #2880: marketing.md + finance.md + legal.md skills files, SeedInitialAgentPlugins migration, PluginAgentService wwwroot reader

## Deliverables
Write Deploy Report to: `/home/fredw/projects/fip/fait-v2/pipeline/ADO-SPRINT5-DEPLOY-REPORT.md`

Include: pre-deploy snapshot, migration results (3 tables + 3 seed rows), new task def revision, health check result, rollback commands.

## ADO Comments (add after deploy)
Post to WIs 2877, 2878, 2879, 2880 (project: Fortress):
```
**[War Machine — DEPLOY]**
fait-v2:987a94f deployed as task def fait-v2:<revision>. Migrations applied (scheduled_tasks, scheduled_task_runs, agent_plugins + 3 seed rows). Health: OK.
```
