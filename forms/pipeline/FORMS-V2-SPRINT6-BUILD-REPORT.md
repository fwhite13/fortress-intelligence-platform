# FORMS v2 Sprint 6 — Build Report

**Branch:** `main`  
**Commit:** `1f4ae83`  
**Status:** ✅ Build succeeded — 0 errors  
**Warnings:** 122 (all pre-existing — MUD analyzer + nullable context warnings, none introduced by Sprint 6)

---

## Checklist Audit

| # | Item | Status | Notes |
|---|------|--------|-------|
| 1 | Home page navigation | ✅ Fixed | Cross-Reference and Generate JSON cards were linking to `/question-sets` — corrected to `/projects` per spec |
| 2 | NavMenu — Projects link | ✅ Already present | Home, Projects, Form Library, Question Sets, JSON Generator, Data Dictionary all in nav |
| 3 | Empty state — Projects.razor | ✅ Already exists | Dashed-paper empty state with Create Project button was already implemented in S1/S2 |
| 3 | Empty state — ProjectQuestionSet.razor | ✅ Implemented | Added `MudAlert` info message with Cross-Reference link when selected section has no fields |
| 4 | Loading feedback — ProjectDetail.razor | ✅ Already wired | `_loading` flag with `MudProgressLinear Indeterminate` was already implemented |
| 5 | Error boundaries — ProjectCrossReference.razor | ✅ Already done | No-approved-docs shows `MudAlert Severity.Warning` with link; error result shows `MudAlert Severity.Error` |
| 6 | Survey Preview project name | ✅ Already done | `_projectName` loaded in `OnInitializedAsync`, displayed in header as "Survey Preview — {name}" |
| 7 | Completion status chips — Projects.razor | ✅ Implemented | New `ProjectStatus` record + dictionary; 3 EF subqueries (approved docs, field codes, approved QS); "Progress" column with Docs ✓, Cross-Referenced ✓, QS Approved ✓ chips |
| 8 | Navigation audit | ✅ Fixed + verified | Full flow confirmed intact; fixed Home card nav issues (items 1); all back buttons and breadcrumbs present |

---

## Files Modified

| File | Change |
|------|--------|
| `FortressFormTools.Web/Components/Pages/Home.razor` | Fixed Cross-Reference and Generate JSON card `Href` from `/question-sets` to `/projects` |
| `FortressFormTools.Web/Components/Pages/ProjectQuestionSet.razor` | Added empty state `MudAlert` in right panel when a section is selected but has no fields |
| `FortressFormTools.Web/Components/Pages/Projects.razor` | Added `ProjectStatus` record, `_projectStatuses` dictionary, 3 EF progress queries in `LoadProjects()`, and "Progress" table column with workflow status chips |

---

## Navigation Flow Verification

1. **Home → Projects** ✅ "Upload & Extract" card links to `/projects`
2. **Home → Projects** ✅ "Cross-Reference" card now links to `/projects` (was `/question-sets`)
3. **Home → Projects** ✅ "Generate JSON" card now links to `/projects` (was `/question-sets`)
4. **Projects → Project Detail** ✅ Project name link + View icon button navigate to `/projects/{id}`
5. **Project Detail → Approve doc → Run Cross-Reference** ✅ "Run Cross-Reference →" button appears when approved docs exist
6. **Cross-Reference → Question Set** ✅ "View Question Set →" button appears after successful analysis
7. **Question Set → Survey Preview** ✅ "Generate Survey →" button (enabled only after QS Approved)
8. **Survey Preview ← Back** ✅ "← Back to Question Set" link
9. **Question Set ← Back** ✅ "← Back to Project" link
10. **Project Detail ← Back** ✅ `MudIconButton ArrowBack` to `/projects`

---

## Technical Notes

- Status chips use `IDbContextFactory<AppDbContext>` with subqueries via `GroupBy`/`Select` — no navigation property joins in `Where()` as required
- `FormLibrary.ProjectId` is nullable (`int?`) so queries use `f.ProjectId ?? 0` and `f.ProjectId == p.Id` pattern  
- `QuestionSet.ProjectId` is nullable; queries use `.Value` unwrap after null check
- Pre-existing warnings (MUD0001/MUD0002 analyzer, CS8669 nullable context) are from prior sprints and unchanged

---

## Build Output

```
Build succeeded.
122 Warning(s)
0 Error(s)
```

---

*Sprint 6 complete. FORMS v2 is fully functional.*
