# Build Report — ADO#3188

## What was built
Exposed `read_memory` and `write_memory` tools to the Fargate harness by creating a new `MemoryController` in the FAIT Blazor app and adding corresponding route handlers, BUILTIN_TOOLS entries, and system prompt guidance in harness-server.js.

---

## Files changed

- `src/FortressAI.Web/Controllers/MemoryController.cs` — **Created**. New controller with `/api/memory/read` (POST) and `/api/memory/write` (POST) endpoints. Both use `[AllowAnonymous]` + manual `IsInternalAuthorized()` check against `X-Internal-Token` header. `IsInternalAuthorized()` guards against empty config token (returns false if `INTERNAL_API_TOKEN` is not configured). Backed by `IMemoryFileService.GetTopicContentAsync` / `WriteTopicAsync`. `title` defaults to `slug` when not provided.

- `../fait-v2/agent-harness/harness-server.js` — **Modified** (4 changes):
  1. Added `read_memory` route handler at `/tools/read_memory` (line ~704) — calls `BLAZOR_BASE_URL/api/memory/read`, returns `{content: "Topic '...' not found"}` on miss (not an error)
  2. Added `write_memory` route handler at `/tools/write_memory` (line ~737) — calls `BLAZOR_BASE_URL/api/memory/write`, best-effort (returns `{success:false, error}` on failure, never crashes)
  3. Updated `BUILTIN_TOOLS` set to include `'read_memory', 'write_memory'` (line 312)
  4. Added memory tool guidance to both cold-start sections (`contextParts` for task mode, `systemParts` for chat/turn mode)

---

## Parallelization used
No — single CC session, sequential. Both deliverables required coordinated changes across repos.

## CC sessions run
1 CC Sonnet session — both deliverables in one pass.

---

## Acceptance Criteria Verification

- [x] `MemoryController.cs` created with `/api/memory/read` and `/api/memory/write` endpoints
- [x] Both endpoints validate `X-Internal-Token` header; return 401 if missing/wrong
- [x] `IsInternalAuthorized()` returns false if config token is empty (no blind allow)
- [x] `read_memory` returns `{ found: false }` (not error) when slug not in S3
- [x] `write_memory` defaults `title` to slug when not provided
- [x] Harness: `read_memory` route handler added
- [x] Harness: `write_memory` route handler added
- [x] Harness: both tools added to `BUILTIN_TOOLS` set
- [x] Harness: system prompt memory tool guidance appended in BOTH cold-start sections (task mode `contextParts` + chat mode `systemParts`)
- [x] `write_memory` harness handler is best-effort (returns `{success:false}` on error, does not crash)
- [x] Build: 0 errors (37 warnings, all pre-existing)

---

## Self-Review Checklist

- [x] CC invocation included (CC Sonnet, piped brief)
- [x] Commit SHA: `9ee7c696`
- [x] `IsInternalAuthorized()` validates against non-empty config value — `if (string.IsNullOrEmpty(configToken)) return false;`
- [x] `userId` resolved from `req.body.userId` (set by harness session context) — no model-exposed internal paths
- [x] No internal S3 paths or userIds exposed in tool results
- [x] `[AllowAnonymous]` present on both controller actions
- [x] Uses `IMemoryFileService` — no raw DB connections

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **BLAZOR_BASE_URL vs FAIT_BASE_URL**: The `read_memory` and `write_memory` harness handlers use `BLAZOR_BASE_URL` (matching `search_memory`'s pattern) not `FAIT_BASE_URL`. These env vars point to the same FAIT backend in production — but if they ever diverge, memory tools will route differently from scheduled-task handlers. Low risk, worth noting.

2. **`write_memory` best-effort**: On harness-side failure, the tool returns `{success: false, error: "..."}` rather than HTTP 500. This means the model may not know a write failed unless it checks the response. By design per spec — just know it's silent on error.

3. **System prompt guidance is unconditional**: The memory tool guidance is pushed to `systemParts`/`contextParts` regardless of whether `memoryMd` is populated. This is correct — the tools are always available — but means every cold start gets this guidance even if there's no MEMORY.md yet.

---

## How to Test Locally

```bash
# 1. Start FAIT locally (needs INTERNAL_API_TOKEN set in env)
# 2. Test read endpoint:
curl -X POST http://localhost:5000/api/memory/read \
  -H "Content-Type: application/json" \
  -H "X-Internal-Token: <your-token>" \
  -d '{"userId":"<valid-guid>","slug":"user-preferences"}'
# Expected: {"found":false,"content":null} or {"found":true,"content":"..."}

# 3. Test write endpoint:
curl -X POST http://localhost:5000/api/memory/write \
  -H "Content-Type: application/json" \
  -H "X-Internal-Token: <your-token>" \
  -d '{"userId":"<valid-guid>","slug":"test-topic","title":"Test Topic","content":"Test content"}'
# Expected: {"success":true}

# 4. Test 401 (missing token):
curl -X POST http://localhost:5000/api/memory/read \
  -H "Content-Type: application/json" \
  -d '{"userId":"<valid-guid>","slug":"test"}'
# Expected: 401 {"error":"Unauthorized"}
```

---

## Build Output

```
Build succeeded.
37 Warning(s)
0 Error(s)
Time Elapsed: 00:00:06.52
```

---

*Commit: `9ee7c696` — feat(fait#3188): read_memory + write_memory tool handlers + MemoryController*

---

## Review Cycle 2 — Targeted Fixes

**Triggered by:** Clint's review of commit `9ee7c696`
**Cycle 2 commit:** `124d2388`

### CC Invocation
```bash
cd /home/fredw/projects/fip/fait && cat /tmp/cc-brief-3188-c2.md | claude --model sonnet --print --dangerously-skip-permissions
```
1 CC Sonnet session — all 3 fixes in one pass.

### Fixes Applied

- ✅ **Fix 1a — Bedrock toolConfig**: Added `read_memory` and `write_memory` tool specs to `toolConfig.tools[]` in `harness-server.js`. Bedrock can now invoke these tools; previously they were system-prompt-only dead instructions.

- ✅ **Fix 1b — Bedrock dispatch loop**: Added `else if (toolUseAccumulator.name === 'read_memory')` and `else if (toolUseAccumulator.name === 'write_memory')` branches in the tool dispatch block. Both use `userId` from outer scope (not `toolInput`), matching the `list_workspace_files` pattern.

- ✅ **Fix 2 — CC userId injection**: Added `contextParts.push(\`## Session Identifiers\nuserId: ${userId}\`)` after the `userEmail` push in the CC spawn contextParts path. CC now receives the GUID required by `MemoryController` to pass `Guid.TryParse` validation.

- ✅ **Fix 3 — ArgumentException catch in WriteTopic**: Wrapped `WriteTopicAsync` + `return Ok(...)` in `try/catch (ArgumentException ex)` that returns `BadRequest(new { error = ex.Message })`. Reserved slug "MEMORY" guard no longer propagates as unhandled 500.

### Build Result
```
Build succeeded.
37 Warning(s)  (all pre-existing MUD0002)
0 Error(s)
Time Elapsed: 00:00:06.04
```

### Files Modified
- `fait-v2/agent-harness/harness-server.js` — Fix 1a, 1b, 2
- `fait/src/FortressAI.Web/Controllers/MemoryController.cs` — Fix 3

*Cycle 2 commit: `124d2388` — ADO#3188 cycle 2: Bedrock toolConfig + dispatch for read/write_memory, CC userId injection, ArgumentException catch in WriteTopic*
