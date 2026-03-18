# Pipeline Completion: WI856 — CC Memory MCP Server

## Outcome: DEPLOYED ✅
**Date:** 2026-03-17
**Total pipeline time:** ~69 minutes (20:25 build → 21:33 confirm)

## What Shipped
`mcp-memory.service` — pgvector-backed memory MCP server running on SteamServer:3100.
- 4 MCP tools: `memory_add`, `memory_list`, `memory_search`, `memory_delete`
- Bearer token auth (bcrypt hashed, 5-min cache)
- Per-user isolation: personal scope (user_id = UUID) + org scope (user_id = NULL)
- Titan embed v2 (1024-dim) via Bedrock InvokeModel
- Startup migration: checks `atttypmod` and auto-corrects vector column if needed
- Admin CLI: `node dist/admin.js add-user|reset-token|list-users`
- Python CLI served at `/cli/memory.py`

## Pipeline Summary
- PLAN → BUILD (Tony, CC Sonnet, 2 commits) → REVIEW PASS cycle 2 (Clint: vector dim fix + org IS NULL) → SECURITY PASS (1 non-blocking WARN: .env.example — redacted b3ce226) → DEPLOY (Rhodey, systemd) → VERIFY PASS 10/10 (QA retry: startup migration fix b3ce226)
- Review cycles: 2
- QA attempts: 2 (first fail: CREATE TABLE IF NOT EXISTS missed existing column; fixed inline)
- Total commits: fbf93f1, 71ddced, 1631ee8, 5645ef8, b3ce226

## Artifacts
- WI856-BUILD-REPORT.md
- WI856-REVIEW-REPORT.md
- WI856-SECURITY-REPORT.md
- WI856-DEPLOY-REPORT.md
- WI856-QA-REPORT.md

## Lessons Logged
- `CREATE TABLE IF NOT EXISTS` silently skips column type changes on existing tables. Startup migrations that change column types need an explicit ALTER path (check pg_attribute.atttypmod and ALTER if mismatch).
- Titan embed v2 (`amazon.titan-embed-text-v2:0`) outputs 1024 dims by default — NOT 1536. The 1536 dim is Titan v1. Future pgvector work: always use `vector(1024)` for Titan v2.

## Service Details
- URL: http://localhost:3100 (SteamServer, Tailscale-accessible)
- Systemd: mcp-memory.service (not enabled for reboot — `sudo systemctl enable` when ready)
- DB: openclaw-rag container, rag database, cc_memory_users + cc_memory_entries tables
- Admin: `cd ~/projects/fip/mcp-memory && node dist/admin.js add-user --username <name> --email <email>`

## Next: WI#857 (FIRM v2) — pending Entra admin consent + app reg rename
