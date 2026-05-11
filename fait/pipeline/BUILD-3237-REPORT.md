# Build Report — ADO#3237

## What was built
Fixed MS365 task auth: `ScheduledTaskBackgroundService` was not passing `EnabledMcpSlugs` to `TurnRequest`, causing the harness to omit `graph_*` tools from `toolConfig` entirely — the model couldn't call them.

## Root Cause
Path 3 from the bug analysis was the culprit. `ScheduledTaskBackgroundService.ProcessTaskAsync` built `TurnRequest` with only 4 fields: `UserId`, `Message`, `IsScheduledTask`, `TaskMode`. `EnabledMcpSlugs` defaulted to `null`.

In `harness-server.js`, `enabledMcpSlugs` was destructured as `rawBody.EnabledMcpSlugs ?? rawBody.enabledMcpSlugs ?? []` — so it arrived as `[]`. The loop `for (const slug of enabledMcpSlugs)` never ran, and `MCP_TOOL_SPECS['m365']` was never added to `toolConfig`. The Bedrock model had no `graph_*` tools available and could not invoke them.

The "auth error" was misleading — it wasn't an auth failure, it was the model having no tools to call at all (or calling something that didn't exist).

## Paths 1 and 2 — Not the bug
- Path 1 (userId scope in agentic loop): The `/turn` handler correctly reads `userId` from `rawBody` and `userId` is in scope for all dispatch branches including `graph_*` tools. ✅ Not the bug.
- Path 2 (userId format): The harness `getUserMs365Token(userId)` queries `WHERE user_id = ?` — same string format as what Blazor sends (`task.UserId.ToString()` produces standard GUID string). ✅ Not the bug.

## Files Changed
- `fait/src/FortressAI.Web/Services/ScheduledTaskBackgroundService.cs` — added `IMcpToolService` resolution from DI scope and `EnabledMcpSlugs` population in `TurnRequest`

## Parallelization Used
No — single-file change.

## CC Sessions Run
1 (CC Sonnet)

## The Fix
Before building `TurnRequest` in `ProcessTaskAsync`:
1. Resolve `IMcpToolService` from the existing `services` (IServiceProvider) scope
2. Call `GetActiveServersForUserAsync(task.UserId)` — user-level lookup because `ScheduledTask` has no `ConversationId`
3. Extract `.Slug` from each active server, deduplicate
4. Pass as `EnabledMcpSlugs: enabledMcpSlugs.Count > 0 ? enabledMcpSlugs : null`

This mirrors the `ChatView.razor` pattern (which uses `GetConversationToolsAsync` + slug extraction) but adapts it for the task context.

## Acceptance Criteria Verification
- [x] `EnabledMcpSlugs` populated in TurnRequest for scheduled tasks — verified in code, line 131
- [x] `dotnet build` passes — 0 errors, 46 pre-existing MudBlazor warnings
- [x] No harness changes needed — harness already handles `enabledMcpSlugs` correctly
- [x] Commit: `bfc297e9` — `fix(fait#3237): MS365 task auth — pass EnabledMcpSlugs in ScheduledTaskBackgroundService`

## Known Edge Cases / Things Clint Should Scrutinize
- `GetActiveServersForUserAsync` does a DB query — this adds one DB call per task execution. Acceptable for a background service, but worth noting.
- This is user-level (not conversation-level) server resolution. If a user disables an MCP server globally but a task was created expecting it, the task won't have that server. This is the correct behavior — same as if the user revokes OAuth.
- No `ConversationId` is passed in `TurnRequest` for scheduled tasks (it was already null before this fix). The harness uses `conversationId` only for `create_document` routing, so this is fine.

## How to Test Locally
1. Ensure a user has MS365 authenticated in FAIT settings (token in `user_microsoft_tokens`)
2. Create an on-demand scheduled task with a prompt like "List my last 5 emails"
3. Trigger the task (set `NextRunAt` to now in DB, or wait for poll)
4. Check `scheduled_task_runs` for success status and verify `result_summary` contains email data
5. Previously this would fail with an error; now the model should have `graph_list_emails` available and call it successfully
