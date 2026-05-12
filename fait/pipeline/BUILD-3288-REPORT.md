# Build Report — ADO#3288: INTERNAL_API_TOKEN Structured Logging

## What was built
Added structured ILogger-based logging to the `/api/internal/user-tokens/{userId}` Blazor endpoint and
full HTTP response body logging to `getUserTokens()` in the harness, so future 401 failures appear in
CloudWatch with enough detail to diagnose without guessing.

## CC Invocation
```bash
cat /tmp/brief-3288.md | claude --model sonnet --print --dangerously-skip-permissions
```
**Result:** ✅ Both changes applied, `dotnet build` 0 errors, `node --check` passed.

## Files Changed

- `fait/src/FortressAI.Web/Program.cs` — `/api/internal/user-tokens/{userId}` endpoint
  - Added `ILogger<Program> logger` parameter to minimal API delegate
  - Log `503` path: `LogWarning "InternalToken validation: FAIL ... INTERNAL_API_TOKEN not configured"`
  - Log incoming token masked (first 8 chars + `...`)
  - Log mismatch: `LogWarning "InternalToken validation: FAIL userId={UserId} token={MaskedToken}"`
  - Log success: `LogInformation "InternalToken validation: PASS userId={UserId} token={MaskedToken}"`
  - Log lookup: `LogInformation "InternalToken user-tokens: looking up tokens for userId={UserId}"`

- `fait-v2/agent-harness/harness-server.js` — `getUserTokens()` function
  - Replaced `res.json()` with `res.text()` + `JSON.parse()` to capture body before ok check
  - On non-ok: logs `[getUserTokens] status={status} userId={userId} body={responseBody}`
  - On JSON parse failure: logs and returns nulls safely

## Parallelization Used
Yes — ran alongside ADO#3289 brief (different files, no shared state).

## CC Sessions Run
1 CC run covering both files.

## Acceptance Criteria Verification
- [x] Incoming token logged masked (8 chars + `...`) — ✅ implemented
- [x] PASS/FAIL logged with userId — ✅ `LogInformation`/`LogWarning` with structured params
- [x] ILogger used (not Console.WriteLine) — ✅ confirmed
- [x] Harness logs `[getUserTokens] status=... userId=... body=...` — ✅ confirmed format
- [x] `dotnet build` 0 errors — ✅ confirmed by CC run
- [x] `node --check` — ✅ confirmed by CC run

## Commit
`81f02cb1` — `fix(fait#3288): structured logging on internal token auth + getUserTokens response body`

## Known Edge Cases / Clint Should Note
- The `ILogger<Program>` injection in a minimal API lambda is valid in .NET 8 (DI auto-resolves it).
  If the project uses a custom logger category, it can be changed without logic impact.
- Masking logic: tokens shorter than 8 chars log as `***`; empty header logs as `(empty)`.

## How to Test Locally
1. Set `INTERNAL_API_TOKEN` to a known value in the Blazor app
2. Call `/api/internal/user-tokens/{userId}` with a wrong token → expect `401` + `LogWarning` in console
3. Call with correct token → expect `200` + `LogInformation PASS` in console
4. In harness: send a request with a bad token → check console for `[getUserTokens] status=401 userId=... body=...`
