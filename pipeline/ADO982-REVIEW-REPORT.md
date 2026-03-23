# Review Report: ADO #982 — Branding Accent Shift (sky-blue → TIG red)

**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `f86c536`  
**Cycle:** 1  
**Date:** 2026-03-20  
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Checklist

| # | Check | Result |
|---|-------|--------|
| 1 | `FipTheme.cs` Secondary = `#C0272D` | ✅ PASS |
| 2 | `FipTheme.cs` DrawerIcon = `#C0272D` | ✅ PASS |
| 3 | Zero `#0090d0` in both files | ✅ PASS |
| 4 | `#DC2626` (error red) untouched | ✅ PASS |
| 5 | `#002050` (navy) untouched | ✅ PASS |
| 6 | Scope clean — only 2 files | ✅ PASS |

---

## What Was Changed (f86c536)

**`FipTheme.cs`** (2 lines):
- `Secondary` → `#C0272D` ✅
- `DrawerIcon` → `#C0272D` ✅

**`famos.css`** (6 lines):
- `.kpi-sky::before` background → `#C0272D` ✅
- `.famos-kcard:hover` border-color → `#C0272D` ✅
- `.famos-pill-binding` color + background → `#C0272D` / `#fde8e8` ✅
- `.famos-nav-item.active` border-left-color → `#C0272D` ✅
- `.famos-nav-badge` background → `#C0272D` ✅
- `.famos-topbar-avatar` gradient first stop → `#C0272D` ⚠️ (see finding below)

---

## Findings

### 🔶 IMPORTANT — Incomplete Gradient Migration (famos.css line 540)

**File:** `famos/src/FamOs.Web/wwwroot/css/famos.css`  
**Line:** 540  

```css
/* CURRENT (broken) */
background: linear-gradient(135deg, #C0272D, #10b0f0);

/* EXPECTED */
background: linear-gradient(135deg, #C0272D, #8B1B20);  /* or solid #C0272D */
```

The `.famos-topbar-avatar` gradient had its **first stop** migrated from `#0090d0` → `#C0272D`, but the **second stop `#10b0f0`** — a sky-blue tint — was left untouched. The result is a red-to-sky-blue gradient on the user avatar, which is visually inconsistent and retains the old palette.

**Options:**
1. Replace second stop with a TIG red tint (e.g. `#8B1B20` or `#E03030`)
2. Use solid `#C0272D` (no gradient)
3. Use a navy-to-red gradient matching brand: `linear-gradient(135deg, #002050, #C0272D)`

---

### 📝 NOTE — Pre-existing RGBA sky-blue references (out of scope for this WI)

Multiple pre-existing `rgba(0,144,208,...)` references remain in `famos.css`. These were **not introduced or modified by this commit** and are outside the stated scope of ADO #982 (which targeted `#0090d0` hex replacements). Logging for awareness; recommend a follow-up WI.

**Locations:**
- Line 184: `box-shadow: 0 4px 12px rgba(0,144,208,0.10)` (.famos-kcard hover shadow)
- Line 277: `background: rgba(0,144,208,0.13)` (.famos-nav-item.active bg)
- Line 728: `background: rgba(0, 144, 208, 0.12)` 
- Line 780: `rgba(0,144,208,0.05)` (.famos-owner-row--selected)
- Line 857: `rgba(0,144,208,0.05)` (.famos-quote-row--selected)
- Line 873: `rgba(0,144,208,0.03)`

Also: `--sky` CSS variable is used in ~10 selectors (lines 715–873) and is not defined in `famos.css` or `fip-tokens.css` — appears to be an inherited/upstream token that was never resolved. Follow-up recommended.

---

## Required Fix

**1 change required before PASS:**

In `famos/src/FamOs.Web/wwwroot/css/famos.css`, line 540:

```css
/* Replace: */
background: linear-gradient(135deg, #C0272D, #10b0f0);

/* With (recommended — navy-to-red brand gradient): */
background: linear-gradient(135deg, #002050, #C0272D);
```

---

## Summary

The core migration is solid — all 8 hex `#0090d0` occurrences were replaced, protected colors (`#DC2626`, `#002050`) are untouched, and scope is clean (2 files only). One incomplete gradient migration slipped through: the second stop `#10b0f0` on `.famos-topbar-avatar`. Fix that one line and this clears review.

**Verdict: NEEDS-CHANGES** (1 Important issue — easy fix, single line)
