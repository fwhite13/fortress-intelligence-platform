# Review Report — ADO#2820

### Verdict: NEEDS-CHANGES

**Commit reviewed:** `a4b5a2f`
**Reviewer:** Hawkeye (Clint Barton) — Review cycle 1
**Date:** 2026-05-06

---

## CC Review Summary

CC performed a full adversarial read of all 3 changed files plus 6 supporting context files (entity models, DTO, DbContext, UserContextService, NexusRoles). No false positives were returned. All CC findings were confirmed as real issues.

---

## Spec Compliance Check

**What was specified:**
- `DecomposeAndPersistAsync` added to interface + service ✅
- `ArtifactSet` saved before `WorkItemRecord` bulk insert ✅
- All DTO fields mapped to entity ✅ (with caveats — see nitpick #3)
- `Submission.Status` → `ArtifactsCreated` in-method ✅
- `_isEditor` guard on Decompose button and `HandleGenerateWorkItems` ✅
- `AdoService` injection removed from SubmissionDetail ✅
- Button split: `Approved + _isEditor` → Decompose; `ArtifactsCreated` → Review Work Items ✅
- Error handling: snackbar + `_isGeneratingWorkItems` reset in finally ✅
- No `IDbContextFactory` misuse — uses scoped `NexusDbContext _db` directly ✅

**Spec compliance verdict:** ✅ COMPLIANT on structure — two functional gaps block PASS.

---

## Consistency Audit

**Files cross-referenced:**
- `AdoWorkItemDto` ↔ `WorkItemRecord` mapping — ✅ all business fields mapped; `Tags`/`StoryPoints` intentionally absent (see nitpick)
- `NexusRoles` ↔ `UserContextService.IsNexusEditorAsync` ↔ `_isEditor` guard — ✅ consistent (Admin OR Reviewer)
- `SubmissionStatus.ArtifactsCreated` ↔ button condition ↔ service write — ✅ consistent enum value used throughout
- `WorkItemRecord.Status` default `"Created"` ↔ mapping explicit `"Pending"` — ⚠️ inconsistent default (see nitpick #4)
- `ArtifactSet.Id` usage timing — ✅ SaveChanges called before WorkItemRecord construction; ID is real before FK used

**No undocumented consistency failures found beyond those catalogued below.**

---

## Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| **Important** | `ArtifactGenerationService.cs` | 135–149 / `WorkItemRecord.cs`:9–10 | `AdoWorkItemId = 0` and `AdoWorkItemUrl = ""` persisted — these ADO-post fields have no pre-ADO values and should be nullable | Make both nullable in entity + DbContext; add migration |
| **Important** | `ArtifactGenerationService.cs` | 118 | Empty DTO list (Bedrock silent fail) → phantom `ArtifactSet` created, status → `ArtifactsCreated`, user stuck with 0 work items | Guard: `if (dtos.Count == 0) throw new InvalidOperationException(...)` after `GenerateWorkItemsAsync` call |
| Nitpick | `ArtifactGenerationService.cs` | 135–149 | `Tags` and `StoryPoints` from DTO silently dropped — no comment | Add `// Tags and StoryPoints not persisted pre-ADO` comment |
| Nitpick | `WorkItemRecord.cs` | 13 | Entity default `Status = "Created"` contradicts explicit `Status = "Pending"` in mapping | Change entity default to `"Pending"` |
| Nitpick | `SubmissionDetail.razor` | 472 | `callerUpn` re-fetched via `UserContextService.GetUpnAsync()` — `_currentUserUpn` already set | Use `_currentUserUpn` directly |

---

## Verification Results — All 11 Checklist Items

| Check | Result |
|-------|--------|
| 1. `ArtifactSet` saved before `WorkItemRecord` uses its ID | ✅ Correct ordering confirmed |
| 2. All DTO fields mapped | ✅ All business fields mapped (Tags/StoryPoints not in entity — see nitpick) |
| 3. `Submission.Status → ArtifactsCreated` in-method | ✅ Done in `DecomposeAndPersistAsync`, saves included |
| 4. `_isEditor` guard — button + method server-side | ✅ Both present; `IsNexusEditorAsync` = Admin OR Reviewer |
| 5. `AdoService` injection removed | ✅ No `@inject IAdoService` or `@inject AdoService` anywhere |
| 6. Button state mutual exclusivity | ✅ `Approved` → Decompose; `ArtifactsCreated` → Review Work Items; no zombie states |
| 7. Error handling completeness | ✅ `try/catch`, `finally`, snackbar, `StateHasChanged()` all present |
| 8. `IDbContextFactory` misuse | ✅ None — scoped `NexusDbContext _db` via constructor, correct for scoped service |
| 9. Empty DTO list | ❌ Not guarded — see Important issue #2 |
| 10. Double-submit guard | ✅ Button disabled state + Blazor Server serializes circuit events |
| 11. `AdoWorkItemId/Url` values persisted | ⚠️ `0`/`""` written — not a crash but silent schema design issue (Important #1) |

---

## Critical Issues: 0

No runtime crashes or constraint failures.

---

## Important Issues: 2

### I1: `AdoWorkItemId = 0` and `AdoWorkItemUrl = ""` — Wrong Schema Design

**File:** `ArtifactGenerationService.cs:135–149`, `WorkItemRecord.cs:9–10`, `NexusDbContext.cs:151–152`

**Issue:** `WorkItemRecord` has `AdoWorkItemId` (int, required) and `AdoWorkItemUrl` (varchar 500, required NOT NULL). Neither field has a corresponding value on `AdoWorkItemDto` — they're post-ADO-creation fields (set after calling the ADO API). The mapping block never sets them, so EF writes `0` and `""` respectively. The DB INSERT succeeds (no constraint violation), but every downstream code path that uses these fields to determine "has this been posted to ADO?" will misread `0`/`""` as valid ADO state.

**Impact:** The `NexusArtifacts.razor` page and any future ADO-posting service that checks `if (record.AdoWorkItemId > 0)` or `!string.IsNullOrEmpty(record.AdoWorkItemUrl)` will produce incorrect results for every pre-ADO record.

**Fix:**
```diff
// WorkItemRecord.cs
- public int AdoWorkItemId { get; set; }
- public string AdoWorkItemUrl { get; set; } = "";
+ public int? AdoWorkItemId { get; set; }
+ public string? AdoWorkItemUrl { get; set; }

// NexusDbContext.cs — WorkItemRecord config block
- entity.Property(e => e.AdoWorkItemId).HasColumnName("ado_work_item_id").IsRequired();
- entity.Property(e => e.AdoWorkItemUrl).HasColumnName("ado_work_item_url").HasMaxLength(500).IsRequired();
+ entity.Property(e => e.AdoWorkItemId).HasColumnName("ado_work_item_id");
+ entity.Property(e => e.AdoWorkItemUrl).HasColumnName("ado_work_item_url").HasMaxLength(500);
```

Requires a migration (`ALTER TABLE work_item_records MODIFY ado_work_item_id INT NULL, MODIFY ado_work_item_url VARCHAR(500) NULL`).

---

### I2: Empty DTO List Not Guarded — Phantom ArtifactSet / Stuck Submission

**File:** `ArtifactGenerationService.cs:118`

**Issue:** `GenerateWorkItemsAsync` never throws — on any failure (Bedrock down, spec not found, JSON parse error) it returns `new List<AdoWorkItemDto>()`. `DecomposeAndPersistAsync` has no guard on this. When the list is empty:
1. An `ArtifactSet` is created and saved with `ExternalDependencyCount = 0`
2. Zero `WorkItemRecord` rows are inserted
3. `Submission.Status` → `ArtifactsCreated`
4. Success snackbar shown: *"Decomposition complete — 0 external dependencies flagged"*
5. User navigates to `/nexus/{Id}/artifacts` → empty page

The submission is now stuck in `ArtifactsCreated` with no work items. The Decompose button is gone. There is no retry path without manual DB intervention.

**Fix:**
```csharp
// ArtifactGenerationService.cs — after line 118 (the GenerateWorkItemsAsync call)
var dtos = await GenerateWorkItemsAsync(specDocumentId);
if (dtos.Count == 0)
    throw new InvalidOperationException(
        $"Decomposition produced no work items for SpecDocument {specDocumentId}. " +
        "Bedrock may have failed or returned an unparseable response. Check logs for [WI_GEN] entries.");
```

The existing `catch` in `HandleGenerateWorkItems` will surface this to the user via snackbar. Status stays `Approved`. User can retry.

---

## Nitpicks: 3

**N1:** `Tags`/`StoryPoints` silently dropped from DTO → entity mapping — no comment. Not blocking. Add `// Tags and StoryPoints not persisted at decomposition stage` comment.

**N2:** `WorkItemRecord.Status` entity default is `"Created"` but mapping sets `"Pending"`. Not a runtime bug (explicit set wins), but the entity default is misleading for future code paths. Change default to `"Pending"`.

**N3:** `callerUpn` re-fetched in `HandleGenerateWorkItems` (`UserContextService.GetUpnAsync()`) when `_currentUserUpn` is already loaded. Use `_currentUserUpn ?? await UserContextService.GetUpnAsync()` or just `_currentUserUpn!`.

---

## Positive Observations

- **Ordering is perfect**: ArtifactSet save → real ID → WorkItemRecord bulk insert. No FK issues.
- **Auth guard is airtight**: Both UI (`@if`) and method-level (`if (!_isEditor) return`) guards present. NexusUser cannot see or trigger.
- **Error handling in razor is thorough**: try/catch/finally, snackbar for both success and failure, `_isGeneratingWorkItems` always reset, `StateHasChanged()` always called.
- **AdoService cleanly removed**: No dead injection left in SubmissionDetail.
- **DB context usage is correct**: Scoped `NexusDbContext _db` via constructor — no factory misuse.
- **`_generatingStatusText` UX is a nice touch**: Clear user feedback during the 30-60 second Bedrock call.

---

## What to Fix (Tony)

Two changes required before merge:

**Fix 1 — Make `AdoWorkItemId` and `AdoWorkItemUrl` nullable** (`WorkItemRecord.cs` + `NexusDbContext.cs` + migration). These are post-ADO fields — they have no value at decomposition time.

**Fix 2 — Guard empty DTO list in `DecomposeAndPersistAsync`** (`ArtifactGenerationService.cs`). If Bedrock produces 0 items, throw instead of persisting a phantom `ArtifactSet`. The existing catch/snackbar in the razor handles it.

Nitpicks are optional. Fix both Important issues and re-submit for cycle 2.
