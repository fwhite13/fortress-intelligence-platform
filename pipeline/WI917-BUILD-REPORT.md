# BUILD REPORT: WI#917 — FAM OS navy-on-navy invisible buttons

**Date:** 2026-03-20
**Branch:** main
**Scope:** `famos/src/FamOs.Web/` only

## Summary

Fixed 3 bare `<MudButton>` Cancel buttons in dialog components by adding `Class="famos-btn-outline"`. Full audit confirmed no additional bare MudButtons exist in Components/ or Pages/.

## Files Changed

| File | Line | Change |
|------|------|--------|
| `Components/Dialogs/AddTaskDialog.razor` | 25 | Added `Class="famos-btn-outline"` to Cancel button |
| `Components/Dialogs/CloseOpportunityDialog.razor` | 25 | Added `Class="famos-btn-outline"` to Cancel button |
| `Components/Dialogs/OpportunityCreateDialog.razor` | 16 | Added `Class="famos-btn-outline"` to Cancel button |

## Audit Results

**Components/ grep (bare MudButton, no Class/Style/Variant):**
- Result: 3 matches — exactly the 3 known Cancel buttons. All fixed.

**Pages/ grep:**
- Result: No matches (no Pages/ subdirectory with bare MudButtons).

**Total bare MudButtons fixed: 3**

## Self-Review Checklist

- [x] All 3 known bare Cancel buttons fixed
- [x] Full audit grep run — no additional bare MudButtons found
- [x] No files outside `famos/src/FamOs.Web/` modified
- [x] No new CSS classes added — used existing `famos-btn-outline` class only
