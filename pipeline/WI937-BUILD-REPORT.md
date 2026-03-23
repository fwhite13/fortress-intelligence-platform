# Build Report: WI937 — FAM OS CSS Button Regressions

**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-20  
**Commit:** d7dc8d5  
**Branch:** main  
**Status:** ✅ COMPLETE — pushed

---

## Task
Fix 3 CSS button regressions in FAM OS blocking the Steve demo.

## File Modified
`famos/src/FamOs.Web/wwwroot/css/famos.css`

## Changes Applied (via Claude Code CLI)

### Fix 1 & 2 — `.mud-button-label` color override (Bugs 1 + 2)
MudBlazor renders button text inside a `.mud-button-label` span that was winning specificity over our `color: white` in `.famos-btn-primary` and `.famos-btn-primary-sm`. Added nested selectors after each block:

```css
.famos-btn-primary .mud-button-label { color: white !important; }   /* line 338 */
.famos-btn-primary-sm .mud-button-label { color: white !important; } /* line 363 */
```

Covers: "+ New Opportunity" button (Bug 1) and "Create" / "Route to Market" buttons (Bug 2).

### Fix 3 — `famos-btn-danger` height alignment (Bug 3)
Removed `height: 28px !important;` from `.famos-btn-danger` and changed padding to `5px 12px !important;` to use padding-driven height matching `famos-btn-outline-sm`.

```
REMOVED: height: 28px !important;
CHANGED: padding → 5px 12px !important;
```

Covers: "Close" button height mismatch vs "Assign Owner" / "Park" buttons.

## Verification
```
grep -n "mud-button-label|height: 28px|famos-btn-danger|padding: 5px 12px"
→ line 338: .famos-btn-primary .mud-button-label { color: white !important; }
→ line 363: .famos-btn-primary-sm .mud-button-label { color: white !important; }
→ line 582: .famos-btn-danger {
→ line 589: padding: 5px 12px !important;
→ NO height: 28px in famos-btn-danger ✅
```

## Self-Review Checklist
- [x] Only `famos.css` modified — no Razor files touched
- [x] 3 insertions, 2 deletions — scoped exactly as specified
- [x] All 3 bugs addressed
- [x] Committed and pushed to origin/main
- [x] CC invocation used for all code changes

---

*Ready for Clint.*
