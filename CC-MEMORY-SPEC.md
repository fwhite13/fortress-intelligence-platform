# CC Memory MCP Server — Architecture Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17 (updated 2026-03-18 — AWS deployment)  
**Status:** Ready for Implementation  
**Users:** Rob, Len, Leslie (Claude Code on Ubuntu VMs, Bedrock backend)  
**Goal:** Cross-session memory for CC — decisions, lessons, project context — without `CLAUDE.md` bloat  
**Deploy target:** ECS Fargate, `fortress-tools-cluster`  
**Dev URL:** `https://mcp.dev.fortressam.ai`  
**Prod URL:** `https://mcp.fortressam.ai`  
**ECR repo:** `mcp-memory`

---

## 1. Architecture Decision: pgvector vs. Bedrock KB, and Database Choice

### pgvector vs. Bedrock KB

**Decision: pgvector only. No new Bedrock KB.**

Rationale:

| Factor | pgvector | Bedrock KB |
|--------|----------|------------|
| Cost | ~$20/month (RDS t4g.micro PostgreSQL) | $0.10/GB/month + $0.0004/query |
| Latency | ~10–50ms (same VPC) | 200–500ms (cold) |
| Chunk control | Full — choose size, metadata, TTL | Fixed by Bedrock ingestion pipeline |
| Per-user isolation | Native — `user_id` column + query filter | Requires metadata filter in retrieve call |
| Schema flexibility | Arbitrary metadata, project tags, scopes | Limited to predefined metadata |

FORGE (Bedrock KB) is large-chunk, document-optimized, enterprise knowledge. Memory is small-chunk, decision-optimized, low-latency. Wrong tool for the job.

**Embedding model:** `amazon.titan-embed-text-v2:0` (1536-dim, same as FAIT's corpus KB). Use Bedrock InvokeModel API directly — not BedrockAgent. Costs ~$0.00002/1K tokens.

### Database Decision: New RDS PostgreSQL Instance

**The FIP Aurora cluster (`fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com`) is Aurora MySQL.** Aurora MySQL does not support the `pgvector` extension natively. The three options:

| Option | Verdict |
|--------|---------|
| **A — RDS PostgreSQL t4g.micro** (new, standalone) | ✅ **Recommended** |
| B — Aurora PostgreSQL-compatible cluster (new) | ❌ Overkill — Aurora PostgreSQL costs 3–5× more than a single RDS instance for this workload |
| C — Aurora MySQL + manual cosine similarity (JSON float arrays + application-side dot product) | ❌ Unacceptable — no index, O(N) scan, 500ms+ at scale |
| D — OpenSearch Serverless vector store | ❌ $700+/month minimum OCU cost — far too expensive for this use case |

**Decision: Option A — new `db.t4g.micro` RDS PostgreSQL 16 instance with the `pgvector` extension.**

**Why not reuse OpenClaw's local pgvector container?** OpenClaw's `openclaw-rag` container runs on SteamServer (a local WSL machine), port 5433. It is not accessible from AWS VPC or the internet. Rob, Len, and Leslie on Azure Ubuntu VMs need an AWS-hosted service. SteamServer is not the right backend.

**RDS PostgreSQL instance spec (Rhodey — infrastructure):**
```
Engine:           PostgreSQL 16
Instance class:   db.t4g.micro (2 vCPU, 1 GB RAM) — ~$20/month
Storage:          20 GB gp3 (auto-scaling on)
Multi-AZ:         No (dev/prod share single instance for now; add read replica in Phase 2)
DB name:          mcp_memory
VPC:              Same VPC as fortress-tools-cluster
Security group:   Allow inbound 5432 from ECS tasks sg only (not internet-exposed)
Encryption:       At rest: yes (AWS KMS); In transit: yes (require_ssl=1)
Backup:           7-day automated backup window
Parameter group:  shared_preload_libraries = 'pg_vector'  ← required for pgvector
```

**Credentials:** Stored in AWS Secrets Manager as `mcp-memory/db-credentials` (JSON: `{username, password, host, port, dbname}`). ECS task IAM role gets `secretsmanager:GetSecretValue` on this secret.

**Note on SteamServer deploy:** The spec previously described a `mcp-memory.service` systemd unit on SteamServer. That is a throwaway artifact — discard it. All deployment is via ECS Fargate.

---

## 2. Database Schema

Two new tables in the existing pgvector database (shared with OpenClaw's own memory, different tables).

```sql
-- Memory users: Rob, Len, Leslie + future
CREATE TABLE cc_memory_users (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    username    VARCHAR(64) NOT NULL UNIQUE,           -- 'rob', 'len', 'leslie'
    email       VARCHAR(256) NOT NULL UNIQUE,
    api_token   VARCHAR(128) NOT NULL UNIQUE,          -- bcrypt hash stored, plaintext on creation only
    scope       VARCHAR(20)  NOT NULL DEFAULT 'user',  -- 'user' | 'admin'
    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ
);
CREATE INDEX idx_ccmu_token ON cc_memory_users (api_token);

-- Memory entries
CREATE TABLE cc_memory_entries (
    id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID        REFERENCES cc_memory_users(id) ON DELETE CASCADE,  -- NULL = org-level
    scope       VARCHAR(20) NOT NULL,    -- 'org' | 'personal'
    project     VARCHAR(64),             -- 'iaapa', 'firm', 'fait', NULL = global
    content     TEXT        NOT NULL,    -- the memory text
    entry_type  VARCHAR(32) NOT NULL DEFAULT 'note',  -- 'decision' | 'lesson' | 'context' | 'note'
    source      VARCHAR(32) NOT NULL DEFAULT 'manual', -- 'cc_session' | 'cli' | 'git_hook'
    embedding   vector(1536),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_by  UUID        REFERENCES cc_memory_users(id),  -- who wrote it (even for org entries)
    expires_at  TIMESTAMPTZ,             -- NULL = permanent; set for ephemeral context
    metadata    JSONB       NOT NULL DEFAULT '{}'
);
CREATE INDEX idx_ccme_scope       ON cc_memory_entries (scope);
CREATE INDEX idx_ccme_project     ON cc_memory_entries (project);
CREATE INDEX idx_ccme_user        ON cc_memory_entries (user_id);
CREATE INDEX idx_ccme_created_at  ON cc_memory_entries (created_at DESC);
CREATE INDEX idx_ccme_embedding   ON cc_memory_entries USING ivfflat (embedding vector_cosine_ops) WITH (lists = 50);
```

**Org entries:** `user_id = NULL`, `scope = 'org'`. Readable by all users. Writable only by users with `scope = 'admin'` or explicit `--scope org` with confirmation.

**Personal entries:** `user_id = <caller's id>`, `scope = 'personal'`. Readable + writable only by the owner.

---

## 3. MCP Server

### Runtime

Node.js/TypeScript. Deployed as a Fargate container on `fortress-tools-cluster`. Exposed at `https://mcp.dev.fortressam.ai` (dev) and `https://mcp.fortressam.ai` (prod).

CC on Ubuntu VMs in Azure reaches it over public HTTPS. TLS termination at `fortress-tools-alb` (same ALB as FAIT/FIRM). No new certificate needed — the wildcard `*.fortressam.ai` cert already covers `mcp.fortressam.ai`.

### Auth

API token in the `Authorization: Bearer <token>` header (standard MCP convention). Each user gets one token at account creation — stored as `bcrypt(token)` in DB. Token is a 32-byte hex string (`crypto.randomBytes(32).toString('hex')`).

Token validation middleware:

```typescript
async function authenticate(req: Request): Promise<CcMemoryUser | null> {
  const auth = req.headers['authorization'];
  const token = auth?.startsWith('Bearer ') ? auth.slice(7) : null;
  if (!token) return null;

  // Pull all active user tokens (small table — cache in process for 60s)
  const users = await getUsersFromCache();
  for (const user of users) {
    if (await bcrypt.compare(token, user.api_token)) {
      await updateLastUsed(user.id);
      return user;
    }
  }
  return null;
}
```

**Token issuance:** CLI `memory-admin add-user --username rob --email rob@fortressam.ai` → prints a one-time plaintext token. Rob saves it; only the hash is stored.

### MCP Protocol

CC expects MCP servers to expose tools via JSON-RPC 2.0 over HTTP(S) (streamable HTTP transport, the 2025 MCP spec). The server exposes these tools:

#### `memory_search`

Search org + personal memory for a query. Returns top-N ranked results from both namespaces merged.

```typescript
{
  name: "memory_search",
  description: "Search org-level and personal memory for relevant context. Call this at the start of any session or when you need background on a topic. Returns decisions, lessons, and context from previous sessions.",
  inputSchema: {
    type: "object",
    properties: {
      query: { type: "string", description: "What to search for" },
      project: { type: "string", description: "Filter to a specific project (e.g. 'iaapa', 'firm'). Omit for global search." },
      limit: { type: "number", description: "Max results (default 10, max 20)" }
    },
    required: ["query"]
  }
}
```

Implementation:

```typescript
async function memorySearch(params: SearchParams, user: CcMemoryUser): Promise<SearchResult[]> {
  const embedding = await embedText(params.query);
  const limit = Math.min(params.limit ?? 10, 20);

  // Query: org entries + personal entries for this user
  // Optionally filtered by project
  const rows = await db.query(`
    SELECT id, user_id, scope, project, content, entry_type, source,
           created_at, metadata,
           1 - (embedding <=> $1) AS similarity
    FROM cc_memory_entries
    WHERE
      -- Org entries are readable by everyone
      (scope = 'org' OR user_id = $2)
      -- Expired entries are excluded
      AND (expires_at IS NULL OR expires_at > NOW())
      -- Project filter: if set, match project OR global (NULL project)
      ${params.project ? `AND (project = $3 OR project IS NULL)` : ''}
    ORDER BY embedding <=> $1
    LIMIT $4
  `, [
    JSON.stringify(embedding),
    user.id,
    ...(params.project ? [params.project] : []),
    limit * 2  // Over-fetch before dedup
  ]);

  // Merge: interleave org + personal, deduplicate near-duplicates (cosine > 0.97)
  return deduplicateAndRank(rows, limit);
}
```

#### `memory_add`

Store a new memory entry.

```typescript
{
  name: "memory_add",
  description: "Store a decision, lesson learned, or important context for future sessions. Use at session end for anything worth remembering.",
  inputSchema: {
    type: "object",
    properties: {
      content: { type: "string", description: "The memory to store (be concise — 1-3 sentences)" },
      entry_type: { type: "string", enum: ["decision", "lesson", "context", "note"], description: "Type of memory" },
      project: { type: "string", description: "Project tag (e.g. 'iaapa', 'firm'). Omit for global." },
      scope: { type: "string", enum: ["personal", "org"], description: "personal = only you see it; org = shared with the team. Default: personal." }
    },
    required: ["content"]
  }
}
```

Implementation rules:
- `scope` defaults to `personal` — CC never writes to org without explicit `scope: 'org'`
- Writing `scope: 'org'` requires the user to have `scope = 'admin'` in the DB, OR returns a special `confirmation_required` response (see below)
- Content trimmed to 2000 chars max
- Embed with Titan, store in `cc_memory_entries`

**Org write confirmation flow:**

When a non-admin user calls `memory_add` with `scope: 'org'`, the server returns:

```json
{
  "confirmation_required": true,
  "message": "This will write to org memory visible to all team members. Call memory_add again with confirmed: true to proceed.",
  "preview": "<the content you submitted>"
}
```

CC will surface this to the user naturally ("I need confirmation to write org memory — shall I proceed?"). On the second call with `confirmed: true`, the write executes.

#### `memory_list`

List recent entries for a project.

```typescript
{
  name: "memory_list",
  description: "List recent memory entries for a project or scope.",
  inputSchema: {
    type: "object",
    properties: {
      project: { type: "string" },
      scope: { type: "string", enum: ["personal", "org", "all"] },
      limit: { type: "number", description: "Default 20, max 50" }
    }
  }
}
```

#### `memory_delete`

Delete a personal entry by ID. Org entries can only be deleted by admins.

---

## 4. Project Tagging

No separate KB per project. Entries carry a `project` column. The `memory_search` tool filters:

- `project = 'iaapa'` → returns `project='iaapa'` entries + `project=NULL` (global) entries
- No project filter → returns all entries the user can read, ranked by relevance

Project names are free-form strings. Convention: lowercase, no spaces. Examples: `iaapa`, `firm`, `fait`, `fffe`, `cowork`.

**Retirement:** When a project is done, entries can be soft-retired by setting `expires_at = NOW() + INTERVAL '1 year'`. This keeps them retrievable for a year then auto-expires. No hard delete needed.

---

## 5. `CLAUDE.md` Template

```markdown
# Project Context

## Memory
At session start: search memory for relevant context.
At session end: add key decisions and lessons learned.

<!-- MCP server is configured in ~/.claude/settings.json -->

## Project Tag
project: <PROJECT_NAME>

## Session Start Protocol
1. `memory_search(query="<PROJECT_NAME> recent decisions context", project="<PROJECT_NAME>")` 
2. Read results and load relevant context before starting work.

## Session End Protocol
For each significant decision or lesson:
`memory_add(content="...", entry_type="decision|lesson", project="<PROJECT_NAME>", scope="personal")`
For team-wide decisions: confirm org scope with user before writing.

## What to Remember
- Architecture decisions and rationale
- Bugs found and root causes  
- Patterns that worked / didn't work
- Environment quirks (infra, auth, config)
- Do NOT store: credentials, PII, anything that changes frequently
```

**Line count: 27 lines.** Under the 30-line budget.

---

## 6. CLI Tool

**Language:** Python 3. Simpler than Node.js for a one-file CLI. Requires only `httpx` and `typer` (both standard in Ubuntu).

**Location:** `~/bin/memory` (or `/usr/local/bin/memory` system-wide).

**Install:**
```bash
pip3 install httpx typer --quiet
curl -o ~/bin/memory https://mcp.fortressam.ai/cli/memory.py
chmod +x ~/bin/memory
```

**Configuration file:** `~/.memory-config.json`
```json
{
  "server": "https://mcp.fortressam.ai",
  "token": "<YOUR_TOKEN>"
}
```

### CLI commands

```bash
# Add a memory (defaults to personal scope)
memory add "Decided to use Redis for queue state — in-memory Map had concurrency issues" \
  --project cowork --type decision

# Explicitly write to org memory (prompts for confirmation)
memory add "S3 bucket naming convention: fip-<service>-workspaces" \
  --project iaapa --scope org

# Search
memory search "Redis queue decision" --project cowork

# List recent
memory list --project firm --scope personal --limit 10

# Delete by ID
memory delete abc123
```

### Confirmation flow for org writes:

```
$ memory add "IAAPA uses blue/yellow branding..." --project iaapa --scope org

⚠  This will write to ORG memory — visible to all team members:
   "IAAPA uses blue/yellow branding..."

   Project: iaapa | Scope: org
   Type: note

Confirm? [y/N]: y
✓ Saved to org memory (id: abc123)
```

### `memory.py` implementation sketch:

```python
#!/usr/bin/env python3
"""FIP Memory CLI — manage CC cross-session memory"""
import json, os, sys
import httpx
import typer
from pathlib import Path

app = typer.Typer(help="FIP Memory CLI")
CONFIG_PATH = Path.home() / ".memory-config.json"

def load_config():
    if not CONFIG_PATH.exists():
        typer.echo("Run: memory configure --server https://mcp.fortressam.ai --token <TOKEN>", err=True)
        raise typer.Exit(1)
    return json.loads(CONFIG_PATH.read_text())

def headers(cfg):
    return {"Authorization": f"Bearer {cfg['token']}", "Content-Type": "application/json"}

@app.command()
def add(content: str,
        project: str = typer.Option(None, help="Project tag"),
        scope: str = typer.Option("personal", help="personal|org"),
        type_: str = typer.Option("note", "--type", help="decision|lesson|context|note")):
    cfg = load_config()
    payload = {"content": content, "entry_type": type_, "scope": scope}
    if project: payload["project"] = project
    
    r = httpx.post(f"{cfg['server']}/mcp/memory/add", json={"params": payload}, headers=headers(cfg))
    data = r.json()
    
    if data.get("confirmation_required"):
        typer.echo(f"\n⚠  This will write to ORG memory — visible to all team members:")
        typer.echo(f"   \"{content[:120]}{'...' if len(content)>120 else ''}\"")
        typer.echo(f"\n   Project: {project or 'global'} | Scope: org | Type: {type_}")
        if not typer.confirm("\nConfirm?", default=False):
            typer.echo("Cancelled.")
            raise typer.Exit()
        payload["confirmed"] = True
        r = httpx.post(f"{cfg['server']}/mcp/memory/add", json={"params": payload}, headers=headers(cfg))
        data = r.json()
    
    typer.echo(f"✓ Saved to {scope} memory (id: {data.get('id', '?')})")

@app.command()
def search(query: str, project: str = typer.Option(None), limit: int = 10):
    cfg = load_config()
    r = httpx.post(f"{cfg['server']}/mcp/memory/search",
                   json={"params": {"query": query, "project": project, "limit": limit}},
                   headers=headers(cfg))
    results = r.json().get("results", [])
    if not results:
        typer.echo("No results found.")
        return
    for i, res in enumerate(results, 1):
        scope_badge = "🔒" if res["scope"] == "personal" else "🌐"
        proj = f"[{res['project']}]" if res.get("project") else "[global]"
        typer.echo(f"\n{i}. {scope_badge} {res['entry_type'].upper()} {proj}")
        typer.echo(f"   {res['content'][:200]}")
        typer.echo(f"   id:{res['id'][:8]} | {res['created_at'][:10]} | sim:{res['similarity']:.2f}")

@app.command()
def configure(server: str, token: str):
    CONFIG_PATH.write_text(json.dumps({"server": server.rstrip("/"), "token": token}))
    typer.echo(f"✓ Config saved to {CONFIG_PATH}")

if __name__ == "__main__":
    app()
```

---

## 7. Ingestion Paths

### Primary: CC session end (via `memory_add` MCP tool)

CC's `CLAUDE.md` instructs it to call `memory_add` at session end. No automation needed — CC does this natively as part of its normal tool-use loop.

### Secondary: CLI (manual)

`memory add "..."` for decisions made outside a CC session, or to bulk-import existing notes.

### Optional: Git commit hook

A `post-commit` hook that parses conventional commit messages for `[memory]` tags:

```bash
#!/bin/bash
# .git/hooks/post-commit
COMMIT_MSG=$(git log -1 --pretty=%B)
if echo "$COMMIT_MSG" | grep -q "\[memory\]"; then
    CONTENT=$(echo "$COMMIT_MSG" | sed 's/\[memory\]//' | head -1)
    PROJECT=$(git rev-parse --show-toplevel | xargs basename)
    memory add "$CONTENT" --project "$PROJECT" --source git_hook --scope personal
fi
```

This is optional — Rob, Len, and Leslie can install it themselves. Not mandatory.

### Not recommended: CI/CD automated writes

Automated pipelines writing to org memory introduce noise. Human-confirmed writes only for org scope.

---

## 8. MCP Server: File Structure + Dockerfile + ECS Deployment

### File Structure

```
fip/mcp-memory/
├── src/
│   ├── server.ts          ← Express app + MCP HTTP transport
│   ├── auth.ts            ← Bearer token validation + user cache
│   ├── tools/
│   │   ├── search.ts      ← memory_search implementation
│   │   ├── add.ts         ← memory_add + org confirmation flow
│   │   ├── list.ts        ← memory_list
│   │   └── delete.ts      ← memory_delete
│   ├── db.ts              ← PostgreSQL + pgvector connection (pg + pgvector npm)
│   ├── embed.ts           ← Titan embed via Bedrock InvokeModel
│   └── admin.ts           ← CLI admin: add-user, reset-token, list-users
├── cli/
│   └── memory.py          ← Served at /cli/memory.py for curl-install
├── Dockerfile
├── buildspec.yml
├── package.json
└── tsconfig.json
```

**npm packages:** `express`, `@modelcontextprotocol/sdk`, `pg`, `pgvector`, `bcrypt`, `@aws-sdk/client-bedrock-runtime`, `@aws-sdk/client-secrets-manager`

**Port:** `8080` — matches the convention used by all other FIP services (FAIT, FIRM, FORMS). ALB routes `mcp.dev.fortressam.ai` → ECS target group on port 8080.

### Dockerfile

```dockerfile
# fip/mcp-memory/Dockerfile
FROM node:22-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src/ ./src/
RUN npm run build

FROM node:22-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production

# Install production deps only
COPY package*.json ./
RUN npm ci --omit=dev

COPY --from=builder /app/dist ./dist
COPY cli/ ./cli/

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

EXPOSE 8080
CMD ["node", "dist/server.js"]
```

### `src/db.ts` — RDS PostgreSQL Connection

```typescript
// Fetch DB credentials from Secrets Manager at startup
// Falls back to env vars for local dev (no Secrets Manager needed locally)

import { SecretsManagerClient, GetSecretValueCommand } from '@aws-sdk/client-secrets-manager';
import { Pool } from 'pg';

let pool: Pool | null = null;

async function getDbCredentials(): Promise<{
  host: string; port: number; database: string; user: string; password: string;
}> {
  // Local dev: use env vars directly
  if (process.env.PGHOST) {
    return {
      host:     process.env.PGHOST,
      port:     parseInt(process.env.PGPORT ?? '5432', 10),
      database: process.env.PGDATABASE ?? 'mcp_memory',
      user:     process.env.PGUSER ?? 'mcp_memory',
      password: process.env.PGPASSWORD ?? '',
    };
  }

  // AWS: fetch from Secrets Manager
  const sm = new SecretsManagerClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
  const secretId = process.env.DB_SECRET_ARN ?? 'mcp-memory/db-credentials';
  const resp = await sm.send(new GetSecretValueCommand({ SecretId: secretId }));
  return JSON.parse(resp.SecretString!) as {
    host: string; port: number; database: string; user: string; password: string;
  };
}

export async function getDb(): Promise<Pool> {
  if (pool) return pool;
  const creds = await getDbCredentials();
  pool = new Pool({
    host:     creds.host,
    port:     creds.port,
    database: creds.database,
    user:     creds.user,
    password: creds.password,
    ssl:      process.env.NODE_ENV === 'production' ? { rejectUnauthorized: true } : false,
    max:      5,
    idleTimeoutMillis: 30_000,
  });
  // Enable pgvector extension (idempotent)
  await pool.query('CREATE EXTENSION IF NOT EXISTS vector');
  return pool;
}
```

### ECS Service (Rhodey — infrastructure)

**ECS Task Definition:**
```
Family:           mcp-memory
CPU:              256
Memory:           512
Execution role:   ecsTaskExecutionRole (existing)
Task role:        mcp-memory-task-role (new — see IAM below)
Container name:   mcp-memory
Image:            <account>.dkr.ecr.us-east-1.amazonaws.com/mcp-memory:latest
Port:             8080
Log group:        /ecs/mcp-memory

Environment variables:
  NODE_ENV=production
  AWS_REGION=us-east-1
  DB_SECRET_ARN=arn:aws:secretsmanager:us-east-1:<account>:secret:mcp-memory/db-credentials
  BEDROCK_REGION=us-east-1
```

**IAM Task Role (`mcp-memory-task-role`) permissions:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["bedrock:InvokeModel"],
      "Resource": "arn:aws:bedrock:us-east-1::foundation-model/amazon.titan-embed-text-v2:0"
    },
    {
      "Effect": "Allow",
      "Action": ["secretsmanager:GetSecretValue"],
      "Resource": "arn:aws:secretsmanager:us-east-1:<account>:secret:mcp-memory/db-credentials*"
    }
  ]
}
```

**ECS Service:**
```
Cluster:          fortress-tools-cluster
Service name:     mcp-memory
Launch type:      FARGATE
Desired count:    1
Subnets:          private subnets (same as FAIT/FIRM)
Security group:   Allow inbound 8080 from ALB sg; allow outbound 5432 to RDS sg
```

### ALB + Route53 (Rhodey — infrastructure)

**Target group:**
```
Name:             mcp-memory-tg
Protocol:         HTTP
Port:             8080
Health check:     GET /health (200 OK)
```

**ALB listener rules** (add to `fortress-tools-alb`):
```
Dev:  Host: mcp.dev.fortressam.ai  → Target group: mcp-memory-dev-tg
Prod: Host: mcp.fortressam.ai      → Target group: mcp-memory-tg
```

**Route53** (hosted zone `fortressam.ai`):
```
mcp.dev.fortressam.ai  CNAME  fortress-tools-alb.us-east-1.elb.amazonaws.com
mcp.fortressam.ai      CNAME  fortress-tools-alb.us-east-1.elb.amazonaws.com
```

### `buildspec.yml`

```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com
      - IMAGE_TAG=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c1-7)
      - ECR_URI=$AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/mcp-memory
  build:
    commands:
      - docker build -t $ECR_URI:$IMAGE_TAG -t $ECR_URI:latest .
  post_build:
    commands:
      - docker push $ECR_URI:$IMAGE_TAG
      - docker push $ECR_URI:latest
      - aws ecs update-service --cluster fortress-tools-cluster --service mcp-memory --force-new-deployment
      - printf '[{"name":"mcp-memory","imageUri":"%s"}]' $ECR_URI:$IMAGE_TAG > imagedefinitions.json

artifacts:
  files:
    - imagedefinitions.json
```

### Health Endpoint

`GET /health` returns `{"status":"ok","service":"mcp-memory","timestamp":"..."}` — no auth required. Used by ALB health check and ECS health check.

---

## 9. `~/.claude/settings.json` MCP Configuration

Each user configures this on their Ubuntu VM:

```json
{
  "mcpServers": {
    "fip-memory": {
      "type": "http",
      "url": "https://mcp.fortressam.ai/mcp",
      "headers": {
        "Authorization": "Bearer <YOUR_TOKEN>"
      }
    }
  }
}
```

CC discovers tools via `GET /mcp` (MCP discovery endpoint). The server returns the tool manifest. CC then calls `POST /mcp` with JSON-RPC tool invocations.

**Dev env:** Use `https://mcp.dev.fortressam.ai/mcp` while testing. Switch to `https://mcp.fortressam.ai/mcp` for prod.

**Note on CC version:** CC must be ≥ v1.0.0 (streamable HTTP MCP transport). The version on the Ubuntu VMs must be verified. `claude --version` to check.

---

## 10. User Guide Outline

### What Goes Where

**Org memory** (`scope: org`) — shared, visible to Rob, Len, and Leslie:
- Platform-wide architecture decisions ("We use Redis for queue state across all FIP services")
- Naming conventions and patterns ("S3 bucket naming: `fip-<service>-workspaces`")
- Hard-won lessons that apply to the whole codebase ("Never use `docker build --no-cache` in the FIP buildspec — breaks layer caching")
- Infrastructure facts that change rarely ("ECS cluster: `fortress-tools-cluster`, region: `us-east-1`")
- **Not:** personal preferences, project-specific context, anything that changes weekly

**Personal memory** (`scope: personal`, default) — private:
- Your own notes from sessions ("I keep forgetting the correct import path for FipShared")
- Work-in-progress decisions you haven't confirmed with the team yet
- Personal workflow shortcuts
- Draft decisions before proposing them as org-wide

### Project Tagging

Use `--project <tag>` whenever the memory applies to a specific project. This narrows search results and reduces noise in other projects.

```bash
# Session on IAAPA work:
memory add "Client requires WCAG 2.1 AA compliance — affects all component choices" \
  --project iaapa --scope org --type context

# Session on FIRM:
memory add "Teams native transcription takes 5-15 min after meeting ends — not instant" \
  --project firm --type lesson
```

Searching without `--project` returns all results (org + personal), ordered by relevance.

### CC Configuration

1. Save token: `memory configure --server https://mcp.fortressam.ai --token <YOUR_TOKEN>`
2. Add MCP config to `~/.claude/settings.json` (see section 9)
3. Copy `CLAUDE.md` template to project root, set `<PROJECT_NAME>`
4. On next CC session: CC will call `memory_search` at start and `memory_add` at end automatically

---

## 11. Security Model

| Threat | Mitigation |
|--------|------------|
| User A reads User B's personal memory | `WHERE user_id = <caller_id>` in all personal queries — cannot be overridden |
| User writes to org without consent | Non-admin users get `confirmation_required` response; must call again with `confirmed: true` |
| Token leaked (e.g. in git history) | Token is bcrypt-hashed in DB; plaintext never stored; rotation via `memory-admin reset-token --username rob` |
| Replay attack | Tokens don't expire by default; rotation is manual; future: short-lived tokens via `/token/refresh` |
| Token brute-force | bcrypt comparison is slow (~100ms); rate limit `/mcp` to 60 req/min per IP at ALB |

**Token rotation:** `memory-admin reset-token --username rob` generates a new token, prints it once, updates the hash. Old token immediately invalid.

---

## 12. Local Dev Setup

For local development (Tony building the service), no Secrets Manager or ECS needed:

```bash
# Start a local pgvector container
docker run -d --name mcp-memory-dev \
  -e POSTGRES_USER=mcp_memory \
  -e POSTGRES_PASSWORD=dev \
  -e POSTGRES_DB=mcp_memory \
  -p 5432:5432 \
  pgvector/pgvector:pg16

# .env for local dev
PGHOST=localhost
PGPORT=5432
PGDATABASE=mcp_memory
PGUSER=mcp_memory
PGPASSWORD=dev
NODE_ENV=development
AWS_REGION=us-east-1
# AWS credentials needed for Titan embed (use your local ~/.aws/credentials)
```

Run: `npm run dev` (uses `ts-node-dev src/server.ts`). Server starts on port 8080.

---

## 13. New WI Needed

| WI | Title | Owner |
|----|-------|-------|
| WI#? | MCP Memory Server: RDS PostgreSQL instance + pgvector extension | Rhodey |
| WI#? | MCP Memory Server: ECR repo + ECS service + IAM role | Rhodey |
| WI#? | MCP Memory Server: ALB rules + Route53 CNAME for mcp.dev / mcp.prod | Rhodey |
| WI#? | MCP Memory Server: Express server + MCP tools + Dockerfile + buildspec | Tony |
| WI#? | MCP Memory Server: CLI tool (`memory.py`) + admin CLI | Tony |
| WI#? | CC Memory: CLAUDE.md template + onboarding guide for Rob/Len/Leslie | Reed |

---

_Spec by Reed Richards (updated 2026-03-18) | AWS ECS Fargate deploy. New RDS PostgreSQL t4g.micro + pgvector (Aurora MySQL incompatible). ECR repo: `mcp-memory`. Port 8080. Dev: `mcp.dev.fortressam.ai`, Prod: `mcp.fortressam.ai`. SteamServer local container discarded._
