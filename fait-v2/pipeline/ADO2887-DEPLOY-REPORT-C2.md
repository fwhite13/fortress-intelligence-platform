# fait-v2 Deploy Report — Cycle 2
**ADO:** #2887  
**Date:** 2026-05-07  
**Agent:** Rhodey (War Machine)  
**Task Def:** `fait-v2:5` (image `555b283`)

---

## What Was Found

### Original Error
```
Unable to connect to any of the specified MySQL hosts.
RetryLimitExceededException (3 retries) at Program.cs:140
Database: fait_v2_dev, Server: localhost
```

### Investigation

The "Server: localhost" error was misleading. `FORTRESS_DB_HOST` is correctly set in the task def to the Aurora cluster endpoint. The actual cause was a **two-part failure**:

**Failure 1: `fait_v2_dev` database didn't exist**
- Aurora cluster `fortress-ai-cluster` had no `fait_v2_dev` database
- Databases present: `ai`, `fait_dev`, `fait_prod`, `famos_dev`, `fip_dev`, `firm_dev`, `formiq_dev`, `fortress_tools`, `fortress_tools_dev`, `fred_dev`, etc.
- `fait_v2_dev` was never provisioned
- App crashed before connecting because the first connection attempt (to the keyring/SharedKeyRingDbContext connecting to `fait_dev`) succeeded, but the `FaitV2DbContext` connection to the non-existent `fait_v2_dev` failed
- The "localhost" in the error was from the design-time factory fallback path — not from the runtime env var

**Failure 2: EF Core migrations not applied**
- Once `fait_v2_dev` was created, the app connected but crashed at the seed step:
  ```
  MySqlConnector.MySqlException: Table 'fait_v2_dev.mcp_servers' doesn't exist
  ```
- The app does NOT call `MigrateAsync()` at startup — it goes straight to seeding
- Migrations had to be applied manually

### Security Group Check (confirmed OK)
- ECS task SG: `sg-0fb53615b1eb4a175` — full egress (`0.0.0.0/0`)
- Aurora SG: `sg-008f9970403aba844` — inbound TCP 3306 explicitly allows `sg-0fb53615b1eb4a175`
- Network path is clear — not a SG issue

---

## What Was Fixed

### Step 1: Created `fait_v2_dev` database
```sql
CREATE DATABASE IF NOT EXISTS fait_v2_dev 
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

### Step 2: Applied EF Core migrations
Generated idempotent migration SQL and applied directly:
```bash
cd ~/projects/fip/fait-v2/src/FortressAI.V2.Web
dotnet ef migrations script --context FaitV2DbContext --idempotent --output /tmp/fait-v2-migrations.sql
mysql -h fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com \
  -u fortress_mysql -p"..." fait_v2_dev < /tmp/fait-v2-migrations.sql
```

Migrations applied (all 4):
- `20260506224542_InitialSchema`
- `20260506225415_AddUserSessionTimestamps`
- `20260507032637_AddFargateColumnsToUserSession`
- `20260507125357_AddMcpTables`

### Step 3: Registered new task def `fait-v2:5`
- Image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:555b283`
- Task role: `arn:aws:iam::742932328420:role/fait-v2-task-role`
- All env vars carried from `:4` (FORTRESS_DB_HOST, FORTRESS_DB_NAME, FORTRESS_DB_USER, FORTRESS_DB_PORT, FORTRESS_DB_PASS via Secrets Manager)

### Step 4: Updated ECS service
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 \
  --task-definition fait-v2:5 --desired-count 1 --force-new-deployment
```

---

## Final Status

| Item | Status |
|------|--------|
| Task definition | `fait-v2:5` ✅ |
| Image | `555b283` ✅ |
| Service running/desired | 1/1 ✅ |
| ALB target health | HEALTHY ✅ |
| ECS task ID | `632fe54b13bb4bc1b6738ae048ce7fb3` |
| DB schema | All 4 migrations applied ✅ |
| App startup log | `[INF] Seeded forge-kb mcp_servers entry` ✅ |
| Health endpoint | ALB confirms healthy (direct curl blocked by WSL DNS) |

### CloudWatch Evidence
```
2026-05-07T15:13:47 ecs/fait-v2/632fe54b... [INF] Seeded forge-kb mcp_servers entry
2026-05-07T15:13:47 ecs/fait-v2/632fe54b... [WRN] Overriding HTTP_PORTS '8080'... (expected warning)
```

---

## Rollback Plan

**Quick rollback (stop service):**
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 --desired-count 0 \
  --profile fortress-tools-deployer --region us-east-1
```

**Rollback to previous task def:**
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 \
  --task-definition fait-v2:4 --desired-count 0 \
  --profile fortress-tools-deployer --region us-east-1
```

Schema changes are **non-destructive** (additive only). DB can be dropped and recreated if needed:
```sql
DROP DATABASE fait_v2_dev;
CREATE DATABASE fait_v2_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

---

## Lessons Learned

1. **New ECS services need their DB provisioned before first deploy** — this is a manual pre-deploy step that must be documented in the deploy checklist for .NET EF Core apps
2. **The "localhost" in EF errors is the design-time factory fallback** — it doesn't mean `FORTRESS_DB_HOST` is wrong; it means either the DB doesn't exist or config isn't loading at all
3. **EF Core apps that don't call `MigrateAsync()`** require manual migration runs before the first deploy to production-like environments
4. **`dotnet ef database update` with special chars in passwords** — use `--connection` carefully or rely on env vars + design-time factory (the design-time factory in fait-v2 is hardcoded to localhost, so use `migrations script` + direct mysql instead)

---

_Rhodey — War Machine | 2026-05-07_
