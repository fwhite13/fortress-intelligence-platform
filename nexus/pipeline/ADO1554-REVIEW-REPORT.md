# Review Report — ADO#1554 (NexusDashboard.razor, commit 246dd0d)

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC ran a full 20-item checklist plus 5 adversarial checks against both `Dashboard.razor` and `SubmissionDetail.razor`. 17/20 checklist items passed cleanly. Two warnings confirmed as real issues (items 17 and 20). One adversarial warning (B) is a lower-priority defensive concern.

No false positives dismissed — all three flagged items are real.

---

### Spec Fidelity

All structural requirements are met: route, auth, render mode, page title, table columns, MudLink on title, null Feature Area, date format, View button, loading state, empty state, admin guard, and separate admin table. The layout and wiring are correct.

**Spec compliance verdict:** ✅ COMPLIANT on structure — blocked only by correctness/maintenance issues below.

---

### Consistency Audit

**Files cross-referenced:** `Dashboard.razor` ↔ `SubmissionDetail.razor` — `GetStatusColor` switch expressions are byte-for-byte identical across all 6 named cases plus `_ => Color.Default`.

**ArtifactsCreated:** Not explicitly mapped in either file. Falls through to `Color.Default` in both. Tony's note is confirmed — this is the intentional fallback, not a gap. ✅ Consistent.

---

### Issues Found

| # | Severity | File | Location | Issue | Fix |
|---|----------|------|----------|-------|-----|
| 1 | **Important** | `Dashboard.razor` | `OnInitializedAsync` — `authState.User.Identity?.Name` | `Identity.Name` in ASP.NET Core with Azure AD maps to the `name` claim (display name), **not** UPN. Silently queries by display name; user sees their own submissions only if their display name happens to match what `GetByUserAsync` expects. No error thrown. | Use `authState.User.FindFirst("preferred_username")?.Value ?? ""` (or the `upn` claim, depending on token configuration). Verify with the team what claim `GetByUserAsync` was designed to receive. |
| 2 | **Nitpick** | `Dashboard.razor` | `OnInitializedAsync` — `authState.User.IsInRole("NexusAdmin")` | `"NexusAdmin"` is a bare magic string. If the role name changes, this breaks silently at runtime. | Replace with `NexusRoles.Admin` constant (or equivalent shared constant). If no constant exists yet, create one. |
| 3 | **Nitpick** | `Dashboard.razor` | `_submissions = await SubmissionService.GetByUserAsync(userUpn)` | No null guard on return value. If the service returns null, `.Any()` in the template throws NullReferenceException. | Change to `_submissions = await SubmissionService.GetByUserAsync(userUpn) ?? new();` Same for `_pendingReview`. |

---

### Positive Observations

- Clean DI usage throughout — no `new`-ing services anywhere in `@code`.
- `_loading = true` / `finally { _loading = false; }` pattern is correct and robust.
- Admin section is properly separated (own table, own header, own empty state) — not mixed with user submissions.
- Status color mapping is perfectly consistent with `SubmissionDetail.razor`.
- Feature Area null handling is correct (`?? "—"`).
- Date formatting matches spec (`"MMM d, yyyy"`).

---

### What to Fix (Tony)

**Required before PASS:**

**Issue 1 — UPN claim (Important):**
In `OnInitializedAsync`, change:
```csharp
var userUpn = authState.User.Identity?.Name ?? "";
```
to:
```csharp
var userUpn = authState.User.FindFirst("preferred_username")?.Value
              ?? authState.User.FindFirst("upn")?.Value
              ?? "";
```
Confirm with the team which claim `GetByUserAsync` was built to receive — if the service was designed for `Identity.Name` (i.e. it queries by display name), fix the service contract instead. One of these two sides needs to be correct.

**Optional (do it anyway — takes 2 minutes):**

**Issue 2 — Magic string:**
Replace `"NexusAdmin"` with `NexusRoles.Admin` (create the constant if it doesn't exist):
```csharp
_isAdmin = authState.User.IsInRole(NexusRoles.Admin);
```

**Issue 3 — Null guard:**
```csharp
_submissions = await SubmissionService.GetByUserAsync(userUpn) ?? new();
// and if needed:
_pendingReview = await SubmissionService.GetAllPendingReviewAsync() ?? new();
```

---

### Checklist Results

| # | Item | Result |
|---|------|--------|
| 1 | `@page "/"` + `@attribute [Authorize]` | ✅ |
| 2 | `@rendermode InteractiveServer` | ✅ |
| 3 | Page title "NEXUS Dashboard" | ✅ |
| 4 | New Submission → `/nexus/new` | ✅ |
| 5 | Table columns: #, Title, Feature Area, Status, Submitted, Action | ✅ |
| 6 | Title → MudLink → `/nexus/{s.Id}` | ✅ |
| 7 | Feature Area null → `"—"` | ✅ |
| 8 | Status color mapping matches SubmissionDetail.razor | ✅ |
| 9 | Date format `MMM d, yyyy` | ✅ |
| 10 | Action "View" → `/nexus/{id}` | ✅ |
| 11 | Loading: MudProgressLinear while `_loading` | ✅ |
| 12 | Empty state: MudAlert when no submissions | ✅ |
| 13 | Admin guard on `GetAllPendingReviewAsync()` | ✅ |
| 14 | Separate admin table | ✅ |
| 15 | `ISubmissionService` via DI | ✅ |
| 16 | `AuthenticationStateProvider` via DI | ✅ |
| 17 | UPN claim — `Identity.Name` ≠ UPN in Azure AD | ⚠️ NEEDS-CHANGES |
| 18 | `_loading = true` before / `false` in `finally` | ✅ |
| 19 | No inline `new Service()` in `@code` | ✅ |
| 20 | Role string — `"NexusAdmin"` magic string | ⚠️ Nitpick |

---

*Reviewed by Hawkeye (Clint Barton) — cycle 1 — 2026-04-02*

---

## REVIEW cycle 2 — 2026-04-02

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** 7656ffd
**Scope:** Targeted re-review — 3 previously flagged items only

### Verdict: ✅ PASS

All 3 cycle 1 issues confirmed fixed in `src/FortressNexus.Web/Components/Pages/Dashboard.razor`.

### Item-by-Item Results

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | UPN claim (`preferred_username` first, `Identity.Name` fallback) | ✅ PASS | `var userUpn = authState.User.FindFirst("preferred_username")?.Value ?? authState.User.Identity?.Name ?? "";` (lines 122–124) |
| 2 | `NexusRoles.Admin` — no magic string | ✅ PASS | `_isAdmin = authState.User.IsInRole(NexusRoles.Admin);` (line 125) |
| 3 | Null guard on `GetByUserAsync` | ✅ PASS | `_submissions = await SubmissionService.GetByUserAsync(userUpn) ?? new();` (line 127) |

### Summary
All 3 cycle 1 NEEDS-CHANGES items have been correctly addressed. No new issues found in reviewed scope. Cycle 2 closes clean.

