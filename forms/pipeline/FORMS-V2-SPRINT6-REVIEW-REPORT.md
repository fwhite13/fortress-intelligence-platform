# Review Report: FORMS-V2-SPRINT6

**Commit:** `1f4ae83` (Sprint 6 code) / `ca5a0f3` (build report)
**Reviewed by:** Hawkeye
**Date:** 2026-03-03
**Risk Level:** Low — UI polish, navigation fixes, status chips

---

### Verdict: ✅ PASS

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Result |
|-------|--------|
| `Home.razor` Cross-Reference card `Href` → `/projects` | ✅ |
| `Home.razor` Generate JSON card `Href` → `/projects` | ✅ |
| `Home.razor` Upload & Extract card `Href` → `/projects` | ✅ (was already correct) |
| `ProjectQuestionSet.razor` empty state MudAlert present | ✅ |
| `ProjectQuestionSet.razor` Cross-Reference link in empty state | ✅ |
| `Projects.razor` `ProjectStatus` record type defined | ✅ |
| `Projects.razor` `IDbContextFactory` used for status queries | ✅ |
| `Projects.razor` EF subqueries — no nav properties in `Where()` | ✅ |
| `Projects.razor` Three chips shown conditionally | ✅ |
| `Projects.razor` `StateHasChanged()` after async status load | ✅ (not needed — see notes) |

**Undocumented Dependencies Checked:**
- `Projects.razor` uses `context.Documents.Count` — this is safe; `Documents` is already eagerly loaded via `Include(p => p.Documents)` before the status subqueries run.
- No cross-file consistency issues found.

---

## Critical Issues: 0

None.

---

## Important Issues: 0

None.

---

## Nitpicks: 1

**N1: `StateHasChanged()` not explicitly called after `_projectStatuses` is populated** (`Projects.razor`)

Not a bug. `LoadProjects()` is only ever called from:
1. `OnInitializedAsync` — Blazor re-renders automatically after lifecycle methods
2. `CreateProject` / `DeleteProject` — both are `OnClick` EventCallback handlers, and Blazor's `EventCallback` mechanism automatically calls `StateHasChanged()` after the async handler chain completes

No explicit `StateHasChanged()` is required here and its absence is correct. Flagging for transparency only — this is fine as-is.

---

## Acceptance Criteria Verification

### Home.razor

- [x] **"Cross-Reference" card links to `/projects`** — Verified: `Href="/projects"` ✅
- [x] **"Generate JSON" card links to `/projects`** — Verified: `Href="/projects"` ✅
- [x] All three cards consistently link to `/projects` ✅

### ProjectQuestionSet.razor

- [x] **Empty state `MudAlert` shown when section selected but has no fields** — Verified:
  ```razor
  @if (_sectionFields.Count == 0 && !_addingField)
  {
      <MudAlert Severity="Severity.Info" Variant="Variant.Text" Class="ma-2">
          No fields in this section. Add fields using the "Add Field" button above, or run
          <MudLink Href="@($"/projects/{ProjectId}/cross-reference")">Cross-Reference</MudLink>
          to auto-populate from your approved documents.
      </MudAlert>
  }
  ```
  Guard condition `_sectionFields.Count == 0 && !_addingField` is correct — alert is hidden while the add-field form is open (good UX). ✅

- [x] **Includes link/reference to Cross-Reference page** — `MudLink Href="@($"/projects/{ProjectId}/cross-reference")"` is present with correct project-scoped path ✅

### Projects.razor

- [x] **`ProjectStatus` record defined with approved docs, field codes, approved QS booleans**:
  ```csharp
  private record ProjectStatus(bool HasApprovedDocs, bool HasFieldCodes, bool HasApprovedQS);
  ```
  All three booleans present. ✅

- [x] **EF subqueries use no navigation properties in `Where()` before `Include()`** — Verified: the three status subqueries (`approvedDocCounts`, `fieldCodeCounts`, `approvedQsCounts`) each operate directly on their own `DbSet` (`FormLibraries`, `FormFieldCodes`, `QuestionSets`) using scalar FK columns only. The `Include(p => p.Documents)` on the main `FormProjects` query is separate and correct. ✅

- [x] **Three chips "Docs ✓", "Cross-Referenced ✓", "QS Approved ✓" shown conditionally based on status**:
  ```razor
  @if (ps.HasApprovedDocs)  { <MudChip ...>Docs ✓</MudChip> }
  @if (ps.HasFieldCodes)    { <MudChip ...>Cross-Referenced ✓</MudChip> }
  @if (ps.HasApprovedQS)    { <MudChip ...>QS Approved ✓</MudChip> }
  ```
  All three chips conditionally gated. ✅

- [x] **`IDbContextFactory` used for status queries** — `@inject IDbContextFactory<AppDbContext> DbFactory` at top; all database work in `LoadProjects()` uses `await using var db = await DbFactory.CreateDbContextAsync()`. ✅

- [x] **`StateHasChanged()` after status dictionary populated (if async)** — See Nitpick N1. Not required here because all callers are lifecycle methods or EventCallback handlers. Blazor auto-renders in both cases. ✅

---

## Positive Observations

- **Nullable FK handling is careful.** `FormLibrary.ProjectId` is `int?`, and the query correctly uses `f.ProjectId ?? 0` to avoid nulls in `Contains()`. Similarly, `QuestionSet.ProjectId` nullable is handled with `.Value` after a null guard.

- **Chip guard condition in `ProjectQuestionSet` is UX-correct.** Hiding the empty-state alert while `_addingField` is true (`!_addingField`) prevents a confusing flash of "no fields" while the user is mid-way through adding one.

- **Cross-Reference link in empty state is project-scoped.** `Href="@($"/projects/{ProjectId}/cross-reference")"` correctly threads the `ProjectId` parameter so the user lands on the right cross-reference run, not a generic page.

- **All three status subqueries are independent parallel-friendly fetches.** Each hits one table, uses a scalar `Contains()` filter, and groups client-side — clean and fast. No N+1 problem.

- **`StateHasChanged()` discipline is consistent** with existing patterns in this codebase (see Sprint 5 `SelectSection` and `ToggleFieldExpand` for explicit calls only where lifecycle doesn't cover re-render).

---

## Summary

Sprint 6 is a clean polish pass. Three targeted changes:

1. **Home.razor** — Two nav links corrected from `/question-sets` → `/projects`. Straightforward fix, verified correct.
2. **ProjectQuestionSet.razor** — Empty state `MudAlert` added with correct conditional guard and a properly-scoped Cross-Reference link.
3. **Projects.razor** — `ProjectStatus` record and three EF subqueries added correctly. No navigation property abuse, correct use of `IDbContextFactory`, chips display conditionally and accurately represent workflow state.

No bugs found. No regressions. No consistency mismatches across files.

**Pipeline continues to Stage 4 (Security).**

---

*Review duration: ~15 minutes*
*Hawkeye — 2026-03-03*
