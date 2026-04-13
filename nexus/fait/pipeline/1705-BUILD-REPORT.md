# Build Report — ADO #1705
**Add .md, .json, .txt to file upload allowlist**

---

## What was built
Extended the NEXUS file upload system to accept Markdown (.md), JSON (.json), and plain text (.txt) files alongside the existing HTML/PNG/JPG/WEBP/PDF types. Changes applied to both the client-side component and the server-side storage service.

---

## Files changed

| File | Change |
|------|--------|
| `src/FortressNexus.Web/Components/Shared/FileUploadZone.razor` | 4 changes (see below) |
| `src/FortressNexus.Web/Services/FileStorageService.cs` | 2 changes (see below) |

### FileUploadZone.razor
1. **`Accept` attribute** — added `.md,.json,.txt` to browser file picker filter
2. **`AcceptedTypes` default** — added `"text/markdown"`, `"text/x-markdown"`, `"application/json"`, `"text/plain"` (text/x-markdown included for browser compat — some browsers send this for .md files)
3. **`_hint` string** — updated display text to include `MD, JSON, TXT`
4. **`GetFileIcon` switch** — added `"text/markdown" or "text/x-markdown" or "text/plain" => Icons.Material.Filled.Description` and `"application/json" => Icons.Material.Filled.DataObject`

### FileStorageService.cs
1. **`AllowedTypes` array** — extended with `"text/markdown"`, `"text/x-markdown"`, `"application/json"`, `"text/plain"` (server-side MIME guard kept in sync with client)
2. **Error message** — updated rejection message to list new types

---

## Files NOT changed (by design)
- **`FileType` enum** — no new values needed; new types correctly fall through to `FileType.Other` in `DetectFileType` (raw bytes stored, no special text extraction needed — consistent with how images are handled)
- **`SubmissionService.cs`** — no MIME validation present, no change required
- **`DetectFileType` switch** — correctly falls through to `Other` for new types

---

## Parallelization used
No — single CC session (changes were in 2 files with tight logical coupling, easier to do in one pass).

---

## Build result
```
Build succeeded.
    0 Error(s)
    0 Warning(s)
```

---

## Acceptance criteria verification
- [x] `Accept` attribute includes `.md,.json,.txt` — verified in diff
- [x] `AcceptedTypes` default includes all 4 new MIME types incl. `text/x-markdown` — verified in diff
- [x] `_hint` updated — verified in diff
- [x] Icon mapping covers new types — `Description` for text/md/txt, `DataObject` for JSON — verified in diff
- [x] Server-side `AllowedTypes` in sync with client — verified in diff
- [x] `dotnet build` — 0 errors — confirmed

---

## Known edge cases / things Clint should scrutinize
- **`text/x-markdown`**: included proactively for browser compat. No operational risk — it's an additional accepted type.
- **No text extraction for .md/.json/.txt**: These files will have `ProcessedText = null` and `FileType.Other`. If the AI pipeline needs to read their content, a future story should add extraction in `FileStorageService.UploadAsync` (similar to how HTML is handled). Out of scope for #1705.
- **`DataObject` icon**: Confirmed present in MudBlazor 7.16.0 via binary string inspection.

---

## Commit
`cb0b412` — `feat(nexus): add .md/.json/.txt to file upload allowlist [#1705]`

---

## How to test locally
1. `cd ~/projects/fip/nexus && dotnet run --project src/FortressNexus.Web`
2. Navigate to any page with a `FileUploadZone`
3. Click "Choose Files" — file picker should show `.md`, `.json`, `.txt` as valid selections
4. Select a `.md` file — should be accepted without error, Description icon shown
5. Select a `.json` file — should be accepted, DataObject icon shown
6. Select a `.txt` file — should be accepted, Description icon shown
7. Select a `.exe` or other disallowed type — should show error message including "MD, JSON, TXT"
