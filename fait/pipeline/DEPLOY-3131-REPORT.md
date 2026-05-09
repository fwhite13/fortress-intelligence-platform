# Deploy Report — ADO#3131: Full 4-Step Setup Wizard

**Date:** 2026-05-09  
**Deployed by:** Rhodey (DevOps subagent)  
**Commit:** `db33dcc4` — feat(fait#3131): full 4-step setup wizard — role, preferences, use cases, personalization

---

## Summary

Deployed the full 4-step setup wizard to `fred-dev` ECS service. All 8 new `user_assistant_config` columns applied to `fait_dev` via startup migration. Service stable.

---

## Image

| Property | Value |
|----------|-------|
| Tag | `fred-chat:db33dcc4` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:db33dcc4` |
| Digest | `sha256:4c239282d7afc7f097b758b51b6cc0d01d74a0058bfbfdee056ee1f9f8d3c9df` |
| Size | 286 MB |
| Pushed | 2026-05-09T16:12:10 EDT |

---

## ECS

| Property | Value |
|----------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |
| Previous task def | `fred-dev:132` (fred-chat:173138d3) |
| New task def | `fred-dev:133` |
| Desired / Running / Pending | 1 / 1 / 0 |
| Status | ACTIVE — STABLE |

---

## Database Migrations (fait_dev)

All 8 new columns applied to `user_assistant_config`:

| Migration | Status |
|-----------|--------|
| `ALTER TABLE user_assistant_config ADD COLUMN role VARCHAR(100) NULL` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN responsibilities TEXT NULL` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN communication_style VARCHAR(20) NULL` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN response_format VARCHAR(30) NULL` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN show_citations TINYINT(1) NULL DEFAULT 1` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN use_cases_json TEXT NULL` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN additional_context TEXT NULL` | ✅ Applied |
| `ALTER TABLE user_assistant_config ADD COLUMN preferred_name VARCHAR(100) NULL` | ✅ Applied |

**Database init result:** `Database initialization complete`  
**No startup errors.** Application listening on `http://[::]:8080`.

---

## Verification

- ✅ ECS service stable: Running=1, Pending=0
- ✅ Task definition: fred-dev:133 (fred-chat:db33dcc4)
- ✅ All 8 wizard columns applied to fait_dev
- ✅ No fatal startup errors
- ✅ MCP servers seeded (Brave, DevOps, M365)
- ✅ ADO#3131 set to Resolved

---

## Rollback Plan

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:132 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Build Command

```bash
cd /home/fredw/projects/fip
docker build --no-cache -f fait/Dockerfile -t fred-chat:db33dcc4 .
docker tag fred-chat:db33dcc4 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:db33dcc4
aws ecr get-login-password --region us-east-1 --profile fortress-tools-deployer | \
  docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:db33dcc4
```

---

_Rhodey — deployed and verified._
