# QA Report: WI856 — CC Memory MCP Server
**Agent:** Black Widow (Natasha Romanoff) — `qa-analyst`  
**Date:** 2026-03-17  
**Commit:** `1631ee8`  
**Service:** `mcp-memory.service` on `SteamServer:3100`  
**Verdict:** ❌ FAIL

---

## Executive Summary

The service deploys, starts, and handles auth correctly. However, **all write/search operations fail** due to a DB schema mismatch: the live `cc_memory_entries` table has `embedding vector(1536)` while the Titan embed v2 model outputs 1024-dimensional vectors. The migration fix in commit `1631ee8` updated the SQL file to `vector(1024)` but did **not** run a `DROP`/`ALTER` on the existing table — `CREATE TABLE IF NOT EXISTS` silently skips when the table exists. Tests 4, 5, 6, and 7 are all blocked by this.

---

## Test Results

| # | Test | Result | Notes |
|---|------|--------|-------|
| 1 | Health endpoint `GET /health` | ✅ PASS | `{"status":"ok"}` |
| 2 | Auth required — unauth returns 401 | ✅ PASS | 401 confirmed |
| 3 | Create test user via admin CLI | ✅ PASS | User `qa-test` created, token captured |
| 4 | Authenticated `memory_add` | ❌ FAIL | `"expected 1536 dimensions, not 1024"` |
| 5 | `memory_list` returns added entry | ❌ BLOCKED | Blocked by Test 4 failure — no entry to list |
| 6 | `memory_search` (vector similarity) | ❌ BLOCKED | Blocked by Test 4 failure — no entry to search |
| 7 | `memory_delete` | ❌ BLOCKED | Blocked by Test 4 failure — no entry ID to delete |
| 8 | DB tables exist (`cc_memory_users`, `cc_memory_entries`) | ✅ PASS | Both tables present |
| 9 | Service active under systemd | ✅ PASS | `active (running)` since 21:26:49, PID 933547, 34.9M RSS |
| 10 | CLI tool accessible at `/cli/memory.py` | ✅ PASS | HTTP 200 |

---

## Root Cause — Test 4 Failure

### What Happened
Commit `1631ee8` (fix: `vector(1024)` for Titan v2) changed `migrations/001_init.sql` from `vector(1536)` to `vector(1024)`. However, the migration runner uses:

```sql
CREATE TABLE IF NOT EXISTS cc_memory_entries (
    ...
    embedding   vector(1024),
    ...
);
```

The table was already created by the previous deployment (with `vector(1536)`). `CREATE TABLE IF NOT EXISTS` is a no-op when the table exists — it does **not** alter the column type. The running service calls Bedrock Titan embed v2 which returns 1024-dim vectors, and pgvector rejects the insert:

```
"expected 1536 dimensions, not 1024"
```

### Evidence
```bash
# Live DB schema (wrong):
docker exec openclaw-rag psql -U jarvis -d rag -c "\d cc_memory_entries"
# → embedding | vector(1536)  ← should be vector(1024)

# Migration file (correct):
cat migrations/001_init.sql | grep vector
# → embedding   vector(1024),  ← fixed in 1631ee8 but not applied
```

### Fix Required
A migration is needed to alter the existing column:

```sql
-- Option A: ALTER COLUMN (drops existing index first, re-creates after)
ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024);

-- Option B: Full table rebuild (safest for dimension change with IVFFlat index)
DROP INDEX IF EXISTS idx_ccme_embedding;
ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024)
    USING embedding::text::vector(1024);
CREATE INDEX idx_ccme_embedding ON cc_memory_entries
    USING ivfflat (embedding vector_cosine_ops) WITH (lists = 50);
```

Since `cc_memory_entries` is currently empty (0 rows), Option A is sufficient with minimal risk.

---

## Passing Details

**Test 1 — Health:**
```json
{"status":"ok"}
```

**Test 2 — Auth gate:**
```
HTTP 401
```
(unauthenticated POST to `/mcp` returns 401 as required)

**Test 3 — Admin CLI:**
```
✓ User created: qa-test (qa@test.local)
  ID: b60141ce-2083-4256-ac81-9c84c4dea428
  Token (save this — shown once): bb1a3d7aaa...
```
Token-once pattern working correctly.

**Test 8 — DB tables:**
```
 Schema |       Name        | Type  | Owner  
--------+-------------------+-------+--------
 public | cc_memory_entries | table | jarvis
 public | cc_memory_users   | table | jarvis
```

**Test 9 — Systemd:**
```
● mcp-memory.service - CC Memory MCP Server
   Active: active (running) since Tue 2026-03-17 21:26:49 EDT
   Main PID: 933547 (node)
   Memory: 34.9M
```

**Test 10 — CLI tool:** HTTP 200

---

## Required Fix Before Re-Verify

1. **Add `002_fix_embedding_dim.sql` migration** (or inline ALTER) that runs `ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024)`
2. **Run the migration** against the live `openclaw-rag` DB
3. **Redeploy/restart** `mcp-memory.service` to confirm startup migration runs clean
4. **Re-verify** Tests 4–7

Since the table is empty (0 rows), no data migration is needed — just the `ALTER COLUMN`.

---

## Verdict Summary

| Criterion | Met? |
|-----------|------|
| Service running (systemd) | ✅ |
| Health endpoint | ✅ |
| Auth gate (401) | ✅ |
| DB tables present | ✅ |
| CLI tool accessible | ✅ |
| memory_add works | ❌ |
| memory_list works | ❌ (blocked) |
| memory_search works | ❌ (blocked) |
| memory_delete works | ❌ (blocked) |

**Verdict: ❌ FAIL — DB vector dimension mismatch (1536 vs 1024). Fix required: ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024). Table is empty — safe to run immediately.**

---

## QA RETRY — 2026-03-18T01:33 UTC

**Agent:** Black Widow (Natasha Romanoff) — `qa-analyst`
**Triggered by:** Dimension fix (vector(1536) → vector(1024)) via startup migration + direct ALTER
**Service:** `mcp-memory.service` @ SteamServer:3100

### Test Results

| # | Test | Expected | Result | Status |
|---|------|----------|--------|--------|
| 1 | Health check | `{"status":"ok"}` | `{"status":"ok"}` | ✅ PASS |
| 2 | Unauthenticated → 401 | 401 | 401 | ✅ PASS |
| 3 | Create test user (qa-test2) | Token issued | Token issued | ✅ PASS |
| 4 | memory_add | id + created_at, no dimension error | `id: 2126fae7`, `created_at: 2026-03-18T01:33:09Z` | ✅ PASS |
| 5 | memory_list | Entry from Test 4 present | Entry confirmed in list | ✅ PASS |
| 6 | memory_search | Semantic match for Test 4 | Match returned, similarity: 0.585 (Bedrock working) | ✅ PASS |
| 7 | memory_delete | Deleted confirmation | `{"deleted":"2126fae7-9079-4147-a255-499e1ddf7672"}` | ✅ PASS |
| 8 | DB column vector(1024) | `embedding \| vector(1024)` | `embedding \| vector(1024)` ✓ | ✅ PASS |
| 9 | systemd service active | `active` | `active` | ✅ PASS |
| 10 | CLI endpoint served | 200 | 200 | ✅ PASS |

### Notes
- Test 6 (memory_search): Bedrock creds are valid and working — full semantic search operational, similarity score 0.585
- No dimension errors anywhere in test run — root cause fully resolved
- Accept header requirement: `/mcp` endpoint requires `Accept: application/json, text/event-stream` — expected MCP spec behavior

### Verdict: ✅ PASS (10/10)

**All tests pass. WI856 is clear for closure.**
