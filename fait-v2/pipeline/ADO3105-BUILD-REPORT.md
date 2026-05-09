# Build Report — ADO#3105

## What was built
Added `scrubSecrets()` helper to `harness-server.js` with 5 secret patterns and applied it to all CC stdout/stderr relay, raw body dump logging, CC process error handler, and Bedrock error handler — ensuring no credentials or tokens leak to the SSE stream or logs.

## Files changed
- `agent-harness/harness-server.js` — Added `SECRET_PATTERNS` array and `scrubSecrets()` function; wrapped CC stdout/stderr SSE relay, raw body dump console.log, ccProcess.on('error') errorMessage, and Bedrock ConverseStream catch errorMessage

## Commit
`77f00607`

## Parallelization used
Yes — ran alongside ADO#3108 (no shared files).

## CC sessions run
2 (first was reverted by accidental git stash; re-ran clean)

## Acceptance criteria verification
- [x] `scrubSecrets()` function present with all 5 patterns — ✅ lines 118–134
- [x] CC stdout relay applies scrubber before SSE send — ✅ line 1066
- [x] `node --check` passes — ✅ confirmed

## Scrubber patterns
1. `Bearer\s+[A-Za-z0-9\-._~+/]+=*` — Bearer tokens
2. `[A-Za-z0-9]{20,}==[A-Za-z0-9]{5,}` — Base64-like secrets
3. `sk-[A-Za-z0-9]{20,}` — OpenAI-style keys
4. `AKIA[0-9A-Z]{16}` — AWS access key IDs
5. `(?:password|passwd|secret|token|key)\s*[:=]\s*['"]?[^\s'"]{8,}['"]?` — key=value patterns

## Implementation note
Uses `new RegExp(pattern.source, pattern.flags)` per call to avoid stateful global regex `lastIndex` bugs.

## Known edge cases / things Clint should scrutinize
- The `raw body dump` log is scrubbed, but the other high-verbosity log lines (userId, messageLen, etc.) are static format strings — no secrets expected there
- CC stdin (briefContent) is not scrubbed — it's write-only to the CC process, not sent to client
- Pattern #2 (`base64-like`) may have false positives on long base64 strings in normal content, but the threshold (`{20,}==[A-Za-z0-9]{5,}`) is conservative

## How to test locally
```bash
node --check agent-harness/harness-server.js
```
