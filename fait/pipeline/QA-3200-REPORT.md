# QA Report: ADO#3200 — 5.1-A: user_workspace_files, S3 storage, artifact SSE event + chat card

**Verdict: ✅ QA PASS**

**Analyst:** Black Widow (Natasha Romanoff)
**Date:** 2026-05-10
**Commit:** `aca376f2`
**Task Def:** `fred-dev:166`

---

## Tests Run

- Smoke: 3 — 3 passed
- Code-level: 12 — 12 passed
- Regression: 1 — 1 passed

---

## Service Health

| Check | Result | Detail |
|-------|--------|--------|
| ECS Service `fred-dev:166` | ✅ PASS | ACTIVE, 1/1 running, taskDef matches |
| CloudWatch: `Database initialization complete` | ✅ PASS | Present in log, no DI/startup exceptions |
| CloudWatch: `Application started` | ✅ PASS | Present, no errors |
| CloudWatch: `ScheduledTaskBackgroundService starting, poll interval: 60s` | ✅ PASS | Regression check — present |

---

## Migration

| Check | Result | Detail |
|-------|--------|--------|
| EF migration `20260510174001_AddWorkspaceFiles` exists | ✅ PASS | `Migrations/20260510174001_AddWorkspaceFiles.cs` present |
| Migration creates `user_workspace_files` table | ✅ PASS | `migrationBuilder.CreateTable(name: "user_workspace_files")` confirmed |
| Migration schema correct | ✅ PASS | id, user_id, conversation_id, task_run_id (nullable), filename, mime_type, s3_key, size_bytes, created_at — all present with correct types |
| Indexes created | ✅ PASS | `idx_uwf_conversation_id`, `idx_uwf_user_id` — both present |
| No errors touching existing tables | ✅ PASS | CloudWatch shows all prior migrations idempotent, no new table errors |
| EF migration ran at startup | ✅ PASS | No "pending migrations" error in log; `Database initialization complete` reached — migration applied cleanly |

**Note:** EF Core's `MigrateAsync()` runs before `DatabaseInitializationService` and doesn't emit verbose "Applying migration" logs at the default info level. Absence of migration errors + reaching `Database initialization complete` confirms the migration ran successfully.

---

## Code-Level

| Check | Result | Detail |
|-------|--------|--------|
| `UserWorkspaceFile.cs` in Shared/Models | ✅ PASS | `/src/FortressAI.Shared/Models/UserWorkspaceFile.cs` — Id, UserId, ConversationId, TaskRunId (nullable), Filename, MimeType, S3Key, SizeBytes, CreatedAt |
| `IWorkspaceFileService.cs` in Services | ✅ PASS | Defines `SaveArtifactAsync`, `GetConversationArtifactsAsync`, `GetUserArtifactsAsync`, `GetPresignedDownloadUrlAsync` |
| `WorkspaceFileService.cs` in Services | ✅ PASS | Full implementation — EF Core + IAmazonS3; bucket from `WORKSPACE_S3_BUCKET` config with fallback |
| `ArtifactCard.razor` in Components/Chat | ✅ PASS | Present and complete |
| `IWorkspaceFileService` registered in Program.cs | ✅ PASS | `builder.Services.AddScoped<IWorkspaceFileService, WorkspaceFileService>();` confirmed |
| `HarnessEvent` type comment includes "artifact" | ✅ PASS | `// "text" \| "log" \| "done" \| "error" \| "mode_switch" \| "artifact"` at line 63 of IUserAgentRuntime.cs |
| `ChatView.razor`: `artifact` SSE handler present (try/catch wrapped) | ✅ PASS | `else if (evt.Type == "artifact")` block at ~line 925, wrapped in try/catch; logs warning on failure |
| `ChatView.razor`: `_conversationArtifacts = new()` in both null-conversation branches | ✅ PASS | Two branches confirmed: `conversation == null` after fetch (line 447) and lazy-create branch (line 453) |
| `ChatView.razor`: `GetConversationArtifactsAsync` called on conversation load | ✅ PASS | `_conversationArtifacts = await WorkspaceFileSvc.GetConversationArtifactsAsync(conversation.Id)` at line 443 |
| `ArtifactCard.razor`: Preview button `Disabled="true"` with tooltip | ✅ PASS | `<MudTooltip Text="Preview coming soon"><MudButton ... Disabled="true">Preview</MudButton></MudTooltip>` |
| `ArtifactCard.razor`: Download uses `GetPresignedDownloadUrlAsync` (no raw S3 key in render) | ✅ PASS | `DownloadAsync()` calls `WorkspaceFileSvc.GetPresignedDownloadUrlAsync(Artifact.S3Key)` — presigned URL opened in new tab, S3 key never rendered in markup |

---

## Pre-Existing Blockers

| Item | Status |
|------|--------|
| Browser E2E (Cloudflare + TestAuth__Secret) | ⚠️ PRE-EXISTING — not a regression. E2E artifact flow testing (send a message → receive artifact SSE → card renders) blocked by Cloudflare WAF + TestAuth config on the dev environment. This was a known blocker before this WI. |

E2E flow verification (artifact card rendering in chat after SSE event) deferred to Fred's manual acceptance when accessible.

---

## Key Findings

1. **Service is healthy.** `fred-dev:166` running 1/1, startup clean, no DI exceptions.
2. **Migration is correctly structured** — creates `user_workspace_files` with proper schema, FK-ready columns (user_id, conversation_id, task_run_id), and indexed on both FKs.
3. **Service layer is complete and correct** — all 4 interface methods implemented, S3 presigned URL generation uses AWS SDK (not raw key exposure), bucket configurable via env var.
4. **ChatView wiring is thorough** — artifacts loaded on conversation load, reset on null-conversation in both branches, SSE handler is fault-tolerant (warning log, not exception propagation).
5. **ArtifactCard is UI-safe** — Preview is intentionally disabled with tooltip, download is properly async with loading state.

---

## Test Duration

~8 minutes

---

## Recommendations

- None blocking. Story 5.1-A is complete and correctly implemented.
- Follow-up: E2E smoke test of the full artifact flow (harness → SSE → DB → card) when Cloudflare/TestAuth is bypassed for the dev environment.
- Follow-up (Epic 5 continued): 5.1-B will want to verify `WORKSPACE_S3_BUCKET` env var is set in the task definition before artifacts can actually be saved.
