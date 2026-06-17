# Pipeline State: WI864

## Current Stage: QUEUED (infra-first)
## Risk Level: high
## Pipeline Path: full
## Review Cycles: 0

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Reed Richards | — | 2026-03-18 | Spec: ~/projects/fait-for-excel/CC-MEMORY-SPEC.md (same spec as WI856 — production-path deploy) |
| BUILD | ⏳ BLOCKED | Tony Stark | — | — | ⚠️ INFRA BLOCKER: Rhodey must provision all 6 infra items + post "INFRA READY" ADO comment before Tony builds RDS-wired version |
| REVIEW | ⏳ PENDING | Hawkeye | — | — | Focus: RDS vs localhost DB wiring; Secrets Manager integration; IAM task role |
| SECURITY | ⏳ PENDING | CodeSec | — | — | High risk: new DB, new IAM role, new ALB routing |
| APPROVE | ✅ DONE | Fred | — | 2026-03-18 | Standing approval |
| DEPLOY | ⏳ PENDING | Rhodey | — | — | Dev only: mcp.dev.fortressam.ai. Prod promotion separate Fred/Jarvis decision. |
| VERIFY | ⏳ PENDING | Natasha | — | — | Sprint QA — ECS endpoint, 4 MCP tools, /health, auth |
| CONFIRM | ⏳ PENDING | Maria | — | — | |

### Key Context
- Source: ~/projects/fip/mcp-memory/ (already built in WI856 for SteamServer)
- WI856 = SteamServer dev artifact (superseded by this WI for production path)
- New code changes needed: swap localhost pg Pool → RDS via Secrets Manager; adapt for ECS (no .env file)
- Deploy target: DEV ONLY — mcp.dev.fortressam.ai (fred's standing rule 2026-03-18)
- Blocked until: WI#863 Done AND Rhodey posts "INFRA READY" ADO comment

### Rhodey Infra Checklist (must ALL be done before Tony starts)
1. RDS PostgreSQL 16 db.t4g.micro — same VPC as ECS; pgvector extension enabled
2. Secrets Manager: `mcp-memory/db-credentials` (host, port, dbname, username, password)
3. IAM task role: `mcp-memory-task-role` (bedrock:InvokeModel on titan-embed-text-v2:0; secretsmanager:GetSecretValue on mcp-memory/db-credentials; logs:CreateLogGroup + logs:PutLogEvents)
4. ECR repo: `mcp-memory` (742932328420.dkr.ecr.us-east-1.amazonaws.com/mcp-memory)
5. ALB rules: `mcp.dev.fortressam.ai` → target group `mcp-memory-tg` → ECS service on port 3100
6. Route53 CNAME: `mcp.dev.fortressam.ai` → ALB DNS

### Tony's Code Changes (after INFRA READY)
- Replace `new Pool({ host: process.env.PG_HOST, ... })` with Secrets Manager fetch
- Remove .env dependency for DB; use task role for AWS auth (no explicit AWS credentials needed)
- Port 3100 unchanged
- Dockerfile must expose 3100 and run `node dist/server.js`

### Blocked until: WI#863 Done (per queue order)
