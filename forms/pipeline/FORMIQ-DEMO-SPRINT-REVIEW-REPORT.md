# Review Report: FormIQ Demo Sprint — Side-by-Side Review + QS Editor + Upload Polish

**Reviewer:** Hawkeye (Code Review — Stage 3)  
**Date:** 2026-02-27  
**From:** Maria Hill | **Priority:** HIGH — demo prep  
**Commits reviewed:** HEAD~2..HEAD (`5640b34`, `2162b9e`)

---

### Verdict: ✅ PASS

All three features load without crashing, stubs fail gracefully, no memory leaks found. Rhodey is clear to deploy.

---

## Consistency Audit

**Files cross-referenced:**
- `FormsController.cs` ↔ `FormDetail.razor` — ✅ all three API paths match exactly
- `QuestionSetsController.cs` ↔ `QuestionSetEdit.razor` — ✅ GET endpoint exists and shape matches DTO
- `FormDtos.cs` ↔ `FormDetail.razor` (`Fields` property) — ✅ `Fields = new()` initialized; `.Count` is safe
- `QuestionSetDetail.razor` Edit button → `/question-sets/{Id}/edit` ↔ `@page "/question-sets/{Id:int}/edit"` — ✅ route matches

**URL prefix inconsistency (style only):**
- `FormDetail.razor` / `FormLibrary.razor` use `api/forms/...` (no leading slash)
- `QuestionSetEdit.razor` uses `/api/question-sets/{Id}` (leading slash)
- With `BaseAddress = "http://localhost:5200/"` (root path), both forms resolve identically. ✅ functionally safe, but inconsistent style to fix post-demo.

---

## Feature 1: Side-by-Side Review — FormDetail.razor

### Endpoint Verification

| Endpoint | Controller Location | Exists? |
|---|---|---|
| `GET /api/forms/{id}/pdf` | `FormsController.cs` line 229 | ✅ Yes |
| `PUT /api/forms/{id}/fields` | `FormsController.cs` line 187 | ✅ Yes |
| `POST /api/forms/{id}/approve` | `FormsController.cs` line 239 | ✅ Yes |

All three API endpoints exist in `FormsController.cs`. The build report's claim is verified.

### IDisposable
`@implements IDisposable` present. `Dispose()` calls `_pollTimer?.Dispose()`. ✅

### Null Safety
- `FormDetailDto.Fields` is initialized as `= new()` — `.Count` in metadata row is safe. ✅
- `_form` only accessed in the non-null `else` branch. ✅
- `_editModels` initialized as `new()` — `SaveChanges` and `BuildEditModels` are safe. ✅

### UI States
- Loading: skeleton ✅
- Not found / 404: alert + back button ✅
- Error: alert with `ErrorMessage ?? "Please re-upload."` ✅
- Queued: alert + progress bar ✅
- Processing: alert + indeterminate progress + auto-poll ✅
- Draft/Reviewed/Approved: side-by-side review layout ✅

### Notes
- `SaveChanges` and `ApproveForm` both catch exceptions and show snackbar errors — no unhandled exceptions possible. ✅
- Approve button correctly hidden once status is outside `"Draft" or "Reviewed" or "Approved"`. ✅

---

## Feature 2: Question Set Editor — QuestionSetEdit.razor

### Route
`@page "/question-sets/{Id:int}/edit"` — correct Blazor route format with int constraint. ✅

### Data Loading
`GET /api/question-sets/{Id}` — endpoint exists in `QuestionSetsController.cs` line 48. Response shape verified:

| Field | Controller returns | DTO expects | Match |
|---|---|---|---|
| `Id`, `Name`, `Description`, `Vertical`, `Status`, `CreatedAt`, `UpdatedAt`, `CreatedBy` | ✅ | ✅ | ✅ |
| `Forms` → `FormLibraryId`, `FormName`, `CarrierName` | ✅ | `FormItem` class | ✅ |
| `Questions` → `Id`, `QuestionText`, `FieldType`, `SectionName`, `IsRequired`, `SortOrder`, `SourceFormCount`, `DictionaryFieldCode` | ✅ | `QuestionItem` class | ✅ |

### Stub Action Behavior (graceful failure check)

| Action | What it does | Graceful? |
|---|---|---|
| **Save Header** | Fires `PUT /api/question-sets/{Id}` (no endpoint), catches any exception, shows "Saved" snackbar | ✅ Never crashes |
| **Add Form** | `ShowMessageBox` → "Form linked!" snackbar | ✅ No API call |
| **Remove Form** | Confirmation dialog → removes from local `_forms` list → snackbar | ✅ UI-only, no API |
| **Add Question** | `ShowMessageBox` → "Got it" | ✅ No API call |

Per task brief, these stubs are acceptable for demo context. Documented in build report.

### IDisposable
No polling timers. No IDisposable needed. ✅

### Razor Directive Conflicts
- Loop variables are `sec` and `grp` (not `section`/`group` which would conflict with Razor directives). ✅
- No `@section` keyword conflicts. ✅

### Notes
- `_notFound` flow: `Task.Delay(1500)` then `NavigateTo("/question-sets")`. No cancellation token — if user navigates away during 1.5s window, `NavigateTo` fires on a potentially-disposed component. In Blazor Server `NavigationManager.NavigateTo` is safe after dispose; on WASM it's a no-op. **Low risk, not blocking.**

---

## Feature 3: Upload Flow Polish — FormLibrary.razor

### Timer Leak Check
- `StartStatusPolling()`: `_pollTimer?.Dispose()` before creating new timer. ✅
- Polling callback: self-stops when no pending items (`_pollTimer?.Dispose(); _pollTimer = null`). ✅
- `ClearDoneItems()`: disposes timer if queue emptied. ✅
- `Dispose()`: `_pollTimer?.Dispose()`. ✅

No timer leaks. ✅

### Empty Panel Behavior
`@if (_uploadQueue.Count > 0)` guards the entire upload queue panel. Panel does not appear when zero files are uploading. ✅

### Form Link
`<MudLink Href="@($"/forms/{item.FormId}")">` — correct path, matches `@page "/forms/{Id:int}"` in FormDetail. ✅

### Notes
- `DisplayStatus` correctly maps `Draft/Reviewed/Approved → "Completed"` for queue display. ✅
- `IsTerminal` check in `ClearDoneItems()` is consistent with `DisplayStatus`. ✅
- Link only renders when `item.FormId > 0 && item.FormName != null` — safe from NullRef. ✅

---

## Acceptance Criteria Verification

| Criterion | Status |
|---|---|
| PDF viewer loads via `/api/forms/{id}/pdf` | ✅ Endpoint exists |
| Save (`PUT /api/forms/{id}/fields`) succeeds or snackbars | ✅ Endpoint exists + graceful catch |
| Approve (`POST /api/forms/{id}/approve`) succeeds or snackbars | ✅ Endpoint exists + graceful catch |
| IDisposable preserved in FormDetail | ✅ |
| Processing/Error/404 states intact | ✅ |
| QS Editor route correct | ✅ |
| GET /api/question-sets/{id} shape matches | ✅ |
| Stub actions don't crash | ✅ All show snackbar |
| FormLibrary: no timer leaks | ✅ |
| Upload panel hidden when queue empty | ✅ |
| Form link to `/forms/{id}` correct | ✅ |

---

## Issues Found

### Critical: 0
### Important: 0

### Nitpicks: 2 (non-blocking, fix post-demo)

**N1: URL prefix inconsistency** (`QuestionSetEdit.razor`)  
`/api/question-sets/...` uses a leading slash; all other pages use `api/...` without it. Functionally identical at root BaseAddress but should be normalized post-demo.

**N2: SaveHeader always shows "Saved"** (`QuestionSetEdit.razor:SaveHeader`)  
Catch block shows `Snackbar.Add("Saved", Severity.Success)` even on network failure. Intentional demo stub per build report. Fine for demo, replace with real error handling when PUT endpoint is implemented.

---

## Positive Observations

- `DisplayStatus` computed property on `UploadQueueItem` is a clean abstraction — keeps the status mapping logic in one place.
- `BuildSectionGroups()` correctly sorts "General" to the bottom with the `\uffff` sentinel — nice touch.
- FormDetail's `_filteredEditModels` as a computed property (not a field) means it's always fresh without needing explicit refresh calls.
- Error handling is consistent across all three components — every async action wraps in try/catch with a snackbar.
- The IDisposable fix from last cycle is solid and carried through correctly into the new polling paths.

---

_Review complete. Clean code, no crashes, no leaks. Ship it._
