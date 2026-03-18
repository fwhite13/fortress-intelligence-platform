# Review Brief: WI856 — CC Memory MCP Server
# Hawkeye (Clint Barton) — Code Review Cycle 1 of 2

You are doing a security-focused code review of a pgvector-backed memory MCP server.
Repo: /home/fredw/projects/fip/mcp-memory/

## Files to review (already read by human reviewer — verify findings below):
- src/auth.ts
- src/server.ts
- src/db.ts
- src/embed.ts
- src/tools/search.ts
- src/tools/add.ts
- src/tools/list.ts
- src/tools/delete.ts
- src/admin.ts
- migrations/001_init.sql

## Specific questions to answer for each check:

### CHECK 1: bcrypt.compare usage (src/auth.ts)
- Is `bcrypt.compare(token, user.api_token)` used? (token = incoming plaintext, user.api_token = stored hash)
- Is this correct? (bcrypt.compare takes plaintext first, hash second — this is the right order)
- Does any code path store the plaintext token? Check logs, DB inserts.

### CHECK 2: admin add-user token handling (src/admin.ts)
- Does addUser() generate plaintext with crypto.randomBytes?
- Does it bcrypt.hash before inserting?
- Is ONLY the hash inserted into the DB?
- Is the plaintext only printed to stdout via console.log?
- Any risk of plaintext being logged elsewhere?

### CHECK 3: personal scope isolation (src/tools/search.ts, list.ts, delete.ts)
- search.ts: The WHERE clause is `(scope = 'org' OR user_id = $2)`. 
  QUESTION: Is this safe? Could User A's personal entries leak to User B?
  The `user_id = $2` uses the authenticated user's ID from the auth middleware — so this only returns entries where user_id matches the current user. CONFIRM this is correct.
  
- list.ts: scope='personal' branch uses `user_id = $1`. CONFIRM this is the authenticated user's ID.
  scope='org' branch uses `scope = 'org'` WITHOUT `user_id IS NULL`. 
  QUESTION: Is this a problem? If an org entry was accidentally inserted with user_id set (not null), would it appear here? YES — but the add.ts code always sets user_id=NULL for org scope. Is the omission of `user_id IS NULL` a security issue?

- delete.ts: Non-admin delete uses `WHERE id = $1 AND user_id = $2`. CONFIRM user_id=$2 is the authenticated user.

### CHECK 4: org isolation in add.ts
- Is `userId = scope === 'personal' ? user.id : null`?
- Does the INSERT use $1 for user_id, which is null for org?
- Is `created_by` stored as user.id (for audit trail) even for org entries? Is that correct?

### CHECK 5: auth before tool dispatch (src/server.ts)
- Is `authenticate(req)` called before Server creation and tool dispatch?
- Is 401 returned immediately if user is null?
- Is there any path to reach the tool handlers without authentication?

### CHECK 6: initDb() before app.listen() (src/server.ts, src/db.ts)
- Does main() call initDb() then app.listen()?
- Is the migration SQL loaded and executed on startup?

### CHECK 7: migrations idempotent (migrations/001_init.sql)
- Does it use `CREATE EXTENSION IF NOT EXISTS vector`?
- Does it use `CREATE TABLE IF NOT EXISTS` for both tables?
- Does it use `CREATE INDEX IF NOT EXISTS` for all indexes?

### CHECK 8: embed model (src/embed.ts)
- Is MODEL_ID exactly 'amazon.titan-embed-text-v2:0'?
- Does the response parsing extract `result.embedding`?
- What is the vector dimension? Check migrations/001_init.sql for `vector(1536)`.
- Is the Titan embed v2 API call format correct? (inputText field, InvokeModel)

### CHECK 9: MCP tool names (src/server.ts)
- Are the exact tool names: memory_search, memory_add, memory_list, memory_delete?

### CHECK 10: TypeScript strict mode
- tsconfig.json has "strict": true — confirmed.
- `npx tsc --noEmit` ran clean with no output.

### CHECK 11: admin token not logged
- In admin.ts addUser() and resetToken(): Is the plaintext only in console.log to stdout?
- Is there any file logger, database log, or process.env.DEBUG path that could capture it?

## CRITICAL ISSUE TO ANALYZE:

### Issue A: search.ts org scope — missing user_id IS NULL guard
The search.ts WHERE clause: `(scope = 'org' OR user_id = $2)`

This means: return entries where (scope is 'org') OR (user_id matches current user).
This does NOT verify that org entries have user_id IS NULL.

The spec says: org scope queries should have `WHERE scope = 'org' AND user_id IS NULL`.

Question: Is this a security vulnerability? 
- If an org entry was inserted with user_id set (e.g., a bug), it would appear in ALL users' searches.
- But more importantly: the clause `scope = 'org'` without `AND user_id IS NULL` means even a "personal" entry with scope='org' that somehow got created with a user_id would be visible to others.
- In practice, add.ts always sets user_id=NULL for org scope. But defensive coding would add the IS NULL check.
- CLASSIFY: Is this a Critical, Important, or Nitpick finding?

### Issue B: list.ts org scope — missing user_id IS NULL guard  
The list.ts org scope branch: `scope = 'org'` without `AND user_id IS NULL`.
Same concern as Issue A.
- CLASSIFY: Same severity as Issue A.

### Issue C: list.ts — LIMIT uses $2 hardcoded in query but queryParams[1] = limit
Looking at the query: `LIMIT $2` — and queryParams is built as `[user.id, limit]` for personal/all scope, and `[user.id, limit]` for org scope too.
Wait: for org scope, queryParams = [user.id, limit] — but whereClause = "scope = 'org'" doesn't use $1 (user.id is not referenced in the query).
This means $1 in the LIMIT reference... wait, no. The LIMIT is $2, and the WHERE clause for org is `scope = 'org'` which has no $1 parameter.
So queryParams = [user.id, limit] but the query uses LIMIT $2 where $2 = limit, and $1 = user.id is unused in the WHERE clause but present in params.
PostgreSQL will accept this (extra params don't cause errors), but $1 is dead/unused.
This is a code quality issue, not a security issue. But the LIMIT value is correct.

Actually wait — re-read: for project filter, paramIndex = queryParams.length + 1. For org scope without project: queryParams=[user.id, limit], paramIndex would be 3. So project would be $3. That's fine.

For org scope WITH project: queryParams=[user.id, limit, project]. The query becomes:
`WHERE scope = 'org' AND (expires_at...) AND project = $3 LIMIT $2`
$1 = user.id (unused in WHERE), $2 = limit, $3 = project. This works but $1 is wasted.

CLASSIFY: Nitpick — dead parameter for org scope, but functionally correct.

Please read the actual files, confirm all findings, and provide your assessment.
Read the files at: /home/fredw/projects/fip/mcp-memory/src/
