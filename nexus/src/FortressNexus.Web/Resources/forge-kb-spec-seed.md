# FORGE KB MCP Server — Engineering Spec
**Version:** 1.0 | **Date:** 2026-04-27 | **Status:** Draft — Pending WI

---

## Table of Contents
1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Tool Definitions](#3-tool-definitions)
4. [Auth & Identity](#4-auth--identity)
5. [CloudFlare Brief for Rob](#5-cloudflare-brief-for-rob)
6. [Deployment](#6-deployment)
7. [FORGE KB Migration — What Needs to Happen](#7-forge-kb-migration--what-needs-to-happen)
8. [Open Questions / Prerequisites](#8-open-questions--prerequisites)

---

## 1. Overview

### What This Is

The FORGE KB MCP Server is a unified gateway for all Bedrock Knowledge Base operations across FIP apps. It exposes KB search, ingest, and metadata as standard MCP tools over HTTP/SSE — so FAIT v2 Claude Code agents (and any other FIP service) can call a single, authenticated endpoint instead of wiring `bedrock-agent-runtime` directly into each app.

### Why It Exists

Right now, direct `bedrock-agent-runtime` calls are scattered across FIRM, NEXUS, and FAIT v1. Every app manages its own AWS credentials, its own Bedrock client, its own scoping logic. That's three places to update when KB IDs change, three places that can get auth wrong, and no central audit trail for KB access.

This server centralizes all of that. One service owns Bedrock. All other apps call the MCP tools.

### Key Facts

| Property | Value |
|----------|-------|
| Service name | `fip-mcp` |
| ECS cluster | `fortress-tools-cluster` |
| Transport | HTTP/SSE (MCP over SSE — **NOT stdio**) |
| Auth | Entra OAuth bearer token (same pattern as MS365 MCP server) |
| Tech stack | Node.js + `@modelcontextprotocol/sdk` |
| Bedrock region | `us-east-1` |
| Phase | Phase 0/1 prerequisite for FAIT v2 |

### What It Replaces

- FIRM: direct `bedrock-agent-runtime` calls to push meeting summaries to Personal KB
- NEXUS: direct calls to NEXUS-Discovery KB (WHB6WU9CVW) for discovery question generation
- FAIT v1: direct KB retrieval calls in the backend
- Any future FIP service that would otherwise wire Bedrock directly

---

## 2. Architecture

### Component Diagram

```
┌──────────────────────────────────────────────────────────────────┐
│  FIP Clients                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌────────────┐               │
│  │  FAIT v2    │  │    FIRM     │  │   NEXUS    │  (future...)  │
│  │ CC Agents   │  │  (backend)  │  │  (backend) │               │
│  └──────┬──────┘  └──────┬──────┘  └─────┬──────┘               │
│         └────────────────┼───────────────┘                      │
│                          │  Entra JWT (Bearer)                   │
└──────────────────────────┼───────────────────────────────────────┘
                           │ HTTPS
                           ▼
              ┌────────────────────────┐
              │  CloudFlare Proxy      │
              │  api.fortressam.ai     │  ← /mcp/* route
              └────────────┬───────────┘
                           │
                           ▼
              ┌────────────────────────┐
              │  ALB (fortress-tools)  │
              │  host-based routing    │
              └────────────┬───────────┘
                           │
                           ▼
              ┌────────────────────────┐
              │  ECS: fip-mcp          │
              │  Fargate, 0.25 vCPU   │
              │  512MB RAM             │
              │  Node.js MCP Server    │
              └────────────┬───────────┘
                           │  AWS SDK (bedrock-agent-runtime)
                           ▼
              ┌────────────────────────────────────────────────────┐
              │  Bedrock Agent Runtime (us-east-1)                 │
              └────────────┬───────────────────────────────────────┘
                           │
         ┌─────────────────┼─────────────────┐
         ▼                 ▼                 ▼
  ┌────────────┐   ┌────────────┐   ┌────────────┐
  │  Corp KB   │   │ Personal   │   │  Team KB   │  ...etc
  │WYSKBKWHPL  │   │    KB      │   │NRGEACKSBJ  │
  └────────────┘   │ZCEZCJGHQC  │   └────────────┘
                   └────────────┘
```

### FORGE KB Inventory

All KB IDs the server manages. The stale `EE1X6QJ9WH` (FORGE-DevTeam-Shared) is excluded — do not reference it.

#### Production KBs

| KB Type | KB Name | KB ID | Data Source ID | Scoping Rule |
|---------|---------|-------|----------------|--------------|
| Corp | Corp KB | WYSKBKWHPL | O6DPFQ08WN | None (org-wide) |
| Personal | Personal KB | ZCEZCJGHQC | 3X5E9L4HAC | Auto-inject `user_id` from token |
| Team | Team KB | NRGEACKSBJ | VYMEB3BA12 | Auto-inject `team_id` from token |
| Project | Project KB | A5U1GKN0TS | QAP3QMUD5N | Require `project_id` in filters; validate access |
| NEXUS | NEXUS-Discovery KB | WHB6WU9CVW | C9P8RCCNSO | None (org-wide) |

#### Dev KBs

| KB Type | KB Name | KB ID | Data Source ID |
|---------|---------|-------|----------------|
| Corp | FORGE-Corp-Dev | AOFDTSHGNT | VEJXTDPXXR |
| Project | FORGE-Project-Dev | 70MDNR521D | UJUDDNJTE1 |
| Team | FORGE-Team-Dev | XLVSGM2BXH | ERBMWIFKG4 |
| Personal | FORGE-Personal-Dev | PBKCTCPNUU | JBYQ1PRBPC |

> **Note:** No dev equivalent for NEXUS-Discovery KB. Use production KB (WHB6WU9CVW) for NEXUS dev/test.

### Endpoint Design

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/mcp` | JSON-RPC 2.0 — tool calls, tool list, etc. |
| `GET` | `/mcp/sse` | SSE stream — server-initiated notifications |
| `GET` | `/health` | Health check (no auth required) |
| `GET` | `/admin/entitlements` | Entitlement admin (requires `forge-kb-admin` role) |

**JSON-RPC request shape (standard MCP):**
```json
{
  "jsonrpc": "2.0",
  "id": "req-123",
  "method": "tools/call",
  "params": {
    "name": "search_kb",
    "arguments": { "query": "...", "kb_id": "WYSKBKWHPL", "top_k": 5 }
  }
}
```

### CORS

The server needs to accept cross-origin requests. FAIT v2 runs on Azure; FIRM and NEXUS run on AWS. CORS must allow the known FIP origins:

```
https://fait.dev.fortressam.ai
https://fait.fortressam.ai
https://firm.dev.fortressam.ai
https://nexus.fortressam.ai
```

Plus any `*.fortressam.ai` sub-origin. Do NOT use `*` — auth headers won't work with wildcard CORS on credentialed requests.

The server sets CORS headers; CloudFlare must pass them through unmodified (see Section 5).

---

## 3. Tool Definitions

### 3.1 `search_kb`

**Purpose:** Retrieve semantically relevant chunks from a FORGE KB.

**Signature:**
```typescript
search_kb(args: {
  query: string,          // required — the search query
  kb_id: string,          // required — target KB ID (from KB inventory)
  top_k?: number,         // optional, default 5 — number of results to return
  filters?: object        // optional override — server auto-injects security filters;
                          // caller may add additional metadata filters on top
}) → {
  results: Array<{
    content: string,
    metadata: object,
    relevance_score: number   // 0.0–1.0
  }>
}
```

**Calls:** `bedrock-agent-runtime:Retrieve`

**Auto-scoping by KB type (security-enforced, not optional):**

| KB ID | Scoping Rule |
|-------|-------------|
| WYSKBKWHPL (Corp) | No filter — org-wide access |
| AOFDTSHGNT (Corp Dev) | No filter — org-wide access |
| ZCEZCJGHQC (Personal) | Auto-inject `{ metadataField: "user_id", value: token.oid }` |
| PBKCTCPNUU (Personal Dev) | Auto-inject `{ metadataField: "user_id", value: token.oid }` |
| NRGEACKSBJ (Team) | Require `team_id` in filters; validate `token.oid` is a member of that FAIT team (DB lookup: `team_memberships` table) |
| XLVSGM2BXH (Team Dev) | Require `team_id` in filters; validate `token.oid` is a member of that FAIT team (DB lookup: `team_memberships` table) |
| A5U1GKN0TS (Project) | Require `project_id` in `filters`; validate token has access to that project |
| 70MDNR521D (Project Dev) | Same as Project prod |
| WHB6WU9CVW (NEXUS) | No filter — org-wide, discovery context |

Server **merges** the auto-injected filter with any caller-supplied `filters` using AND logic. Callers cannot override the auto-injected scoping filter — it is always applied.

**Error cases:**
- `kb_id` not in inventory → 400 `UNKNOWN_KB`
- Caller not entitled to read the KB → 403 `NOT_ENTITLED`
- `project_id` missing for Project KB → 400 `PROJECT_ID_REQUIRED`
- Caller does not have access to requested `project_id` → 403 `PROJECT_ACCESS_DENIED`

---

### 3.2 `list_kbs`

**Purpose:** Returns the list of KBs the caller is entitled to read.

**Signature:**
```typescript
list_kbs() → {
  kbs: Array<{
    kb_id: string,
    kb_type: "corp" | "personal" | "team" | "project" | "nexus",
    description: string,
    writable: boolean
  }>
}
```

**Entitlement source (priority order):**

1. **DB table** (`kb_entitlements` in FAIT v2 DB — not yet created):
   ```sql
   CREATE TABLE kb_entitlements (
     user_id     VARCHAR(36) NOT NULL,   -- Entra oid
     kb_id       VARCHAR(20) NOT NULL,
     can_read    TINYINT(1)  NOT NULL DEFAULT 1,
     can_write   TINYINT(1)  NOT NULL DEFAULT 0,
     granted_by  VARCHAR(36) NOT NULL,
     granted_at  DATETIME    NOT NULL,
     PRIMARY KEY (user_id, kb_id)
   );
   ```

2. **Fallback static config** (pre-FAIT v2 DB): JSON file (`FALLBACK_ENTITLEMENTS_CONFIG` env var) mapping Entra group GUIDs → KB IDs + read/write flags. Example:
   ```json
   {
     "groups": {
       "<entra-group-guid-fam-team>": {
         "kbs": [
           { "kb_id": "WYSKBKWHPL", "read": true, "write": false },
           { "kb_id": "NRGEACKSBJ", "read": true, "write": true }
         ]
       }
     },
     "defaults": [
       { "kb_id": "WYSKBKWHPL", "read": true, "write": false },
       { "kb_id": "ZCEZCJGHQC", "read": true, "write": true }
     ]
   }
   ```
   All authenticated users get the `defaults` set. Group membership (from `token.groups`) adds additional entitlements.

**Note:** NEXUS KB (WHB6WU9CVW) is always read-only, always returned for authenticated users regardless of entitlements table — it's org-wide discovery context.

---

### 3.3 `add_to_kb`

**Purpose:** Ingest new content into a KB. Async — returns immediately with a job ID.

**Signature:**
```typescript
add_to_kb(args: {
  kb_id: string,       // required — target KB
  content: string,     // required — text content to ingest
  metadata: object     // required — must include { source: string, created_by: string }
                       // additional fields allowed (e.g., document_id, tags, project_id)
}) → {
  status: "queued",
  job_id: string,      // use with get_job_status to poll
  kb_id: string
}
```

**Calls:** `bedrock-agent-runtime:StartIngestionJob` (via S3 put + sync trigger, or direct API — implementation detail for Tony to resolve based on KB data source config).

**Behavior:**
- Validates caller has `write` entitlement for the KB
- Validates `metadata` includes `source` and `created_by` at minimum
- Writes content to the KB data source
- Returns `job_id` immediately — does NOT poll or wait
- Use `get_job_status(job_id)` to check completion

**Error cases:**
- Caller not entitled to write → 403 `WRITE_NOT_ENTITLED`
- `metadata.source` or `metadata.created_by` missing → 400 `METADATA_REQUIRED`
- Project KB: `metadata.project_id` required → 400 `PROJECT_ID_REQUIRED`
- NEXUS KB and Corp KB: write entitlement requires `forge-kb-admin` role

---

### 3.4 `get_kb_metadata`

**Purpose:** Get stats and config about a specific KB.

**Signature:**
```typescript
get_kb_metadata(args: {
  kb_id: string
}) → {
  kb_id: string,
  kb_type: string,
  document_count: number,
  last_updated: string,    // ISO 8601
  data_source_id: string
}
```

**Calls:** `bedrock-agent-runtime:GetKnowledgeBase` + `bedrock-agent-runtime:ListIngestionJobs` (to derive `last_updated` from the most recent completed job).

**Error cases:**
- `kb_id` not in inventory → 400 `UNKNOWN_KB`
- Caller not entitled to read → 403 `NOT_ENTITLED`

---

### 3.5 `get_job_status`

**Purpose:** Universal async polling tool for all FIP MCP async operations.

**Signature:**
```typescript
get_job_status(args: {
  job_id: string
}) → {
  status: "queued" | "running" | "complete" | "failed",
  result?: object,          // present when status = "complete"
  error?: string,           // present when status = "failed"
  percent_complete?: number  // 0–100, when available
}
```

**This is the FIP MCP async contract.** All future async tools in any FIP MCP server must return a `job_id` and expect callers to use this tool to poll. Do not build per-tool status endpoints.

For KB ingestion jobs: calls `bedrock-agent-runtime:GetIngestionJob`.

For future job types: the server maintains a `jobs` table (or in-memory map for MVP) keyed by `job_id` to route polling requests to the correct backend call.

**Note for Tony:** For MVP, in-memory job tracking is fine — the server is single-container, restarts are rare. Add DB-backed job tracking when needed.

---

### Tool Summary Table

| Tool | AWS API | Auth check |
|------|---------|------------|
| `search_kb` | `Retrieve` | Read entitlement + auto-scope filters |
| `list_kbs` | None (DB/config query) | Valid JWT only |
| `add_to_kb` | `StartIngestionJob` | Write entitlement |
| `get_kb_metadata` | `GetKnowledgeBase` + `ListIngestionJobs` | Read entitlement |
| `get_job_status` | `GetIngestionJob` (KB jobs) | Valid JWT only |

---

## 4. Auth & Identity

### Token Validation

Every request to `/mcp` or `/mcp/sse` must include a valid Entra OAuth bearer token:

```
Authorization: Bearer <JWT>
```

The server validates the token on every request:
- Signature verification against Entra JWKS endpoint
- `aud` claim must match `ENTRA_CLIENT_ID`
- `tid` claim must match `ENTRA_TENANT_ID`
- Token must not be expired

No token → 401. Invalid/expired token → 401. Valid token, insufficient entitlements → 403.

### Token Claims Used

| Claim | Field | Used For |
|-------|-------|----------|
| `oid` | `user_id` | Personal KB auto-scoping; entitlement lookups |
| `groups` | `team_ids[]` | Team KB auto-scoping; group-based entitlement fallback |
| `tid` | tenant ID | Tenant validation |
| `roles` | role claims | `forge-kb-admin` check for admin endpoints |

### KB Auto-Scoping Rules (Security Enforcement)

These are **security rules**, not UX. The server enforces them regardless of what the caller passes in `filters`.

| KB Type | Filter Injected | Source |
|---------|----------------|--------|
| Corp | None | — |
| Personal | `user_id = token.oid` | Entra token |
| Team | `team_id` from caller filters | FAIT `team_memberships` DB table (caller passes `team_id`, server validates membership) |
| Project | `project_id` from caller filters | Validated against entitlements |
| NEXUS | None | — |

> **FAIT Teams vs. Entra Groups (design decision 2026-04-27):** Team KB scoping uses FAIT's internal team membership model, not Entra Groups. Users create and manage teams through the FAIT settings control panel. Team membership is stored in the FAIT v2 DB (`team_memberships` table). This keeps IT out of the loop for user-formed working groups (deal teams, project squads, etc.) and gives FAIT full control over the team membership lifecycle. Entra Groups are used only for tenant-level authentication (`tid` claim) — not for KB access scoping.

### Admin Endpoints

`GET /admin/entitlements` — requires `forge-kb-admin` role claim in the token's `roles` array.

Returns the full entitlements table (or fallback config) for audit purposes. Supports optional `?user_id=<oid>` query param to filter by user.

---

## 5. CloudFlare Brief for Rob

> **Note to Fred:** Rob Nethery is the CF admin. His entity file is at `memory/entities/rob-nethery.md`. Copy-paste the section below into a Slack DM or email when ready to configure the CF route. Domain/path below (`api.fortressam.ai/mcp/*`) is the expected pattern — confirm with Rob whether that subdomain/path is already wired or needs new CF config.

---

**To: Rob**
**Re: New `/mcp` route on api.fortressam.ai — a few CF config asks**

We're deploying a new internal service (`fip-mcp`) and need a CF route set up. Here's what I need:

- **Route:** `https://api.fortressam.ai/mcp/*` → forward to the ALB (same ALB as other FIP services)
  _(Let me know if this path conflicts with anything or if a separate subdomain like `mcp.fortressam.ai` makes more sense)_

- **SSE connections — do not terminate early:** The `/mcp/sse` path uses Server-Sent Events (long-lived HTTP connections). CF's default 100s proxy timeout will kill them. Need that timeout set to at least 300s (600s preferred) for the `/mcp/sse` route specifically.

- **No response buffering on `/mcp/sse`:** CF's response buffering breaks SSE streaming. Needs to be disabled for that route so events flow through in real-time.

- **CORS headers — pass through unmodified:** The server sets its own `Access-Control-Allow-*` headers. CF should not add, modify, or strip them. If CF is configured to inject CORS headers globally, that needs a carve-out for `/mcp/*`.

- **No special caching needed:** `/mcp/*` should be cache-bypass (no-store). These are live API calls.

That's it — nothing exotic. Just the SSE timeout + buffering + CORS passthrough. Let me know if you have questions or if the routing path needs adjustment.

---

## 6. Deployment

### ECS Service

| Property | Value |
|----------|-------|
| Service name | `fip-mcp` |
| Cluster | `fortress-tools-cluster` |
| Launch type | Fargate |
| vCPU | 0.25 (256) — scale up if needed |
| Memory | 512 MB — scale up if needed |
| Desired count | 1 (always-on) |
| ECR repo | `fip-mcp` |
| Container port | 3000 (or 8080 — Tony's call, document it) |

This is a lightweight, stateless Node.js server. 0.25 vCPU / 512MB is sufficient for the expected call volume. Scale out horizontally (desired count) before scaling up the task definition.

### ECR Repository

Repo `fip-mcp` needs to be created in `742932328420.dkr.ecr.us-east-1.amazonaws.com`. Add to the standard FIP ECR lifecycle policy.

### IAM Role

The ECS task execution role needs the following Bedrock permissions (resource: `*` for now — scope down to specific KB ARNs in a follow-up):

| Permission | Used By |
|-----------|---------|
| `bedrock-agent-runtime:Retrieve` | `search_kb` |
| `bedrock-agent-runtime:StartIngestionJob` | `add_to_kb` |
| `bedrock-agent-runtime:GetKnowledgeBase` | `get_kb_metadata` |
| `bedrock-agent-runtime:ListIngestionJobs` | `get_kb_metadata` |
| `bedrock-agent-runtime:GetIngestionJob` | `get_job_status` |

File IAM request when the service is ready to deploy.

### Environment Variables

| Variable | Value | Notes |
|----------|-------|-------|
| `ENTRA_TENANT_ID` | `<from Secrets Manager>` | Required |
| `ENTRA_CLIENT_ID` | `<from Secrets Manager>` | Required |
| `BEDROCK_REGION` | `us-east-1` | Hard-coded default |
| `DB_CONNECTION_STRING` | `<from Secrets Manager>` | For `kb_entitlements` table (once FAIT v2 DB exists) |
| `FALLBACK_ENTITLEMENTS_CONFIG` | `/app/config/entitlements.json` | Path to fallback config file (baked into image or mounted) |
| `PORT` | `3000` | Server listen port |
| `NODE_ENV` | `production` | |
| `LOG_LEVEL` | `info` | Adjust to `debug` in dev |

Store secrets (`ENTRA_TENANT_ID`, `ENTRA_CLIENT_ID`, `DB_CONNECTION_STRING`) in Secrets Manager and inject via ECS secrets config — do not bake into the image or pass as plaintext env vars.

### Tech Stack

- **Runtime:** Node.js 22 (LTS — consistent with other FIP tooling)
- **MCP scaffolding:** `@modelcontextprotocol/sdk` — use the `McpServer` class with SSE transport (`SSEServerTransport`)
- **HTTP server:** Express.js (or Fastify — Tony's call; document choice in README)
- **AWS SDK:** `@aws-sdk/client-bedrock-agent-runtime`
- **Auth:** `jsonwebtoken` + Entra JWKS endpoint (`jwks-rsa` or `@azure/msal-node` for validation)
- **Build:** Standard Dockerfile, Node 22 alpine base image

### ALB Target Group

Add `fip-mcp` as a new target group on the existing ALB. Host-based routing rule:
- If header `Host` = `api.fortressam.ai` AND path starts with `/mcp` → forward to `fip-mcp` target group

(Coordinate with CF route setup in Section 5.)

---

## 7. FORGE KB Migration — What Needs to Happen

> **Scope note:** Migration is NOT part of the initial `fip-mcp` build. This section flags the follow-on WIs so they get filed when the server goes live.

> **Current MCP adapter pattern in FAIT v1:** MS365, ADO, and Brave Search are currently implemented as loopback-internal adapters inside the FAIT container (seeded to `localhost:8080/internal/mcp/*` via `DatabaseInitializationService`). They are NOT standalone services. FAIT v2 will migrate all of these to external `fip-mcp` tool groups — see FAIT v2 spec Section 7 for the full MCP gateway architecture.

### FIRM

FIRM's summarization pipeline (`firm-transcriber` Batch container) calls `bedrock-agent-runtime` directly to push meeting summaries to the Personal KB.

**Action when fip-mcp is live:** Migrate the `StartIngestionJob` call to `add_to_kb`. File WI against FIRM after `fip-mcp` deploy.

### NEXUS

NEXUS calls the NEXUS-Discovery KB (WHB6WU9CVW) directly to generate discovery questions.

**Action when fip-mcp is live:** Migrate the `Retrieve` call to `search_kb`. File WI against NEXUS after `fip-mcp` deploy.

### FAIT v1

FAIT v1 backend has direct KB retrieval calls used for agent context injection.

**Action when fip-mcp is live:** Migrate `Retrieve` calls to `search_kb`. Coordinate with FAIT v2 timeline — may be skipped if FAIT v1 is being retired in parallel.

---

## 8. Open Questions / Prerequisites

| # | Question / Prerequisite | Owner | Status |
|---|------------------------|-------|--------|
| 1 | **FAIT v2 DB must exist** before `kb_entitlements` table can be created. Fallback static config bridges this gap — ship the server with fallback config, migrate to DB table when FAIT v2 DB is live. | Fred / FAIT v2 timeline | Pending |
| 2 | **IAM permissions** — deployer role needs `bedrock-agent-runtime:*` added. File IAM request when ready to deploy. | Fred → AWS | Not filed |
| 3 | **CF route** — confirm domain/path with Rob. Is `api.fortressam.ai/mcp/*` correct? Or separate subdomain (`mcp.fortressam.ai`)? | Rob Nethery | Not started |
| 4 | **ECR repo `fip-mcp`** needs to be created before first build/push. | Tony / pipeline | Not created |
| 5 | **Team KB — team_memberships table schema** — FAIT v2 DB needs a `team_memberships` table `(team_id, user_id, role, joined_at)` and a `teams` table `(team_id, name, created_by, created_at)`. Schema to be designed as part of FAIT v2 Phase 1 DB work. This table is a prerequisite for Team KB entitlement validation. | Fred / FAIT v2 DB | Pending |
| 6 | **Project KB data source ID** — `A5U1GKN0TS` has no data source ID in current records. Confirm or create. | Fred | Missing |
| 7 | **S3 trigger vs direct ingestion** — `add_to_kb` can write via S3 put + sync trigger or call `StartIngestionJob` directly. Depends on KB data source config. Tony to verify against actual KB setup. | Tony | Pending |
| 8 | **MCP SDK version** — confirm `@modelcontextprotocol/sdk` version to pin. Use latest stable at time of build; document in `package.json`. | Tony | At build time |

---

## Appendix — File Locations

| File | Path |
|------|------|
| KB inventory | `memory/topics/bedrock-forge.md` |
| FIP architecture | `memory/topics/fip-architecture.md` |
| Rob Nethery entity | `memory/entities/rob-nethery.md` |
| Auth standards | `memory/topics/auth-standards.md` |
| DB access reference | `memory/ops/db-access-reference.md` |
| This spec | `memory/projects/forge-kb-mcp-server-spec-2026-04-27.md` |
