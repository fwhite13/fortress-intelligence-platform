# Review Report — ADO #1785 — FIRM: Summary Markdown Renderer

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `9b44e90`
**Cycle:** 1
**Date:** 2026-04-13
**Risk:** Low-Medium (new NuGet package + UI change)

---

## Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**What was specified:**
- Markdig added to csproj ✅
- `@using Markdig` in `_Imports.razor` ✅
- `MeetingDetail.razor` — Summary tab replaced with `RenderMarkdown(SummaryText)` helper; old parsed sections removed; legacy JSON fallback retained ✅
- `MeetingDetail.razor.css` — new file with `.firm-summary-markdown` styles ✅

**Spec compliance verdict:** ✅ COMPLIANT — all specified files modified as described. No out-of-scope changes detected.

---

## CC Review Summary

Used Claude Code Sonnet for adversarial analysis. CC read all four changed files in full and evaluated all six review focus areas. CC confirmed two critical/important issues and surfaced one additional edge case. One false positive in the briefing (Markdig version concern) was resolved — 1.1.2 IS the current stable release. CC output directly informed the findings below.

---

## Consistency Audit

| Check | Files | Result |
|-------|-------|--------|
| `@using Markdig` → `MarkdownPipelineBuilder` / `Markdown.ToHtml()` in razor | `_Imports.razor` ↔ `MeetingDetail.razor` | ✅ Resolves correctly |
| `.firm-summary-markdown` class in razor div ↔ CSS file | `MeetingDetail.razor` ↔ `MeetingDetail.razor.css` | ✅ Name matches |
| Scoped CSS penetration of MarkupString | `MeetingDetail.razor.css` child selectors | ❌ `::deep` absent — dead CSS |

---

## Critical Issues — 1

### C1: Scoped CSS Does Not Apply to MarkupString — All Child Styles Are Dead Code

- **File:** `Components/Pages/MeetingDetail.razor.css` (entire file, lines 7–81)
- **Category:** Correctness / Blazor scoped CSS behavior
- **Issue:** All child element selectors (`.firm-summary-markdown h1`, `.firm-summary-markdown p`, `.firm-summary-markdown ul`, etc.) are in a Blazor component-scoped CSS file **without `::deep`**. Blazor compiles these to `.firm-summary-markdown h1[b-xxxx]`. The `[b-xxxx]` scope attribute is only stamped on Razor-authored DOM elements. The `@RenderMarkdown()` output is a `MarkupString` — a raw HTML blob injected directly into the DOM, bypassing the Blazor render tree. None of the `<h1>`, `<p>`, `<ul>`, `<li>`, `<table>`, `<code>`, `<pre>`, or `<blockquote>` elements inside it carry the scope attribute. Every child rule silently fails to match.
- **Evidence:**
  ```css
  /* These compile to .firm-summary-markdown h1[b-xxxx] — never matches MarkupString content */
  .firm-summary-markdown h1,
  .firm-summary-markdown h2,
  .firm-summary-markdown h3 {
      color: var(--color-gold);
      ...
  }
  ```
  Only the wrapper `<div class="firm-summary-markdown">` is Razor-authored, so only the base `.firm-summary-markdown { color; line-height; }` rule applies. 80 lines of styling for headings, lists, tables, code, blockquotes are dead code.
- **Impact:** Rendered markdown will display with unstyled browser defaults — serif font headings, no gold color, no list indentation, no table borders, no code block backgrounds. Looks broken in production.
- **Fix — Option A (preferred):** Add `::deep` to every child selector in the scoped CSS file:
  ```css
  .firm-summary-markdown ::deep h1,
  .firm-summary-markdown ::deep h2,
  .firm-summary-markdown ::deep h3 {
      color: var(--color-gold);
      ...
  }
  .firm-summary-markdown ::deep p { ... }
  .firm-summary-markdown ::deep ul,
  .firm-summary-markdown ::deep ol { ... }
  /* etc. for all child selectors */
  ```
- **Fix — Option B:** Move the entire `.firm-summary-markdown` ruleset to `wwwroot/css/app.css` or a global stylesheet. Global CSS is not scoped and applies to dynamically injected HTML without issue. This is the simpler fix but pollutes the global scope slightly.

---

## Important Issues — 1

### I1: `RenderMarkdown` Pipeline Missing `DisableHtml()` — Defense-in-Depth Gap

- **File:** `Components/Pages/MeetingDetail.razor` (~line 414–422)
- **Category:** Security / XSS defense-in-depth
- **Issue:** `UseAdvancedExtensions()` does NOT disable raw HTML passthrough. Markdig's default behavior passes raw HTML blocks and inline HTML verbatim to the output. If `SummaryText` contains any raw HTML (e.g., `<script>`, `<img onerror=...>`, `<a href="javascript:...">`), it is passed through to `MarkupString` and rendered by the browser.
- **Evidence:**
  ```csharp
  var pipeline = new MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
      // ← DisableHtml() is absent
      .Build();
  var html = Markdown.ToHtml(markdown, pipeline);
  return new MarkupString(html);
  ```
- **Risk assessment:** Low-to-medium. Source is Claude/Bedrock AI output — not direct user input. Active exploitation requires an indirect injection path: adversarial content spoken into the meeting gets transcribed, feeds the summarization prompt, and Bedrock outputs raw HTML. Unlikely but not impossible (prompt injection via spoken content is a documented attack class). Defense-in-depth is cheap here.
- **Fix:**
  ```diff
  var pipeline = new MarkdownPipelineBuilder()
      .UseAdvancedExtensions()
  +   .DisableHtml()
      .Build();
  ```

---

## Nitpicks — 1

### N1: FollowUpsJson-Only Meetings Fall Through to "No Summary Available"

- **File:** `Components/Pages/MeetingDetail.razor` (line 119–120)
- **Issue:** The legacy fallback gate is `KeyDecisionsJson OR ActionItemsJson`. A meeting that has only `FollowUpsJson` (no KeyDecisions, no ActionItems) falls to the final `else` branch and shows "No summary available" — `FollowUpsJson` is silently ignored.
- **Severity:** Nitpick. Pre-#1723 meetings without any KeyDecisions or ActionItems are an edge case that may not exist in the DB. Tony should confirm.
- **Fix (if edge case exists):** Add `FollowUpsJson` to the gate condition:
  ```diff
  else if (!string.IsNullOrEmpty(_meeting.Summary.KeyDecisionsJson) ||
  -        !string.IsNullOrEmpty(_meeting.Summary.ActionItemsJson))
  +        !string.IsNullOrEmpty(_meeting.Summary.ActionItemsJson) ||
  +        !string.IsNullOrEmpty(_meeting.Summary.FollowUpsJson))
  ```

---

## Checks That Cleared

| Check | Result | Notes |
|-------|--------|-------|
| Double-render | ✅ CLEAR | `if/else if/else if/else` is fully exclusive. "Overview" MudText completely removed. No path renders both SummaryText and JSON sections. |
| Legacy fallback | ✅ CLEAR | KeyDecisions + ActionItems + FollowUps all present in legacy branch. See N1 for edge case. |
| `RenderMarkdown` null safety | ✅ CLEAR | Call site guards with `!IsNullOrEmpty` before calling. Method itself has internal guard. Redundant but safe. |
| Markdig version | ✅ CLEAR | `1.1.2` IS the current stable release. Markdig migrated from 0.x to 1.x versioning. Not outdated. |
| `@using Markdig` API resolution | ✅ CLEAR | `MarkdownPipelineBuilder` and `Markdown.ToHtml()` are in the `Markdig` namespace. Global import in `_Imports.razor` is sufficient. |

---

## Positive Observations

- **Branch structure is clean** — the `if/else if/else if/else` refactor is much better than the original nested `else { if { ... } }` structure. Easier to read, impossible to double-render.
- **Null guard at both layers** — belt-and-suspenders on `RenderMarkdown` is good practice even if redundant from the call site.
- **Legacy fallback preserved correctly** — the comment `@* Legacy fallback: structured sections for pre-#1723 meetings that lack SummaryText *@` is a nice breadcrumb.
- **"No summary available" catch-all** — the final `else` is a good addition. Previously if Summary was non-null but empty, the tab would render blank.
- **Markdig version is current** — 1.1.2 is the right pick.

---

## What Tony Needs to Fix

**C1 (blocking) — Add `::deep` to every child selector in `MeetingDetail.razor.css`:**

Every selector of the form `.firm-summary-markdown X { ... }` needs to become `.firm-summary-markdown ::deep X { ... }`. This applies to: `h1/h2/h3`, `h4/h5/h6`, `p`, `ul/ol`, `li`, `table`, `th/td`, `th`, `blockquote`, `hr`, `code`, `pre`. The wrapper rule `.firm-summary-markdown { ... }` is fine as-is.

Alternatively, move the entire ruleset to `app.css`. Either fix works.

**I1 (non-blocking but recommended) — Add `.DisableHtml()` to the Markdig pipeline:**

One line in `RenderMarkdown`. Prevents any raw HTML in AI output from passing through to the DOM.

---

_Hawkeye — Cycle 1 complete. Resubmit after C1 is fixed._

---

# Cycle 2 Review

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `bb3ecc6`
**Cycle:** 2
**Date:** 2026-04-13
**Risk:** Low — two targeted fixes from C1

---

## Verdict: PASS

---

## What Tony Fixed

| Issue | Fix Applied | Verified |
|-------|-------------|----------|
| C1: `::deep` absent from all CSS child selectors | Added `::deep` to all 16+ descendant selectors | ✅ |
| I1: `.DisableHtml()` missing from Markdig pipeline | `.DisableHtml()` chained after `.UseAdvancedExtensions()`, before `.Build()` | ✅ |

---

## CC Review Summary

Used Claude Code Sonnet for adversarial verification. CC read the full CSS file and the `RenderMarkdown` method in `MeetingDetail.razor`. Verified all 18 individual selectors, method chain order, exact method name, scope, and C1 regression checks. **Zero issues found.**

---

## Selector Audit — 18 Selectors Verified

All descendant selectors under `.firm-summary-markdown` confirmed with `::deep`:

| Selector | Line | Status |
|----------|------|--------|
| `h1` | 7 | ✅ PASS |
| `h2` | 8 | ✅ PASS |
| `h3` | 9 | ✅ PASS |
| `h4` | 15 | ✅ PASS |
| `h5` | 16 | ✅ PASS |
| `h6` | 17 | ✅ PASS |
| `p` | 23 | ✅ PASS |
| `ul` | 27 | ✅ PASS |
| `ol` | 28 | ✅ PASS |
| `li` | 33 | ✅ PASS |
| `table` | 37 | ✅ PASS |
| `th` (combined with td) | 43 | ✅ PASS |
| `td` | 44 | ✅ PASS |
| `th` (standalone) | 49 | ✅ PASS |
| `blockquote` | 55 | ✅ PASS |
| `hr` | 62 | ✅ PASS |
| `code` | 68 | ✅ PASS |
| `pre` | 75 | ✅ PASS |

Base `.firm-summary-markdown { color; line-height; }` wrapper rule intact, correctly **without** `::deep`. ✅

---

## DisableHtml Audit

| Check | Result |
|-------|--------|
| `.DisableHtml()` present | ✅ YES — line 419 |
| Order: `UseAdvancedExtensions → DisableHtml → Build` | ✅ CORRECT |
| Exact method name `.DisableHtml()` | ✅ EXACT |

---

## Scope Audit

Only two files modified — exactly as specified:
- `MeetingDetail.razor` — 1 line added (`.DisableHtml()`)
- `MeetingDetail.razor.css` — 18 selectors updated (bare → `::deep`)

No out-of-scope changes. ✅

---

## Regression Check (C1 passing criteria)

| Check | Result | Notes |
|-------|--------|-------|
| No double-render | ✅ PASS | `if/else if/else if/else` branch at lines 99–178 fully exclusive |
| Legacy fallback intact | ✅ PASS | `else if` on `KeyDecisionsJson\|\|ActionItemsJson` at lines 119–120, inner null guards at 123/139/161 |
| Null safety on `RenderMarkdown` | ✅ PASS | Call site `!IsNullOrEmpty` guard at line 113; internal guard at line 416 |

---

## Issues Found

None. Zero critical, zero important, zero nitpicks.

---

_Hawkeye — Cycle 2 complete. PASS — ships._
