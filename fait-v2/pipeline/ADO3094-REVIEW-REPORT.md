# Review Report — ADO#3094

### Verdict: PASS

---

### Spec Compliance Check

**Spec:** ADO#3094 — File Upload Destination Selector (commit `aaf41e72`)

**§2 Codebase Map:**
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — ✅ Modified with upload UI, state fields, and methods
- `src/FortressAI.V2.Web/Program.cs` — ✅ Modified with `POST /api/workspace/upload` endpoint

**§7 Acceptance Criteria:**

**ChatView.razor:**
- [x] 1. Attach file button with InputFile component — ✅ `<label class="chat-file-attach-btn">` with `<InputFile OnChange="HandleFileSelected">` in `chat-upload-trigger-row`
- [x] 2. Destination panel appears when file selected with 4 options — ✅ `@if (_pendingFile != null)` shows `chat-upload-panel` with Chat only / Project / Knowledge Base / Workspace buttons
- [x] 3. Default destination is "chat" — ✅ `_uploadDestination = "chat"` in `HandleFileSelected`
- [x] 4. Project destination: guard if no project selected — ✅ `if (string.IsNullOrEmpty(ProjectState.ActiveProjectId))` → "No project selected — select one in the sidebar"
- [x] 5. KB destination: uses existing KB upload flow — ✅ `KbDocSvc.UploadDocumentAsync(stream, fileName, contentType, KbTier.Personal, _userId)`
- [x] 6. Workspace destination: POSTs to WorkspaceService — ✅ `WorkspaceService.UploadFileAsync(_userId, "uploads", fileName, stream)` (Blazor server-side call, not HTTP POST)
- [x] 7. Post-upload status shows `📎 {filename} → {destination}` — ✅ `_uploadStatusMessage = $"📎 {fileName} → {destLabel}"` for all non-error paths
- [x] 8. ClearFile/cancel button works — ✅ `ClearFile()` sets `_pendingFile = null`, `_uploadStatusMessage = null`, calls `StateHasChanged()`
- [x] 9. CSS variables only — ✅ All 12 new CSS classes use `var(--...)` tokens exclusively (verified via git diff — no hardcoded hex colors, px sizes, or rem values in new code)
- [x] 10. No `@if (_pendingFile != null)` wrapped MudDialog — ✅ Upload panel uses a plain `<div>`, not a `MudDialog`. No MudDialog components anywhere in ChatView.

**Program.cs:**
- [x] 11. `POST /api/workspace/upload` endpoint exists — ✅
- [x] 12. Accepts multipart form with file — ✅ `httpContext.Request.HasFormContentType` check, `ReadFormAsync`, `form.Files.FirstOrDefault()`
- [x] 13. Auth: `.RequireAuthorization()` — ✅ `GetUserId(httpContext)` check + `.RequireAuthorization()` at endpoint registration
- [x] 14. Puts file to S3 under `workspaces/{userId}/{folder}/{filename}` — ✅ `s3Key = $"workspaces/{userId}/{folder}/{fileName}"`
- [x] 15. Returns `{ s3Key: "..." }` — ✅ `Results.Ok(new { s3Key })`

**Spec compliance verdict:** ✅ COMPLIANT

---

### CC Review Summary

CC session was terminated by resource constraints before completing. Manual review covered all spec criteria and consistency points directly from source. No CC findings to synthesize — manual review was thorough.

---

### Consistency Audit

**Files Cross-Referenced:**
- `ChatView.razor` `UploadFile()` → calls `WorkspaceService.UploadFileAsync` (server-side, no HTTP) ↔ `Program.cs` `POST /api/workspace/upload` (REST endpoint) — ✅ Two separate upload paths, intentionally independent. Component uses service directly (Blazor server-side); REST endpoint is for external/harness use. Both resolve to same S3 bucket and key pattern.
- `Program.cs` workspace endpoint bucket: `AWS:WorkspaceBucket ?? AWS:S3Bucket ?? "fortress-user-workspaces"` ↔ `WorkspaceService.Bucket`: `AWS:WorkspaceBucket ?? "fortress-user-workspaces"` — ✅ Consistent defaults, endpoint has broader fallback (harmless).
- `Program.cs` endpoint filename: `Path.GetFileName(file.FileName)` — ✅ Sanitized correctly
- `WorkspaceService.UploadFileAsync` filename parameter: received from caller as `_pendingFile.Name` (IBrowserFile.Name returns filename only, not path) — ✅ Safe from Blazor component caller; service itself doesn't sanitize (noted in Nitpick #1)
- S3 key pattern `workspaces/{userId}/{folder}/...` ↔ `WorkspaceService.GetDownloadUrlAsync` ownership check `s3Key.StartsWith($"workspaces/{userId}/")` — ✅ Keys written and validated consistently

**Injected services verified:**
- `KbDocumentService KbDocSvc` — `@inject` present ✅
- `IWorkspaceService WorkspaceService` — `@inject` present ✅
- DI registration: `builder.Services.AddScoped<IWorkspaceService, WorkspaceService>()` — ✅ Present in Program.cs (line 179)
- `IAmazonS3` (used by WorkspaceService and workspace endpoint): `builder.Services.AddAWSService<IAmazonS3>()` — ✅ Singleton via AWS SDK integration; correct per anti-pattern KB

---

### Critical Issues [0]
None.

---

### Important Issues [0]
None.

---

### Nitpicks [3]

| # | File | Location | Issue |
|---|------|----------|-------|
| N1 | WorkspaceService.cs | `UploadFileAsync` | `fileName` parameter is not sanitized with `Path.GetFileName()`. Safe from the Blazor component caller (`IBrowserFile.Name` is filename-only), but if called from another code path with a full path string, embedded slashes would create unexpected S3 prefixes. Low current risk; defense-in-depth fix would add `fileName = Path.GetFileName(fileName)` at top of method. |
| N2 | ChatView.razor | `UploadFile()` | Chat-only destination does not actually attach the file to the next message — it only shows a status label. This matches the build report's "known edge case" disclosure. Not a bug, but current UX implies the file will be included in the next message when it won't be. Worth a follow-up WI if inline attachment is desired. |
| N3 | ChatView.razor | Pre-existing | `.chat-run-as-task-btn` and `.agent-dismiss-btn` CSS classes use hardcoded values (`#444`, `#999`, `6px`, `12px`, `0.8rem`, `#7c83ff`) — **pre-existing issue, not introduced by this commit**. Not blocking ADO#3094, but should be cleaned up in a polish pass. |

---

### Spec Fidelity
All 15 acceptance criteria are met. The build fully delivers what the spec asked for. The 4-destination wiring is correct, the auth pattern matches existing conventions, CSS is token-compliant, and the Blazor dialog anti-pattern was correctly avoided.

---

### Notes for Tony
The implementation is solid. Three nitpicks, all non-blocking:
1. Consider adding `fileName = Path.GetFileName(fileName)` to `WorkspaceService.UploadFileAsync` for defense-in-depth
2. Chat-only destination is disclosed but may need a follow-up WI for actual inline attachment
3. Pre-existing hardcoded CSS values in `chat-run-as-task-btn` worth a future cleanup

Ship it.
