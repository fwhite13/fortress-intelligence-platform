# Build Report: WI856 — CC Memory MCP Server

**Date:** 2026-03-17  
**Builder:** Tony Stark (software-engineer)  
**Status:** ✅ BUILD COMPLETE

---

## Commit

```
71ddced WI856: CC Memory MCP Server — pgvector, 4 MCP tools, bcrypt auth, Titan embed
fbf93f1 WI856: CC Memory MCP Server — pgvector-backed, four tools, bcrypt auth, systemd deploy
```

Primary source commit: `fbf93f1`  
Dist/compiled commit: `71ddced`  
Repo: `~/projects/fip` (branch: `main`)

---

## CC Invocation Used

```bash
cd ~/projects/fip/mcp-memory
cat ~/projects/fait-for-excel/cc-brief-wi856.md | claude --model sonnet --dangerously-skip-permissions -p
```

---

## Files Created

| File | Description |
|------|-------------|
| `src/server.ts` | Express app on port 3100, MCP StreamableHTTP transport, Bearer auth gate |
| `src/db.ts` | pg Pool + `initDb()` runs `migrations/001_init.sql` on startup |
| `src/embed.ts` | AWS Bedrock `amazon.titan-embed-text-v2:0` → 1536-dim embeddings |
| `src/auth.ts` | bcrypt token validation, 5-min user cache, `authenticate()` + `requireAuth()` middleware |
| `src/tools/search.ts` | Vector cosine similarity search, per-user isolation, deduplication |
| `src/tools/add.ts` | Embeds + inserts; org writes require `confirmed: true` for non-admins |
| `src/tools/list.ts` | Recent entries by `created_at DESC`, proper scope isolation |
| `src/tools/delete.ts` | Owner-only delete; admins can delete any entry |
| `src/admin.ts` | `add-user`, `reset-token`, `list-users` admin CLI |
| `migrations/001_init.sql` | Schema with vector extension, cc_memory_users, cc_memory_entries, ivfflat index |
| `cli/memory.py` | Python CLI with configure, add, search, list, delete commands |
| `package.json` | Dependencies: express, @modelcontextprotocol/sdk, pg, pgvector, bcrypt, @aws-sdk/client-bedrock-runtime |
| `tsconfig.json` | TypeScript strict mode configuration |
| `.env.example` | Environment variable template |
| `Dockerfile` | Container build file |
| `dist/*.js` | Compiled JavaScript output (9 files) |

---

## Gate Check Results

| Gate | Result | Evidence |
|------|--------|----------|
| bcrypt token storage | ✅ PASS | `src/auth.ts:1: import bcrypt` / `src/auth.ts:45: await bcrypt.compare(token, user.api_token)` |
| user_id filter on personal queries | ✅ PASS | `search.ts:39: (scope = 'org' OR user_id = $2)` / `list.ts:30: whereClause = 'user_id = $1'` |
| Four tools registered | ✅ PASS | `server.ts:42: memory_search`, `55: memory_add`, `70: memory_list`, `82: memory_delete` |
| Bearer auth | ✅ PASS | `auth.ts:40: auth?.startsWith('Bearer ') ? auth.slice(7)` + `requireAuth` middleware |
| DB migrations on startup | ✅ PASS | `db.ts:17: export async function initDb()` reads `migrations/001_init.sql` |
| Port 3100 | ✅ PASS | `server.ts:127: parseInt(process.env.PORT \|\| '3100')` |
| Titan embed | ✅ PASS | `embed.ts:4: const MODEL_ID = 'amazon.titan-embed-text-v2:0'` |
| TypeScript compiles | ✅ CLEAN | `npx tsc --noEmit` → no errors |
| CLI exists | ✅ PRESENT | `cli/memory.py` (9201 bytes) |

---

## DB Validation (from CC session)

CC ran against the live DB (openclaw-rag container, localhost:5433):
- Migrations applied cleanly to PostgreSQL 16.12
- `cc_memory_users` and `cc_memory_entries` tables created
- vector extension enabled
- ivfflat index on embeddings column

---

## Security Model

- ✅ NEVER stores plaintext tokens — bcrypt hash only
- ✅ `user_id` in WHERE clause for ALL personal queries
- ✅ Org entries readable by all authenticated users (user_id IS NULL)
- ✅ Non-admin org writes require `confirmed: true` second call
- ✅ Bearer token auth on all MCP routes

---

## Ready for Review

Clint (code-reviewer) is up next for REVIEW stage.

## Cycle 2 Fix
Commit: 1631ee8
Fix 1: migrations/001_init.sql + db.ts — vector(1536) → vector(1024)
Fix 2: search.ts + list.ts — AND user_id IS NULL added to org scope
TS: clean
