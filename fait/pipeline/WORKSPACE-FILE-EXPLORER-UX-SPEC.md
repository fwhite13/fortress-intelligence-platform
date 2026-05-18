# Workspace File Explorer UX — Full Redesign Spec

**Created:** 2026-05-17  
**Status:** Ready to file as WI — pending Fred approval on library choice  
**Depends on:** ADO#3396 (foundation — DB, service layer, S3 already built)  
**Replaces:** Current custom MudBlazor implementation in `WorkspaceFiles.razor`

---

## Problem

The current workspace UI (ADO#3396) is a basic file list with upload/download/delete. It does not provide the UX users expect from a file manager:
- No drag-and-drop between folders
- No right-click context menu
- No cut/copy/paste
- No confirmation/warning on destructive actions (folder delete)
- No keyboard shortcuts
- No multi-select with bulk operations
- No file preview

---

## Recommended Approach: Syncfusion Blazor File Manager

Do not reinvent this. Syncfusion's `FileManager` component provides a complete desktop-class file explorer out of the box.

**Features included:**
- Folder tree (left pane) + details/thumbnail view (right pane)
- Drag-and-drop within and between folders
- Right-click context menu (open, cut, copy, paste, rename, delete, download, details)
- Multi-select with Ctrl/Shift click
- Keyboard shortcuts (F2 rename, Delete, Ctrl+C/V/X, Ctrl+A)
- Confirmation dialogs on destructive operations
- Breadcrumb navigation
- New folder creation
- Rename inline
- Search/filter
- File type icons

**Licensing:**
- Community License = free for companies < $1M annual revenue
- Verify Fortress/FAM qualifies before implementing
- If not: evaluate commercial license cost vs. build-from-scratch cost

**NuGet:** `Syncfusion.Blazor.FileManager`

---

## Architecture

Syncfusion FileManager uses a provider pattern — it communicates with a backend `FileSystemProvider` via HTTP. We need to implement the provider endpoints that map to our S3/DB-backed workspace storage.

### Backend: `WorkspaceFileManagerController.cs` (new)
Implement the Syncfusion File Manager service interface:

| Operation | Syncfusion Action | Our Implementation |
|-----------|------------------|--------------------|
| List directory | `Read` | `IWorkspaceFileService.ListFilesAsync()` |
| Create folder | `Create` | `IWorkspaceFileService.CreateFolderAsync()` |
| Rename | `Rename` | `IWorkspaceFileService.RenameAsync()` |
| Delete | `Delete` | `IWorkspaceUploadService.DeleteFileAsync()` / `DeleteFolderAsync()` |
| Move | `Move` | New: update `folder_path` in DB + move S3 key |
| Copy | `Copy` | New: duplicate DB row + copy S3 object |
| Download | `Download` | Existing presigned URL flow |
| Upload | `Upload` | Existing `SaveUploadAsync()` |
| Search | `Search` | Query DB by filename |

### Frontend: Replace `WorkspaceFiles.razor`
- Drop in `SfFileManager` component pointed at the new controller endpoints
- Style to match FIP design system (dark theme, gold accents)
- Keep the GENERATED tab (AI-generated files) — Syncfusion supports custom tabs/views via toolbar customization

---

## New Service Methods Needed

The service layer (ADO#3396) built read/delete/upload. Need to add:

1. **`MoveFileAsync(fileId, newFolderPath)`** — update `folder_path` in `user_workspace_uploads` + rename S3 key (copy + delete)
2. **`CopyFileAsync(fileId, destinationFolderPath)`** — duplicate DB row with new GUID + S3 copy
3. **`RenameAsync(fileId, newName)`** — update `file_name` in DB (S3 key stays same)
4. **`CreateFolderAsync(userId, folderPath)`** — insert a folder-type record (or virtual folder via path convention — no separate folder table needed if using path prefixes)
5. **`SearchAsync(userId, query)`** — query `user_workspace_uploads` by filename

---

## Acceptance Criteria

1. **Folder tree** — left pane shows folder hierarchy; expand/collapse; clicking navigates
2. **Drag-and-drop** — files and folders can be dragged between folders; DB + S3 updated on drop
3. **Right-click context menu** — all standard operations: rename, cut, copy, paste, delete, download, get details
4. **Delete confirmation** — folder deletes require confirmation dialog showing item count; single file deletes also confirm
5. **Multi-select** — Ctrl+click, Shift+click, Ctrl+A; bulk delete/move/download
6. **Rename** — F2 or context menu; inline editing
7. **New folder** — toolbar button + right-click; instant creation
8. **Keyboard shortcuts** — F2 (rename), Delete, Ctrl+C/X/V, Ctrl+A, Escape
9. **GENERATED tab preserved** — AI-generated files accessible separately (toolbar customization or secondary view)
10. **Version history** — accessible via right-click context menu → "Version History" (existing modal from ADO#3396)
11. **Upload** — drag files from desktop onto the workspace; also toolbar button
12. **Dark theme** — styled to match FIP design system

---

## Implementation Notes

- Syncfusion requires a license key set in `Program.cs` via `Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense()`
- Add to Blazor task def env vars: `SYNCFUSION_LICENSE_KEY`
- The FileManager provider controller must handle Syncfusion's specific request/response JSON schema — reference Syncfusion docs for `FileManagerDirectoryContent` model
- S3 move = copy object to new key + delete original (no server-side rename in S3)
- Folder paths stored as string in `user_workspace_uploads.folder_path` — virtual folders (no separate folder table needed)

---

## Files

- **New:** `fip/fait/src/FortressAI.Web/Controllers/WorkspaceFileManagerController.cs`
- **Modified:** `fip/fait/src/FortressAI.Web/Components/Pages/WorkspaceFiles.razor` — replace current UI with SfFileManager
- **Modified:** `fip/fait/src/FortressAI.Web/Services/IWorkspaceFileService.cs` — add Move, Copy, Rename, CreateFolder, Search
- **Modified:** `fip/fait/src/FortressAI.Web/Services/WorkspaceUploadService.cs` — implement new methods
- **Modified:** `fip/fait/src/FortressAI.Web/FortressAI.Web.csproj` — add Syncfusion.Blazor.FileManager NuGet

---

## Open Question for Fred

**Syncfusion Community License:** Free for < $1M annual revenue. Does Fortress/FAM qualify?
- If yes → use Syncfusion, this spec is ready to file
- If no → two options: (a) pay for commercial license, or (b) build full UX from scratch on MudBlazor (significantly more work, estimated 3-4x effort)

Syncfusion is strongly recommended. The FileManager component is mature, well-documented, and solves exactly this problem.
