# Review Report: WI887 — FAM OS Sprint 3 UI/UX Restyling
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`
**Commit:** `237342526aaa109a2311caeb32622eca2c08fd99`
**Review Cycle:** 1
**Date:** 2026-03-19
**Review Method:** Manual file-by-file inspection + Claude Code CLI (`cat /tmp/review-wi887-c1.md | claude --model sonnet -p`)

---

## 🟡 Verdict: NEEDS-CHANGES

Pure CSS/theme sprint — no functional regressions found. Two **Important** spec-compliance issues and four **Nitpicks**. No Critical findings.

---

## Regression Check Results

| Check | Result |
|-------|--------|
| `@namespace FamOs.Web.Components.Pages` on OpportunityWorkspace.razor | ✅ Present |
| `@namespace FamOs.Web.Components.Panels` on all 7 Panel files | ✅ All intact |
| `@using FamOs.Web.Domain` NOT duplicated in individual page files | ✅ Clean — only in `_Imports.razor` |
| `MudDialogInstance` (not `IMudDialogInstance`) in dialog files | ✅ Correct in both dialogs |
| `_Imports.razor` has `@using` for Dialogs, Panels, Shared | ✅ All present |
| `GoToPipeline()` named method in Dashboard.razor (WI872 fix) | ✅ Present |
| `NavigateToOpportunity()` named method in OpportunityCard.razor | ✅ Named method, not inline lambda |

**No WI870 or WI872 regressions introduced.** ✅

---

## File-by-File Findings

### 1. `App.razor`
✅ Google Fonts link present and correct — both `Plus+Jakarta+Sans` (300;400;500;600;700;800) and `Fraunces` (wght@600;700;800) loaded in single request with `display=swap`.

### 2. `MainLayout.razor`
✅ Sidebar 262px set via `FipTheme.cs` `DrawerWidthLeft = "262px"` — correct approach (theme, not inline).
✅ Topbar height 54px via `AppbarHeight = "54px"` in theme; `padding-top: 54px` on `MudMainContent` matches.
✅ Content padding `24px 28px` on inner div — matches mockup spec.
⚠️ **Nitpick [N-1]:** Drawer footer user-info block uses fully inline styles. `famos.css` already defines `.fip-drawer-footer`; this block should use it.
⚠️ **Nitpick [N-2]:** `MudDrawerHeader` has `background: white` inline (white logo area over navy nav). Verify intentional per mockup — if so, consider a named class (`.famos-drawer-header`).

### 3. `NavMenu.razor`
✅ All nav items use `.famos-nav-item`, `.famos-nav-item--active`, `.famos-nav-icon`, `.famos-nav-badge` — no inline colors on interactive elements.
✅ Section labels use CSS class-driven nav items.
❗ **Important [I-1]:** Section label divs (`"Main"`, `"Coming Soon"`) and the horizontal divider `<div>` use fully inline `style=` with hardcoded font sizes, colors (`rgba(255,255,255,0.3)`), spacing, etc. The spec requires no hardcoded colors inline — colors should come from famos.css classes. No `.famos-nav-section-label` class exists in famos.css. These need to be extracted to a CSS class.

### 4. `Dashboard.razor`
✅ `famos-page-h2` class used for heading (Fraunces via CSS).
✅ Uses `<StatCard>` components with `AccentClass` variants (`kpi-navy`, `kpi-red`, `kpi-amber`, `kpi-green`).
✅ `GoToPipeline()` named method preserved (WI872 fix intact).
⚠️ **Nitpick [N-3]:** `@using FamOs.Web.Services` declared locally — already covered by `_Imports.razor`. Harmless but redundant.

### 5. `Pipeline.razor`
✅ `GetStageColor(LifecycleStage)` method present, correctly maps all 7 stages + default to hex colors.
✅ Column headers use `.famos-kcol-dot` (colored via inline `style="background: @GetStageColor(...)"` — correct since color is data-driven), `.famos-kcol-label`, `.famos-kcol-count`.
✅ Count badge uses `.famos-kcol-count` class.
⚠️ **Nitpick [N-3]:** `@using FamOs.Web.Services` declared locally — same as Dashboard.razor. Already in `_Imports.razor`.

### 6. `OpportunityWorkspace.razor`
✅ `@namespace FamOs.Web.Components.Pages` present (WI870 regression guard — intact).
✅ `famos-page-header famos-page-header-row` applied to page header div.
✅ Status pill: `<span class="famos-status-pill @GetStagePillClass(...)">` — all lifecycle stages mapped.
✅ `GetStagePillClass()` covers all 8 stages including `ClosedNotBound` + default.
✅ No `@using FamOs.Web.Domain` duplication.

### 7. `OpportunityCard.razor`
✅ Renders `<div class="famos-kcard">` — no `MudCard` wrapper. Correct.
✅ `NavigateToOpportunity()` is a named `@code` method (not inline lambda). WI872 pattern preserved.
✅ Uses `<SignalChip>` component in footer.

### 8. `StatCard.razor`
✅ `@namespace FamOs.Web.Components.Shared` present.
✅ Left-border accent works via `.famos-kpi-card::before` + modifier class (`.kpi-navy`, `.kpi-sky`, etc.) in famos.css.
✅ Fraunces number rendered via `.famos-kpi-value` CSS class.
✅ Supports Label, Value, Sub, Trend, AccentClass, TrendClass parameters.
❗ **Important [I-2]:** Spec says "Color parameter" — component has `AccentClass` parameter instead. Functionally fine (arguably better), but diverges from spec. Additionally, the root CSS class is `.famos-kpi-card` (not `.famos-stat-card` as spec requires). If any other component references `.famos-stat-card`, it won't find it.
⚠️ **Nitpick [N-4]:** Consider whether `AccentClass` vs `Color` matters for the spec. If spec is authoritative, rename or add a `Color`-named alias. Low priority given functional equivalence.

### 9. `FipTheme.cs`
✅ `Primary = "#002050"` (navy) — sidebar, appbar, primary buttons.
✅ `Secondary = "#0090d0"` (sky-blue) — active states, highlights.
✅ `DrawerBackground = "#002050"`, `AppbarBackground = "#002050"` — consistent.
✅ `DrawerWidthLeft = "262px"`, `AppbarHeight = "54px"`.
✅ Typography: Fraunces on H1–H4; Plus Jakarta Sans on body, button.
✅ `Button.TextTransform = "none"` — correct (no uppercase buttons).
✅ Minimal shadow system — elevation[5] has sky-blue tint for card hover.
✅ Only `.cs` file modified — no business logic touched.

### 10. `famos.css`
✅ `.famos-nav-item` — present, with hover/active/disabled states.
✅ `.famos-kcard` — present, with hover (sky border + shadow).
✅ `.famos-signal-chip` — present, with all signal variant classes.
✅ `.famos-status-pill` — present, with all stage variant classes.
✅ `.famos-pipeline-board`, `.famos-pipeline-column`, `.famos-pipeline-column-header` — present.
✅ `.famos-page-header`, `.famos-page-h2`, `.famos-page-sub` — present.
✅ `.famos-kpi-grid` with responsive media queries.
❗ **Important [I-2] (same as StatCard):** Class is `.famos-kpi-card` not `.famos-stat-card`. Spec required `.famos-stat-card`. The two are equivalent in this codebase but the name diverges from spec.
✅ Responsive breakpoints at 960px (2-col KPI) and 600px (2-col, smaller heading).
✅ MudBlazor overrides: `.mud-drawer`, `.mud-nav-menu`, `.mud-card` — all scoped correctly.

---

## Issue Summary

### Critical — None ✅

### Important

| ID | File | Issue |
|----|------|-------|
| I-1 | `NavMenu.razor` | Section label divs (`Main`, `Coming Soon`) + horizontal divider use inline `style=` with hardcoded colors/fonts. Spec requires CSS classes from famos.css. No `.famos-nav-section-label` class exists. Extract to CSS class. |
| I-2 | `StatCard.razor` + `famos.css` | CSS class is `.famos-kpi-card` not `.famos-stat-card` (spec required). Parameter is `AccentClass` not `Color` (spec language). Internally consistent but diverges from spec naming. Add `.famos-stat-card` alias or rename. |

### Nitpick

| ID | File | Issue |
|----|------|-------|
| N-1 | `MainLayout.razor` | Drawer footer user-info block uses inline styles; should use `.fip-drawer-footer` class already defined in famos.css. |
| N-2 | `MainLayout.razor` | `MudDrawerHeader background: white` inline — if intentional per mockup, consider extracting to `.famos-drawer-header` class. |
| N-3 | `Dashboard.razor`, `Pipeline.razor` | `@using FamOs.Web.Services` redundant in both files (already in `_Imports.razor`). Harmless but noisy. |
| N-4 | `StatCard.razor` | `AccentClass` parameter name vs spec's "Color parameter" language. Low priority since it works. |

---

## Required Fixes (NEEDS-CHANGES)

Tony needs to fix the **Important** issues before PASS:

1. **NavMenu.razor — I-1:** Add `.famos-nav-section-label` and `.famos-nav-divider` classes to `famos.css`. Replace the two inline-styled section label `<div>`s and the divider `<div>` in NavMenu.razor with these classes. No hardcoded colors inline.

2. **famos.css + StatCard.razor — I-2:** Either:
   - Rename `.famos-kpi-card` → `.famos-stat-card` everywhere (razor + css), OR
   - Add `.famos-stat-card` as an alias in famos.css alongside `.famos-kpi-card`
   
   Whichever is chosen, update consistently across StatCard.razor and famos.css. This is a spec naming compliance fix.

**Nitpicks** may be addressed at Tony's discretion — they won't block PASS.

---

## What Went Well

- Zero functional regressions. All WI870/WI872 guards intact.
- FipTheme.cs is clean, well-commented, internally consistent.
- famos.css is comprehensive — 406 lines of well-structured Sprint 3 styles.
- OpportunityCard, Pipeline, OpportunityWorkspace all cleanly restyle without touching domain logic.
- StatCard is a solid new component — namespace, parameters, documentation all correct.
- No `.cs` files touched outside FipTheme.cs.

---

*Hawkeye out. Two fixes needed. Send it back to Stark.*
