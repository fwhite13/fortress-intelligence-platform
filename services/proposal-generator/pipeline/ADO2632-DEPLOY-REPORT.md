# ADO#2632 Deploy Report — proposal-generator-dev:27

**Date:** 2026-05-01  
**Engineer:** War Machine (Rhodey)  
**ADO WI:** [#2632 — Proposal Generator: NBAIS WC template — remaining fidelity issues after ADO#2631](https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2632)  
**Status:** ✅ COMPLETE

---

## Summary

Deployed commit `dd7052e` to `proposal-generator-dev` on cluster `fortress-tools-cluster`.  
Fixes: cover header/footer sections, logo aspect ratio, column widths, contact box gap, signature lines, callout box border.

---

## Deploy Timeline

| Time (EDT)    | Event |
|---------------|-------|
| 19:28         | Pre-deploy snapshot captured — task def `:26`, 1/1 running |
| 19:28         | ADO pre-flight comment posted (#769337) |
| 19:28–19:30   | Docker build (`--no-cache`) — SUCCEEDED |
| 19:30         | ECR push — `fip-proposal-generator:dd7052e` + `:latest` pushed |
| 19:30         | Task def `proposal-generator-dev:27` registered |
| 19:30         | ECS service updated — force new deployment |
| ~19:32        | ECS stabilized — 1/1 RUNNING on `:27` |
| 19:32         | `/health` → **200** |
| 19:33         | ADO post-deploy comment posted (#769338), WI → Closed |

---

## Image

| Field       | Value |
|-------------|-------|
| ECR Repo    | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator` |
| Commit Tag  | `dd7052e` |
| Latest Tag  | `latest` |
| Digest      | `sha256:e42a030cf1ad537e7662ccd680ea58866a5f0d8db4f9859afc56790597eae2e0` |

---

## ECS

| Field          | Value |
|----------------|-------|
| Cluster        | `fortress-tools-cluster` |
| Service        | `proposal-generator-dev` |
| Previous TD    | `proposal-generator-dev:26` |
| New TD         | `proposal-generator-dev:27` |
| Running        | 1/1 |
| Health         | `/health` → 200 |

---

## Rollback

```bash
aws ecs update-service --cluster fortress-tools-cluster --service proposal-generator-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:26 \
  --force-new-deployment --profile fortress-tools-deployer --region us-east-1
```

---

## Notes

- Build uses `node:22-alpine` with LibreOffice — heavy image (~1GB), most layers already cached in ECR
- No schema/DB changes in this commit
- ADO WI state → Closed
