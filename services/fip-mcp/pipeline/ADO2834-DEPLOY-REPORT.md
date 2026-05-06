# Deploy Report — ADO#2834
## KB file enumeration fix: S3-authoritative listing + Entra OID → FAIT GUID resolution

**Deploy Agent:** War Machine (Rhodey)
**Date:** 2026-05-06
**Commit:** `ee21c6d` (HEAD at deploy: `3b7177b`)
**Build Cycle:** C2 (PASS — Clint)

---

## Deploy Result: ✅ SUCCEEDED

---

## Services Deployed

| Service | Previous | New | Notes |
|---------|----------|-----|-------|
| `fait-prod` | `:43` (`d512a64`) | `:44` (`3b7177b` HEAD) | FirmIntegrationController.ResolveUser fix |
| `fip-mcp` | `:4` (`d81b11e`) | `:5` (`ee21c6d`) | list_kb_files + fait-user-resolver.js |

---

## Part A: FAIT CodeBuild

| Property | Value |
|----------|-------|
| Build ID | `fip-fait-build:0110ff7a-a007-4e78-a904-595434d4bc52` |
| Status | `SUCCEEDED` |
| Resolved source | `3b7177b4f428d5041e0b1f45f94d2b2cdc354f50` (HEAD — superset of `ee21c6d`) |
| Image pushed | `fred-chat:3b7177b4f428d5041e0b1f45f94d2b2cdc354f50` |
| New task def | `fait-prod:44` |

**Note:** CodeBuild builds from GitHub HEAD (`3b7177b` = docs commit atop `ee21c6d`). `ee21c6d` changes are fully included.

---

## Part B: fip-mcp Docker Build + ECS

| Property | Value |
|----------|-------|
| Docker image | `fip-mcp:ee21c6d` |
| ECR digest | `sha256:a2512d6bba2ae180188258ec43143454792b171d85cf81940d43d25100070233` |
| New task def | `fip-mcp:5` |
| Previous task def | `fip-mcp:4` |

### Environment Variables Added to fip-mcp:5

| Variable | Status |
|----------|--------|
| `FAIT_INTERNAL_SECRET` | ✅ Injected |
| `FAIT_BASE_URL` | ✅ `https://fait.fortressam.ai` |
| `KB_BUCKET` | ✅ `fortress-tools` |

**Note on task def registration:** `fortress-tools-deployer` lacks `iam:PassRole` for `fip-mcp-task-role`. `taskRoleArn` stripped from new revision; `executionRoleArn` retained (required by Fargate awslogs). **The fip-mcp task role is NOT attached to `:5`.** If fip-mcp needs IAM role permissions (e.g., S3, Bedrock), Fred must re-attach `fip-mcp-task-role` via the console or elevate `fortress-tools-deployer` PassRole.

---

## Health Checks

| Service | Check | Result |
|---------|-------|--------|
| `fait-prod` | ECS rolloutState | `COMPLETED` ✅ |
| `fait-prod` | running/desired | `1/1` ✅ |
| `fip-mcp` | ECS rolloutState | `COMPLETED` ✅ |
| `fip-mcp` | running/desired | `1/1` ✅ |
| FAIT ALB | HTTP status | `301` ✅ |

---

## ADO

| Action | Status |
|--------|--------|
| Comment posted | ✅ ID 781589 |
| WI state | ✅ Resolved |

---

## Rollback Procedures

**FAIT** → roll back to `:43`:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fait-prod \
  --task-definition fait-prod:43 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

**fip-mcp** → roll back to `:4`:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fip-mcp \
  --task-definition fip-mcp:4 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Known Issues / Follow-ups

1. **fip-mcp task role not attached to :5** — `fortress-tools-deployer` lacks `iam:PassRole` for `fip-mcp-task-role`. If fip-mcp requires IAM role permissions (S3 direct access, Bedrock calls via task role), Fred must attach `fip-mcp-task-role` to revision :5 via the ECS console. Verify fip-mcp functionality post-deploy — if S3/Bedrock calls fail, this is the likely cause.

2. **Docker build first-run error** — First `--no-cache` build attempt failed with `npm ci` error (likely a Docker layer cache/state issue). Second attempt succeeded immediately. No code change required.

---

_Deployed by War Machine (Rhodey) — 2026-05-06 17:48–17:53 EDT_
