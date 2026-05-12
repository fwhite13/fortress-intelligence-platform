# Review Report — ADO#3298 + #3299 + #3300 + #3301

**Reviewer:** Clint Barton (Hawkeye)
**HEAD:** `1ed77b5b`
**Review cycle:** 1 of 2
**Date:** 2026-05-12

---

### Verdict: PASS

All four WIs pass. One pre-existing Important issue documented for backlog. One nitpick.

---

## CC Review

**CC invocation:**
```bash
cat /tmp/clint-review-3298-3301.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC read all relevant files (Dockerfile, harness-server.js, ChatView.razor, WorkspaceController.cs) and ran a systematic check of every acceptance criterion. CC findings synthesized below.

---

## ADO#3298 — Dockerfile non-root user

**Verdict: PASS**

### Spec Compliance
All AC verified against actual Dockerfile (`fait-v2/agent-harness/Dockerfile`):

| AC | Check | Status |
|----|-------|--------|
| `groupadd -r harness && useradd -r -g harness -m -d /home/harness harness` | Line 48: exact match | ✅ |
| `ENV PATH` includes `/usr/local/bin` AND `/usr/local/lib/node_modules/.bin` | Line 50: both paths present | ✅ |
| `chown -R harness:harness /app` before USER | Line 51, before USER harness at line 63 | ✅ |
| `/workspace` created AND chowned to harness | Line 54: `mkdir -p /workspace && chown harness:harness /workspace` | ✅ |
| `USER harness` before `CMD` | Line 63 before line 64 | ✅ |
| No root-owned files added after chown | All COPY/npm install complete before line 51 | ✅ |

### Consistency Audit
- `npm install -g @anthropic-ai/claude-code` and `npm install -g stitch-mcp` install to `/usr/local/lib/node_modules/.bin` ✅
- ENV PATH set at image build time — available for runtime harness user ✅
- No hardcoded paths in harness-server.js that bypass `/app`, `/workspace`, or `/tmp` (checked) ✅

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Nitpick | `Dockerfile` | 60-61 | `HEALTHCHECK` declared before `USER harness`. Docker runs healthcheck under the current user at HEALTHCHECK instruction time — effectively root. No functional impact (curl reaches localhost as root fine), inconsistent with least-privilege intent. | Move HEALTHCHECK block to after `USER harness` (line 63) |

---

## ADO#3299 — getUserTokens success log

**Verdict: PASS** *(with Important pre-existing issue documented for backlog)*

### Spec Compliance

| AC | Check | Status |
|----|-------|--------|
| `getUserTokens(userId)` unconditional before SSE headers | Line 1533 calls it; SSE headers at line 1537 — correct order | ✅ |
| Success log: `[harness] /turn: getUserTokens success for userId=..., ms365=..., ado=...` | Line 1534 — exact format match | ✅ |
| `ms365`/`ado` logged as boolean (not raw token) | `!!userTokens?.ms365`, `!!userTokens?.ado` — booleans only | ✅ |
| `userTokens` in scope for tool execution | Declared const at line 1533 (turn scope); used at lines 2354, 2378 for MS365/ADO calls | ✅ |
| `normalizedUserId` used consistently | Line 1532 normalizes; line 1533 passes to `getUserTokens`; log and validation use `normalizedUserId` | ✅ |
| Entra OID passes userId validation regex | `/^[a-zA-Z0-9_-]{1,64}$/` — GUIDs are 36 chars, alphanumeric + hyphens, all match | ✅ |

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Important (pre-existing, not regression) | `harness-server.js` | 44 | `getUserTokens` has no fetch timeout. `fetch()` without `AbortController` can hang for OS-level TCP timeout (~1-2 min). Since `getUserTokens` is called **before** SSE headers are sent (line 1533 < line 1537), a hung FAIT instance blocks the entire `/turn` response — client gets no headers and eventually a connection drop. This is pre-existing from ADO#3240, not introduced by #3299. | Add AbortController: `const ac = new AbortController(); setTimeout(() => ac.abort(), 5000); fetch(url, { headers, signal: ac.signal })`. Catch block already returns `{ ms365: null, ado: null }` on error — graceful degradation already handled. |

**Judgment:** Not blocking this batch. Pre-existing issue, not a regression. Ticket for follow-up.

---

## ADO#3300 — KbFlags construction

**Verdict: PASS**

This change is in the **FAIT** repo (`fait/src/FortressAI.Web/Components/Chat/ChatView.razor`), not fait-v2. Confirmed via git diff `2c7a7937`.

### Spec Compliance

| AC | Check | Status |
|----|-------|--------|
| `TeamKbEnabled` set from `hasTeamKb` only (NOT `hasTeamKb \|\| hasProjectKb`) | Line 915: `TeamKbEnabled: hasTeamKb` — `\|\| hasProjectKb` removed | ✅ |
| `PersonalKbUserId` populated when `personalKbEnabled && Session.UserId != Guid.Empty` | Lines 916-918: exact guard matches spec | ✅ |
| KB flags log includes `UserId:{Session.UserId}` | Line 836: `UserId:{Session.UserId}` present | ✅ |
| `dotnet build` → 0 errors | Verified: 0 errors, 45 pre-existing MUD analyzer warnings | ✅ |

### Consistency Audit
- `hasProjectKb` still in `anyKbActive` (line 835) — project KB still activates the harness KB path ✅
- All 5 KbFlags parameters present: `CorpKbEnabled`, `PersonalKbEnabled`, `TeamKbEnabled`, `PersonalKbUserId`, `TeamIds` ✅
- Diff clean — only the two lines changed from spec, plus the log line ✅

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Nitpick/Observation | `ChatView.razor` | 912 | When `hasProjectKb` is the only active flag, `anyKbActive=true` → `KbFlags` is non-null, but all 5 fields are false/null. No `ProjectKbEnabled` field exists. The harness receives a non-null KbFlags with nothing enabled. This appears intentional (project KB is resolved via `ConversationId` in the harness, not via a KbFlags field), but worth confirming. No code change needed if harness handles this correctly. | Verify harness treats all-false KbFlags identically to null KbFlags for project KB path. |

---

## ADO#3301 — list_files via Blazor API

**Verdict: PASS**

### Spec Compliance — Harness Side

| AC | Check | Status |
|----|-------|--------|
| No `getDbConnection()` / mysql2 in `list_files` handler | Handler (lines 996-1032) is clean; mysql2 import remains but only used by `read_file` — unaffected | ✅ |
| Fetch to `${FAIT_BASE_URL}/api/workspace/internal/list-files` | Line 1014: exact URL match | ✅ |
| `X-Internal-Token` header sent | Lines 1005-1006: conditional on `INTERNAL_API_TOKEN` | ✅ |
| Non-200 error handling | Line 1023: logs + returns 500; line 1030: catches network errors → 500 | ✅ |
| `folder_path` deferral: warning logged, root listing returned | Lines 1010-1012: `console.warn` + continues to root listing | ✅ |

### Spec Compliance — Blazor Side

| AC | Check | Status |
|----|-------|--------|
| `POST /api/workspace/internal/list-files` exists | `[Route("api/workspace")]` on class + `[HttpPost("internal/list-files")]` → correct route | ✅ |
| `[AllowAnonymous]` with `X-Internal-Token` validation | `IsInternalAuthorized()` at line 142 called first; reads `_config["INTERNAL_API_TOKEN"]` | ✅ |
| `UserId` accepted as string GUID | `Guid.TryParse(request.UserId, out var userId)` — case-insensitive, handles Entra OIDs | ✅ |
| Returns `{ items }` via existing upload service methods | Lines 152-160: same `GetFoldersAsync`/`GetFilesAsync` as authorized endpoints | ✅ |
| `dotnet build` → 0 errors | Verified: 0 errors | ✅ |

### Consistency Audit
- `IsInternalAuthorized()` reads `_config["INTERNAL_API_TOKEN"]` — same key as harness env var `INTERNAL_API_TOKEN`; ASP.NET Core maps flat env vars directly ✅
- `[AllowAnonymous]` + internal token is the established pattern in this controller (`save-artifact`, `generate-document`) ✅
- Response shape: `{ items }` (Blazor) → `data.items || []` (harness) — consistent ✅
- `FolderId` nullable field handled: null if not provided, `Guid.TryParse` fallback if provided ✅

### Issues Found
None.

---

## Summary

| ADO | Verdict | Key finding |
|-----|---------|-------------|
| #3298 | **PASS** | HEALTHCHECK before USER (nitpick, zero functional impact) |
| #3299 | **PASS** | Pre-existing `getUserTokens` fetch has no timeout — add AbortController as follow-up (not blocking) |
| #3300 | **PASS** | Clean fix; `hasProjectKb` correctly preserved in `anyKbActive` |
| #3301 | **PASS** | Clean implementation; auth pattern consistent; error handling correct |

**No blocking issues. Ships.**

---

## Follow-up ticket (non-blocking)

**getUserTokens fetch timeout** — `harness-server.js:44`. Add 5s AbortController to the `fetch` in `getUserTokens`. Pre-existing from ADO#3240. Low urgency (only manifests if FAIT is completely unreachable, which would cause other failures anyway), but good hygiene for live environments.
