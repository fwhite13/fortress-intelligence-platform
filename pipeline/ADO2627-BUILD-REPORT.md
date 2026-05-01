# Build Report — ADO#2627: fip-mcp FORGE KB MCP Server Phase 0

**Built by:** Tony Stark (software-engineer)
**Date:** 2026-05-01
**Commit:** `11aab1f` — feat(ADO#2627): fip-mcp FORGE KB MCP Server Phase 0 — 5 tools, Entra auth, fallback entitlements

---

## What Was Built

A new Node.js 22 MCP server at `/home/fredw/projects/fip/services/fip-mcp/` that exposes FORGE KB (AWS Bedrock Knowledge Base) operations as 5 MCP tools over HTTP/SSE. Uses Entra JWT auth, fallback static entitlements, and in-memory job tracking per Phase 0 spec.

---

## HTTP Framework: Express.js

**Rationale:** Consistent with the existing `mcp-memory` FIP MCP server (which also uses Express). Simpler middleware model for per-request auth + user closure injection. Mature ecosystem, well-understood patterns for both Streamable HTTP and SSE transport. No performance gap at the expected FIP call volumes.

---

## File Structure

```
services/fip-mcp/
  .gitignore
  Dockerfile
  README.md
  package.json                    — ESM module, both bedrock clients, Express + JWT libs
  package-lock.json
  src/
    server.js                     — Express app, CORS, MCP tool registration via closure pattern
    auth.js                       — Entra JWT validation via jwks-rsa + jsonwebtoken
    config/
      kb-inventory.js             — All KB IDs, types, scoping rules, data source IDs
      entitlements.json           — Phase 0 fallback: Corp+Personal+NEXUS read for all users
    tools/
      search_kb.js                — bedrock-agent-runtime:Retrieve + auto-scope security filters
      list_kbs.js                 — entitlement resolution from fallback static config
      add_to_kb.js                — bedrock-agent:StartIngestionJob, writes to job map
      get_kb_metadata.js          — bedrock-agent:GetKnowledgeBase + ListIngestionJobs
      get_job_status.js           — bedrock-agent:GetIngestionJob + in-memory job map
```

---

## Tool Implementation Notes

### search_kb
- Uses `@aws-sdk/client-bedrock-agent-runtime` (data plane — `RetrieveCommand`)
- Auto-scope security filters applied by KB type BEFORE any caller filters
- TEAM KB: requires `team_id` in caller `filters`, Phase 0 skips DB membership validation
- PROJECT KB: requires `project_id` in caller `filters`
- CORP + NEXUS: no filter (org-wide)
- PERSONAL: auto-injects `user_id = token.oid`
- Caller cannot override injected security filters (reserved key list enforced)

### list_kbs
- Reads fallback `entitlements.json` at startup
- Merges `defaults` (all users) + `groups` (Entra group GUIDs → additional KBs)
- NEXUS KB always readable for authenticated users regardless of entitlements table
- Phase 0: no FAIT v2 DB dependency

### add_to_kb
- Uses `@aws-sdk/client-bedrock-agent` (management plane — `StartIngestionJobCommand`)
- **Phase 0 note:** Calls `StartIngestionJob` to trigger a KB data source sync. The S3 content write step (actually placing `content` in the data source bucket before ingestion) is NOT yet implemented — see spec §8 open question #7. Tony notes: this needs a follow-up WI to implement the S3 put step. For now, the job is queued and a `job_id` is returned.
- Corp KB + NEXUS KB: require `forge-kb-admin` role for write
- validates `metadata.source` + `metadata.created_by` required fields

### get_kb_metadata
- Uses `@aws-sdk/client-bedrock-agent` (management plane)
- `GetKnowledgeBase` + `ListIngestionJobs` (sorted by STARTED_AT desc, takes most recent COMPLETE job for `last_updated`)
- `document_count` is approximated from summed `numberOfDocumentsIndexed` across completed jobs
- Missing data source ID (Project KB: `A5U1GKN0TS`) handles gracefully — job stats skipped, non-fatal

### get_job_status
- In-memory `jobMap` (Map keyed by `job_id`) — Phase 0 acceptable, single container
- Bedrock status mapping: `STARTING/IN_PROGRESS/STOPPING → running`, `COMPLETE → complete`, `FAILED/STOPPED → failed`
- On restart, unknown job_id returns `status: "unknown"` with explanatory message

### User Context Architecture
- `createMcpServer(user)` factory pattern — user captured via closure
- All 5 tool handlers close over `user` from the validated JWT
- No `_meta` injection or request context leakage needed
- Each POST /mcp request creates a fresh McpServer + StreamableHTTPServerTransport instance (stateless per-request)
- SSE sessions tracked in `sseSessions` Map for multi-request lifecycle

---

## ECR Repo Creation

```
aws ecr create-repository --repository-name fip-mcp \
  --profile fortress-tools-deployer --region us-east-1
```

**Result:** ✅ SUCCEEDED
**URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp`
**Created at:** 2026-05-01T11:09:43.579000-04:00

---

## npm install + Load Verification

```
cd /home/fredw/projects/fip/services/fip-mcp && npm install
```
**Result:** ✅ Clean install, no errors.

```
ENTRA_TENANT_ID=test ENTRA_CLIENT_ID=test node src/server.js
```
**Result:** ✅ Server starts cleanly:
```
[fip-mcp] FORGE KB MCP Server v1.0.0 listening on port 3000
[fip-mcp] Entra tenant: test
[fip-mcp] Entra client: test
[fip-mcp] Bedrock region: us-east-1
[fip-mcp] Entitlements config: (bundled default)
```

```
curl -s http://localhost:3000/health
```
**Result:** ✅ `{"status":"ok","version":"1.0.0"}`

---

## Self-Review Checklist

- [x] All 5 tools implemented — search_kb, list_kbs, add_to_kb, get_kb_metadata, get_job_status
- [x] /health returns 200 no-auth — verified via curl
- [x] JWT validation on /mcp routes — authMiddleware applied to POST /mcp and GET /mcp/sse
- [x] KB inventory in config (not hardcoded in tool logic) — all KB IDs in `src/config/kb-inventory.js`
- [x] CORS set for fortressam.ai origins — explicit list + `*.fortressam.ai` regex, no wildcard `*`
- [x] Fallback entitlements.json ships with default Corp+Personal+NEXUS read access — verified
- [x] ECR repo created — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp`
- [x] Dockerfile builds cleanly — multi-stage Node 22 alpine, port 3000, healthcheck

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **add_to_kb S3 write gap (Phase 0):** `StartIngestionJob` is called but content is NOT written to S3 first. This means the ingestion job will sync whatever is already in the data source bucket, not the new content. Follow-up WI needed to implement the S3 PutObject step before triggering ingestion. Documented in code with TODO comment.

2. **In-memory job map:** `jobMap` is lost on server restart. Callers polling `get_job_status` after a restart will get `status: "unknown"`. Acceptable for Phase 0 per spec.

3. **Team KB validation (Phase 0 skip):** Team membership DB validation is skipped. Any user with a valid JWT can pass any `team_id` for Team KB queries. Phase 0 acceptable per spec — will need `team_memberships` table when FAIT v2 DB is live.

4. **McpServer per-request instantiation:** A new `McpServer` + transport is created per POST /mcp request. This is correct for stateless HTTP but slightly heavier than a shared server. For SSE, the server lives for the connection duration. This matches the pattern in mcp-memory.

5. **`zod` dependency:** `McpServer.tool()` requires zod schemas — `zod` is a peer/transitive dependency of `@modelcontextprotocol/sdk`. It's not explicitly in our package.json but resolves fine. If SDK version changes, add `zod` as an explicit dependency.

---

## How to Test Locally

```bash
# 1. Set env vars
export ENTRA_TENANT_ID=7152ea12-c930-44b0-bb52-069152161c5b
export ENTRA_CLIENT_ID=eda4d502-8c93-422e-b7fb-bb922a2a472e
export AWS_PROFILE=fortress-tools-deployer

# 2. Start server
cd /home/fredw/projects/fip/services/fip-mcp
node src/server.js

# 3. Health check (no auth)
curl http://localhost:3000/health

# 4. MCP tool call (requires valid Entra Bearer token)
curl -X POST http://localhost:3000/mcp \
  -H "Authorization: Bearer <entra-token>" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":"1","method":"tools/list","params":{}}'

# 5. SSE stream test
curl -N http://localhost:3000/mcp/sse \
  -H "Authorization: Bearer <entra-token>"
```

---

## Build Report Summary

| Item | Status |
|------|--------|
| 5 tools implemented | ✅ |
| Entra JWT auth | ✅ |
| Fallback entitlements.json | ✅ |
| KB inventory in config | ✅ |
| CORS for fortressam.ai | ✅ |
| ECR repo fip-mcp | ✅ Created |
| npm install | ✅ Clean |
| Server load test | ✅ |
| /health endpoint | ✅ 200 no-auth |
| Git commit | ✅ `11aab1f` |

**Sending to Clint for review.**
