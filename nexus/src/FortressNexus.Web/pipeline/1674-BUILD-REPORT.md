# Build Report — WI #1674 — Narrative edits lost on wizard step advance (resume mode)

**Date:** 2026-04-08  
**Engineer:** Tony Stark  
**Commit:** `4ba528c8d796b0eb09edc12e7113baa51224cd0d`  
**Risk:** low-medium (behavior fix, no schema change)

---

## Root Cause Confirmed

**Method:** `GoToStep2Discovery()` in `NewSpecWizard.razor`  
**Location:** ~line 419 (`_activeStep = 2;`)

In resume mode, `OnInitializedAsync` pre-populates `_narrativeText` from the DB. The user edits the narrative on Step 0 (Details), advances to Step 1 (Files) via `GoToStep2()` (validation only — no DB write), then advances to Step 2 (Discovery) via `GoToStep2Discovery()`. That method handled file uploads and submission creation but **never persisted the current `_narrativeText` to the DB** before advancing. If anything triggered a data re-read (page refresh, navigation back-and-forward), the edit was lost.

---

## Fix Applied

Inserted a `UpdateNarrativeAsync` call in `GoToStep2Discovery()` immediately before `_activeStep = 2;`, guarded by `_isResume && _submissionId.HasValue`:

```csharp
// Persist narrative edit before advancing (resume mode only)
if (_isResume && _submissionId.HasValue)
{
    try
    {
        await SubmissionService.UpdateNarrativeAsync(_submissionId.Value, _narrativeText);
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Warning: narrative save failed — {ex.Message}", Severity.Warning);
    }
}

_activeStep = 2;
```

**`UpdateNarrativeAsync`** was already implemented in `SubmissionService` (WI #1655) — no new service code required.

---

## Step 1 → Step 2 Check

`GoToStep2()` (Step 0 → Step 1 transition) is synchronous, validation-only, and does not re-read any data. No data loss occurs there. No fix needed.

---

## Files Changed

| File | Change |
|------|--------|
| `Components/Pages/NewSpecWizard.razor` | +13 lines — persist narrative before advancing to Discovery step in resume mode |

---

## Build Result

```
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj
Build succeeded.
    0 Error(s)
    0 Warning(s)
```

**Commit:** `4ba528c` — `fix(nexus): persist narrative edit on step advance in resume mode (WI #1674)`

---

## Acceptance Criteria

- [x] Narrative edit is persisted to DB when advancing from Files → Discovery in resume mode
- [x] `UpdateNarrativeAsync` not re-implemented — uses existing service method
- [x] Fix is guard-gated (`_isResume && _submissionId.HasValue`) — no impact on new-submission flow
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] Minimal change — one insertion point, no other methods touched

---

## How to Test

1. Create a NEXUS submission and save as draft
2. Resume it via `/nexus/{id}/resume`
3. On Step 1 (Details), edit the narrative to something new
4. Advance to Step 2 (Files), then advance to Step 3 (Discovery)
5. Complete or skip Discovery, advance to Step 4 (Review)
6. Verify the narrative on the Review page reflects the edited value (not the original)
7. Submit — confirm the generated spec uses the updated narrative
