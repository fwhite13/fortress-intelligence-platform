# Active Pipeline Session Context — WI856

## Pipeline: WI856 — CC Memory MCP Server
## Started: 2026-03-17 ~20:25 EDT
## Risk: high | Path: full

---

### [20:25] Maria — Architecture flag for WI856

**DB connectivity issue:** The spec says "the pgvector instance already running for OpenClaw's own memory" but that is the `openclaw-rag` Docker container on SteamServer (localhost:5433, DB=rag, user=jarvis). An ECS container cannot reach this — no network path.

**Decision for this sprint:** Deploy as systemd service on SteamServer (same pattern as vendorply-triage.service), NOT ECS. This matches the actual network reality. ECS + ALB routing is a follow-up WI once the DB is reachable externally.

**Deploy pattern:** `git pull` + `npm run build` + `sudo systemctl restart mcp-memory.service`
**Service URL while on SteamServer:** accessible via Tailscale at `https://steamserver.tail7a7e88.ts.net:3100` (no ALB needed for Rob/Len/Leslie on their VMs)

### pgvector DB connection details
- Host: localhost (or 127.0.0.1 from SteamServer)
- Port: 5433 (Docker host port)
- DB: rag
- User: jarvis
- Password: lGxWwQYRsIcUOLJeuzcTNkn4lJOBLk7e
- (ECS env vars: use these same values when ECS deploy happens later)

### Critical flags for Tony
1. **No ECS for this sprint** — systemd on SteamServer (like vendorply)
2. **Repo location**: `~/projects/fip/mcp-memory/` (inside the fip monorepo per spec §8)
3. **New tables go in the existing `rag` pgvector DB** — `cc_memory_users` + `cc_memory_entries`
4. **bcrypt for token storage** — never store plaintext tokens; only the bcrypt hash in DB
5. **user_id filter on ALL queries** — personal scope: `WHERE user_id = $1`; org scope: `WHERE user_id IS NULL`; NEVER return another user's personal memories
6. **MCP transport**: streamable HTTP (2025 MCP spec) via `@modelcontextprotocol/sdk`
7. **Port 3100** — same as spec
8. **Titan embed v2**: `amazon.titan-embed-text-v2:0` via `@aws-sdk/client-bedrock-runtime`; use existing AWS credentials (same as other services)
9. **memory.py CLI**: goes in `cli/` subdirectory; served at `/cli/memory.py` endpoint
10. **npm packages allowed**: `express`, `@modelcontextprotocol/sdk`, `pg`, `pgvector`, `bcrypt`, `@aws-sdk/client-bedrock-runtime`
