# Review Report — ADO#3117: Chat UI v1 Parity
## Cycle 2 Fast-Verify
**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `e80239f4`  
**Date:** 2026-05-09  
**Verdict:** ❌ NEEDS-CHANGES

---

## Summary

6 of 8 C1 findings are fixed. 2 remain open.

---

## Finding-by-Finding Results

### Finding 1: `.chat-empty-state` padding
✅ **FIXED**  
`padding: 3rem 2rem` → `padding: var(--space-12) var(--space-8)`

---

### Finding 2: `.chat-pill-icon` — no `!important`, uses tokens, specificity fix
✅ **FIXED**  
- `!important` removed from all properties  
- `font-size: var(--text-lg)`, `width: var(--space-4)`, `height: var(--space-4)`  
- Selector expanded to `.chat-pill-icon, .mud-chip .chat-pill-icon`

---

### Finding 3: `.chat-send-btn` — width/height use token
✅ **FIXED**  
`width: 40px; height: 40px` → `width: var(--space-10); height: var(--space-10)`

---

### Finding 4: `.chat-input-field` — min-height and max-height tokens
✅ **FIXED**  
`min-height: var(--space-10)`, `max-height: var(--chat-input-max-height)`  
`--chat-input-max-height: 200px` defined in `:root`

---

### Finding 5: `.message-bubble.message-user` — corner radius direction
✅ **FIXED**  
`border-top-left-radius` → `border-top-right-radius`

---

### Finding 6: No hardcoded hex colors in chat CSS or ChatView.razor
❌ **STILL FAILING**

`#7c83ff` is absent (was already gone) ✅ and `ChatView.razor` is clean ✅, but the following hardcoded hex values remain in chat-section selectors in `fortress.css`:

| Selector | Property | Value |
|----------|----------|-------|
| `.chat-kb-toggle.active` | `color` | `#fff` |
| `.chat-kb-toggle.active` | `background` | `var(--accent-blue, #2196F3)` (hex fallback) |
| `.chat-kb-toggle.active` | `border-color` | `var(--accent-blue, #2196F3)` (hex fallback) |
| `.jump-to-bottom` | `color` | `#d4af37` |
| `.jump-to-bottom:hover` | `color` | `#e8c84a` |

**Required fixes:**
- Replace `#fff` with `var(--color-white)` or `var(--surface-primary)` (whichever is appropriate)
- Move `#2196F3` to a `:root` variable (e.g. `--accent-blue: #2196F3`) and remove inline fallbacks
- Replace `#d4af37` and `#e8c84a` with appropriate design tokens (gold accent — define `--color-gold` and `--color-gold-hover` if not already present)

---

### Finding 7: `--chat-content-max-width: 900px` in `:root`, all occurrences replaced
❌ **STILL FAILING**

`:root` variable is defined ✅ and 3 occurrences were replaced ✅, but **2 hardcoded `max-width: 900px` remain**:

| Line (approx) | Selector |
|---------------|----------|
| ~901 | `.message` |
| ~1075 | `.chat-input-wrapper` |

**Required fix:** Replace both remaining `max-width: 900px` literals with `var(--chat-content-max-width)`.

---

### Finding 8: `--font-weight-light: 300` in `:root`, used in `.chat-streaming-cursor`
✅ **FIXED**  
`:root` defines `--font-weight-light: 300`, `.chat-streaming-cursor` uses `font-weight: var(--font-weight-light)`

---

## Required Changes (C3 scope — targeted only)

1. **`fortress.css` — Finding 6:** Replace 5 hardcoded hex values in chat KB toggle and jump-to-bottom selectors with tokens
2. **`fortress.css` — Finding 7:** Replace 2 remaining `max-width: 900px` in `.message` and `.chat-input-wrapper` with `var(--chat-content-max-width)`

**No other changes in scope. Do NOT fix anything outside these two findings.**

---

*Hawkeye out. Fix these two and bring it back for C3.*
