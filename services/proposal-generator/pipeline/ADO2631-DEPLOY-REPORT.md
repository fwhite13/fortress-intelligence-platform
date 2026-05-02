# Deploy Report: ADO#2631
## Status: SUCCEEDED

## Pre-Deploy Snapshot
- Task def: `arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:25`
- Image: `fip-proposal-generator:1db791c`

## Deployment
- New image: `fip-proposal-generator:de138c5`
- New task def: `arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:26`
- ECS running/desired: 1/1

## Health Check
- `/health`: 200 ✅

## Rollback Plan
```bash
aws ecs update-service --cluster fortress-tools-cluster --service proposal-generator-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:25 \
  --force-new-deployment --profile fortress-tools-deployer --region us-east-1
```

## Notes
- Build used `--no-cache` per ADO#2593 incident policy
- Both `:de138c5` and `:latest` tags pushed to ECR; task def pinned to SHA tag only
- S3 templates pre-synced by Tony (cycles 1 & 2) — no S3 step required
- Commit: `de138c5` (cycle 2 fix on top of `35e25ca`)
- Deployed by: War Machine (Rhodey) — 2026-05-01
