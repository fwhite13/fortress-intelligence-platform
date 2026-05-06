# Review Report — ADO#2819

**Verdict: NEEDS-CHANGES**

**Commit:** `4199b57`
**File:** `src/FortressNexus.Web/Components/Pages/NexusReview.razor`
**Reviewer:** Hawkeye (Clint Barton) — Cycle 1
**Date:** 2026-05-06

---

## CC Review Summary

CC reviewed `NexusReview.razor` against the ADO#2819 acceptance criteria, `UserContextService.cs`, `NexusRoles.cs`, and `SubmissionService.cs`. 

CC found **1 Important issue** and **7 nitpicks**. I confirmed the Important issue against the actual file — it's real.

---

## Spec Compliance Check

**§ Codebase Map:**
- `src/FortressNexus.Web/Components/Pages/NexusReview.razor` — ✅ modified as specified (sole changed file)

**§ Out of Scope:**
- ✅ No out-of-scope changes detected

**§ Acceptance Criteria:**

- [x] AC1: `/nexus/{id}/review` route renders the generated spec — ✅ route unchanged, renders correctly
- [ ] AC2: Sections are editable inline by **NexusAdmin/NexusReviewer** — ❌ **NOT MET** — edit icons visible and functional for submitters too (see Critical #1 below)
- [x] AC3: Approve button advances state to `Approved` — ✅ `HandleApprove` calls `SpecService.ApproveAsync` with `ClaimsPrincipal`
- [x] AC4: NexusUser cannot see the Approve button — ✅ `<AuthorizeView Roles="@NexusRoles.Admin">` gates the button; submitters who are not admin do not see it
- [x] AC5: NexusAdmin can access any submission's review page — ✅ `GetByIdAsync` has no ownership filter; role guard accepts admin/editor/submitter
- [x] AC6: Approved state reflected — ✅ status chip + approved chip in header

**Spec compliance verdict:** ❌ NON-COMPLIANT — AC2 not met

---

## Consistency Audit

**Files Cross-Referenced:**
- `UserContextService.cs` ↔ `NexusReview.razor` — `IsNexusEditorAsync()` = `IsInRole(Admin) || IsInRole(Reviewer)` ✅
- `NexusRoles.cs` ↔ `AuthorizeView Roles="@NexusRoles.Admin"` — `"NexusAdmin"` matches Entra role name ✅
- `SubmissionService.GetByIdAsync` — no ownership filter, page-level guard pattern matches ADO#2811 precedent ✅

**`_isAdmin` — stored but not used in template — dead state. Direct evidence that the role guard for the edit button was intended but never wired.**

---

## Critical Issues — 0

---

## Important Issues — 1

### I1: Section edit icons (and save path) accessible to submitters

- **File:** `NexusReview.razor` lines 90–97 (edit icon) and lines 100–110 (MudTextField editor)
- **Category:** Spec non-compliance / access control
- **Issue:** The section edit icon button is guarded only by `!_specDoc.IsApproved`. There is no check for `_isAdmin` or `_canEdit`. A submitter who passes the `LoadAsync` role guard (because `isSubmitter = true`) sees the Edit pencil icon on every section and can click it, enter edit mode, modify the body text, blur to save — which calls `SaveSectionAsync` → `ReassembleSections()` → `SaveDraftAsync`. The submitter's edits persist to DB.

  `_isAdmin` is stored (line 190, set at line 237) but **never referenced in the template or any method** — dead state. The gate was intended but never wired.

  The fallback full textarea (line 124) is also only `Disabled="@_specDoc.IsApproved"`, so a submitter on non-sectioned content gets an enabled textarea.

- **Evidence:**
  ```razor
  @if (!_specDoc.IsApproved)   ← line 90: only approval check, no role check
  {
      <MudIconButton ... OnClick="@(() => _editingSections.Add(idx))" />
  }
  ```
  ```csharp
  _isAdmin = isAdmin;   // line 237: stored but never read in template
  ```

- **Impact:** AC#2 states "Sections are editable inline by NexusAdmin/NexusReviewer" — submitters should be read-only on the review page. Any submitter can alter the spec during the review cycle, undermining the reviewer's authority.

- **Fix:** Add a `_canEdit` field, set it in `LoadAsync` from `isEditor`, gate the edit icon and textarea on it:

  ```csharp
  // In LoadAsync, after role check:
  _isAdmin = isAdmin;
  _canEdit = isEditor;   // isEditor = IsNexusEditorAsync() = Admin || Reviewer
  ```

  ```razor
  @* Section edit icon — add _canEdit guard *@
  @if (!_specDoc.IsApproved && _canEdit)
  {
      <MudIconButton ... />
  }
  
  @* Fallback textarea — add _canEdit to Disabled *@
  <MudTextField ... Disabled="@(_specDoc.IsApproved || !_canEdit)" />
  ```

  The `Save Draft` button in the actions area is also unguarded — submitters can click it. Same `_canEdit` check should wrap the Save Draft button (or at least disable it when `!_canEdit`).

---

## Nitpicks — 6

- **N1:** `_isAdmin` is dead state (`_isAdmin` stored, never used) — remove or replace with `_canEdit`. (`NexusReview.razor:190, 237`)

- **N2:** `_showEdit` toggle button is orphaned — it changes the label text ("Editing"/"AI Original") but gates nothing in the template. The section editor visibility is controlled by `_editingSections`, not `_showEdit`. Clicking the toggle does nothing functional. (`NexusReview.razor:42-49`)

- **N3:** `isAdmin` redundancy in role guard — `IsNexusEditorAsync()` already covers admin (`IsInRole(Admin) || IsInRole(Reviewer)`), so `!isAdmin && !isEditor` simplifies to `!isEditor`. Results in one extra `GetAuthenticationStateAsync` call. Not a bug. (`NexusReview.razor:224-230`)

- **N4:** `ReassembleSections()` is not lossless on the first save — if original content has no blank line between heading and body, one is inserted. Subsequent saves are idempotent. Acceptable for markdown. (`NexusReview.razor:292-308`)

- **N5:** `ParseSections` regex `(?=^## |\A)` — when content starts with `##` at position 0, both `\A` and `^##` match, producing an empty first split element. Handled correctly by the `IsNullOrEmpty(trimmed)` guard. Safe but non-obvious. (`NexusReview.razor:258-260`)

- **N6:** `SpecSection.Index` and list position are assumed identical (sequentially assigned). No reorder/delete exists in this WI so it's safe. If a future WI adds section reorder/delete, `_sections[index]` in `UpdateSectionBody` will diverge. Flag for future work. (`NexusReview.razor:311-315`)

---

## Positive Observations

- `_upn` caching is correctly done — resolved once in `LoadAsync`, used in all three save paths, removing the per-handler `GetUpnAsync()` calls. Clean.
- The `SaveSectionAsync` call chain is correct: `_editingsections.Remove` → `_isSaving = true` → `ReassembleSections()` → `SaveDraftAsync` → `_lastSavedAt`. `StateHasChanged()` in both try and finally. Solid.
- Foreach closure variable capture is correct — `var idx = sec.Index` inside the loop body captures correctly per-iteration.
- `GetByIdAsync` page-level admin pattern is consistent with the ADO#2811 precedent, and `[@attribute [Authorize]]` is present to prevent unauthenticated access.
- Status chip added cleanly to header.

---

## What Tony Needs to Fix

**One change, three lines:**

1. Add `private bool _canEdit;` to fields
2. In `LoadAsync`, after setting `_isAdmin = isAdmin;` → add `_canEdit = isEditor;`
3. Gate the section edit icon: change `@if (!_specDoc.IsApproved)` → `@if (!_specDoc.IsApproved && _canEdit)`
4. Gate the fallback textarea: change `Disabled="@_specDoc.IsApproved"` → `Disabled="@(_specDoc.IsApproved || !_canEdit)"`
5. Optionally: Wrap `Save Draft` button in `@if (_canEdit)` or `Disabled="@(!_canEdit)"` — submitters shouldn't be able to save either

This is a targeted fix. No architectural changes needed.
