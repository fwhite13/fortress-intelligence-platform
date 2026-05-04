# ADO#2709 — Deploy Report
**NBAIS WC v2.1 spec: cover letter letterhead removed, premium summary restructured, policy pages renamed, section split for footer runners**

## Summary

| Field | Value |
|---|---|
| ADO Work Item | 2709 |
| Service | `proposal-generator-dev` |
| Cluster | `fortress-tools-cluster` |
| Commit SHA | `acf9a25` (HEAD; functional code at `3e9c96d`) |
| Image | `fip-proposal-generator:acf9a25` |
| Image Digest | `sha256:6277e3f2b8f5aae12c3ec745b095e0e77514a3b8a673228ff04c292a21cdfb6f` |
| New Task Def | `proposal-generator-dev:32` |
| Rollback Target | `proposal-generator-dev:31` |
| Deploy Date | 2026-05-04 |
| Deployed By | War Machine (Rhodey) |

## Timeline

| Time (EDT) | Event |
|---|---|
| ~14:53 | Deploy initiated |
| ~14:54 | Pre-deploy ADO comment posted |
| ~14:54 | ECR login succeeded |
| ~14:55–15:01 | Docker build (`--no-cache`) — LibreOffice layer cached, npm ci fresh |
| ~15:01 | Image pushed (`:acf9a25`, `:latest`) |
| ~15:02 | Task def `:32` registered |
| ~15:02 | ECS service updated, force-new-deployment |
| ~15:04 | ECS stabilized: RUNNING 1/1 |
| ~15:04 | `/health` → **200** |
| ~15:05 | Post-deploy ADO comment posted |

## Build Notes

- HEAD was `acf9a25` (one docs-only commit on top of `3e9c96d` — build cycle 2 notes appended to BUILD-REPORT.md)
- Image tagged as `acf9a25` — functional code identical to `3e9c96d`
- `docker-credential-desktop.exe` absent in WSL2 — resolved by removing `credsStore` from `~/.docker/config.json` before login
- All layers except app-level pushed fresh; LibreOffice base layers already existed in ECR

## Health Check

- ECS: RUNNING 1/1, PENDING 0, DESIRED 1
- `/health` HTTP: **200**
- Task def pinned to `:32`

## Rollback

To roll back to the previous revision:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:31 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```
