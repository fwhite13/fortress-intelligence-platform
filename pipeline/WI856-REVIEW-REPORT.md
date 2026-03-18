# Review Report: WI856
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

## CC Invocation
```bash
cd /home/fredw/projects/fip/mcp-memory
cat ~/projects/fait-for-excel/review-brief-wi856.md | claude --model sonnet -p
```

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| bcrypt.compare (not plaintext) | ✅ | auth.ts:45 — `bcrypt.compare(token, user.api_token)`, plaintext first, hash second |
| admin add-user stores hash only | ✅ | admin.ts:20,24 — `bcrypt.hash(plaintext,12)` stored, `$3=hash` in INSERT |
| personal scope: WHERE user_id = $userId | ✅ | search.ts:39 `user_id=$2`, list.ts:30 `user_id=$1`, delete.ts:22 `user_id=$2` |
| org scope: WHERE user_id IS NULL | ❌ | search.ts:39 `scope='org'` (no IS NULL guard); list.ts:33 same omission |
| org add: INSERT user_id = NULL | ✅ | add.ts:37 — `userId = scope === 'personal' ? user.id : null` |
| auth enforced before tool dispatch | ✅ | server.ts:28-31 — authenticate() called first, 401 on null, return before Server creation |
| initDb() before app.listen() | ✅ | server.ts:130-131 — `await initDb()` then `app.listen()` |
| migrations idempotent (IF NOT EXISTS) | ✅ | 001_init.sql:1,3,16 — `IF NOT EXISTS` on extension, both tables, all 5 indexes |
| embed model: titan-embed-text-v2:0 | ✅ | embed.ts:4 — `MODEL_ID = 'amazon.titan-embed-text-v2:0'` |
| vector(1536) dimension match | ❌ | embed.ts returns 1024 dims (Titan v2 default); migration uses `vector(1536)` — MISMATCH |
| tool names exact | ✅ | server.ts:42,55,70,82 — memory_search, memory_add, memory_list, memory_delete |
| TS clean | ✅ | `npx tsc --noEmit` — no output, clean |
| admin token not logged | ✅ | admin.ts:28,44 — console.log to stdout only; no file logger, no secondary sink |

## Issues Found

### 🔴 CRITICAL — Vector dimension mismatch: embed.ts produces 1024-dim, schema expects 1536-dim

**File:** `src/embed.ts` + `migrations/001_init.sql`

`amazon.titan-embed-text-v2:0` outputs **1024 dimensions** by default (confirmed per AWS docs). Supported dimensions for Titan v2 are 256, 512, and 1024 — **1536 is not a valid Titan v2 output size**. The value 1536 belongs to Titan v1 (`amazon.titan-embed-text-v1`).

The migration declares `embedding vector(1536)`, which means **every call to `memory_add` will fail** with a PostgreSQL vector dimension mismatch error. The entire memory storage function is broken.

**Fix required — choose one:**
- **Option A (recommended):** Change migration to `vector(1024)` — matches Titan v2 default
- **Option B:** Switch to `amazon.titan-embed-text-v1` and keep `vector(1536)` — but v1 is older/deprecated

Note: If this is a fresh database (no existing data), simply update the migration. If data exists, the `embedding` column must be dropped and recreated (or the table dropped) to change vector dimensions.

---

### ⚠️ IMPORTANT — Missing `user_id IS NULL` guard on org-scope queries

**Files:** `src/tools/search.ts` (lines 37-42) and `src/tools/list.ts` (line 33)

The org-scope WHERE clauses do not include `AND user_id IS NULL`. Today this is safe because `add.ts` always sets `user_id = null` for org entries (line 37). However, the database schema has no CHECK constraint enforcing this invariant (`user_id` is nullable on `cc_memory_entries` with no constraint tying `scope='org'` to `user_id IS NULL`).

A future bug in `add.ts`, a direct DB insert, or a migration change could silently break org isolation — and the query layer would not catch it.

**search.ts fix:**
```sql
-- Current:
WHERE (scope = 'org' OR user_id = $2)
-- Fixed:
WHERE ((scope = 'org' AND user_id IS NULL) OR user_id = $2)
```

**list.ts fix:**
```sql
-- Current:
WHERE scope = 'org'
-- Fixed:
WHERE scope = 'org' AND user_id IS NULL
```

**Optional hardening:** Add a DB CHECK constraint:
```sql
ALTER TABLE cc_memory_entries
  ADD CONSTRAINT chk_org_no_user_id
  CHECK (scope != 'org' OR user_id IS NULL);
```

---

### 🔵 NITPICK — Dead `$1` parameter in list.ts org-scope query

**File:** `src/tools/list.ts` (lines 32-34)

For `scope = 'org'`, `queryParams = [user.id, limit]` but `whereClause = "scope = 'org'"` — `$1` (user.id) is unused in the WHERE clause. PostgreSQL silently ignores the extra parameter. Functionally correct (LIMIT `$2 = limit` is right, project `$3` is right), just slightly confusing code.

**Fix:** For org scope, start `queryParams = [limit]` with LIMIT as `$1`, and shift project to `$2` if needed. Adjust the paramIndex calculation accordingly.

---

## Verdict

**NEEDS-CHANGES** — 1 Critical, 2 Important, 1 Nitpick.

The **Critical** bug (vector dimension mismatch) renders `memory_add` completely non-functional. This must be fixed before any deployment. The **Important** issues (missing `user_id IS NULL` guards) are defense-in-depth hardening — not currently exploitable but a real risk surface.

All HIGH security checks passed: bcrypt comparison is correct, token isolation is solid, auth is enforced before dispatch, admin CLI handles plaintext safely. The security foundations are sound — the dimension bug is an implementation error, not a security failure.

Return to Tony with these three fixes. Security and auth foundations do NOT need re-review — focus next cycle on the embed/vector dimension resolution and the IS NULL guard additions.

---
## Cycle 2 Re-review (71ddced → 1631ee8)

| Fix | Result |
|-----|--------|
| vector(1024) in migration (not 1536) | ✅ |
| No 1536 remaining anywhere | ✅ |
| user_id IS NULL in org-scope search.ts | ✅ |
| user_id IS NULL in org-scope list.ts | ✅ |
| Original security checks intact | ✅ |
| TS clean | ✅ |

## Cycle 2 Verdict: PASS
