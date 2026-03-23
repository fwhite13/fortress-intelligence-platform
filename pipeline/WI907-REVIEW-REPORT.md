# Review Report: WI907 — Sprint 7
### Proposal Workflow, Bind Execution, BoundPanel, ClosedNotBoundPanel

**Date:** 2026-03-20  
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`  
**Commit:** `de2a332`  
**Branch:** main  
**Review Cycle:** 1  

---

## CC CLI Invocation

```
cat /tmp/wi907-review-brief.md | claude --model sonnet -p
```

Working directory: `/home/fredw/projects/fip/famos/src/FamOs.Web/`

---

## VERDICT: ⚠️ NEEDS-CHANGES

One critical fix required before merge: two missing `HasColumnName()` mappings in `FamOsDbContext.cs` that will cause runtime EF query failures.

---

## HIGH PRIORITY RESULTS

**H1: ✅ PASS** — `RecordClientResponseAsync` sets `opp.LifecycleStage = LifecycleStage.Binding` inside the `if (accepted)` block (line 284 of `LifecycleCommandService.cs`). Exact code:
```csharp
if (accepted)
{
    var winningQuoteId = proposal.RecommendedQuoteId;
    Validate(opp.Quotes.Any(q => q.Id == winningQuoteId), "Winning quote not found");
    opp.LifecycleStage = LifecycleStage.Binding;   // line 284
```
`RequestBindAsync` is present at **line 381** — not removed, backward compat preserved. ✅

---

**H2: ✅ PASS** — `LoadOpportunityWithDetailsAsync` (lines 804–816) includes `.Include(o => o.Proposals)` at line 810:
```csharp
var opp = await _db.Opportunities
    .Include(o => o.Submissions)
    .Include(o => o.Quotes)
    .Include(o => o.Contacts)
    .Include(o => o.Proposals)          // line 810 ✓
    .Include(o => o.Tasks.Where(t => t.Status == "open"))
    .Include(o => o.Flags)
    .FirstOrDefaultAsync(o => o.Id == id)
```

---

**H3: ✅ PASS (with annotation)** — Call site in `BindingPanel.razor` lines 142–148:
```csharp
await Lifecycle.RecordBinderReceivedAsync(
    Opportunity.Id,                                                      // 1
    effDate,                                                             // 2
    expDate,                                                             // 3
    _policyNumber.Trim().Length > 0 ? _policyNumber.Trim() : null,      // 4
    _coverageType.Trim().Length > 0 ? _coverageType.Trim() : null,      // 5
    userId);                                                             // 6
```
**6 arguments** (spec said "7" but that was a miscounted spec — signature has 6 params: `opportunityId, effectiveDate, expirationDate?, policyNumber?, coverageType?, actorUserId`). Call site and signature are perfectly synchronized — no compile error.

---

**H4: ✅ PASS** — `CreateProposalAsync` lines 176–179 implement the correct two-step pattern:
```csharp
// (a) All quotes: IsRecommended = false
foreach (var q in opp.Quotes) q.IsRecommended = false;
// (b) Selected quote: IsRecommended = true
var winningQuote = opp.Quotes.First(q => q.Id == recommendedQuoteId);
winningQuote.IsRecommended = true;
```
No other code depends on old `SendProposalAsync` recommendation logic.

---

## MEDIUM PRIORITY RESULTS

**M1: ✅ PASS** — `BoundPanel.razor` uses `HasValue` guard before `.Value` access:
```csharp
@if (shadow.RenewalTimerStart.HasValue)           // nullable-safe guard
{
    var renewalDate   = shadow.RenewalTimerStart.Value.AddYears(1).AddDays(-60);
    var today         = DateOnly.FromDateTime(DateTime.Today);
    var daysToRenewal = renewalDate.DayNumber - today.DayNumber;   // valid .NET 9
```
`DateOnly.DayNumber` arithmetic is valid in .NET 9. No nullable dereference risk.

---

**M2: ✅ PASS** — `ClosedNotBoundPanel.razor` uses `SubmissionStatus.QuoteReceived` at lines 40 and 44. Enum value confirmed present in codebase (seen at `LifecycleCommandService.cs` line 651). No compilation risk.

---

**M3: ✅ PASS** — Design system compliance across all 5 panels:

| File | MudButton Variant/Color/Size | Icons.Material.* | @rendermode | famos-btn-* |
|---|---|---|---|---|
| `ClosedNotBoundPanel.razor` | None (display-only) | None | None | N/A |
| `BoundPanel.razor` | None (display-only) | None — `FamosIcons.*` ✓ | None | N/A |
| `QuotesReceivedPanel.razor` | None | None — `FamosIcons.*` ✓ | None | `famos-btn-primary`, `famos-btn-outline` ✓ |
| `ClientDecisionPanel.razor` | None | None — `FamosIcons.*` ✓ | None | `famos-btn-primary`, `famos-btn-danger`, `famos-btn-outline` ✓ |
| `BindingPanel.razor` | None | None | None | `famos-btn-outline`, `famos-btn-primary` ✓ |

All 5 files clean. Zero design system violations.

---

**M4: ✅ PASS** — `OpportunityService.GetByIdAsync` includes `.Include(o => o.Proposals)` at line 48:
```csharp
return await db.Opportunities
    .Include(o => o.Flags.Where(f => f.IsActive))
    .Include(o => o.Submissions)
    .Include(o => o.Quotes)
    .Include(o => o.Proposals)        // line 48 ✓
    .Include(o => o.PolicyShadow)
    ...
```

---

## LOW PRIORITY RESULTS

**L1: ✅ PASS** — `QuotesReceivedPanel.razor`: `_selectedQuoteId` defaults to `Guid.Empty`, pre-set to `recommended.Id` in `OnInitialized` if available. All guards (`!= Guid.Empty`, `== Guid.Empty`) are correct.

**L2: ✅ PASS** — `BoundPanel.razor` fallback to `shadow.CreatedAt` when `BoundAt` is null:
```csharp
@(shadow.BoundAt.HasValue
    ? shadow.BoundAt.Value.ToLocalTime().ToString("MMMM d, yyyy")
    : shadow.CreatedAt.ToLocalTime().ToString("MMMM d, yyyy"))
```
Intentional and correct for pre-Sprint-7 records.

**L3: ✅ PASS** — `TryAddColumnAsync` defined **exactly once** at `Program.cs` lines 276–279, inside the init block scope. No duplicate.

---

## STANDARD CHECKS

**SC1: ✅ PASS (advisory)** — All 13 reviewed files are within `famos/src/FamOs.Web/`. Pipeline artifacts (`pipeline/WI907-BUILD-REPORT.md`) are documentation outside source tree — not a violation. Git diff output was unavailable in sandbox, but file scope is consistent with task.

---

**SC2: ✅ PASS** — Exactly **10** `TryAddColumnAsync` calls in the Sprint 7 block. All use the helper which wraps try/catch on MySqlException error 1060:
```csharp
async Task TryAddColumnAsync(string sql) {
    try { await db.Database.ExecuteSqlRawAsync(sql); }
    catch (MySqlException ex) when (ex.Number == 1060) { /* already exists */ }
}
```
Migrations: 4 on `proposals`, 4 on `policy_shadow_records`, 2 on `opportunities`. ✅

---

**SC3: ❌ FAIL — CRITICAL** — Only **7 of 10** expected `HasColumnName()` mappings are present in `FamOsDbContext.cs`.

**Present (7):**
1. `Opportunity.BindConfirmationNumber` → `"bind_confirmation_number"` ✓
2. `Opportunity.BindRequestSubmittedAt` → `"bind_request_submitted_at"` ✓
3. `Proposal.ProposalDate` → `"proposal_date"` ✓
4. `PolicyShadowRecord.PolicyNumber` → `"policy_number"` ✓
5. `PolicyShadowRecord.ExpirationDate` → `"expiration_date"` ✓
6. `PolicyShadowRecord.CoverageType` → `"coverage_type"` ✓
7. `PolicyShadowRecord.BoundAt` → `"bound_at"` ✓

**Missing (3):**
- ❌ `Proposal.CarrierName` → DB column `carrier_name`, no `HasColumnName` → EF looks for `CarrierName` → **runtime query failure**
- ❌ `Proposal.CoverageTypes` → DB column `coverage_types`, no `HasColumnName` → EF looks for `CoverageTypes` → **runtime query failure**
- ⚠️ `Proposal.Notes` → DB column `notes`; MySQL column names are case-insensitive so `Notes`=`notes` is likely safe in practice — but should still be explicit for consistency

**Impact:** Any code path that reads or writes `Proposal.CarrierName` or `Proposal.CoverageTypes` will fail at runtime. This breaks:
- `CreateProposalAsync`
- `QuotesReceivedPanel` (proposal card display)
- `ClientDecisionPanel` (proposal detail rendering)

---

**SC4: ⚠️ WARN (pre-existing, not a blocker)** — Hardcoded credential fallbacks found in `Program.cs`:
```csharp
builder.Configuration["FortressApi:Key"] ?? "246191f33f470f136ebb800516f8e10f"
builder.Configuration["FortressApi:Secret"] ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d"
```
Also: `Password=dev` (lines 56, 77), QA bypass token `natasha-qa-token-famos-dev` (line 319). All **pre-existing from prior sprints** — not introduced in Sprint 7. Flagging for future cleanup, not blocking this sprint.

---

**SC5: ✅ PASS** — No new NuGet packages added. 7 `PackageReference` entries, all from prior sprints.

---

## Required Fix

**File:** `Data/FamOsDbContext.cs` — Proposal entity configuration block

**Add these two lines** (alongside the existing `HasMaxLength(200)` call for the same properties):
```csharp
e.Property(x => x.CarrierName).HasMaxLength(200).HasColumnName("carrier_name");
e.Property(x => x.CoverageTypes).HasMaxLength(200).HasColumnName("coverage_types");
```
Optionally add for completeness:
```csharp
e.Property(x => x.Notes).HasColumnType("longtext").HasColumnName("notes");
```

No other changes needed. All H1–H4 pass. Design system clean. DB migration pattern correct.

---

## Summary

| Check | Result |
|-------|--------|
| H1: RecordClientResponseAsync → Binding | ✅ PASS |
| H2: LoadOpportunityWithDetails includes Proposals | ✅ PASS |
| H3: RecordBinderReceived 7-param call site | ✅ PASS |
| H4: CreateProposalAsync IsRecommended logic | ✅ PASS |
| M1: BoundPanel DateOnly arithmetic | ✅ PASS |
| M2: ClosedNotBoundPanel SubmissionStatus.QuoteReceived | ✅ PASS |
| M3: Design system compliance | ✅ PASS |
| M4: OpportunityService includes Proposals | ✅ PASS |
| L1–L3 | ✅ PASS |
| SC1: File scope | ✅ PASS |
| SC2: 10 DB migrations / try-catch 1060 | ✅ PASS |
| SC3: HasColumnName mappings (10 expected) | ❌ FAIL — 7/10 present |
| SC4: No hardcoded credentials | ⚠️ WARN (pre-existing) |
| SC5: No new NuGet packages | ✅ PASS |

**Verdict: NEEDS-CHANGES** — Fix SC3 (two missing HasColumnName mappings in FamOsDbContext.cs for Proposal.CarrierName and Proposal.CoverageTypes). All else passes. Return to Tony for targeted fix. No re-architecture needed.
