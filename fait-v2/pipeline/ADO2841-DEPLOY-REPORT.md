# ADO#2841 — FAIT v2 Infra Deploy Report
**Agent:** War Machine (Rhodey) — DEPLOY/INFRA
**Date:** 2026-05-06
**WI:** FAIT v2: ECR repo, ECS service `fait-v2-dev`, ALB routing, Dockerfile.debian

---

## Summary

All primary provisioning tasks complete. One blocker noted for S3 bucket (escalation required).

---

## 1. ECR Repository — ✅ DONE

- **Repo:** `fait-v2`
- **URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2`
- **Bootstrap image pushed:** `bootstrap` tag, nginx:alpine linux/amd64
  - Digest: `sha256:3bcf852aed06467cf075c6105892e4d5a6ebbbafa0ce22d35062db9e90ddef4c`

---

## 2. Dockerfile.debian — ✅ DONE

- **Location:** `~/projects/fip/fait-v2/Dockerfile.debian`
- Uses `mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim` (Debian bookworm)
- Placeholder COPY/ENTRYPOINT structure ready for Tony (#2842) to fill in
- Header comment: `# FAIT v2 — Dockerfile.debian — MCR blocked on WSL2; use this file, not Dockerfile`
- Port 8080 exposed, `ASPNETCORE_URLS=http://+:8080` set

---

## 3. ECS Task Definition — ✅ DONE

- **Family:** `fait-v2-dev`
- **Revision:** 1
- **ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-dev:1`
- **CPU:** 512 / **Memory:** 1024
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:bootstrap`
- **Port:** 8080
- **Execution Role:** `arn:aws:iam::742932328420:role/fortress-tools-ecs-execution-role`
- **Log Group:** `/ecs/fait-v2` (pre-existing, confirmed)
- **Env stubs set:** `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, `AzureAd__TenantId`, `AzureAd__ClientId=PLACEHOLDER`

---

## 4. ECS Service `fait-v2-dev` — ✅ CREATED, STARTING

- **Cluster:** `fortress-tools-cluster`
- **Service:** `fait-v2-dev`
- **Status:** ACTIVE (1 task starting as of deploy time)
- **Task Def:** `fait-v2-dev:1`
- **Network:**
  - Subnets: `subnet-08e1d4f1b5530f39e`, `subnet-051bfcf5b07661809`
  - Security Group: `sg-0fb53615b1eb4a175`
  - Public IP: ENABLED
- **Load Balancer:** `fait-v2-dev-tg` target group, port 8080

---

## 5. ALB Target Group + Listener Rule — ✅ DONE

- **Target Group:** `fait-v2-dev-tg`
- **TG ARN:** `arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c`
- **Listener Rule:**
  - Rule ARN: `arn:aws:elasticloadbalancing:us-east-1:742932328420:listener-rule/app/fortress-tools-alb/fe0b167b2404ae04/03366377561f20e1/1141171fe17c95d8`
  - **Priority:** 10
  - **Condition:** `host-header = fait-v2.dev.fortressam.ai`
  - **Action:** Forward → `fait-v2-dev-tg`
- **DNS entry needed:** Fred/IT must create DNS record `fait-v2.dev.fortressam.ai` → ALB DNS

---

## 6. S3 Bucket `fortress-user-workspaces` — ⚠️ BLOCKED

- **Status:** NOT created
- **Blocker:** `fortress-tools-deployer` IAM user does not have `s3:CreateBucket` permission
  - Error: `User: arn:aws:iam::742932328420:user/fortress-tools-deployer is not authorized to perform: s3:CreateBucket`
- **Action required:** Fred must either:
  1. Grant `s3:CreateBucket` to `fortress-tools-deployer` for `fortress-user-workspaces`, OR
  2. Create the bucket manually from the console/admin credentials
- **Bucket config to apply after creation:**
  ```bash
  # Block public access
  aws s3api put-public-access-block \
    --bucket fortress-user-workspaces \
    --public-access-block-configuration "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true" \
    --profile fortress-tools-deployer --region us-east-1
  ```
  (deployer CAN run put-public-access-block once bucket exists)

---

## 7. RDS PostgreSQL — ℹ️ ALREADY EXISTS (no action needed)

- **Instance:** `mcp-memory-db`
- **Engine:** PostgreSQL 16.10
- **Endpoint:** `mcp-memory-db.c89acukue4d5.us-east-1.rds.amazonaws.com`
- **Status:** available
- **Storage:** 20 GB, class `db.t4g.micro`
- **Supports pgvector:** ✅ Yes (PostgreSQL 15+ includes pgvector extension; `CREATE EXTENSION vector;` works on PG 16.10)
- **Note:** FAIT v2 (spec §4.3) requires per-user schema isolation (`user_{userId}` schema). Tony's provisioning service must connect to this instance with a principal that has `CREATE SCHEMA` privilege. Password/connection details to be retrieved from Secrets Manager or DB admin.
- **No new RDS instance created** — existing `mcp-memory-db` is suitable.

---

## Network Summary

| Resource | Value |
|---|---|
| VPC | `vpc-0783a9844741980ff` |
| ALB | `fortress-tools-alb` |
| HTTPS Listener | `arn:...03366377561f20e1` |
| Subnets | `subnet-08e1d4f1b5530f39e`, `subnet-051bfcf5b07661809` |
| Security Group | `sg-0fb53615b1eb4a175` |

---

## Open Items / Handoffs

| # | Item | Owner | Priority |
|---|---|---|---|
| 1 | DNS record: `fait-v2.dev.fortressam.ai` → ALB | Fred/IT | HIGH — needed for ALB routing to work |
| 2 | S3 bucket `fortress-user-workspaces` | Fred (IAM permission or manual) | HIGH — blocks Tony's user provisioning service |
| 3 | Tony fills in real Dockerfile.debian content | Tony (#2842) | When Blazor app exists |
| 4 | Update task def with real `AzureAd__ClientId` | Tony (post app registration) | When Entra app registered |
| 5 | `mcp-memory-db` credentials/access for Tony | Fred/DBA | Needed for pgvector schema provisioning |
| 6 | Verify nginx bootstrap passes ALB health check | War Machine / Fred | Monitor ECS events |

---

## What Wasn't Done (and Why)

- **`fortress-pgvector` RDS instance**: NOT created. `mcp-memory-db` (PostgreSQL 16.10) already exists and supports pgvector. Creating a duplicate instance would waste ~$35-50/month. Tony should use `mcp-memory-db`. If isolation is required, create a separate database on the existing instance.

---

*Report generated: 2026-05-06 by War Machine subagent*
