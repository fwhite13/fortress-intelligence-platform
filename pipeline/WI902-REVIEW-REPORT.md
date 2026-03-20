# Review Report: WI902 — FAM OS Design System Migration, Cycle 1

**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`  
**Commit:** `f696ee5`  
**ADO WI:** 902  
**Date:** 2026-03-19  
**Cycle:** 1 of 2  
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Summary

The core migration work is solid — FamosIcons.cs is properly populated, icon references are correctly migrated in TaskCenter and NavMenu, and all 9 panel files have had MudButton Variant/Color/Size removed. CSS classes are all present in famos.css. Regression checks pass.

Two blocking issues require fixes before this can pass:

1. **Pipeline.razor — Dual-class pattern `famos-btn-primary famos-btn-primary-sm`** is not a defined pattern in DESIGN-SYSTEM.md and produces undefined visual behavior (two classes competing, neither defines color/background).
2. **Components/Dialogs/ — 3 dialog files contain `MudButton Variant="Variant.Filled"` / `Color=`** — Dialogs/ is a subdirectory of Components/, making them fully in scope for the design system checklist. These were not included in WI902 scope but must be addressed.

---

## Regression Checks ✅

| Check | Result |
|-------|--------|
| `git show f696ee5 --stat` — only famos/ files | ✅ PASS — 12 famos/ files only |
| IntakePanel form fields: `Variant="Variant.Outlined"` present | ✅ PASS — all MudTextField, MudSelect, MudNumericField in IntakePanel retain `Variant="Variant.Outlined"` (exempt) |
| WI893 DrawerVariant.Persistent in MainLayout | ✅ PASS — `Variant="DrawerVariant.Persistent"` on line 13 intact |
| WI901 QA bypass in Program.cs | ✅ PASS — bypass logic intact at lines 196–235 |

---

## Part A — FamosIcons.cs ✅ PASS

All three required icons verified present:

| Icon | Section | Status |
|------|---------|--------|
| `OpenInNew` | Actions | ✅ Present — `Icons.Material.Outlined.OpenInNew` |
| `CheckCircle` | Status/Signals | ✅ Present — `Icons.Material.Outlined.CheckCircle` |
| `BarChart` | Data Viz | ✅ Present — `Icons.Material.Outlined.BarChart` |

Full icon registry looks correct with proper grouping (Navigation, Actions, Search/Filter, Status/Signals, Lifecycle, Data, Data Viz).

---

## Part B — Icon Migration ✅ PASS

### TaskCenter.razor
| Check | Status |
|-------|--------|
| `AdornmentIcon="@FamosIcons.Filter"` (not Icons.Material.Filled.FilterList) | ✅ PASS |
| `StartIcon="@FamosIcons.Add"` on Add Task button | ✅ PASS |
| `Icons.Material.Filled.CheckCircle` → `FamosIcons.CheckCircle` | ✅ PASS |
| `Icons.Material.Filled.OpenInNew` → `FamosIcons.OpenInNew` | ✅ PASS |
| Filter MudTextField: `Class="famos-input-filter"` | ✅ PASS |
| Filter MudTextField: no `Style="min-width..."` inline | ✅ PASS — no inline width style present |
| Filter MudTextField: `Variant="Variant.Outlined"` REMOVED | ✅ PASS — no Variant= on filter field |

### NavMenu.razor
All 5 icons verified using FamosIcons.*:

| Icon | Reference | Status |
|------|-----------|--------|
| Dashboard | `@FamosIcons.Dashboard` | ✅ |
| Pipeline | `@FamosIcons.Pipeline` | ✅ |
| Tasks | `@FamosIcons.Tasks` | ✅ |
| Accounts | `@FamosIcons.Accounts` | ✅ |
| BarChart (Reports) | `@FamosIcons.BarChart` | ✅ |

No `Icons.Material.*` anywhere in Components/ — full component-wide grep returned clean.

---

## Part C — MudButton CSS Migration

### Panels & Core Pages — ✅ PASS on 11 of 12 files

| File | MudButton Variant= Present | CSS Classes Used | Status |
|------|---------------------------|-----------------|--------|
| Dashboard.razor | None | `famos-btn-outline-sm` | ✅ |
| Pipeline.razor | None | `famos-btn-primary famos-btn-primary-sm` | ⚠️ SEE ISSUE #1 |
| OpportunityWorkspace.razor | None | `famos-btn-outline-sm`, `famos-btn-danger` | ✅ |
| UnderwritingPrepPanel.razor | None | `famos-btn-primary` | ✅ |
| IntakePanel.razor | None | `famos-btn-outline`, `famos-btn-primary` | ✅ |
| BindingPanel.razor | None | `famos-btn-primary` | ✅ |
| MarketedPanel.razor | None | `famos-btn-primary` | ✅ |
| QuotesReceivedPanel.razor | None | `famos-btn-primary`, `famos-btn-outline` | ✅ |
| ClientDecisionPanel.razor | None | `famos-btn-primary`, `famos-btn-outline` | ✅ |

All 9 panel files and Dashboard — clean. No inline Variant/Color/Size anywhere.

---

## CSS Class Existence Check ✅ PASS

All four Tony-flagged classes are defined in `famos.css`:

| Class | Line | Status |
|-------|------|--------|
| `.famos-btn-danger` | 565 | ✅ Present |
| `.famos-btn-primary-sm` | 341 | ✅ Present |
| `.famos-btn-outline` | 546 | ✅ Present |
| `.famos-input-filter` | 600 | ✅ Present |

---

## Issues Requiring Fixes

---

### 🔴 ISSUE #1 — IMPORTANT: Pipeline.razor — Undefined dual-class pattern

**File:** `Components/Pages/Pipeline.razor`, line 16  
**Code:**
```razor
<MudButton Class="famos-btn-primary famos-btn-primary-sm" OnClick="OpenCreateDialog">
    + New Opportunity
</MudButton>
```

**Problem:** `famos-btn-primary famos-btn-primary-sm` is not a documented pattern in DESIGN-SYSTEM.md. Looking at the CSS:

- `.famos-btn-primary` defines: border-radius, font-size, font-weight, padding — **but NO background-color or border**. MudBlazor's default unstyled button has no fill.
- `.famos-btn-primary-sm` defines: font-size, padding, text-transform, border-radius, font-weight, letter-spacing — also **no background-color or border**.

The result is a button with no fill color and no border — it renders as bare text. The intent appears to be a **small primary button** (filled navy, smaller size). DESIGN-SYSTEM.md lists only `famos-btn-primary` (standard primary) and `famos-btn-outline-sm` (small secondary outlined).

**Required fix — pick one:**

**Option A (preferred):** Add a dedicated `famos-btn-primary-sm` that includes the primary fill color + the size overrides, and update Pipeline.razor to use just that class:
```css
/* In famos.css */
.famos-btn-primary-sm {
    background-color: var(--navy) !important;
    color: #fff !important;
    font-size: 12.5px !important;
    padding: 5px 14px !important;
    /* ... other overrides */
}
```
```razor
<MudButton Class="famos-btn-primary-sm" OnClick="OpenCreateDialog">
```

**Option B:** If the button doesn't need to be smaller, use `famos-btn-primary` alone:
```razor
<MudButton Class="famos-btn-primary" OnClick="OpenCreateDialog">
```

Update DESIGN-SYSTEM.md to document whichever pattern is chosen.

---

### 🟡 ISSUE #2 — IMPORTANT: Dialogs/ — 3 files still use inline MudButton Variant/Color

**Files:**  
- `Components/Dialogs/AddTaskDialog.razor`, line 26  
- `Components/Dialogs/CloseOpportunityDialog.razor`, line 21  
- `Components/Dialogs/OpportunityCreateDialog.razor`, line 17  

**Code examples:**
```razor
<!-- AddTaskDialog.razor -->
<MudButton Variant="Variant.Filled" Color="Color.Primary" ...>Create Task</MudButton>

<!-- CloseOpportunityDialog.razor -->
<MudButton Variant="Variant.Filled" Color="Color.Error" ...>Close Opportunity</MudButton>

<!-- OpportunityCreateDialog.razor -->
<MudButton Variant="Variant.Filled" Color="Color.Primary" ...>Create</MudButton>
```

**Why this matters:** Dialogs/ is a subdirectory of Components/. DESIGN-SYSTEM.md states "No inline Variant/Color/Size on any MudButton in Components/" — full stop, no directory carve-outs. These files were not included in WI902's commit scope, but they are violations that exist in the codebase post-migration.

These should be added to WI902 scope or a new WI raised before this sprint closes. The `CloseOpportunityDialog` button (`Color.Error`) maps directly to `famos-btn-danger`. The two `Color.Primary` / `Variant.Filled` buttons map to `famos-btn-primary`.

**Required fix:**
```razor
<!-- AddTaskDialog.razor -->
<MudButton Class="famos-btn-primary" OnClick="Submit" Disabled="...">Create Task</MudButton>

<!-- CloseOpportunityDialog.razor -->
<MudButton Class="famos-btn-danger" OnClick="Submit" Disabled="...">Close Opportunity</MudButton>

<!-- OpportunityCreateDialog.razor -->
<MudButton Class="famos-btn-primary" OnClick="Submit" Disabled="...">Create</MudButton>
```

---

## Notes (Non-blocking)

**famos-btn-primary CSS completeness:** The `.famos-btn-primary` class does not define `background-color` or `color` — it relies on MudBlazor's `Variant.Filled` + `Color.Primary` defaults being applied by MudBlazor's internal theming. Since there's no `Variant=` on the buttons now, verify that the navy fill is actually rendering in the UI. If MudBlazor requires `Variant` to apply background, these buttons may be rendering as unstyled. This may be the root cause behind the dual-class attempt in Pipeline.razor. **Tony should confirm visual rendering before this is marked done.**

---

## Design System Checklist Enforcement

| Rule | Status |
|------|--------|
| No `Icons.Material.*` in any Components/ razor file | ✅ CLEAN |
| No inline `Variant/Color/Size` on MudButton in Components/Pages/ and Panels/ | ✅ CLEAN |
| No inline `Variant/Color/Size` on MudButton in Components/Dialogs/ | ❌ 3 violations |
| No inline `Style="width:..."` on MudTextField | ✅ CLEAN |
| All icons use `FamosIcons.*` | ✅ CLEAN |

---

## Verdict

**NEEDS-CHANGES**

**Send back to Tony with:**
1. Fix Pipeline.razor dual-class pattern (Issue #1) — define `famos-btn-primary-sm` properly with fill color, update to single class, update DESIGN-SYSTEM.md
2. Fix Dialogs/ inline Variant/Color violations (Issue #2) — 3 files, straightforward class substitutions
3. Tony to visually confirm `.famos-btn-primary` buttons are rendering with navy fill (no Variant= needed check)

Cycle 2 should be a quick pass once these three items are addressed. No architectural concerns.
