# fip-mcp — FORGE KB MCP Server

Phase 0 implementation. Node.js 22 MCP server exposing FORGE KB (Bedrock Knowledge Base) operations as MCP tools over HTTP/SSE.

## HTTP Framework

**Express.js** — chosen over Fastify for:
- Consistency with mcp-memory (existing FIP MCP server uses Express)
- Simpler middleware model for one-off auth injection
- Mature ecosystem, well-understood error handling

## Endpoints

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | /health | None | Health check |
| POST | /mcp | Entra Bearer JWT | JSON-RPC 2.0 MCP tool calls (Streamable HTTP) |
| GET | /mcp/sse | Entra Bearer JWT | SSE stream |
| GET | /admin/entitlements | Entra Bearer JWT + forge-kb-admin role | Entitlement admin |

## Tools

| Tool | Description |
|------|-------------|
| search_kb | Retrieve from FORGE KB with auto-scoped security filters |
| list_kbs | List KBs caller is entitled to access |
| add_to_kb | Async ingest content into a KB — returns job_id |
| get_kb_metadata | Get KB stats (GetKnowledgeBase + ListIngestionJobs) |
| get_job_status | Poll async job status (universal FIP MCP async contract) |

## Auth

Entra OAuth Bearer JWT. Validated against JWKS endpoint on every request. User context is propagated to all tool handlers via per-request closure (no MCP metadata injection required).

Required env vars:
- `ENTRA_TENANT_ID`
- `ENTRA_CLIENT_ID`

## KB Inventory

All KB IDs and scoping rules are in `src/config/kb-inventory.js`. Tool logic never hardcodes KB IDs.

## Phase 0 Simplifications

- Fallback static entitlements only (`src/config/entitlements.json`) — no FAIT v2 DB dependency
- In-memory job tracking (lost on restart) — DB-backed jobs in a future phase
- Team KB: requires `team_id` param but skips DB membership validation
- `add_to_kb`: calls StartIngestionJob to trigger sync — S3 content write TBD (see spec §8 #7)

## Container

Port: 3000
Base: `node:22-alpine`
ECR: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp`

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ENTRA_TENANT_ID` | Entra tenant ID | Required |
| `ENTRA_CLIENT_ID` | Entra client ID | Required |
| `BEDROCK_REGION` | AWS region for Bedrock | `us-east-1` |
| `FALLBACK_ENTITLEMENTS_CONFIG` | Path to entitlements.json | Bundled default |
| `PORT` | Server listen port | `3000` |
| `NODE_ENV` | Environment | `production` |
