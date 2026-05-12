# Build Report — ADO#3301

## What was built
Fixed `list_files` harness tool — replaced direct MySQL connection (causing ECONNREFUSED 127.0.0.1:3306 because no DB runs in the harness container) with a call to a new Blazor internal API endpoint.

## Files changed
- `src/FortressAI.Web/Services/WorkspaceController.cs` — Added `POST /api/workspace/internal/list-files` endpoint + `InternalListFilesRequest` record
- `agent-harness/harness-server.js` (in fait-v2 repo) — Replaced entire `list_files` handler body with fetch to Blazor endpoint

## Blazor endpoint added
```
POST /api/workspace/internal/list-files
[AllowAnonymous] + X-Internal-Token auth
Body: { "UserId": "<guid>", "FolderId": "<guid|null>" }
Response: { "items": [ { "name": "...", "type": "folder"|"file", ... } ] }
```
Uses `_uploadService.GetFoldersAsync(userId, folderId)` + `GetFilesAsync(userId, folderId)` — same service methods as the existing `[Authorize]` endpoints, just called with harness-supplied userId via internal token auth.

## Harness change summary
```javascript
// OLD — direct DB connection (causes ECONNREFUSED in container)
app.post('/tools/list_files', async (req, res) => {
    let conn = await getDbConnection();   // ← explodes: no MySQL in harness container
    // ... SQL queries ...
});

// NEW — Blazor internal API
app.post('/tools/list_files', async (req, res) => {
    const apiRes = await fetch(`${FAIT_BASE_URL}/api/workspace/internal/list-files`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Internal-Token': INTERNAL_API_TOKEN },
        body: JSON.stringify({ UserId: userId }),
    });
    const data = await apiRes.json();
    res.json({ items: data.items || [] });
});
```

## Known limitation
`folder_path` parameter is not resolved to a folderId in this iteration — the handler logs a warning and lists root-level items. The path-to-folderId traversal that was in the SQL code is deferred. Users with organized subfolders won't get them listed via AI tool. This is acceptable for P1 — the primary fix is eliminating the DB crash. A follow-up can add folder resolution via the `GET /api/workspace/folders` endpoint chain.

## Parallelization used
No — sequential with ADO#3300 (both in same repo session).

## CC sessions run
1 CC run (sonnet). Note: CC ran in `/home/fredw/projects/fip/fait` workspace but also needed to modify `fait-v2/agent-harness/harness-server.js` — the brief specified both paths explicitly.

## Acceptance criteria verification
- [x] No direct MySQL connection in list_files — verified in diff
- [x] Tool calls `${FAIT_BASE_URL}/api/workspace/internal/list-files` with `X-Internal-Token` — verified
- [x] `getDbConnection()` and mysql import untouched (read_file still uses them) — verified
- [x] `node --check harness-server.js` passes — verified ✅
- [x] `dotnet build` → 0 errors — verified ✅

## Commit
`5ce678c3` — fix(fait#3301): list_files — replace direct DB with Blazor internal API

## How to test locally
1. Trigger `list_files()` in chat — previously crashed with ECONNREFUSED; now returns items from Blazor
2. Check harness logs for `[list_files]` — should see fetch result, not connection error
3. Verify `read_file` still works (it still uses the DB connection path — unaffected)
