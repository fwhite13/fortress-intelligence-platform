# Review Brief: WI919 — FAM OS Full CSS Audit
## Commit: e09a224 | Reviewer: Hawkeye (Clint Barton) | Cycle 1

## Context
This commit performs a CSS hygiene audit on FAM OS:
- All bare MudButton elements → classified with famos-btn-outline, famos-btn-primary, or famos-btn-danger
- Static inline style="color:var(...)" on text elements → CSS utility classes
- 7 new utility classes added to famos.css
- Dynamic style="@csharpVar" intentionally left untouched

## Pre-collected Findings (from grep sweeps)

### CHECK 1: Bare MudButton sweep
`grep -rn "<MudButton " ~/projects/fip/famos/src/FamOs.Web/Components/ --include="*.razor" | grep -v "Class=\|Style=\|Variant="`
**Result: EMPTY (exit 1 = no matches). ✅ PASS**

### CHECK 2: Spot-check 5 files — every MudButton has a class

ClientDecisionPanel.razor:
- Line 78: `<MudButton Class="famos-btn-primary"` ✅
- Line 84: `<MudButton Class="famos-btn-danger"` ✅  
- Line 89: `<MudButton Class="famos-btn-outline"` ✅
- Line 117: `<MudButton Class="famos-btn-danger"` ✅
- Line 122: `<MudButton Class="famos-btn-outline"` ✅

Pipeline.razor:
- Line 19: `<MudButton Class="famos-btn-primary"` ✅
- Line 57: `<MudButton Class="famos-btn-outline-sm"` ✅

Dashboard.razor:
- Line 17: `<MudButton Class="famos-btn-outline-sm"` ✅
- Line 20: `<MudButton Class="famos-btn-outline-sm"` ✅

Accounts.razor:
- Line 29: `<MudButton Class="famos-btn-outline-sm"` ✅
- Line 48: `<MudButton Class="famos-btn-primary"` ✅

TaskCenter.razor:
- Line 24: `<MudButton Class="famos-btn-outline-sm"` ✅
- Line 112: `<MudButton Class="famos-btn-outline"` ✅

**All 5 files: ✅ PASS**

### CHECK 3: Danger actions use famos-btn-danger

CloseOpportunityDialog.razor:
- Line 26: `<MudButton Class="famos-btn-danger"` (close opportunity) ✅

ClientDecisionPanel.razor:
- Line 84: `<MudButton Class="famos-btn-danger"` ✅
- Line 117: `<MudButton Class="famos-btn-danger"` ✅
**✅ PASS — danger actions correctly classified**

### CHECK 4: Cancel/secondary actions use famos-btn-outline
- CloseOpportunityDialog line 25: `<MudButton Class="famos-btn-outline"` Cancel ✅
- ClientDecisionPanel line 89/122: `<MudButton Class="famos-btn-outline"` ✅
**✅ PASS**

### CHECK 5: No remaining style="color:var(--navy);" on static text elements
`grep -rn 'style="color:var(--navy);"' ~/projects/fip/famos/src/FamOs.Web/Components/ --include="*.razor"`
**Result: EMPTY ✅ PASS**

### CHECK 6: Dynamic style="@..." untouched
- Dashboard.razor:119 `style="@($"height:6px; width:{pct:F0}%..."` ✅ untouched
- OpportunityWorkspace.razor:75 `style="@($"height:6px; width:{completeness.Score}%..."` ✅ untouched
- OwnerPickerDialog.razor:16 `style="@(isSelected ? "background:var(--sky);" ...)"` ✅ untouched
**✅ PASS — dynamic interpolations not touched**

### CHECK 7: No Color="Color.Primary" MudChip remaining with navy-on-navy risk
**⚠️ FINDING — TaskCenter.razor line 69:**
```
<MudChip T="string" Size="Size.Small" Color="Color.Primary"
         Style="font-size:10px; height:18px;">
    @GetStageLabel(opp.LifecycleStage)
</MudChip>
```
This MudChip uses Color="Color.Primary" which maps to --navy, creating navy text on navy background.
This was NOT addressed by this commit. The task brief says to audit for these. **⚠️ NEEDS-CHANGES**

Also note MudCheckBox Color="Color.Primary" on line 90 and MudProgressLinear Color="Color.Primary" on line 34 — progress bars and checkboxes with Color.Primary are less problematic (different rendering) so those are **NITPICK** / acceptable.

### CHECK 8: famos.css contains all 7 new utility classes
From grep on famos.css lines 599-620:
- `.famos-text-navy { color: var(--navy); }` ✅
- `.famos-text-success { color: var(--green); }` ✅
- `.famos-text-muted { color: var(--muted); }` ✅
- `.famos-fw-600 { font-weight: 600; }` ✅
- `.famos-fw-700 { font-weight: 700; }` ✅
- `.famos-btn-icon { ... }` ✅ (with hover state at line 616)
**Count: 6 distinct class names + 1 hover state. The 7th "class" is the :hover pseudo. ✅ PASS**

### CHECK 9: New classes use CSS variables (not hardcoded hex)
From lines 599-620 of famos.css:
- `color: var(--navy)` ✅
- `color: var(--green)` ✅
- `color: var(--muted)` ✅ (assuming --muted is a CSS var — consistent with rest of codebase)
- `color: var(--navy)` in famos-btn-icon ✅
- `background: var(--hover)` in famos-btn-icon:hover ✅
**✅ PASS — no hardcoded hex in new classes**

### CHECK 10: Scope — only famos/src/FamOs.Web/ modified
From `git show e09a224 --name-only`:
- famos/src/FamOs.Web/Components/Pages/Accounts.razor ✅
- famos/src/FamOs.Web/Components/Pages/Dashboard.razor ✅
- famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ClientDecisionPanel.razor ✅
- famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/IntakePanel.razor ✅
- famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/MarketedPanel.razor ✅
- famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuotesReceivedPanel.razor ✅
- famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/UnderwritingPrepPanel.razor ✅
- famos/src/FamOs.Web/Components/Shared/PanelErrorBoundary.razor ✅
- famos/src/FamOs.Web/wwwroot/css/famos.css ✅
- pipeline/WI919-BUILD-REPORT.md (pipeline artifact, acceptable) ✅
**✅ PASS — no FAIT, FORMS, FIRM, or other apps touched**

## Missed Inline Style Conversions (Important)
Additional static text styles NOT converted that are in-scope per the task's definition:

1. **TaskCenter.razor:66** — `<MudText Style="color:var(--navy); font-weight:600;">` — should be `Class="famos-text-navy famos-fw-600"`
2. **QuoteScraperPanel.razor:14** — `<MudText Style="color:var(--navy);">` — should be `Class="famos-text-navy"`
3. **UnderwritingPrepPanel.razor:34** — `<MudText Style="font-weight:600;">` — should be `Class="famos-fw-600"`
4. **MarketedPanel.razor:36** — `<MudText Style="font-weight:600;">` — should be `Class="famos-fw-600"`

These files (UnderwritingPrepPanel, MarketedPanel, QuoteScraperPanel, TaskCenter) appear in-scope but were NOT included in the commit's changed-file list. The commit claims "12 static inline styles converted" but these 4 text/font-weight elements remain unconverted.

## Summary Verdict

**NEEDS-CHANGES** with 2 required fixes:

### Critical (must fix before passing):
None — the primary P1 checks (bare MudButton sweep, dangerous/cancel classification, scope) all pass.

### Important (required):
1. **TaskCenter.razor:69** — MudChip with `Color="Color.Primary"` is navy-on-navy. Replace with `Color="Color.Default"` and apply styling via CSS (e.g. a `famos-chip-stage` class, or use MudBlazor `Color.Transparent`).
2. **4 missed inline text style conversions** — TaskCenter:66, QuoteScraperPanel:14, UnderwritingPrepPanel:34, MarketedPanel:36 — these text elements still have static `Style=` with color/font-weight that the task explicitly set out to convert.

### Nitpick (non-blocking, fix anyway per pipeline rules):
- MudCheckBox Color.Primary and MudProgressLinear Color.Primary instances — these don't cause visual defects but are inconsistent with the CSS hygiene goal. Could be addressed in a follow-up.

## Final Answer
**Verdict: NEEDS-CHANGES**
Core bare-button audit PASSES. The commit misses 4 text-element style conversions and leaves 1 MudChip with navy-on-navy Color.Primary. Fix these 5 items and re-submit.
