# Review Report — WIs #1650, #1651, #1652, #1658

**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `936f3b3`
**Cycle:** 1 of 2
**Date:** 2026-04-08

---

## Verdict: NEEDS-CHANGES

Two issues block PASS: one Critical (security — no server-side ownership check in `DeleteSubmissionAsync`), one Important (fragile `_historicalSpecs` filter using positional Skip instead of ID-based exclusion).

---

## Spec Compliance Check

All five code files in scope were modified as expected. No out-of-scope changes detected in code files. Pipeline docs committed alongside are expected/acceptable.

**§ Scope:**
- `Components/Pages/SubmissionDetail.razor` — ✅ modified (WIs #1650, #1651, #1652, #1658)
- `Services/SubmissionService.cs` — ✅ modified (WI #1651 `DeleteSubmissionAsync`)
- `Services/ISubmissionService.cs` — ✅ modified (`Task DeleteSubmissionAsync(int id)` added)
- `Services/Discovery/DiscoveryService.cs` — ✅ modified (WI #1658 `GetAllSessionsAsync`)
- `Services/Discovery/IDiscoveryService.cs` — ✅ modified (signature added)

**§ Acceptance Criteria:**
- [x] WI #1650 Continue button → `/nexus/{Id}/resume` ✅
- [x] WI #1650 Narrative preview (Draft + non-empty) ✅
- [x] WI #1650 Previous Version panel during Generating with >1 SpecDocs ✅
- [x] WI #1651 Delete button gated to Draft + owner||admin ✅ (UI gate only — see C1)
- [x] WI #1651 MudMessageBox confirm ✅
- [x] WI #1651 `DeleteSubmissionAsync` 3-phase delete order ✅
- [x] WI #1652 `_historicalSpecs` accordion, latest excluded ⚠️ (fragile — see I1)
- [x] WI #1652 `"v{N} — yyyy-MM-dd"` labels + 200-char preview ✅
- [x] WI #1658 `GetAllSessionsAsync` returns ALL sessions including Superseded ✅
- [x] WI #1658 `MudSwitch` defaults off ✅
- [x] WI #1658 Superseded sessions labeled `"Superseded — yyyy-MM-dd"` ✅

---

## CC Review Summary

Claude Code performed a full adversarial review of all five files plus `UserContextService.cs`, `NexusDbContext.cs`, and `NewSpecWizard.razor`. Findings were verified against the actual diff and surrounding codebase context.

CC identified two real issues (confirmed below) and several lower-severity observations. Three CC findings were dismissed as low/theoretical risk or expected patterns.

---

## Consistency Audit

| Check | Result |
|-------|--------|
| Route `/nexus/{Id}/resume` ↔ `NewSpecWizard.razor @page "/nexus/{ResumeSubmissionId:int}/resume"` | ✅ Match |
| `NexusRoles.Admin` usage in `IsAdminAsync` ↔ `Dashboard.razor`, `SpecService.cs` | ✅ Consistent |
| `GetSessionAsync` filter (`Status != Superseded`) ↔ `GetAllSessionsAsync` (no filter) | ✅ Correct divergence |
| `SubmittedBy` comparison in UI gate uses same `GetUpnAsync()` claim chain used at creation | ✅ Consistent |
| `active_spec_document_id` — no DB-level FK constraint confirmed in migrations | ✅ Null-before-delete is safe |

---

## Critical Issues — 1

### C1: No Server-Side Ownership Check in `DeleteSubmissionAsync`

| | |
|-|-|
| **File** | `Services/SubmissionService.cs` |
| **Line** | 195 (method signature) |
| **Category** | Security — broken access control |
| **Severity** | Critical |

**Issue:** `DeleteSubmissionAsync(int id)` accepts only a submission ID with no caller identity. It performs zero ownership or role validation — any code path that can invoke this method with a known ID will delete the submission unconditionally.

The only gate is the Blazor render-time check in `SubmissionDetail.razor:228`. This is a UI gate, not an authorization control. While Blazor Server's circuit model makes cross-user exploitation via the same session impossible, this service method is part of `ISubmissionService` and is callable from any future API endpoint, background job, admin tool, or integration that gets injected with `ISubmissionService`. There is no defense-in-depth.

**Evidence:**
```csharp
// SubmissionService.cs:195
public async Task DeleteSubmissionAsync(int id)
{
    var submission = await _db.Submissions...FirstOrDefaultAsync(s => s.Id == id);
    if (submission is null) return;
    // ← no ownership check, no role check, proceeds unconditionally
```

Compare with `SpecService.cs:35` which enforces role in the service layer:
```csharp
if (!user.IsInRole(NexusRoles.Admin))
    throw new UnauthorizedAccessException("Only NexusAdmin users can approve spec documents.");
```

**Fix:**

Change the signature to accept caller identity:

```csharp
// ISubmissionService.cs
Task DeleteSubmissionAsync(int id, string callerUpn, bool callerIsAdmin);

// SubmissionService.cs
public async Task DeleteSubmissionAsync(int id, string callerUpn, bool callerIsAdmin)
{
    var submission = await _db.Submissions
        ...
        .FirstOrDefaultAsync(s => s.Id == id);

    if (submission is null) return;

    // Server-side ownership gate
    if (!callerIsAdmin && submission.SubmittedBy != callerUpn)
    {
        _logger.LogWarning("[SUBMISSION_DELETE] Unauthorized delete attempt: submissionId={Id} caller={Caller}", id, callerUpn);
        throw new UnauthorizedAccessException("You do not have permission to delete this submission.");
    }

    if (submission.Status != SubmissionStatus.Draft)
        throw new InvalidOperationException("Only Draft submissions can be deleted.");

    // ... rest of method unchanged
```

Update call site in `SubmissionDetail.razor`:
```csharp
// HandleDeleteSubmissionAsync — around line 471
await SubmissionService.DeleteSubmissionAsync(Id, _currentUserUpn!, _isAdmin);
```

---

## Important Issues — 2

### I1: `_historicalSpecs` Excludes by Version Rank, Not by `ActiveSpecDocumentId`

| | |
|-|-|
| **File** | `Components/Pages/SubmissionDetail.razor` |
| **Lines** | 332–335 (LoadSubmissionAsync) + 150 (Previous Version panel) |
| **Category** | Correctness — fragile assumption |
| **Severity** | Important |

**Issue:** `_historicalSpecs` is computed by ordering all SpecDocuments descending by version and skipping the first:

```csharp
_historicalSpecs = _submission.SpecDocuments
    .OrderByDescending(d => d.Version)
    .Skip(1)
    .ToList();
```

This assumes the active spec is always the highest-versioned document. If `ActiveSpecDocumentId` ever points to a non-highest-version document (which `SetActiveSpecDocumentAsync` in `ISubmissionService` would allow), the active spec appears in the Version History accordion and the actual highest-version doc is hidden from it.

No rollback UI exists today, but `SetActiveSpecDocumentAsync` is part of the service interface and is callable. This is fragile.

The same assumption exists in the Previous Version panel (Generating state, line 150) using `Skip(1).FirstOrDefault()`.

**Fix:**
```csharp
// Line 332 — exclude by ID, not position
_historicalSpecs = _submission.SpecDocuments
    .Where(d => !_submission.ActiveSpecDocumentId.HasValue 
                || d.Id != _submission.ActiveSpecDocumentId.Value)
    .OrderByDescending(d => d.Version)
    .ToList();
```

For the Previous Version panel inline expression (line 150), replace the `Skip(1)` with an ID-based filter as well.

### I2: No `try/catch` Around `DeleteSubmissionAsync` in Component Handler

| | |
|-|-|
| **File** | `Components/Pages/SubmissionDetail.razor` |
| **Line** | 461–474 (`HandleDeleteSubmissionAsync`) |
| **Category** | Error handling |
| **Severity** | Important |

**Issue:** `HandleDeleteSubmissionAsync` awaits `DeleteSubmissionAsync` with no exception handling. If a DB error occurs mid-delete (e.g., constraint violation during the SpecDocuments phase), the exception propagates unhandled. The Snackbar success message fires only if the await completes without throwing, but Blazor will surface an unhandled exception rather than showing a user-friendly error.

**Fix:**
```csharp
private async Task HandleDeleteSubmissionAsync()
{
    bool? confirmed = await DialogService.ShowMessageBox(...);
    if (confirmed != true) return;

    try
    {
        await SubmissionService.DeleteSubmissionAsync(Id, _currentUserUpn!, _isAdmin);
        Snackbar.Add("Submission deleted.", Severity.Success);
        Nav.NavigateTo("/nexus");
    }
    catch (UnauthorizedAccessException)
    {
        Snackbar.Add("You do not have permission to delete this submission.", Severity.Warning);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[SUBMISSION_DETAIL] Delete failed for submission {Id}", Id);
        Snackbar.Add("Delete failed. Please try again.", Severity.Error);
    }
}
```

(Note: I2 also pairs with C1 — once the ownership check throws `UnauthorizedAccessException`, the handler needs to catch it.)

---

## Nitpicks — 3

**N1: "Active session" label applies to Skipped/Failed sessions** (`SubmissionDetail.razor` — the history display)
The label logic `session.Status != Superseded ? "Active session" : "Superseded…"` would show "Active session" for a `Skipped` or `Failed` session. In practice all non-current sessions get Superseded status before a new one is created, so this won't manifest. Not blocking, but consider a status-based label switch for clarity.

**N2: Unnecessary eager-load in `DeleteSubmissionAsync`** (`SubmissionService.cs`)
The `.Include(s => s.DiscoverySessions).ThenInclude(ds => ds.Questions).ThenInclude(q => q.Answer)` is unnecessary — all three levels cascade-delete from the DB configuration. These entities are loaded but never explicitly removed. Not harmful, just wasteful overhead on submissions with many discovery questions.

**N3: Previous Version panel renders full spec without truncation** (`SubmissionDetail.razor:150`)
The Previous Version panel (Generating state) renders `prevSpec.EditedContent ?? prevSpec.Content` in a `MudText` without any length cap. Version History accordion truncates to 200 chars. Consider capping the Previous Version panel too, or using `RenderMarkdown()` for consistency with the main spec viewer.

---

## Positive Observations

- **Delete phase order is correct.** The null/batch/SaveChanges approach for `ActiveSpecDocumentId` is clean — EF batches the null assignment and all removes into one transaction. The S3 non-fatal pattern with per-file try/catch is exactly right.
- **`GetAllSessionsAsync` is clean.** No accidental Superseded filter. Single query, no N+1.
- **`_showDiscoveryHistory = false`** correctly initialized. Toggle defaults off.
- **Continue button route** matches `NewSpecWizard.razor`'s `@page` directive exactly.
- **Auth role consistency** — `NexusRoles.Admin` used uniformly across all auth checks.

---

## What to Fix (Tony)

1. **C1 (Critical, blocks ship):** Add `callerUpn` + `callerIsAdmin` params to `DeleteSubmissionAsync` in `ISubmissionService.cs` and `SubmissionService.cs`. Add ownership gate + Draft status guard in the method. Update call site in `HandleDeleteSubmissionAsync` to pass `_currentUserUpn!` and `_isAdmin`.

2. **I1 (Important):** Change `_historicalSpecs` filter from `.OrderByDescending().Skip(1)` to `.Where(d => d.Id != _submission.ActiveSpecDocumentId)`. Same fix for the Previous Version panel's inline expression.

3. **I2 (Important — pairs with C1 fix):** Wrap `HandleDeleteSubmissionAsync` await in try/catch with `UnauthorizedAccessException` and general `Exception` handlers.

---

_Hawkeye — Review cycle 1 of 2_

---

# Cycle 2 Review — WIs #1651, #1652

**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `01934922`
**Cycle:** 2
**Date:** 2026-04-08

---

## Verdict: NEEDS-CHANGES

C1 and I2 are correctly implemented. I1's `_historicalSpecs` filter is fixed. However two items remain: one new UX bug (success Snackbar never displays due to ordering) and one missed fix from I1 (Previous Version panel still uses positional `Skip(1)`).

---

## Cycle 2 Changes Reviewed

| File | Changed |
|------|---------|
| `Services/ISubmissionService.cs` | Signature updated: `DeleteSubmissionAsync(int id, string callerUpn, bool callerIsAdmin)` |
| `Services/SubmissionService.cs` | Pre-flight guard added: FindAsync → Draft check → ownership check → full Include load |
| `Components/Pages/SubmissionDetail.razor` | `@inject ILogger<SubmissionDetail> Logger`, `_historicalSpecs` filter updated, try/catch added to handler |

Build: ✅ 0 errors, 0 warnings.

---

## C1 — Server-Side Ownership Guard

**RESOLVED ✅**

All guard checks verified in order:
- `FindAsync(id)` first (lightweight, no Includes) → `?? throw new KeyNotFoundException` ✅
- `InvalidOperationException` on non-Draft status ✅
- `UnauthorizedAccessException` if `!callerIsAdmin && submittedBy != callerUpn` ✅
- Full Include graph loaded **after** all three guards pass (two-phase confirmed) ✅
- `submissionCheck` and `submission` variables are distinct, no mix-up ✅

Minor: a silent `return` at line ~219 guards a theoretical TOCTOU race after the two-phase load. Acceptable.

---

## I1 — `_historicalSpecs` Filter

**PARTIALLY RESOLVED ⚠️**

**Fixed:** `_historicalSpecs` now uses `.Where(d => d.Id != _submission.ActiveSpecDocumentId)` — ID-based, not positional. Null semantics correct: when `ActiveSpecDocumentId` is null, all docs pass the Where (C# lifted operator — `int != null` → `true`). Comment confirms intent. ✅

**Not Fixed:** Previous Version panel (line 151) still uses:
```razor
_submission.SpecDocuments.OrderByDescending(d => d.Version).Skip(1).FirstOrDefault()
```
The positional `Skip(1)` from original I1 was not addressed here. When the active spec is not the highest-versioned document, this panel shows the wrong "previous version." Consistent fix required.

---

## I2 — try/catch on HandleDeleteSubmissionAsync

**RESOLVED with new UX bug ⚠️**

`UnauthorizedAccessException` caught → Snackbar.Error ✅  
General `Exception` caught → Logger.LogError + generic Snackbar.Error ✅  
`ILogger<SubmissionDetail>` injected — no manual DI registration needed (provided automatically by ASP.NET host) ✅

**New bug introduced — success Snackbar dead code:**

```csharp
Nav.NavigateTo("/nexus");                              // line 479 — navigation fires
Snackbar.Add("Submission deleted.", Severity.Success); // line 480 — never reached by user
```

In Blazor Server (`InteractiveServer`), `NavigationManager.NavigateTo` dispatches navigation synchronously within the render cycle. The component begins teardown before the Snackbar.Add renders. The success toast will **never be shown** to the user.

**Fix:** Swap the two lines — call `Snackbar.Add` first, then `Nav.NavigateTo`.

```csharp
Snackbar.Add("Submission deleted.", Severity.Success);
Nav.NavigateTo("/nexus");
```

---

## Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Important | `SubmissionDetail.razor` | 479–480 | `Nav.NavigateTo` called before `Snackbar.Add` — success toast never displays | Swap: `Snackbar.Add` first, then `NavigateTo` |
| Minor | `SubmissionDetail.razor` | 151 | Previous Version panel still uses positional `Skip(1)` — not fixed from I1 | Replace with `.Where(d => d.Id != _submission.ActiveSpecDocumentId).OrderByDescending(d => d.Version).FirstOrDefault()` |

---

## What to Fix (Tony)

1. **`SubmissionDetail.razor` line 479–480** — Swap `Snackbar.Add(...)` **before** `Nav.NavigateTo("/nexus")`:
   ```csharp
   Snackbar.Add("Submission deleted.", Severity.Success);
   Nav.NavigateTo("/nexus");
   ```

2. **`SubmissionDetail.razor` line 151** — Replace `Skip(1)` in the Previous Version panel with ID-based exclusion:
   ```razor
   @if (_submission.SpecDocuments
       .Where(d => d.Id != _submission.ActiveSpecDocumentId)
       .OrderByDescending(d => d.Version)
       .FirstOrDefault() is { } prevSpec)
   ```

No other changes needed. All three original issues (C1, I1 partial, I2) are substantially fixed; only these two line-level items remain.

---

_Hawkeye — Review cycle 2_

---

## Cycle 3 — Commit `655ef24` — 2026-04-08

**Reviewer:** Hawkeye
**Scope:** WIs #1651, #1652 — two targeted one-liner fixes
**Cycle:** 3

### Verdict: ✅ PASS

---

### Scope Check
Single file changed: `Components/Pages/SubmissionDetail.razor` — 4 lines (2 swaps). No out-of-scope modifications.

---

### WI #1651 — Snackbar before NavigateTo ✅

**Lines 479–480:**
```csharp
Snackbar.Add("Submission deleted.", Severity.Success);
Nav.NavigateTo("/nexus");
```
Snackbar fires before navigation in the success path. Catch branches show `Snackbar.Add` only (no NavigateTo on failure) — correct.

---

### WI #1652 — `_historicalSpecs.FirstOrDefault()` in Previous Version panel ✅

**Line 151:**
```razor
@if (_historicalSpecs.FirstOrDefault() is { } prevSpec)
```
Old inline `.Skip(1).FirstOrDefault()` expression is fully removed — zero occurrences remain. `_historicalSpecs` is backed by an ID-based exclusion in `LoadSubmissionAsync` (more robust than positional Skip).

---

### Build
0 errors, 0 warnings. ✅

---

### Summary

| Check | Result |
|---|---|
| Snackbar before NavigateTo (delete success) | ✅ |
| Old `.Skip(1).FirstOrDefault()` expression gone | ✅ |
| `_historicalSpecs.FirstOrDefault()` in panel | ✅ |
| `_historicalSpecs` backing field correct | ✅ |
| No out-of-scope changes | ✅ |
| Build clean | ✅ |

Both fixes are correct. All cycles complete. This task ships.

---

_Hawkeye — Review cycle 3_
