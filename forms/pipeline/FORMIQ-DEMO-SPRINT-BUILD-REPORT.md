# FormIQ Demo Sprint — Build Report

**Date:** 2026-02-27

## Feature 1: Side-by-Side Review (/forms/{id})
**Status:** ✅
**PDF viewer:** Working — `<object>` embed pointing to existing `GET /api/forms/{id}/pdf` endpoint with fallback download link. Shows placeholder when status is Queued/Processing/Error.
**Fields editable:** Yes — full edit model with Label, Field Type (11 options), Section, Required, Notes (local-only). Two-way binding via EditModel class.
**API endpoint added:** Already existed — `GET /api/forms/{id}/pdf` was in FormsController.cs (line 229)
**Layout:** 55/45 split using `review-layout` CSS class. Left: PDF viewer. Right: scrollable fields panel with search + section filter + MudExpansionPanels.
**Actions:** Save Changes → `PUT /api/forms/{id}/fields` (sets status to Reviewed). Approve Form → `POST /api/forms/{id}/approve`.
**Preserved:** Skeleton loading, 404 handling, Queued/Processing/Error status alerts, polling timer, IDisposable.

## Feature 2: Question Set Editor (/question-sets/{id}/edit)
**Status:** ✅
**Sections:** Header (editable name/description/status), Source Forms (MudTable with Add/Remove stubs), Questions (grouped by section in MudExpansionPanels with ☰ drag handles), Preview (mockup of first 5 questions + Generate Full Preview link)
**Demo stubs:**
- Save Header → attempts `PUT /api/question-sets/{id}` with graceful error handling (shows success snackbar for demo)
- Add Form → opens MudDialog with placeholder form list, shows "Form linked!" snackbar
- Remove Form → confirmation dialog then snackbar
- Add Question → MudDialog with search placeholder, shows success snackbar
- Drag handles → visual only (☰ icon, no actual drag-drop)
**Additional changes:**
- QuestionSetDetail.razor: Added "Edit Question Set" button in the action row
- Route: `/question-sets/{Id:int}/edit`

## Feature 3: Upload Flow Polish
**Status:** ✅
**Auto-polling:** Yes — 3-second intervals via Timer, polls `GET /api/forms/{id}` per queue item
**Queue panel:** Uses `upload-queue-panel` CSS class (gold left border). Each item shows: filename (clickable MudLink to `/forms/{id}` when FormId available), status chip (Queued/Processing/Completed/Error), field count (when complete), elapsed time since upload start.
**Improvements:**
- Summary progress bar with "X of Y completed · N error(s)" caption
- `DisplayStatus` maps Draft/Reviewed/Approved → "Completed" for queue display
- `ClearDoneItems` only removes terminal items (preserves in-progress)
- UploadQueueItem now tracks: FormName, FieldCount, StartTime, computed ElapsedDisplay

## Build Result
- `dotnet build`: ✅ 0 errors (warnings only — pre-existing NU1603, CS8669, MUD0001/MUD0002)

## CC Usage
1. **Feature 1 (FormDetail.razor):** `claude --model sonnet -p --allowedTools 'Write'` — wrote complete side-by-side review UI
2. **Feature 2 (QuestionSetEdit.razor):** `claude --model sonnet -p --allowedTools 'Read,Write'` — created new editor page + attempted QuestionSetDetail update (manual fix applied for Edit button)
3. **Feature 3 (FormLibrary.razor):** `claude --model sonnet -p --allowedTools 'Write'` — polished upload queue panel
4. **Manual fixes:** Renamed `@section` loop variables to avoid Razor directive conflicts (`sec`, `grp`), added missing `T="string"` to MudTextField in preview section

## Files Modified
- `FortressFormTools.Web/Components/Pages/FormDetail.razor` — upgraded from basic table to side-by-side review (436 → 436 lines)
- `FortressFormTools.Web/Components/Pages/QuestionSetEdit.razor` — **NEW** (431 lines)
- `FortressFormTools.Web/Components/Pages/QuestionSetDetail.razor` — added Edit button
- `FortressFormTools.Web/Components/Pages/FormLibrary.razor` — polished upload queue (453 lines)

## Git
- Commit: `feat: demo sprint - side-by-side review, question set editor, upload flow polish`
- 5 files changed, 925 insertions, 106 deletions

## Notes for Review
- No backend PUT endpoint exists for `/api/question-sets/{id}` — Save Header is stubbed with graceful error handling
- No backend endpoints for linking/unlinking forms to question sets or adding questions — all stubbed with snackbars
- MudBlazor analyzer warnings (MUD0001/MUD0002) are pre-existing from other pages — not introduced by this sprint
- **DO NOT deploy — Clint reviews, then Rhodey deploys.**
