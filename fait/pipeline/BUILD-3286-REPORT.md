# Build Report — ADO#3286

## What was built
Fixed two related issues: (A) getUserTokens() userId normalization in harness to ensure consistent lowercase GUID format when calling Blazor's internal token endpoint; (B) replaced localhost Brave proxy URL with FAIT_BASE_URL to fix cross-container routing in separate Fargate tasks.

## Files changed
- `fait-v2/agent-harness/harness-server.js` — Two changes:
  1. `getUserTokens()`: Added `normalizedUserId = (userId || '').trim().toLowerCase()` normalization before building the URL. Early-returns with warning if userId is empty.
  2. Removed `blazorPort` variable and `http://localhost:${blazorPort}/internal/mcp/brave` URL; replaced with `${FAIT_BASE_URL}/internal/mcp/brave` (ADO#3286 comment added).

## Parallelization used
No — single file in harness repo.

## CC sessions run
1 CC run (sonnet). Straightforward targeted changes per brief.

## Acceptance criteria verification
- [x] getUserTokens normalizes userId to lowercase — guards against any mixed-case GUID edge case
- [x] getUserTokens returns early with warning for empty userId
- [x] Brave proxy URL uses FAIT_BASE_URL (http://fait.fip.internal:8080) instead of localhost
- [x] `blazorPort` variable removed (no dead code)
- [x] `node --check` passes — syntax valid

## Known edge cases / things Clint should scrutinize
- The `FAIT_BASE_URL` env var must be set correctly in the Fargate task definition for the Brave proxy to work. In dev (localhost), this would need `localhost:8080` which would fail since Blazor isn't on localhost of the harness container in Fargate. This is expected behavior.
- The userId normalization is defensive — the actual root cause of empty tokens may also be that the user simply hasn't connected their MS365/ADO accounts. The normalization makes the lookup more robust regardless.

## How to test locally
1. In the chat UI, enable MS365 or ADO tools (requires connected accounts)
2. Send a message that triggers tool use
3. Verify tools execute without "No auth token available" error
4. For Brave: send a message triggering web_search — should not return "fetch failed"

## Commit
`35eba9e4` — `fix(fait#3286): MCP token userId normalization + Brave proxy internal URL fix`
