# Review Report: ADO#3117 — Chat UI v1 Parity
**Reviewer:** Hawkeye (Clint Barton)  
**Commit:** `e0f39553`  
**Cycle:** 1  
**Date:** 2026-05-09  
**CC Invocation:** `cat pipeline/review-brief-3117-3118.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Verdict: NEEDS-CHANGES

---

## Files Reviewed
- `src/FortressAI.V2.Web/wwwroot/css/fortress.css`
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

---

## Issues Found

### Critical
None.

### Important

**1. `.chat-empty-state` — hardcoded `rem` padding**
- **Location:** `fortress.css`, `.chat-empty-state`
- **Issue:** `padding: 3rem 2rem` — hardcoded rem values violate CSS variable rule
- **Fix:** Use `var(--space-12) var(--space-8)` (tokens exist for 48px/32px)

**2. `.chat-pill-icon` — hardcoded values + `!important`**
- **Location:** `fortress.css`, `.chat-pill-icon`
- **Issue:** `font-size: 1rem !important; width: 16px !important; height: 16px !important`
- **`!important` indicates a specificity fight** — resolve at selector level instead
- **Fix:** Use `var(--text-lg)` for font-size, `var(--space-4)` for 16px dimensions; remove `!important`

**3. `.chat-send-btn` — hardcoded `40px` dimensions**
- **Location:** `fortress.css`, `.chat-send-btn`
- **Issue:** `width: 40px; height: 40px` — hardcoded
- **Fix:** `var(--space-10)` is exactly 40px and exists

**4. `.chat-input-field` — hardcoded `min-height` and `max-height`**
- **Location:** `fortress.css`, `.chat-input-field`
- **Issue:** `min-height: 40px; max-height: 200px` — both hardcoded
- **Fix:** `min-height: var(--space-10)`; for `max-height: 200px`, define a chat-specific layout variable (e.g., `--chat-input-max-height: 200px`)

**5. `.message-bubble.message-user` — inverted corner radius (wrong side)**
- **Location:** `fortress.css`, `.message-bubble.message-user`
- **Issue:** Bubble is `align-self: flex-end` (right-aligned) but overrides `border-top-left-radius: var(--radius-sm)`. For a right-aligned user bubble, the "tail" corner should be `border-top-right-radius`, not `border-top-left-radius`. The notch is on the wrong side.
- **Fix:** Change to `border-top-right-radius: var(--radius-sm)` (or `var(--radius-xs)` if tighter)

**6. `#7c83ff` → `var(--color-accent)` — color change needs confirmation**
- **Location:** `ChatView.razor`, hover color for `.chat-run-as-task-btn`
- **Issue:** `#7c83ff` is blue-purple; `--color-accent` resolves to gold (`#d4af37`). This is not like-for-like — it's a brand color change. May be intentional for v1 parity, but must be confirmed.
- **Fix:** If intentional, mark as confirmed. If not, use a proper blue-purple CSS variable or define one.

### Nitpick

**7. Repeated `max-width: 900px` — should be a named variable**
- **Locations:** `.chat-streaming-indicator`, `.chat-artifact-progress`, `.message-bubble` (3 places)
- **Issue:** Hardcoded in 3 places; future layout changes require touching all 3
- **Fix:** Define `--chat-content-max-width: 900px` and reference it

**8. `.chat-streaming-cursor` — hardcoded `font-weight: 300`**
- **Issue:** No `--font-light` token defined; minor but noted
- **Fix:** Add token or accept as-is with comment

---

## CC Analysis Notes
- No hardcoded hex colors found in new CSS classes ✓
- The three hex replacements in ChatView.razor (`#444` → `var(--color-text-muted)`, `#999` → `var(--color-text-placeholder)`) are correct ✓
- Hardcoded *dimensional* values (px, rem) are the primary compliance gap

---

## Return to Tony
Fix items 1–6 (Important). Items 7–8 (Nitpick) can be fixed in same pass.

Specific fixes required:
1. Replace `3rem 2rem` with `var(--space-12) var(--space-8)` in `.chat-empty-state`
2. Remove `!important` from `.chat-pill-icon`; use space tokens for dimensions
3. Replace `40px` with `var(--space-10)` in `.chat-send-btn`
4. Replace `40px` with `var(--space-10)` in `.chat-input-field min-height`; define/use `--chat-input-max-height` for 200px
5. Change `.message-bubble.message-user` override to `border-top-right-radius` (not left)
6. Confirm `#7c83ff` → `var(--color-accent)` substitution is intentional; if not, restore correct variable
