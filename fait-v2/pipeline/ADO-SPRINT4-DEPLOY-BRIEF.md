# FAIT v2 — Sprint 4 Deploy Brief

## What to Deploy
Sprint 4 code — HEAD commit `7dbe42b` on `main` branch.
Repo: `/home/fredw/projects/fip/fait-v2/`

## Pre-Deploy Snapshot
- Current task def: `fait-v2:5`
- Current image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:555b283`
- ECS service: `fait-v2` on cluster `fortress-tools-cluster`
- **Document rollback commands before deploying**

## Build Steps

### 1. Build Docker image (use Dockerfile.debian — MANDATORY)
```bash
cd /home/fredw/projects/fip/fait-v2
docker build -f src/FortressAI.V2.Web/Dockerfile.debian \
  -t fait-v2:7dbe42b \
  -t fait-v2:sprint4 \
  src/FortressAI.V2.Web/
```

### 2. Tag and push to ECR
```bash
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com

docker tag fait-v2:7dbe42b 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:7dbe42b
docker tag fait-v2:7dbe42b 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:sprint4

docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:7dbe42b
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:sprint4
```

### 3. Run EF Core migrations (NEW TABLES — MANDATORY before ECS update)

Sprint 4 adds 2 new tables:
- `artifact_records` (ADO#2859 — `20260507173056_AddArtifactRecords`)
- `feedback_submissions` (ADO#2864 — migration added by Tony)

Run migrations against `fait_v2_dev` Aurora:
```bash
cd /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web
dotnet ef database update \
  --connection "Server=fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com;Database=fait_v2_dev;User=admin;Password=$(aws secretsmanager get-secret-value --secret-id fortress-tools/aurora-admin --query SecretString --output text --profile fortress-tools-deployer --region us-east-1 | python3 -c 'import json,sys; print(json.load(sys.stdin)["password"])');GuidFormat=MySqlGuidFormat.None;AllowPublicKeyRetrieval=true;SslMode=Required;"
```

Confirm both migrations applied cleanly.

### 4. Register new ECS task definition (MANDATORY: use the wrapper script)
```bash
cd /home/fredw/projects/fip/fait-v2
./scripts/ecs-register-task-def.sh \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --image 742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:7dbe42b
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
Wait for service to stabilize (1/1 running), then verify:
```bash
curl -s https://fait-v2.dev.fortressam.ai/health
```
Expected: `{"status":"Healthy"}` or 200 OK

## Rollback Plan (document before deploying)
```bash
# Rollback to fait-v2:5
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:5 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

## Sprint 4 Changes Summary (for deploy report)
All changes are in the fait-v2 Blazor app:
- ADO#2857: ICCExecutionService + FargateCCExecutionService + CCProgressHub (CC child process orchestration)
- ADO#2858: IWorkspaceService + WorkspaceService + Workspace.razor (S3-backed file explorer)
- ADO#2859: IArtifactService + ArtifactService + ArtifactRecord + ChatView CC dispatch + progress UI
- ADO#2860: IContextEnvelopeService + ContextEnvelopeService + system CLAUDE.md + rules/
- ADO#2861: IProjectService + ProjectService + ProjectStateService + sidebar (FAIT v1 projects carry-over)
- ADO#2862: FIRM→FAIT v2 push endpoint (POST /api/agent/push-message)
- ADO#2864: FeedbackSubmission + EF migration + FeedbackModal + DispatchToJarvisAsync + /api/feedback endpoints
- New DB tables: artifact_records, feedback_submissions

## Deliverables
Write Deploy Report to: `/home/fredw/projects/fip/fait-v2/pipeline/ADO-SPRINT4-DEPLOY-REPORT.md`

Include:
- Pre-deploy snapshot (current revision/image)
- Migration results (both tables created)
- New task def revision
- Health check result
- Rollback commands
- Any issues encountered

## ADO Comments (add after deploy)
Post to each closed Sprint 4 WI that involved fait-v2 code:
```
**[War Machine — DEPLOY]**
fait-v2:7dbe42b deployed as task def fait-v2:<revision>. Health: OK. Sprint 4 complete.
```
WIs: 2857, 2858, 2859, 2860, 2861, 2862, 2864
