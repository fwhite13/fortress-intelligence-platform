# Build Report — ADO#3200

## What was built
End-to-end artifact pipeline: `user_workspace_files` DB table + EF migration, `IWorkspaceFileService`/`WorkspaceFileService` with S3 presigned URL support, `artifact` SSE event type in `HarnessEvent`, and `ArtifactCard.razor` rendered in ChatView after conversation artifacts load.

## Files Changed

| File | Change |
|------|--------|
| `src/FortressAI.Shared/Models/UserWorkspaceFile.cs` | New — DB model with Id, UserId, ConversationId, TaskRunId (nullable), Filename, MimeType, S3Key, SizeBytes, CreatedAt |
| `src/FortressAI.Web/Data/AppDbContext.cs` | Added `UserWorkspaceFiles` DbSet + `OnModelCreating` config (CHAR(36) IDs, column names, 2 indexes) |
| `src/FortressAI.Web/Migrations/20260510174001_AddWorkspaceFiles.cs` | Generated EF migration — creates `user_workspace_files` only |
| `src/FortressAI.Web/Migrations/20260510174001_AddWorkspaceFiles.Designer.cs` | Migration snapshot |
| `src/FortressAI.Web/Migrations/AppDbContextModelSnapshot.cs` | Updated model snapshot |
| `src/FortressAI.Web/Services/IWorkspaceFileService.cs` | New — interface with 4 methods + `ArtifactPayload` record |
| `src/FortressAI.Web/Services/WorkspaceFileService.cs` | New — implementation using IDbContextFactory + IAmazonS3, reads `WORKSPACE_S3_BUCKET` env var (same as MemoryFileService) |
| `src/FortressAI.Web/Program.cs` | Registered `IWorkspaceFileService` as Scoped (after `IMemoryFileService`) |
| `src/FortressAI.Web/Services/IUserAgentRuntime.cs` | Updated `HarnessEvent.Type` comment to include `"artifact"` |
| `src/FortressAI.Web/Components/Chat/ArtifactCard.razor` | New — MudCard with filename, size, disabled Preview button (tooltip "coming soon"), Download button via presigned URL + JSRuntime.InvokeVoidAsync("open", ...) |
| `src/FortressAI.Web/Components/Chat/ChatView.razor` | Injected `IWorkspaceFileService`, added `_conversationArtifacts`/`_pendingArtifact` fields, loads artifacts on conversation load, handles `artifact` SSE event, renders `ArtifactCard` list after message history |

## CC Invocations
- 1 CC Sonnet run via `cat /tmp/cc-brief-3200.md | claude --model sonnet --print --dangerously-skip-permissions`
- CC was SIGKILL'd at ~280s (output captured before kill — all files written correctly)
- Manual fix: `ArtifactCard.razor` — Razor parser mishandled `< 1024` in switch expression arms as HTML open tags; replaced with `if/return` pattern. Also moved `<style>` block after `@code` for safer Razor parsing.

## Migration SQL Snippet (Up)

```sql
CREATE TABLE `user_workspace_files` (
    `id` CHAR(36) NOT NULL,
    `user_id` CHAR(36) NOT NULL,
    `conversation_id` CHAR(36) NOT NULL,
    `task_run_id` CHAR(36) NULL,
    `filename` varchar(500) NOT NULL,
    `mime_type` varchar(200) NOT NULL,
    `s3_key` varchar(1000) NOT NULL,
    `size_bytes` bigint NOT NULL,
    `created_at` DATETIME(6) NOT NULL,
    PRIMARY KEY (`id`)
);
CREATE INDEX `idx_uwf_conversation_id` ON `user_workspace_files` (`conversation_id`);
CREATE INDEX `idx_uwf_user_id` ON `user_workspace_files` (`user_id`);
```

## Acceptance Criteria Verification

- [x] `UserWorkspaceFile` model in Shared/Models
- [x] `AppDbContext.UserWorkspaceFiles` DbSet added
- [x] EF OnModelCreating config: CHAR(36) IDs, correct column names, two indexes
- [x] Migration `AddWorkspaceFiles` generated — creates `user_workspace_files` only
- [x] `IWorkspaceFileService` interface with 4 methods + `ArtifactPayload` record
- [x] `WorkspaceFileService` implementation — IDbContextFactory pattern (no raw `new AppDbContext()`)
- [x] GuidFormat = MySqlGuidFormat.None: verified existing in Program.cs — not re-added
- [x] `IWorkspaceFileService` registered Scoped in Program.cs
- [x] `HarnessEvent` type comment updated to include "artifact"
- [x] `ArtifactCard.razor` component created
- [x] Preview button disabled + tooltip "Preview coming soon"
- [x] Download button uses presigned URL (no raw S3 key exposed)
- [x] `ArtifactCard` uses `IJSRuntime` for `window.open` download
- [x] ChatView: `IWorkspaceFileService` injected
- [x] ChatView: `artifact` SSE event handler added
- [x] ChatView: `GetConversationArtifactsAsync` called on conversation load
- [x] ChatView: `ArtifactCard` rendered for artifacts
- [x] Build: 0 errors

## Known Edge Cases / Things Clint Should Scrutinize

1. **ArtifactCard `<style>` placement** — Moved after `@code` block for Razor parser safety. CSS classes `.artifact-card-meta` and `.artifact-card-filename` use CSS variables (`--font-semibold`) per the CSS variable rule.
2. **Artifact rendering position** — Artifacts render as a group after the last message and KbIndicator, not inline per-message (no message_id FK in this WI). This is minimal-impact and matches the spec's "simplest acceptable implementation."
3. **`_pendingArtifact` tracking** — Captured per SSE turn but not currently used for per-turn inline positioning. `_conversationArtifacts` is the authoritative display list.
4. **S3 bucket key** — Uses `WORKSPACE_S3_BUCKET` env var (same as `MemoryFileService`) with fallback `"fortress-user-workspaces"`. Verify this is the correct bucket for agent-produced artifacts in prod.
5. **No FK constraint on `conversation_id`** — The `user_workspace_files` table has an index on `conversation_id` but no FK to `conversations`. This matches the spec (no FK defined) and avoids cascade delete issues if conversations are cleaned up differently.

## Commit SHA
`9fba6c72`

## How to Test Locally
1. Run `dotnet ef database update --context AppDbContext` in `src/FortressAI.Web/` to apply migration
2. Start the app and open a chat conversation
3. Simulate an `artifact` SSE event from the harness with payload `{"filename":"test.pdf","s3Key":"workspaces/.../test.pdf","mimeType":"application/pdf","sizeBytes":12345}`
4. Verify `ArtifactCard` appears in ChatView with filename, size, disabled Preview, and Download button
5. Click Download — should open presigned URL in new tab (not expose raw S3 key)

---

## Review Cycle 2 — Targeted Fix

### CC Invocation
```bash
cat /tmp/cc-brief-3200-c2.md | claude --model sonnet --print --dangerously-skip-permissions
```

### Commit
`aca376f2` — fix(chat): reset _conversationArtifacts on conversation switch (#3200)

### Fixes Applied ✅

**Fix 1** — `else` block after `if (conversation != null)` in `ConversationId.HasValue` branch:
```csharp
else
{
    _conversationArtifacts = new();
}
```
Resets artifacts when `GetConversationAsync` returns null for a valid `ConversationId`.

**Fix 2** — `_conversationArtifacts = new();` added at top of `else if (conversation == null)` block:
Resets artifacts when navigating to a new/null conversation (no ConversationId).

### Build Result
```
0 Error(s) | 38 Warning(s) (pre-existing MUD0002 warnings, unrelated)
Time Elapsed 00:00:08.45
```

### File Changed
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — 5 insertions
