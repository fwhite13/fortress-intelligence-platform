# Code Review Report: FAIT DevOps Write Tools
**Commit:** `ec2e9af`
**Reviewer:** Hawkeye (code-reviewer)
**Review Cycle:** 1 of 2
**Date:** 2026-03-13

---

## Verdict: NEEDS-CHANGES

One **Important** bug found (wrong tool name prefix in ChatView system prompt). All other 26 checklist items pass, including both focus items on `application/json-patch+json` and the `create_branch` two-step.

---

## Checklist Results

### Seed manifest — DatabaseInitializationService.cs (items 1–5)

| # | Item | Status | Notes |
|---|------|--------|-------|
| 1 | All 6 new tools in `devOpsManifest` JSON array | ✅ PASS | `create_work_item`, `update_work_item`, `add_work_item_comment`, `create_branch`, `create_pull_request`, `update_pull_request` all present |
| 2 | Required fields correct per tool | ✅ PASS | All required arrays match spec exactly — `[project, type, title]`, `[id]`, `[project, id, comment]`, `[project, repo, branchName]`, `[project, repo, title, sourceBranch, targetBranch]`, `[project, repo, pullRequestId]` |
| 3 | `ON DUPLICATE KEY UPDATE` includes `tool_manifest = VALUES(tool_manifest)` | ✅ PASS | DevOps INSERT block includes `tool_manifest = VALUES(tool_manifest)` |
| 4 | Total tool count 12 (6 read + 6 write) | ✅ PASS | Confirmed 12 tool entries in `new[]` array |
| 5 | All tool names `snake_case` | ✅ PASS | All 12 names are snake_case |

---

### DevOpsMcpAdapter.cs (items 6–10)

| # | Item | Status | Notes |
|---|------|--------|-------|
| 6 | Switch dispatch covers all 6 new tool names | ✅ PASS | All 6 new cases present, names exactly match manifest |
| 7 | Adapter delegates to `DevOpsToolService` — no inline REST | ✅ PASS | Each handler calls `_devOpsSvc.*Async(...)` — zero REST logic in adapter |
| 8 | Return type consistent with existing tools | ✅ PASS | All 6 new tools return `McpCallResponse` with `McpToolResultContent` — same shape as existing tools |
| 9 | No new auth logic — PAT via `GetCredentialsAsync` | ✅ PASS | No auth logic in adapter; all credential resolution remains in `DevOpsToolService.GetCredentialsAsync` |
| 10 | No hardcoded org URLs or project names | ✅ PASS | All URLs built from user's stored `orgUrl` via `DevOpsConnectionService` |

---

### DevOpsToolService.cs (items 11–22)

| # | Item | Status | Notes |
|---|------|--------|-------|
| 11 | `create_work_item`: URL is `PATCH /{project}/_apis/wit/workitems/${type}?api-version=7.1` | ✅ PASS | `$"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version=7.1"` — correct dollar-sign `$type` convention |
| 12 | `create_work_item`: Content-Type is `application/json-patch+json` | ✅ PASS | `new StringContent(..., Encoding.UTF8, "application/json-patch+json")` |
| 13 | `create_work_item`: Only includes JSON Patch ops for non-null/non-empty optional fields | ✅ PASS | All 5 optional fields (`description`, `assignedTo`, `areaPath`, `iterationPath`, `tags`) guarded with `!string.IsNullOrEmpty(...)` before adding to ops list |
| 14 | `update_work_item`: URL is `PATCH /_apis/wit/workitems/{id}?api-version=7.1` | ✅ PASS | `$"{orgUrl}/_apis/wit/workitems/{id}?api-version=7.1"` — no project segment (correct for update) |
| 15 | `update_work_item`: Content-Type is `application/json-patch+json` | ✅ PASS | Same `application/json-patch+json` content type used |
| 16 | `update_work_item`: Only patches provided fields (skips nulls) | ✅ PASS | All 6 optional params guarded with `!string.IsNullOrEmpty()` / `.HasValue` checks; bails early with "No fields to update" if all null |
| 17 | `add_work_item_comment`: URL uses `api-version=7.1-preview.3` | ✅ PASS | `/_apis/wit/workItems/{id}/comments?api-version=7.1-preview.3` — correct preview version, POST method, `application/json` (correct for comments endpoint) |
| 18 | `create_branch`: Two-step — GET SHA then POST with `oldObjectId = "000...0"` | ✅ PASS | Step 1 fetches `objectId` via GET refs, returns error if SHA missing. Step 2 POSTs `{name, newObjectId: baseSha, oldObjectId: "0000000000000000000000000000000000000000"}` |
| 19 | `create_pull_request`: `sourceRefName`/`targetRefName` prefixed `refs/heads/` | ✅ PASS | `$"refs/heads/{sourceBranch}"` and `$"refs/heads/{targetBranch}"` |
| 20 | `update_pull_request`: Builds body with only non-null fields | ✅ PASS | Uses `Dictionary<string, object>` built conditionally; bails early if empty |
| 21 | Reviewer format `[{id: "<email_or_id>"}]` (not bare string array) | ✅ PASS | `reviewers.Select(r => new { id = r }).ToArray()` in both `create_pull_request` and `update_pull_request` |
| 22 | All methods use `BuildRequest` with PAT Basic auth — no hardcoded credentials | ✅ PASS | Every method calls `BuildRequest(method, url, pat)` where PAT comes from `GetCredentialsAsync`. Zero hardcoded credentials. |

---

### ChatView.razor (items 23–25)

| # | Item | Status | Notes |
|---|------|--------|-------|
| 23 | Write tools listed in DevOps system prompt injection block | ❌ **FAIL** | Write tools are listed, but with **wrong prefix** — see issue below |
| 24 | Confirmation-before-action note present for write operations | ✅ PASS | `"Always confirm the action with the user before executing write operations (creating items, completing PRs, etc.)."` present |
| 25 | Non-DevOps users unaffected — injection guarded by `azdo__` prefix check | ✅ PASS | Guard: `availableTools.Any(t => t.FullName.StartsWith("devops__", ...))` — only fires when user has DevOps tools loaded |

---

### Security (items 26–27)

| # | Item | Status | Notes |
|---|------|--------|-------|
| 26 | No auto-executed write operations — all tool dispatch is model/user-initiated | ✅ PASS | All tools go through `McpToolService.ExecuteToolAsync` → `DevOpsMcpAdapter.HandleMcpRequest` → `DevOpsToolService`. No background auto-execution. |
| 27 | No escalation beyond user's PAT permissions | ✅ PASS | PAT is fetched from `DevOpsConnectionService` (user's own stored credential). No privilege escalation — adapter never adds scopes or impersonates. |

---

## Issues Found

### ❌ IMPORTANT — Item #23: Wrong Tool Name Prefix in ChatView System Prompt

**File:** `ChatView.razor` lines 692–697  
**Severity:** Important — the model will be told to use tool names that don't exist; calls will fail

**Problem:**
The write tools in the injected system prompt use an `azdo__` prefix:
```
- `azdo__create_work_item`
- `azdo__update_work_item`
- `azdo__add_work_item_comment`
- `azdo__create_branch`
- `azdo__create_pull_request`
- `azdo__update_pull_request`
```

The actual tool `FullName` is built in `McpToolService.cs` line 124 as:
```csharp
var fullName = $"{server.Slug}__{def.Name}";
```
The DevOps server slug is `"devops"`, so the real tool names are `devops__create_work_item`, `devops__update_work_item`, etc.

The `azdo__*` names do not exist — if the model uses them, `ExecuteToolAsync` will fail to route the call. The read tools (which the model discovers from the tool list) use the correct `devops__` prefix, so there's an inconsistency between the tool list and the system prompt guidance.

**Fix:**
```diff
-  - `azdo__create_work_item` — create a new work item (Task, Bug, User Story, etc.)
-  - `azdo__update_work_item` — update state, title, description, assigned to, priority, tags
-  - `azdo__add_work_item_comment` — add a comment/discussion entry to a work item
-  - `azdo__create_branch` — create a new Git branch from a base ref
-  - `azdo__create_pull_request` — create a pull request between branches
-  - `azdo__update_pull_request` — complete, abandon, or update a pull request
+  - `devops__create_work_item` — create a new work item (Task, Bug, User Story, etc.)
+  - `devops__update_work_item` — update state, title, description, assigned to, priority, tags
+  - `devops__add_work_item_comment` — add a comment/discussion entry to a work item
+  - `devops__create_branch` — create a new Git branch from a base ref
+  - `devops__create_pull_request` — create a pull request between branches
+  - `devops__update_pull_request` — complete, abandon, or update a pull request
```

---

## Focus Item Verdicts

| Focus Item | Result |
|-----------|--------|
| **#12/#15** — `application/json-patch+json` for work item PATCH | ✅ **CORRECT** — both `create_work_item` and `update_work_item` use the right Content-Type |
| **#13/#16** — null field filtering for optional patch ops | ✅ **CORRECT** — all optional fields excluded when null/empty; no risk of API resetting fields |
| **#18** — `create_branch` two-step SHA lookup | ✅ **CORRECT** — properly fetches base SHA first, returns a clear error if not found, then POSTs with `oldObjectId = "000...0"` |

---

## Summary

26 of 27 checklist items pass. The implementation quality is high — the focus items (Content-Type and null-field filtering) were done correctly, the two-step branch creation is solid, and there's zero auth logic leakage into the adapter layer.

The single issue is a copy-paste artifact in the ChatView system prompt: `azdo__` prefix should be `devops__`. This is a one-line-times-six fix.

**Return to Tony for the following fix only:**
- Replace `azdo__` with `devops__` for the 6 write tool names in the ChatView.razor DevOps guidance block (lines 692–697)

No scope creep. No other changes needed.
