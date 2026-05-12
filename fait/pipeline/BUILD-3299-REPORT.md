# Build Report — ADO#3299

## What was built
Ensured `getUserTokens(userId)` is called unconditionally on every `/turn` request with an explicit success log line. The call was already present (ADO#3240) but had no success-path log, making it invisible in logs and unverifiable. Also normalized userId before passing to `getUserTokens` so log lines correlate across the call boundary.

## Root cause investigation
- `getUserTokens` call was at line 1550 — already present, not removed
- The function only logs on failure/warning paths, never on success
- Log evidence "no `[getUserTokens]` line" could indicate either: (a) userId validation failing upstream, or (b) call present but silent on success
- Fix addresses (b) definitively by adding the required success log line
- `userTokens` variable correctly scoped (declared before SSE headers, used at lines 2373/2397 for MS365/ADO tool calls)

## Files changed
- `fait-v2/agent-harness/harness-server.js` — Added `ADO#3299` comment, `normalizedUserId` at turn scope, success log line after `getUserTokens` call

## Parallelization used
No — ran sequentially after #3298.

## CC sessions run
1 CC Sonnet session. Brief piped via `cat /tmp/brief-3299.md | claude --model sonnet --print --dangerously-skip-permissions`.

## Acceptance criteria verification
- [x] `getUserTokens` called unconditionally on every `/turn` — line 1552, no conditional wrapper
- [x] Log line after call — `[harness] /turn: getUserTokens success for userId=${normalizedUserId}, ms365=${!!userTokens?.ms365}, ado=${!!userTokens?.ado}` at line 1553
- [x] `ms365` and `ado` presence logged as boolean — `!!userTokens?.ms365`, `!!userTokens?.ado`
- [x] `userTokens` in scope for tool execution — used at lines 2373, 2397
- [x] `node --check harness-server.js` — PASSED

## Known edge cases / things Clint should scrutinize
- `normalizedUserId` is now declared at turn scope (line 1551) AND inside `getUserTokens` itself (line 37). This is intentional redundancy — the outer one is for logging correlation, the inner one handles the actual fetch. No conflict.
- The existing log at line 1555 (`validation passed for userId=...`) now uses `normalizedUserId` instead of `userId` — slight behavioral change (lowercase), but consistent with what's actually used downstream.

## How to test
Deploy and check CloudWatch logs for `/turn` requests. Should now see:
```
[harness] /turn: getUserTokens success for userId=<guid>, ms365=true, ado=true
```
(or `false` if tokens not configured for that user)

## Commit
`096bad36` — code change (included in #3298 commit; both files staged together)
`de0634dc` — `fix(fait#3299): restore getUserTokens call on every turn — MS365/ADO token fetch` (tagged commit)
