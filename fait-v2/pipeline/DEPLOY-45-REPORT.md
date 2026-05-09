# Deploy Report — fait-v2:45

**Deployed by:** War Machine (Rhodey)
**Date:** 2026-05-09
**Completed at:** ~11:43 EDT

---

## Outcome: ✅ DEPLOYED

---

## Image

| Property | Value |
|----------|-------|
| Tag | `fait-v2:1bb5e191` |
| Digest | `sha256:d81b87a56b4bbaaa08934f3b99f6d8fbd2b574530c0d48e9543359b9afc7f531` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:1bb5e191` |
| Build context | `/home/fredw/projects/fip` (monorepo root) |
| Dockerfile | `fait-v2/Dockerfile.debian` |
| Build flags | No `--no-cache` (MCR cached layers required on WSL2) |

---

## Commits in This Deploy

| Commit | Message |
|--------|---------|
| `1bb5e191` | fix(fait#3122,fait#3119): full chat UI v1 parity rebuild + entra_oid backfill middleware |
| `19f68647` | fix(fait#3121): user chat bubble background var(--color-primary), add color-text-on-primary |

---

## Task Definition

| Property | Value |
|----------|-------|
| Previous revision | `fait-v2:44` |
| New revision | `fait-v2:45` |
| ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:45` |
| taskRoleArn | `arn:aws:iam::742932328420:role/fait-v2-task-role` ✅ preserved |
| Env var changes | None |

---

## ECS Deployment

| Property | Value |
|----------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fait-v2` |
| Final state | running: 1, desired: 1, pending: 0 ✅ |
| Task definition | `fait-v2:45` |
| Rollout state | COMPLETED / STABLE |

---

## Health Verification

| Check | Result |
|-------|--------|
| `aws ecs wait services-stable` | ✅ PASS |
| ECS: running 1/1 on fait-v2:45 | ✅ PASS |
| CloudWatch: EF Core migrations complete | ✅ PASS |
| CloudWatch: ScheduledTaskBackgroundService started | ✅ PASS |
| CloudWatch: no errors or crashes | ✅ PASS |

---

## No DB Changes

No EF migrations in this deploy. No database changes needed.

---

## WIs Shipped

| ADO# | Title | State |
|------|-------|-------|
| #3119 | fait#3119: entra_oid backfill middleware | Closed (comment added) |
| #3122 | fait#3122: full chat UI v1 parity | Closed (comment added) |

Note: Both WIs were already in `Closed` state. Deploy comments added confirming deployment.

---

## Build Notes

- `--no-cache` was initially specified in the deploy brief, but MCR image pulls fail on WSL2 without cached layers.
- Previous successful deploys (`:43`, `:44`) all built without `--no-cache`, relying on Docker's layer cache for MCR base images.
- Built without `--no-cache` (consistent with all prior fait-v2 deploys on this machine).

---

## Rollback Plan

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:44 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Credentials Used

- `fortress-tools-deployer` — all AWS operations ✅
- `openclaw-bedrock` — NOT used ✅
