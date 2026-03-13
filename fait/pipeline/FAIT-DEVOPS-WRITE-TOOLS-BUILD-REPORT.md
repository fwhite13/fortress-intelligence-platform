# Build Report: FAIT DevOps Write Tools
**Task:** Add 6 write tools to the DevOps MCP adapter  
**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-13  
**Commit:** `ec2e9af`

---

## Summary

Added 6 write tools to the FAIT Azure DevOps MCP adapter across 3 files. Build: **0 Error(s)**.

---

## Files Modified

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/DevOpsToolService.cs` | Added 6 new public async methods for write operations |
| `src/FortressAI.Web/Services/DevOpsMcpAdapter.cs` | Updated tool manifest (6 read → 12 total), switch dispatch, 6 handler methods |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | Extended devOpsManifest seed array with 6 new tools |
| `src/FortressAI.Web/Components/Chat/ChatView.razor` | Extended DevOps system prompt guidance with write tool list |

---

## Part 1: DatabaseInitializationService.cs — Tool Manifest Seed

### New tools appended to `devOpsManifest`:
- `create_work_item`
- `update_work_item`
- `add_work_item_comment`
- `create_branch`
- `create_pull_request`
- `update_pull_request`

### ON DUPLICATE KEY UPDATE — `tool_manifest` confirmed:
```sql
ON DUPLICATE KEY UPDATE
    endpoint_url = VALUES(endpoint_url),
    auth_type = VALUES(auth_type),
    requires_user_auth = VALUES(requires_user_auth),
    tool_manifest = VALUES(tool_manifest),   -- ✅ PRESENT — self-heals on redeploy
    updated_at = NOW(6)
```

---

## Part 2: DevOpsMcpAdapter.cs — Tool Dispatch & Handlers

### Helper method used for Azure DevOps API calls:
**`DevOpsToolService`** methods — the adapter delegates all HTTP calls to the service layer. The service uses:
- `GetCredentialsAsync(userId)` — resolves `(orgUrl, pat)` tuple
- `BuildRequest(method, url, pat)` — builds `HttpRequestMessage` with Basic auth (`Authorization: Basic base64(":{PAT}")`)
- `_httpClientFactory.CreateClient("azure-devops")` — sends requests

There is no `CallDevOpsApiAsync` helper in the adapter; the pattern is service-layer delegation (same as all existing tools).

### Content-Type `application/json-patch+json` — confirmed used for:
- `CreateWorkItemAsync` — `PATCH .../wit/workitems/${type}` ✅
- `UpdateWorkItemAsync` — `PATCH .../wit/workitems/{id}` ✅

Both set:
```csharp
req.Content = new StringContent(JsonSerializer.Serialize(ops), Encoding.UTF8, "application/json-patch+json");
```

### Switch cases added:
```csharp
"create_work_item"    => await HandleCreateWorkItem(userId, args),
"update_work_item"    => await HandleUpdateWorkItem(userId, args),
"add_work_item_comment" => await HandleAddWorkItemComment(userId, args),
"create_branch"       => await HandleCreateBranch(userId, args),
"create_pull_request" => await HandleCreatePullRequest(userId, args),
"update_pull_request" => await HandleUpdatePullRequest(userId, args),
```

---

## Part 3: ChatView.razor — System Prompt Update

Added write tool guidance to the DevOps injection block:

```
**Write tools available:**
- `azdo__create_work_item` — create a new work item (Task, Bug, User Story, etc.)
- `azdo__update_work_item` — update state, title, description, assigned to, priority, tags
- `azdo__add_work_item_comment` — add a comment/discussion entry to a work item
- `azdo__create_branch` — create a new Git branch from a base ref
- `azdo__create_pull_request` — create a pull request between branches
- `azdo__update_pull_request` — complete, abandon, or update a pull request

Use write tools only when the user explicitly asks to create, update, or modify something.
Always confirm the action with the user before executing write operations.
```

---

## Build Result

```
dotnet build
→ 29 Warning(s)   (pre-existing MudBlazor analyzer warnings — unrelated)
→ 0 Error(s)      ✅
→ Time Elapsed 00:00:06.30
```

---

## Commit

```
commit ec2e9af
feat(devops): add 6 write tools — create/update work items, branches, PRs
Pushed to: origin/main ✅
```

---

## Self-Review Checklist

- [x] All 6 tools implemented in `DevOpsToolService.cs` with correct REST endpoints
- [x] All 6 handlers wired in `DevOpsMcpAdapter.cs` switch dispatch
- [x] Tool manifest in adapter updated (6 read + 6 write = 12 total)
- [x] `application/json-patch+json` used for `create_work_item` and `update_work_item`
- [x] JSON Patch operations use `op: "add"` for work item fields (correct Azure DevOps convention)
- [x] Optional fields only included in patch array when non-null/non-empty
- [x] `create_branch` implements two-step: GET SHA → POST refs
- [x] `create_pull_request` wraps reviewers as `[{"id": "<email>"}]` array
- [x] `update_pull_request` builds partial body (only non-null fields)
- [x] `DatabaseInitializationService.cs` ON DUPLICATE KEY UPDATE includes `tool_manifest`
- [x] ChatView.razor write tool guidance added with confirmation note
- [x] Build: 0 errors
- [x] Committed and pushed
