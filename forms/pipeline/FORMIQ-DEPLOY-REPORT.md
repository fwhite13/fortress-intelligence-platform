# FormIQ AWS Deploy Report

**Date:** 2026-02-26  
**Engineer:** War Machine (devops)  
**Target:** https://formiq.dev.fortressam.ai  
**Completed:** ~23:30 EST

---

## Final Status

| Resource | Status | Value |
|----------|--------|-------|
| URL | ✅ LIVE | `https://formiq.dev.fortressam.ai → HTTP 302 (Cognito auth)` |
| ECS Service | ✅ HEALTHY | `formiq-dev` running 1/1 |
| ALB Target | ✅ HEALTHY | `172.31.65.166:8080` |
| ECR Image | ✅ PUSHED | `742932328420.dkr.ecr.us-east-1.amazonaws.com/formiq:dev-latest` |
| Task Def | ✅ | `formiq-dev:3` |
| DNS | ✅ | `formiq.dev.fortressam.ai → fortress-tools-alb` |
| Cognito Auth | ✅ | Callback URLs added |
| DB Migration | ⚠️ PENDING | See DB section below |

---

## Steps Completed

| Step | Status | Notes |
|------|--------|-------|
| Pre-deploy snapshot | ✅ | Saved to `FORMIQ-DEPLOY-SNAPSHOT.md` |
| 1. MySQL provider | ✅ | Pomelo.EntityFrameworkCore.MySql 8.0.3 (EF Core 8.x compatible) |
| 2. Dockerfile | ✅ | debian:bookworm-slim + dotnet-install.sh (MCR CDN blocked on WSL2) |
| 3. ECR repo + push | ✅ | `formiq` repo created; `dev-latest` pushed |
| 4. Task definition | ✅ | `formiq-dev:3` — FARGATE 512 CPU / 1024 MB |
| 5. Target group | ✅ | `formiq-dev-tg` port 8080, `/health` path, `200-399` |
| 6. ALB listener rule | ✅ | Priority 4, Cognito auth + forward (matches portal pattern) |
| 7. ECS service | ✅ | `formiq-dev` ACTIVE, 1/1 running |
| 8. Route53 | ✅ | `formiq.dev.fortressam.ai` A ALIAS → `fortress-tools-alb` |
| 9. Cognito | ✅ | Added `https://formiq.dev.fortressam.ai/signin-oidc` and `/` callbacks |
| 10. DB migration | ⚠️ | Background task running — see DB section |
| 11. Health check | ✅ | ALB target HEALTHY, HTTP 302 confirmed |

---

## Infrastructure Resources Created

| Resource | ARN / Value |
|----------|-------------|
| ECR Repo | `arn:aws:ecr:us-east-1:742932328420:repository/formiq` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/formiq-dev:3` |
| ECS Service | `arn:aws:ecs:us-east-1:742932328420:service/fortress-tools-cluster/formiq-dev` |
| Target Group | `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/formiq-dev-tg/c15232052bae16a1` |
| ALB Rule | `arn:aws:elasticloadbalancing:...listener-rule/.../099ffaefb287cfc8` |
| Route53 Record | `formiq.dev.fortressam.ai` A ALIAS (Z003394436J64H3UMZ756) |
| CW Log Group | `/ecs/formiq-dev` |

---

## Code Changes Made

### FortressFormTools.Web/Program.cs
- **MySQL provider**: Replaced `UseSqlite` with `UseMySql` (Pomelo 8.0.3)
- **Connection string**: `FORTRESS_DB_HOST` env var takes priority over appsettings.json (avoids config override issue)
- **Server version**: Hardcoded `MySqlServerVersion(8, 0, 28)` — no `AutoDetect` (it tries DB connection at startup registration, causing crash)
- **Migration**: Background `Task.Run` with 5-second delay so app starts listening on HTTP before migration runs
- **Health endpoint**: `GET /health` returns `{"status":"healthy"}` — no DB dependency, used by ALB health check
- **HttpClient**: Dynamic port (5200 dev, 8080 prod)

### FortressFormTools.Web/appsettings.json
- Removed `Kestrel.Endpoints` hardcoded port 5200 (was overriding `ASPNETCORE_URLS`)

### FortressFormTools.Web/appsettings.Development.json (new)
- Kestrel port 5200 for local dev only

### Dockerfile.debian (new)
- Debian bookworm-slim + dotnet-install.sh for .NET 8 runtime
- Used instead of `mcr.microsoft.com/dotnet/aspnet:8.0` (MCR CDN has EOF error on this WSL2 host)
- App published locally via `dotnet publish`, copied into image

---

## Known Issues Debugged

| Issue | Root Cause | Fix Applied |
|-------|-----------|-------------|
| Container SIGSEGV | Pomelo 9.0.0 installed but EF Core 8.x → DLL mismatch at runtime | Downgraded to Pomelo 8.0.3 |
| Container crash at startup | `ServerVersion.AutoDetect()` connects to DB during service registration | Hardcoded `MySqlServerVersion(8,0,28)` |
| App listening on port 5200 in ECS | `appsettings.json` Kestrel config overrides `ASPNETCORE_URLS` | Moved to `appsettings.Development.json` |
| Wrong connection string in ECS | `GetConnectionString("Default")` returns appsettings value, shadowing env vars | Env var check-first pattern in code |
| ALB health check failing | ECS SG only allowed TCP 8000 from ALB (portal uses 8000); formiq uses 8080 | Added TCP 8080 ingress rule to `sg-0fb53615b1eb4a175` |
| MCR CDN blocked | Docker daemon EOF on `mcr.microsoft.com` manifest pull (known WSL2 issue) | Used debian:bookworm-slim + dotnet-install.sh |

---

## Database Migration Status ⚠️

**The app is running and healthy but `formiq_dev` database may not exist yet on Aurora.**

The EF Core migration runs as a background task on startup. It will:
1. Try to connect to Aurora as `fortress_mysql`
2. Create the `formiq_dev` database (if user has `CREATE DATABASE` permission)
3. Run all EF Core migrations to create tables

**If migration fails** (permission denied or database doesn't exist), the app will serve pages but DB queries will throw errors.

### Fred's action required — create the database:
```sql
-- Connect to Aurora as admin, then:
CREATE DATABASE IF NOT EXISTS formiq_dev 
  CHARACTER SET utf8mb4 
  COLLATE utf8mb4_unicode_ci;

GRANT ALL PRIVILEGES ON formiq_dev.* TO 'fortress_mysql'@'%';
FLUSH PRIVILEGES;
```

### Then restart ECS service to re-run migrations:
```bash
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service formiq-dev \
  --force-new-deployment
```

### Check migration outcome:
```bash
TASK=$(aws ecs list-tasks --cluster fortress-tools-cluster --service-name formiq-dev \
  --desired-status RUNNING --query 'taskArns[0]' --output text)
TASK_ID=$(echo $TASK | awk -F/ '{print $NF}')
aws logs get-log-events \
  --log-group-name /ecs/formiq-dev \
  --log-stream-name "ecs/formiq/${TASK_ID}" \
  --query 'events[*].message' --output text | grep -E "Migration|DB init|Applied"
```

---

## Seed Data

**Script:** `scripts/seed-nba-data.py`

### Dry-run validated:
- 15 forms (5 ACORD, 10 Supplemental)
- 1,448 form fields
- 38 QuestionSet fields (NBA Builders)
- 10 new DictionaryField entries

### How to run against Aurora (after DB is created + migrated):
```bash
export DB_PASSWORD=$(aws secretsmanager get-secret-value \
  --secret-id arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/dev-db-password-9ZKFmr \
  --query SecretString --output text \
  --profile <admin-profile-with-secrets-access>)

python3 scripts/seed-nba-data.py \
  --host fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com \
  --user fortress_mysql \
  --password "$DB_PASSWORD" \
  --database formiq_dev
```

Script is fully idempotent (checks for existing records before inserting).

---

## Rollback Plan

```bash
source /home/fredw/.openclaw/workspace/ai/projects/fortress_tools/.env.deployer

# Scale to 0 (instant, preserves all infra)
aws ecs update-service --cluster fortress-tools-cluster --service formiq-dev --desired-count 0

# Full teardown (if needed):
# Delete ALB rule
aws elbv2 delete-rule \
  --rule-arn arn:aws:elasticloadbalancing:us-east-1:742932328420:listener-rule/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1/099ffaefb287cfc8

# Delete Route53
aws route53 change-resource-record-sets \
  --hosted-zone-id Z003394436J64H3UMZ756 \
  --change-batch '{"Changes":[{"Action":"DELETE","ResourceRecordSet":{"Name":"formiq.dev.fortressam.ai","Type":"A","AliasTarget":{"HostedZoneId":"Z35SXDOTRQ7X7K","DNSName":"fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com","EvaluateTargetHealth":true}}}]}'

# Remove SG rule (TCP 8080 from ALB)
aws ec2 revoke-security-group-ingress \
  --group-id sg-0fb53615b1eb4a175 \
  --protocol tcp --port 8080 \
  --source-group sg-05d4936f153a4ea93
```

---

## Cost Impact

| Resource | Estimated Monthly Cost |
|----------|----------------------|
| Fargate (0.25 vCPU / 0.5 GB avg) | ~$8/month |
| ECR storage (~200 MB image) | <$0.05/month |
| CloudWatch Logs | ~$1/month |
| ALB rule (shared) | $0 additional |
| Route53 record | $0.40/month |
| **Total** | **~$10/month** |

---

## Lessons Learned

1. **Pomelo version must match EF Core major version** — Pomelo 9.x requires EF Core 9.x; use 8.x with EF Core 8.x
2. **`ServerVersion.AutoDetect` connects to DB at startup registration time** — always use hardcoded `MySqlServerVersion` for containers
3. **appsettings.json `Kestrel.Endpoints` overrides `ASPNETCORE_URLS`** — move dev-only Kestrel config to `appsettings.Development.json`
4. **ECS SG must allow the specific port the container uses** — existing SG had port 8000 (portal), not 8080
5. **MCR CDN blocked on this WSL2 host** — use debian:bookworm-slim + dotnet-install.sh as alternative base
6. **Connection string priority** — individual env vars must be checked FIRST before `GetConnectionString()`, which can shadow them with appsettings values
7. **DB migrations must be non-blocking** — run as background task so HTTP server starts before migration attempts

---

## Files Created/Modified

```
fortress-form-tools/
├── Dockerfile.debian                          ← NEW: debian-based Docker build
├── .dockerignore                              ← NEW
├── pipeline/
│   ├── FORMIQ-DEPLOY-SNAPSHOT.md             ← NEW: pre-deploy state
│   └── FORMIQ-DEPLOY-REPORT.md               ← this file
├── scripts/
│   └── seed-nba-data.py                      ← NEW: NBA extraction data seeder
├── FortressFormTools.Web/
│   ├── Program.cs                            ← MODIFIED: MySQL, health endpoint, bg migration
│   ├── appsettings.json                      ← MODIFIED: removed Kestrel port, added MySQL conn str
│   └── appsettings.Development.json          ← NEW: dev-only Kestrel port 5200
│   └── FortressFormTools.Web.csproj          ← MODIFIED: Pomelo 8.0.3, removed SQLite
└── FortressFormTools.Data/
    └── FortressFormTools.Data.csproj         ← MODIFIED: removed SQLite
```
