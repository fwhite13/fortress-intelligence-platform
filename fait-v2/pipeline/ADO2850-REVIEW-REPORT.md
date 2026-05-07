# Review Report — ADO#2850

## Verdict: **NEEDS-CHANGES**

**WI:** FAIT v2: Main assistant chat UI with streaming and KB toggle pills  
**Commit:** `28feac949ddeab63ec84e77d5589df319b3069a4`  
**Review cycle:** 1 of 2  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07  
**CC model:** Sonnet (`--dangerously-skip-permissions`, `--print`)  
**Build:** ✅ 0 errors, 0 warnings

---

## CC Review Summary

CC ran an adversarial analysis over all 6 source files. 8 verification tasks were issued.

**Confirmed real issues:** C7 (hardcoded CSS px value), I2 (empty string vs null contract)  
**False positives:** None  
**All other checks:** PASS

---

## Spec Compliance Check

**Brief:** ADO#2850 Build Report + WI acceptance criteria

**§2 Codebase Map — Files Modified:**
- ✅ `src/FortressAI.V2.Web/Models/ChatMessage.cs` — Created
- ✅ `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — Created
- ✅ `src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor` — Created
- ✅ `src/FortressAI.V2.Web/Components/Pages/Dashboard.razor` — Modified
- ✅ `src/FortressAI.V2.Web/Components/_Imports.razor` — Modified
- ✅ `src/FortressAI.V2.Web/wwwroot/css/app.css` — Modified (ADO#2850 block appended)

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected

**§7 Acceptance Criteria:**
- ✅ `ChatView.razor` and `MessageBubble.razor` in `Components/Chat/` — Verified
- ✅ `ChatMessage` record with `Role`, `Content`, `Timestamp?` — Verified (correct `record` type)
- ✅ KB toggle pills use gold active state via C# method pattern — Verified (`var(--color-gold)` confirmed)
- ✅ `SendMessage` streams via `SendTurnAsync` — Verified (`await foreach` over `HarnessEvent`)
- ✅ Input disabled during streaming — Verified (`disabled="@_isStreaming"` on textarea and button)
- ✅ `Dashboard.razor` uses `<ChatView />` in `DualPaneLayout` `<ChatContent>` slot — Verified
- ❌ **All CSS values are CSS variables (except documented exceptions)** — NOT MET (`max-height: 200px` line 618)
- ✅ Build 0 errors, 0 warnings — Verified

**Spec compliance verdict:** ⚠️ **CONDITIONAL** — AC #7 fails. One hardcoded CSS value outside the exception list.

---

## Consistency Audit

**Files Cross-Referenced:**
- `ChatView.razor` → `ChatMessage.cs` — ✅ `Role`/`Content` used correctly; record matches
- `ChatView.razor` → `IUserAgentRuntime.cs` — ✅ `TurnRequest(Message, SystemPrompt)` contract matches; `HarnessEvent.Content` nullable guard present
- `ChatView.razor` → `MessageBubble.razor` — ✅ `ChatMessage` parameter passed correctly
- `Dashboard.razor` → `ChatView.razor` — ✅ `<ChatView />` inside `<ChatContent>` slot of `DualPaneLayout`
- `_Imports.razor` → All components — ✅ `FortressAI.V2.Web.Components.Chat`, `.Models`, `.Services` all present

**Undocumented Dependencies Found:**
- None. All downstream callers accounted for.

---

## Critical Issues — 1

### C1: Hardcoded `max-height: 200px` in `.chat-input-field`
- **File:** `src/FortressAI.V2.Web/wwwroot/css/app.css` (line 618)
- **Category:** Spec non-compliance
- **Issue:** `max-height: 200px` is a hardcoded pixel value in the ADO#2850 CSS block. The approved structural exceptions are: `28px` (pill height), `44px` (input/send button height), `80%` (max-width), `1.6` (line-height), `20px` (send icon), `16px` (pill icon). `200px` is not in this list and violates the CSS variable compliance requirement.
- **Evidence:**
  ```css
  .chat-input-field {
      /* ...other vars... */
      min-height: 44px;
      max-height: 200px;   /* ← HARDCODED — NOT A CSS VARIABLE */
  }
  ```
- **Impact:** Breaks CSS design token compliance. Future design system changes to input height require app.css surgery rather than a token update. Violates AC#7.
- **Fix:**
  ```diff
  /* In the token block or :root, add: */
  + --chat-input-max-height: 200px;
  
  /* In .chat-input-field: */
  -     max-height: 200px;
  +     max-height: var(--chat-input-max-height);
  ```

---

## Important Issues — 1

### I1: `BuildSystemPrompt()` Returns `""` Instead of `null`
- **File:** `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` (lines ~142–147)
- **Category:** Correctness — contract mismatch
- **Issue:** When both KB toggles are off (`_fortressKbEnabled = false`, `_personalKbEnabled = false`), `string.Join(" ", parts)` with an empty list returns `""`. This is passed to `TurnRequest(SystemPrompt: "")`. The `TurnRequest` record defines `SystemPrompt` as `string?` (nullable, defaults to `null`). Passing `""` vs `null` is semantically different: the backend/Bedrock harness may treat an explicit empty string as "apply empty system prompt" rather than "omit system prompt." With both KBs off, the intent is no system prompt injection — `null` expresses this correctly; `""` does not.
- **Evidence:**
  ```csharp
  private string BuildSystemPrompt()
  {
      var parts = new List<string>();
      if (_fortressKbEnabled) parts.Add("Search the Fortress knowledge base for relevant context.");
      if (_personalKbEnabled) parts.Add("Search the user's personal knowledge base for relevant context.");
      return string.Join(" ", parts);  // returns "" when parts.Count == 0
  }
  
  // Called as:
  var request = new TurnRequest(
      Message: userMessage,
      SystemPrompt: BuildSystemPrompt()  // passes "" instead of null
  );
  ```
  `TurnRequest` contract:
  ```csharp
  public record TurnRequest(
      string Message,
      string? SystemPrompt = null,   // nullable; null = omit field
      string? SessionId = null
  );
  ```
- **Impact:** With both KBs disabled, the assistant may receive an empty system prompt instruction rather than no system prompt. Backend behavior with `SystemPrompt: ""` is undefined in the harness; `null` is the correct "no system prompt" signal per the record's default.
- **Fix:**
  ```diff
  - private string BuildSystemPrompt()
  + private string? BuildSystemPrompt()
    {
        var parts = new List<string>();
        if (_fortressKbEnabled) parts.Add("Search the Fortress knowledge base for relevant context.");
        if (_personalKbEnabled) parts.Add("Search the user's personal knowledge base for relevant context.");
  -     return string.Join(" ", parts);
  +     return parts.Count == 0 ? null : string.Join(" ", parts);
    }
  ```

---

## Nitpicks — 2

### N1: `_messagesRef` Unused for JS Interop (Intentional Deferral)
- **File:** `ChatView.razor` (line ~77)
- `_messagesRef` is declared and assigned via `@ref="_messagesRef"` but `IJSRuntime` is not injected and no scroll-to-bottom interop is wired. Blazor's source generator emits the field assignment — no compiler warning. The build report documents this as Sprint 3 work. **Not blocking.**

### N2: Redundant `@using` Declarations
- **Files:** `ChatView.razor` (lines 1–2), `MessageBubble.razor` (line 1)
- `@using FortressAI.V2.Web.Models` and `@using FortressAI.V2.Web.Services` are re-declared per-file despite already being in `_Imports.razor` (lines 19–21). Harmless. **Not blocking.**

---

## Acceptance Criteria Verification

| Criterion | Status | Evidence |
|-----------|--------|----------|
| `ChatView.razor` + `MessageBubble.razor` in `Components/Chat/` | ✅ | Directory listing confirmed |
| `ChatMessage` record (`Role`, `Content`, `Timestamp?`) | ✅ | `record ChatMessage(string Role, string Content, DateTimeOffset? Timestamp = null)` |
| KB pills use C# method pattern; gold active state | ✅ | `GetFortressKbStyle()`/`GetPersonalKbStyle()` — `var(--color-gold)` confirmed, no hex |
| `SendMessage` calls `SendTurnAsync`, streams token-by-token | ✅ | `await foreach`, `StateHasChanged()` per token |
| Input disabled during streaming | ✅ | `disabled="@_isStreaming"` on textarea and button |
| `Dashboard.razor` `<ChatView />` inside `<ChatContent>` | ✅ | `DualPaneLayout > ChatContent > ChatView` confirmed |
| **All CSS values are CSS variables (except exceptions)** | ❌ | `max-height: 200px` on line 618 — hardcoded, not tokenized |
| Build 0 errors, 0 warnings | ✅ | `dotnet build` — Build succeeded, 0 Warning(s), 0 Error(s) |

---

## Positive Observations

- **Streaming:** `await foreach` over `IAsyncEnumerable<HarnessEvent>` is correct and efficient. Token-by-token `StateHasChanged()` gives good perceived responsiveness.
- **XSS safety:** `RenderMarkdown` calls `HtmlEncode` *before* `Replace("\n", "<br/>")` — correct order. User content is neutralized before any HTML injection point.
- **Auth claim resolution:** `objectidentifier` primary + `oid` short-claim fallback is correct. Not sourced from name/email/UPN.
- **Race condition guard:** `_isStreaming` guard in `SendMessage` cleanly prevents double-sends via Enter hammering.
- **KB pill tokens:** Active state uses `var(--color-gold)` / `var(--color-gold-muted)` — zero hex values or `accent-blue` contamination.
- **Dashboard slot:** `<ChatView />` correctly placed inside `<ChatContent>` render fragment in `DualPaneLayout` — not in `PreviewContent`, not at top level.
- **finally block:** Correctly resets `_isStreaming`, appends assistant message only if content exists, clears `_streamingContent`, calls `StateHasChanged()`.

---

## What to Fix (Before Merge)

### Must fix (2 items):

**1. app.css line 618** — `max-height: 200px` → `max-height: var(--chat-input-max-height)`  
   Add `--chat-input-max-height: 200px` to the token/variable block (`:root` or the ADO#2850 token section).

**2. ChatView.razor `BuildSystemPrompt()`** — Return `null` when both KBs off:
   ```csharp
   private string? BuildSystemPrompt()
   {
       var parts = new List<string>();
       if (_fortressKbEnabled) parts.Add("Search the Fortress knowledge base for relevant context.");
       if (_personalKbEnabled) parts.Add("Search the user's personal knowledge base for relevant context.");
       return parts.Count == 0 ? null : string.Join(" ", parts);
   }
   ```

### Nice to have (not blocking):
- Remove redundant `@using` lines from `ChatView.razor` and `MessageBubble.razor` (already in `_Imports.razor`)

---

## Summary

| Category | Count |
|----------|-------|
| Critical issues | 1 |
| Important issues | 1 |
| Nitpicks | 2 |

**Status:** NEEDS-CHANGES — 2 fixes required. Once applied and build re-verified (0 errors/0 warnings), code is ready for security scanning and deployment.

---

**ADO comment:** Posted — comment ID 781750  
**Review cycle:** 1 of 2  
**Reviewer:** Hawkeye (Clint Barton)  
**CC command:** `cat pipeline/review-2850-final-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Review Cycle 2 — Verdict: **PASS**

**Commit:** `b63835ed4e25812a92b6718a6e3cbedc8949dddd`  
**Review cycle:** 2 of 2 (final)  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07  
**CC model:** Sonnet (`--dangerously-skip-permissions`, `--print`)  
**Build:** ✅ 0 errors, 0 warnings  
**ADO comment:** Posted — comment ID 781755  

### C7 — CSS variable for max-height: ✅ FIXED

`app.css:618`:
```css
max-height: var(--chat-input-max-height, 200px);
```
CSS custom property with `200px` fallback. Not a bare hardcoded value. Compliant with AC#7.

### I2 — BuildSystemPrompt null return: ✅ FIXED

`ChatView.razor:142–148`:
```csharp
private string? BuildSystemPrompt()
{
    var parts = new List<string>();
    if (_fortressKbEnabled) parts.Add("Search the Fortress knowledge base for relevant context.");
    if (_personalKbEnabled) parts.Add("Search the user's personal knowledge base for relevant context.");
    return parts.Any() ? string.Join(" ", parts) : null;
}
```
- Signature: `string?` ✅  
- Both KBs off → `null` ✅  
- One or both KBs on → joined string ✅  

### New Issues: None

No regressions. No new issues introduced by the fix commit.

### Final Status

**PASS — ready for security scan and merge.**  
**CC command:** `cat pipeline/review-c2-2850-brief.md | claude --model sonnet --print --dangerously-skip-permissions`
