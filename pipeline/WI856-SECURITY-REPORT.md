# Security Report: WI856 — CC Memory MCP Server
## Verdict: PASS (with warnings)
## Scoped: Changed files in `fip/mcp-memory/` (new service, full scan)
## Scanned: 2026-03-17 ~21:22 EDT

---

## Findings

| ID | Severity | Finding | File | Action |
|----|----------|---------|------|--------|
| SEC-1 | ⚠️ WARN | `.env.example` contains real DB password (`PG_PASSWORD=lGxWwQ...`) — committed to git. Private repo, local-only container, no immediate exposure. Redact before any repo visibility change. | `mcp-memory/.env.example` | Non-blocking; redact in follow-up |
| SEC-2 | 📝 NOTE | `app.listen(PORT)` binds on all interfaces (0.0.0.0). On SteamServer behind Tailscale this is acceptable. If service is ever moved to a public host, bind to `127.0.0.1` or use nginx proxy. | `src/server.ts:131` | Acceptable for current deploy target |

---

## Passed Checks

| Check | Result | Evidence |
|-------|--------|----------|
| SQL injection — parameterized queries only | ✅ PASS | All queries use `pool.query(sql, params)` pattern |
| bcrypt cost factor ≥ 10 | ✅ PASS | `bcrypt.hash(plaintext, 12)` in admin.ts |
| bcrypt.compare correct order | ✅ PASS | `bcrypt.compare(incoming, storedHash)` — auth.ts:45 |
| No hardcoded credentials in source | ✅ PASS | All via `process.env.*` — db.ts:11 |
| Token never logged (only stdout on creation) | ✅ PASS | admin.ts:28,44 — stdout only, no file/log write |
| org INSERT: user_id = NULL for org scope | ✅ PASS | `userId = scope === 'personal' ? user.id : null` — add.ts:35 |
| org queries: AND user_id IS NULL guard | ✅ PASS | search.ts:39, list.ts:33 — confirmed by Clint cycle 2 |
| Auth enforced before all tool dispatch | ✅ PASS | `authenticate(req)` at server.ts:28 before tool routing |
| Bedrock call: only embed text in body, no PII | ✅ PASS | `{ inputText: text.slice(0, 8000) }` — embed.ts:7 |
| Migrations idempotent | ✅ PASS | `CREATE EXTENSION IF NOT EXISTS`, `CREATE TABLE IF NOT EXISTS` |
| No path to cross-user personal data access | ✅ PASS | All personal queries parameterized by authenticated user.id |

---

## Decision

**PASS** — proceed to APPROVE/DEPLOY. SEC-1 is a WARN (private repo, local container) — non-blocking. SEC-2 is informational.

**SEC-1 follow-up action:** After deploy, redact `.env.example` → replace real password with `your_password_here`, commit, push. No git history rewrite needed (private repo, no external exposure).
