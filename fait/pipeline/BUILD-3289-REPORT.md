# Build Report — ADO#3289: CC Spawn Comprehensive Logging

## What was built
Added comprehensive logging around the Claude Code CLI spawn in task mode: exact command logged before
spawn, every stdout chunk logged, stderr expanded from one-liner to logged block, exit code + silent-exit
warning logged on close, and a startup `claude --version` check so CloudWatch shows CLI availability
before any user interaction.

## CC Invocation
```bash
cat /tmp/brief-3289.md | claude --model sonnet --print --dangerously-skip-permissions
```
**Result:** ✅ All 5 changes applied, `node --check` passed.

## Files Changed

- `fait-v2/agent-harness/harness-server.js`

  **Change 1 — Startup claude --version check** (bootstrap block, ~line 2551):
  - `execSync('claude --version')` in try/catch
  - Success: `[harness] startup: claude CLI found — <version>`
  - Failure: `[harness] startup: claude CLI NOT found or failed — task mode will fail. Error: ...`

  **Change 2 — Extract ccArgs + spawn log** (~line 1757):
  - Extracted spawn args to `const ccArgs`
  - `[CC spawn] command=claude ... cwd=... userId=... briefLen=...` logged before spawn
  - `spawn('claude', ccArgs, ...)` uses extracted const (no duplication)

  **Change 3 — stdout chunk logging** (~line 1789):
  - `[CC spawn] stdout chunk bytes=... userId=...` at top of `stdout.on('data')` handler

  **Change 4 — stderr handler expansion** (~line 1830):
  - `[CC spawn] stderr bytes=... userId=... text=<first 500 chars>`
  - Still forwards to `sendEvent` as before

  **Change 5 — close handler exit code logging** (~line 1836):
  - `[CC spawn] process exited code=... userId=... ccTextEmitted=...`
  - `[CC spawn] Process exited 0 but produced no output — possible silent failure userId=...` (when ccTextEmitted=false)
  - `[CC spawn] Process exited with non-zero code=... userId=...` (when code != 0)

## Parallelization Used
Yes — ran alongside ADO#3288 brief (different files, no shared state).

## CC Sessions Run
1 CC run.

## Acceptance Criteria Verification
- [x] Exact command logged before spawn — ✅ `[CC spawn] command=...`
- [x] stdout logged per chunk in real-time — ✅ `[CC spawn] stdout chunk bytes=...`
- [x] stderr logged per chunk in real-time — ✅ `[CC spawn] stderr bytes=... text=...`
- [x] Exit code logged — ✅ `[CC spawn] process exited code=...`
- [x] Silent-exit warning (code=0, no output) — ✅ implemented
- [x] Startup `claude --version` check — ✅ implemented with error logging
- [x] `node --check` — ✅ confirmed by CC run

## Commit
`6f612ed3` — `fix(fait#3289): CC spawn comprehensive logging — stdout/stderr/exit code/startup check`

## Known Edge Cases / Clint Should Note
- `stdout chunk bytes` log will fire multiple times per task (one per NDJSON chunk from CC); this is expected.
  In CloudWatch, filter by `[CC spawn]` to see the full picture in sequence.
- The startup `claude --version` is synchronous (`execSync`). If the binary hangs for some reason,
  it times out after 10 seconds — acceptable at boot, does not affect request handling.
- Silent-exit warning fires if `ccTextEmitted` is false when CC exits 0. This is the exact scenario
  described in the WI (contentLen=93 but no `text` event). The warning should now make it visible.

## How to Test Locally
1. Start the harness — verify `[harness] startup: claude CLI found —` in logs
2. Send a task-mode request — verify `[CC spawn] command=...` appears in logs
3. Watch for `[CC spawn] stdout chunk bytes=...` per chunk
4. If CC exits silently — verify `[CC spawn] Process exited 0 but produced no output` warning fires
