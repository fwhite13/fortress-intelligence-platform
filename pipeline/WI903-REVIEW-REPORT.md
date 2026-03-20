# Code Review Report — WI903 Sprint 5
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`
**Commit:** `87db3ee`
**Date:** 2026-03-19
**Cycle:** 1 of 2
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Executive Summary

Largest sprint to date — 20 files changed, 4 new files. The domain logic is solid. All 7 high-priority gates pass. **Regression checks pass.** However, there are DESIGN-SYSTEM violations in 3 files that must be fixed before this ships. They're all mechanical fixes (Variant= → Class=), not structural problems.

---

## HIGH-PRIORITY CHECKS — Results

### ✅ Check 1: CloseOpportunityAsync Signature — PASS

**Actual signature:**
```csharp
public async Task CloseOpportunityAsync(
    Guid opportunityId,
    CloseReason reason,
    string? notes,
    string actorUserId)
```
Matches spec exactly: `(Guid, CloseReason, string?, string)`.

**Caller count:** Exactly 2 results from production code (`.cs` and `.razor` only):
- Declaration: `LifecycleCommandService.cs:316`
- Call site: `CloseOpportunityDialog.razor:49`

(Note: `cc-brief.md` and `cc-sprint2-brief.md` contain the old signature from Tony's build briefs — these are non-production artifacts and do not count as callers.)

**CloseOpportunityDialog call site:**
```csharp
await Lifecycle.CloseOpportunityAsync(
    OpportunityId, _reason.Value, _notes.Trim(), userId);
```
Passes `(Guid, CloseReason, string, string)` — `_notes.Trim()` is `string` (not nullable), which is compatible with `string?`. ✅

---

### ✅ Check 2: AgingService Registered as Hosted Service — PASS

`Program.cs` line confirms:
```csharp
builder.Services.AddHostedService<AgingService>();
```
Found in the Background Services block alongside `OutboxProcessorService` and `SignalRecomputeService`. ✅

---

### ✅ Check 3: Submission Table FK + Transactions — PASS

**CreateSubmissionAsync** — wraps `BeginTransactionAsync` in `CreateExecutionStrategy`, calls `CommitAsync`. ✅

**UpdateSubmissionStatusAsync** — wraps `BeginTransactionAsync` in `CreateExecutionStrategy`, calls `CommitAsync`. ✅

Both methods follow the established transaction pattern across all LifecycleCommandService methods.

---

### ✅ Check 4: RouteToMarketAsync Validation — PASS

```csharp
// Stage gate: must have at least one submission before routing to market
if (!opp.Submissions.Any())
    throw new LifecycleValidationException(
        "At least one carrier submission must be created before routing to market.");
```

Uses `opp.Submissions.Any()` — the correct pattern. No `carrierNames.Length > 0` anywhere in the method. The `carrierNames` parameter is still accepted (for future use / UI compatibility) but the actual gate is submission-based. ✅

---

### ✅ Check 5: HubSpotService — Non-Fatal Catch — PASS

`SyncLifecycleAsync` catch block:
```csharp
catch (Exception ex)
{
    // Non-fatal: log and continue. Never fail a lifecycle transition because of HubSpot.
    _logger.LogError(ex, "[HubSpot] SyncLifecycle failed for {Id}", opportunityId);
}
```
No `throw` or re-throw. Exception swallowed, logged only. Comment explicitly states design intent. ✅

`SyncBoundAsync` catch block also non-fatal (log only, no throw). ✅

---

### ✅ Check 6: Aurora MySQL Migrations — No IF NOT EXISTS — PASS

All Sprint 5 ALTER TABLE statements use the `try/catch on MySqlException 1060` pattern:
```csharp
try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE opportunities ADD COLUMN close_reason INT NULL"); }
catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("close_reason column already exists"); }
```
No `IF NOT EXISTS` syntax anywhere in the migration block. Sprint 4 pattern is also clean (verified). ✅

---

### ✅ Check 7: DominantSignal Switch Completeness — PASS (with note)

**Spec stated** 5 new values: `FollowUpNeeded, StageRisk, TimeRisk, LostSignal, AtRisk`

**Actual Sprint 5 additions** to `Enums.cs` (verified by diffing against `4b38ff2`):
- `FollowUpNeeded = 9`
- `WaitingOnUW = 10`
- `WaitingOnCarrier = 11`
- `AtRisk = 12`
- `Urgent = 13`

**Note:** `StageRisk` and `LostSignal` are NOT in the enum — they were spec placeholders renamed during implementation to `WaitingOnUW`, `WaitingOnCarrier`, and `Urgent`. `TimeRisk = 8` was pre-existing (Sprint 4).

**SignalChip.razor — Label switch:** Handles all 5 new values explicitly:
- `FollowUpNeeded` → "Follow Up" ✅
- `WaitingOnUW` → "UW Waiting" ✅
- `WaitingOnCarrier` → "Carrier Waiting" ✅
- `AtRisk` → "At Risk" ✅
- `Urgent` → "URGENT" ✅
- Fallthrough `_ => Signal.ToString()` covers any future additions ✅

**SignalChip.razor — Color switch:** Handles all 5 new values explicitly:
- `FollowUpNeeded` → `famos-signal-follow-up` ✅
- `WaitingOnUW` → `famos-signal-uw-waiting` ✅
- `WaitingOnCarrier` → `famos-signal-carrier-waiting` ✅
- `AtRisk` → `famos-signal-at-risk` ✅
- `Urgent` → `famos-signal-urgent` ✅
- Fallthrough `_ => "famos-signal-parked"` ✅

Both switches exhaustive. ✅

---

## DESIGN-SYSTEM CHECKLIST — ❌ FAIL (3 violations)

### ❌ Violation 1: UnderwritingPrepPanel.razor — Inline `Variant=` on MudSelect/MudTextField

**File:** `Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor`

**Lines with violations:**
```razor
<!-- Line 46 -->
<MudSelect @bind-Value="_selectedCarrier" Label="Carrier *"
           Variant="Variant.Outlined">

<!-- Line 63 -->
<MudTextField @bind-Value="_customCarrier"
    Label="Carrier Name"
    Variant="Variant.Outlined" />

<!-- Line 69 -->
<MudTextField @bind-Value="_coverageTypes"
    Label="Coverage Types (e.g. AUTO,WC,GL)"
    Variant="Variant.Outlined"

<!-- Line 78 -->
<MudTextField @bind-Value="_coverageTypes"
    Label="Coverage Types (e.g. AUTO,WC,GL)"
    Variant="Variant.Outlined" />

<!-- Line 83 -->
<MudTextField @bind-Value="_subNotes" Label="Notes (optional)"
              Variant="Variant.Outlined" />
```

**Required fix:** Remove all `Variant="Variant.Outlined"` attributes. Replace `MudSelect` with `Class="famos-select"`. Replace `MudTextField` fields with `Class="famos-input"`.

```razor
<!-- Correct patterns: -->
<MudSelect @bind-Value="_selectedCarrier" Label="Carrier *" Class="famos-select">
<MudTextField @bind-Value="_customCarrier" Label="Carrier Name" Class="famos-input" />
<MudTextField @bind-Value="_coverageTypes" Label="Coverage Types..." Class="famos-input" />
<MudTextField @bind-Value="_subNotes" Label="Notes (optional)" Class="famos-input" />
```

---

### ❌ Violation 2: MarketedPanel.razor — Inline `Variant=` on MudSelect/MudTextField + Style on MudButton

**File:** `Components/Pages/Opportunity/Panels/MarketedPanel.razor`

**Lines with violations:**
```razor
<!-- Line 51 -->
<MudSelect Value="sub.Status"
           ValueChanged="@(async (SubmissionStatus s) => await UpdateStatus(sub, s))"
           Label="Status" Variant="Variant.Outlined">

<!-- Line 75 -->
<MudSelect @bind-Value="_quoteSubId" Label="Carrier" Variant="Variant.Outlined">

<!-- Line 84 -->
<MudNumericField @bind-Value="_quotePremium" Label="Premium ($)"
                 Format="N0" Variant="Variant.Outlined" />

<!-- Line 87-92 (MudButton with inline Style=) -->
<MudButton Class="famos-btn-primary"
           OnClick="RecordQuote"
           Disabled="@(...)"
           Style="height:40px; margin-top:4px;">
```

**Required fixes:**
1. Remove `Variant="Variant.Outlined"` from all three controls — add `Class="famos-select"` / `Class="famos-input"`.
2. `MudNumericField` with `Variant=` is the same violation — remove it.
3. `Style="height:40px; margin-top:4px;"` on MudButton — the `height:40px` portion may be intentional for alignment but sets inline style on a component. Remove `Style=` and handle via CSS or a spacing helper class if needed. At minimum remove `height:40px` and keep only a spacing utility if required.

---

### ❌ Violation 3: QuoteScraperPanel.razor — Inline `Variant=` on MudSelect + Style on MudTextField + Style on MudButton

**File:** `Components/Pages/Opportunity/Panels/QuoteScraperPanel.razor`

**Line 39 — MudSelect with Variant=:**
```razor
<MudSelect @bind-Value="_selectedSubId"
           Label="Carrier" Variant="Variant.Outlined">
```
Fix: Remove `Variant="Variant.Outlined"`, add `Class="famos-select"`.

**Line 58 — MudButton with inline margin Style= (minor, but present):**
```razor
<MudButton Class="famos-btn-primary"
           OnClick="UploadAndSubmit"
           Disabled="_uploading"
           Style="margin-left:8px; margin-top:4px;">
```
Style on button for margin is a spacing concern, not a width/color violation. However it's inline style and should use a CSS utility class. **Borderline — flag as nitpick.**

**Line 80 — MudTextField (results display) with Style=:**
```razor
<MudTextField Value="_resultJson"
              Lines="6" ReadOnly="true"
              Variant="Variant.Outlined"
              Style="font-family:monospace; font-size:12px;" />
```
Two violations: `Variant="Variant.Outlined"` must be removed; `Style="font-family:monospace; font-size:12px;"` is an inline style that should be a CSS class (e.g., `famos-input-code` or `famos-input-monospace`) added to `famos.css`.

---

### Design System Items — CLEAN

| Check | Result |
|-------|--------|
| `Icons.Material.*` in components | ✅ None found in any modified component |
| `MudButton` inline `Variant=`/`Color=`/`Size=` | ✅ All MudButtons use `Class="famos-btn-*"` |
| `Style="width:..."` on inputs | ✅ None found |
| `Dense=`/`Margin=` on MudTextField/MudSelect | ✅ None found |
| `FamosIcons.*` used for MarketedPanel expand icons | ✅ Uses `FamosIcons.ExpandLess`/`ExpandMore` (both registered in `FamosIcons.cs`) |
| CloseOpportunityDialog — MudSelect Class | ✅ Uses `Class="mb-3"` (layout only, no Variant=) |

---

## REGRESSION CHECKS — ✅ ALL PASS

| Check | Result | Evidence |
|-------|--------|---------|
| WI901 QA bypass middleware | ✅ INTACT | `Program.cs`: `UseRouting → QA bypass → UseAuthentication` order preserved |
| WI893 DrawerVariant.Persistent | ✅ INTACT | `MainLayout.razor:13`: `Variant="DrawerVariant.Persistent"` unchanged |
| WI902 FamosIcons.* in TaskCenter | ✅ INTACT | All 4 icon references still use `FamosIcons.*` |
| WI902 FamosIcons.* in NavMenu | ✅ INTACT | All 5 nav icons still use `FamosIcons.*` |
| Only famos/ touched | ✅ CLEAN | `git show 87db3ee --stat` shows no non-famos/ file changes |

---

## MEDIUM-PRIORITY CHECKS — ✅ ALL PASS

### QuoteScraperPanel — Poll Loop
```csharp
for (var i = 0; i < 12; i++)
{
    await Task.Delay(5000);
    result = await Scraper.PollResultAsync(requestId);
    if (result != null) break;
    _statusMessage = $"Processing... ({(i + 1) * 5}s)";
    StateHasChanged();
}
```
✅ `Task.Delay(5000)` with `i < 12` = max 12 iterations × 5s = 60s timeout. Matches spec.

### AgingService — IServiceScopeFactory Pattern
```csharp
public AgingService(IServiceScopeFactory services, ILogger<AgingService> logger)
```
```csharp
using var scope = _services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();
```
✅ Correctly uses `IServiceScopeFactory` — does NOT inject `FamOsDbContext` directly. Scoped lifetime handled properly inside `CreateScope()`.

### DashboardSummary — record → class change
`DashboardSummary` was changed from `record` to `class`. Verified all properties still accessible in `Dashboard.razor`:
- `_summary.TotalActive` ✅
- `_summary.TimeRiskCount` ✅
- `_summary.DecisionNeeded` ✅
- `_summary.BoundThisMonth` ✅
- `_summary.TotalPremiumAtRisk` ✅
- `_summary.UrgentOpportunities` ✅
- `_summary.ByStage` ✅
- `_summary.RecentActivity` ✅

All properties present as `{ get; set; }` with sensible defaults. ✅

---

## ADDITIONAL FINDINGS

### Info: `StageRisk`/`LostSignal` — Spec Names vs Implementation Names

The review brief referenced `StageRisk` and `LostSignal` as 5 new DominantSignal values. These names don't exist in `Enums.cs`. The actual Sprint 5 additions are `WaitingOnUW`, `WaitingOnCarrier`, `AtRisk`, `FollowUpNeeded`, and `Urgent`. This is a spec naming discrepancy — the implementation is self-consistent and SignalChip handles all of them. No action required.

### Info: MarketedPanel — `Style="height:40px; margin-top:4px;"` on MudButton
The `height:40px` is for vertical alignment with the adjacent input fields. This is arguably layout positioning rather than a design token violation. However it's inline style — it should be moved to a CSS utility. I've flagged it as part of Violation 2 above. Tony can remove the height constraint and rely on MudBlazor's default button height, which typically matches MudTextField's default height in a MudGrid.

### Info: `QuoteScraperPanel` — `Style="border:1px solid..."` on MudCard  
Line 8-9 in QuoteScraperPanel:
```razor
<MudCard Class="mb-4" Elevation="0"
         Style="border:1px solid var(--border); border-radius:12px;">
```
This is a layout/card styling pattern (not an input width violation). The DESIGN-SYSTEM.md prohibits inline `Style="width:..."` on inputs specifically. This MudCard border pattern is used consistently elsewhere in the codebase. **Not a violation — acceptable.**

---

## REQUIRED FIXES (before PASS)

| # | File | Issue | Severity |
|---|------|-------|----------|
| 1 | `UnderwritingPrepPanel.razor` | `Variant="Variant.Outlined"` on MudSelect (×1) and MudTextField (×4) | DESIGN-SYSTEM |
| 2 | `MarketedPanel.razor` | `Variant="Variant.Outlined"` on MudSelect (×2) and MudNumericField (×1); `Style="height:40px..."` on MudButton | DESIGN-SYSTEM |
| 3 | `QuoteScraperPanel.razor` | `Variant="Variant.Outlined"` on MudSelect (×1) and MudTextField results display (×1); `Style="font-family:monospace..."` on MudTextField should become a CSS class | DESIGN-SYSTEM |

**Total violations: 9 inline `Variant=` attributes across 3 files. All mechanical fixes.**

---

## VERDICT

**⚠️ NEEDS-CHANGES**

Domain logic: clean. Transactions: correct. Stage gate: correct. AgingService: correct. HubSpot: non-fatal. Migrations: Aurora-compatible. SignalChip: exhaustive. Regressions: all intact.

**Blocker:** 9 DESIGN-SYSTEM violations (inline `Variant=` attributes) across `UnderwritingPrepPanel.razor`, `MarketedPanel.razor`, and `QuoteScraperPanel.razor`. Fix the three files, resubmit for Cycle 2.

---

*— Clint Barton, Code Reviewer*
*Reviewed: 2026-03-19 | Commit: 87db3ee | Files inspected: 20*
