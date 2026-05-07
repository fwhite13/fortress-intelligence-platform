# Review Report — ADO#2849

**WI:** FAIT v2: Dual-pane layout - artifact preview panel, resize handle, auto-collapse responsive  
**Reviewer:** Hawkeye (Clint Barton)  
**Review cycle:** 1 of 2  
**Commit:** `fe5530d`  
**Date:** 2026-05-06  

---

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC Sonnet ran full adversarial review against all three changed files plus `_Imports.razor`. Build verified clean (0 errors, 0 warnings). CC confirmed all critical structural checks pass. One Important violation found that must be fixed before PASS: hardcoded px values in an inline `Style=` on the close button's `MudIcon`. No false positives in the CC output — all findings are real.

---

### Spec Compliance Check

**§2 Codebase Map:**
- `Components/Layout/DualPaneLayout.razor` — ✅ created as specified
- `Components/Pages/Dashboard.razor` — ✅ modified as specified  
- `wwwroot/css/app.css` — ✅ appended (dual-pane block at lines 344–465)

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected

**§7 Acceptance Criteria:**
- [x] `DualPaneLayout.razor` in `Components/Layout/` — ✅ verified
- [x] `ChatContent`/`PreviewContent` as `RenderFragment?` — ✅ verified
- [x] `@bind-IsPanelOpen` two-way binding — ✅ `IsPanelOpenChanged` present, `ClosePanel()` invokes it correctly
- [x] Resize handle + TODO comment — ✅ present; no unused fields
- [x] CSS appended to `app.css` — ✅ confirmed
- [x] CSS uses CSS variables — ❌ **ONE VIOLATION** (see I1 below)
- [x] `@media (max-width: 1024px)` collapses pane and handle — ✅ verified
- [x] Dashboard uses `DualPaneLayout @bind-IsPanelOpen` — ✅ verified
- [x] Existing welcome content preserved in `<ChatContent>` — ✅ verified
- [x] Build: 0 errors, 0 warnings — ✅ confirmed

**Spec compliance verdict:** ⚠️ CONDITIONAL — one AC item (all CSS values use CSS variables) is violated by the inline `Style=` prop in the Razor component. Fix required.

---

### Consistency Audit

**Files Cross-Referenced:**
- `DualPaneLayout.razor` ↔ `app.css` — ✅ all class names (`dual-pane-container`, `panel-closed`, `panel-open`, `dual-pane-handle`, `dual-pane-preview`, `dual-pane-preview-empty`, `dual-pane-close-btn`) match between component and CSS
- `Dashboard.razor` ↔ `DualPaneLayout.razor` — ✅ `@bind-IsPanelOpen` wires to `IsPanelOpenChanged` correctly; `PreviewTitle` param passed through
- `_Imports.razor` ↔ `DualPaneLayout.razor` — `@using MudBlazor` is declared globally in `_Imports.razor`; `DualPaneLayout.razor` repeats it (see N2)

**Grid state machine verified:**
- Open state (`panel-open` class): base `.dual-pane-container` rule → `var(--chat-pane-width, 55%) 4px 1fr` ✅
- Closed state (`panel-closed` class): override → `1fr 0 0` ✅
- Media query: covers both states via `.dual-pane-container, .dual-pane-container.panel-open` with `!important` ✅

---

### Important Issues — 1

#### I1: Hardcoded px values in MudIcon `Style=` prop
- **File:** `Components/Layout/DualPaneLayout.razor` line 28
- **Category:** CSS variable compliance
- **Issue:** `<MudIcon>` uses `Style="width: 16px; height: 16px;"`. No CSS class exists in the dual-pane block to size this icon via tokens. This is the same category of violation as hardcoded px values in CSS — it's just inside a Razor attribute rather than a stylesheet.
- **Evidence:**
  ```razor
  <MudIcon Icon="@Icons.Material.Filled.Close" Style="width: 16px; height: 16px;" />
  ```
  No `.dual-pane-close-btn-icon` or equivalent class found in `app.css` dual-pane block.
- **Impact:** Icon size bypasses the design token system. If `--icon-sm` or similar tokens are introduced, this won't track.
- **Fix:**

  In `app.css` dual-pane block, add after `.dual-pane-close-btn:hover`:
  ```css
  .dual-pane-close-btn svg {
      width: var(--icon-sm, 16px);
      height: var(--icon-sm, 16px);
  }
  ```
  Then in `DualPaneLayout.razor` line 28, remove the `Style=` prop entirely:
  ```diff
  - <MudIcon Icon="@Icons.Material.Filled.Close" Style="width: 16px; height: 16px;" />
  + <MudIcon Icon="@Icons.Material.Filled.Close" />
  ```
  The CSS rule targets the rendered `<svg>` inside the button. The fallback `16px` preserves current behavior if `--icon-sm` isn't yet defined.

---

### Nitpicks — 2

- **N1:** `.dual-pane-preview.hidden` (`display: none`) is redundant when `.panel-closed` already collapses the grid column to `0`. Belt-and-suspenders behavior — defensive and harmless. Won't ask you to remove it. (`DualPaneLayout.razor` lines 30–32 + `app.css`)

- **N2:** `@using MudBlazor` on line 3 of `DualPaneLayout.razor` is already covered globally by `_Imports.razor` line 16. Remove the duplicate to keep the component lean. Not blocking.

---

### Positive Observations

- The `panel-closed → 1fr 0 0` grid collapse is clean. No JavaScript needed to hide the pane; pure CSS.
- The media query correctly covers `panel-open` with `!important` — this is exactly the right tool here and it's not overused anywhere else.
- `_isResizing` field correctly excluded (Tony's call noted in build report; CC confirmed no unused fields = 0 warnings).
- `--chat-pane-width` passthrough via inline custom property is a smart pattern for the resize stub — easy to wire JS to in Sprint 2.
- Empty state uses a CSS class (`dual-pane-preview-empty`), not inline style — correct call even though the spec example had inline.

---

### What to Fix (NEEDS-CHANGES → PASS)

**One fix required:**

1. **`DualPaneLayout.razor` line 28** — Remove `Style="width: 16px; height: 16px;"` from the `MudIcon`.  
   **`app.css` dual-pane block** — Add `.dual-pane-close-btn svg { width: var(--icon-sm, 16px); height: var(--icon-sm, 16px); }` after the `.dual-pane-close-btn:hover` rule.

That's it. Everything else is clean. Fix that one item and this ships.

---

_Hawkeye out._
