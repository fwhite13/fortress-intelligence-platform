# Review Report — ADO#3112

**WI:** S3 Workspace Sync + list_workspace_files Tool  
**Commit:** `fec310b2`  
**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-09  

---

### Verdict: ✅ PASS

---

### CC Review Summary

CC verified all functional correctness items. One WARN found — inline S3Client instantiation per request in `list_workspace_files` instead of using the global singleton. Non-blocking. All spec requirements met.

---

### Spec Compliance Check

**§ Pre-CC sync:**
- `aws s3 sync s3://${S3_BUCKET}/workspaces/${userId}/ ${userWorkspaceDir}/ --quiet` — ✅ present at line 954, before CC spawn at line 964
- Wrapped in try/catch, catch logs warn and falls through — ✅ non-blocking
- Inside `taskMode=true` branch only — ✅ correct scope

**§ Post-CC sync:**
- `aws s3 sync ${userWorkspaceDir}/ s3://${S3_BUCKET}/workspaces/${userId}/ --quiet` — ✅ in `ccProcess.on('close', ...)` handler
- Wrapped in try/catch, non-blocking — ✅
- Sync completes BEFORE `endResponse()` — ✅ order verified

**§ Route registration order:**
- `app.post('/tools/list_workspace_files', ...)` — ✅ line 700
- `app.post('/tools/:toolName', ...)` — ✅ line 731
- Specific route precedes parameterized catch-all by 31 lines — ✅ Express matches specific first

**§ list_workspace_files implementation:**
- Reads `userId` and `folder` from `req.body` — ✅
- Lists S3 under `workspaces/{userId}/{folder}/` — ✅ prefix built correctly with slash stripping
- Returns `{ files: [{name, size, modified}] }` — ✅ (plus `prefix`, `truncated` — benign additions)
- 400 on missing userId — ✅

**§ Bedrock toolConfig:**
- `list_workspace_files` in tools array — ✅ lines 1092–1108
- Schema: `folder` optional string — ✅ no required array, good description

**§ Tool dispatch:**
- `toolUseAccumulator.name === 'list_workspace_files'` → calls `http://localhost:${PORT}/tools/list_workspace_files` — ✅ line 1145–1155
- Else falls through to KB search — ✅

---

### Consistency Audit

| Check | Status |
|-------|--------|
| `list_workspace_files` in toolConfig ↔ dispatch handler name | ✅ consistent |
| `folder` param in schema ↔ `folder: toolInput.folder \|\| ''` in dispatch | ✅ consistent |
| `S3_BUCKET` env var used consistently across syncs | ✅ consistent |
| `userWorkspaceDir` path in pre/post sync matches CC spawn `cwd` | ✅ same variable |

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| WARN (follow-up) | `harness-server.js` | 707–708 | `list_workspace_files` route does `require('@aws-sdk/client-s3')` inline and creates `new S3ClientLocal(...)` per request. Global `s3Client` singleton exists at line 13. | Replace inline instantiation with top-level `s3Client`; import `ListObjectsV2Command` at module top alongside other S3 imports. |

---

### node --check
✅ Passes clean.

---

### What to Fix
Nothing required for ship. Follow-up: refactor `list_workspace_files` to use global `s3Client` instead of instantiating per-request.

---

_Reviewed with Claude Code (Sonnet). ADO#3112 ships._
