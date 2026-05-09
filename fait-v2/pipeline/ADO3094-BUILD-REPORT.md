# Build Report — ADO#3094

## What was built
File upload destination selector in ChatView. When a user attaches a file, a destination panel appears with 4 options: Chat only, Project, Knowledge Base, or Workspace. Each destination is wired to the appropriate service.

## Files changed
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`:
  - **Markup**: Added `chat-upload-trigger-row` with Attach button (InputFile) and upload status between KB toggles and text input. Added `chat-upload-panel` (shown only when `_pendingFile != null`) with filename display, 4 destination buttons, Upload/Cancel buttons.
  - **Injections**: `KbDocumentService KbDocSvc` and `IWorkspaceService WorkspaceService` added to `@code`.
  - **State fields**: `_pendingFile`, `_uploadDestination`, `_fileUploading`, `_uploadStatusMessage`
  - **Methods**: `HandleFileSelected`, `ClearFile`, `UploadFile` — all 4 destinations wired: Chat only (no upload), Project (`KbDocSvc.UploadProjectDocumentAsync` with active project guard), KB (`KbDocSvc.UploadDocumentAsync(KbTier.Personal)`), Workspace (`WorkspaceService.UploadFileAsync`)
  - **CSS**: 12 new CSS-variable-only classes for the upload UI
- `src/FortressAI.V2.Web/Program.cs`:
  - Added `POST /api/workspace/upload` endpoint (cookie auth, multipart form, S3 PutObject to `workspaces/{userId}/{folder}/{filename}`)

## Parallelization used
Yes — ADO#3093 and ADO#3094 ran in parallel CC sessions (no shared file writes during CC execution).

## CC sessions run
1 CC session (Sonnet) — completed cleanly

## Acceptance criteria verification
- [x] Destination selector visible when file is attached — Upload panel shown when `_pendingFile != null`
- [x] All 4 destinations wired — Chat (no-op), Project (with "no project selected" guard), KB (personal tier), Workspace (S3)
- [x] Workspace upload endpoint works — `POST /api/workspace/upload` with RequireAuthorization
- [x] CSS variables only — no inline styles, all classes use var(--*)
- [x] `dotnet build` 0 errors — verified ✅

## Known edge cases / things Clint should scrutinize
- Chat-only destination: currently just shows a status label but doesn't actually attach the file content to the next message (treated as informational). If we want true inline attachment, that's a follow-up.
- File size limit: `OpenReadStream(maxAllowedSize: 50MB)` — appropriate for most docs but may need tuning.
- Project destination: uses `ProjectState.ActiveProjectId` (singleton scoped state). If user switches projects mid-upload the check is accurate at time of click.

## How to test locally
1. Open chat, click Attach button → file picker appears
2. Select a file → `chat-upload-panel` appears with 4 destination buttons
3. Select "Workspace" → click Upload → file goes to S3 `workspaces/{userId}/uploads/{filename}`
4. Select "Project" with no project active → message "No project selected — select one in the sidebar"
5. Commit: `aaf41e72`
