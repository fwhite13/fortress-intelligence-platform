# Adversarial Code Review Brief — ADO#2850
## Task: FAIT v2 — Main assistant chat UI with streaming and KB toggle pills
## Commit: 28feac9
## Reviewer: Hawkeye (Clint Barton)

You are performing an ADVERSARIAL code review. Be skeptical. Don't take the engineer's word. Verify actual logic.

---

## Files to analyze (read each one in full)

1. `src/FortressAI.V2.Web/Models/ChatMessage.cs`
2. `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`
3. `src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor`
4. `src/FortressAI.V2.Web/Components/Pages/Dashboard.razor`
5. `src/FortressAI.V2.Web/Components/_Imports.razor`
6. `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` (for contract verification)

Also examine the ADO#2850 CSS block in `src/FortressAI.V2.Web/wwwroot/css/app.css` (lines 472 onward).

---

## Critical checks — report PASS/FAIL on each

### C1: File placement
- Is `ChatView.razor` in `Components/Chat/` (NOT Pages/)?
- Is `MessageBubble.razor` in `Components/Chat/`?

### C2: ChatMessage record
- Does it have `Role`, `Content`, and optional `Timestamp`?
- Is it a `record` (not class)?

### C3: KB pills — C# method pattern
- Do `GetFortressKbStyle()` and `GetPersonalKbStyle()` exist as C# methods?
- Do they return inline style strings?
- Active state: must use `var(--color-gold)` border, `var(--color-gold-muted)` background, `var(--color-gold)` color — NO `var(--accent-blue)` or other colors
- Inactive state: must use `var(--color-border)` border, `transparent` background, `var(--color-text-secondary)` color
- Pills have `28px` height (accepted structural exception)

### C4: Streaming
- Does `SendMessage` call `AgentRuntime.SendTurnAsync` with `_userId` and a `TurnRequest`?
- Does it `await foreach` over the returned `IAsyncEnumerable<HarnessEvent>`?
- Does it check `evt.Type == "text"` for content accumulation?
- Does it break on `done` or `error` events?
- Is `StateHasChanged()` called per token to show streaming progress?

### C5: Input disabled during streaming
- Is `disabled="@_isStreaming"` on the textarea?
- Is the send button also disabled when `_isStreaming` is true?

### C6: Dashboard integration
- Does `Dashboard.razor` have `<ChatView />` inside `<ChatContent>` slot of `DualPaneLayout`?
- NOT in Pages/ or outside the slot?

### C7: CSS variable compliance (ADO#2850 block only)
- Scan EVERY CSS property value in the ADO#2850 block
- Accepted structural exceptions: `28px` (pill height), `44px` (send btn/input height), `80%` (max-width), `1.6` (line-height), `20px` (send icon size), `16px` (pill icon size)
- Flag ANY hardcoded hex color (e.g., `#xxxxxx`)
- Flag ANY hardcoded font-size in px/rem EXCEPT the 16px pill icon exception
- Flag ANY hardcoded color value that isn't a CSS variable
- Report: PASS only if ALL values are CSS variables except the documented exceptions above

### C8: Build verification
- Check if there are any obvious C# syntax errors in the razor files
- Check if `_messagesRef` (ElementReference) is declared but never used in JS interop — does this cause a compiler warning?

---

## Important checks — report PASS/FAIL on each

### I1: _userId resolution
- Is `_userId` resolved from `http://schemas.microsoft.com/identity/claims/objectidentifier` claim?
- Is there an `oid` fallback?
- Is it NOT derived from name, email, or UPN?

### I2: BuildSystemPrompt() null/empty guard
- Does `BuildSystemPrompt()` only add text when KB toggles are enabled?
- If BOTH toggles are off, does it return an empty string `""`?
- Is `TurnRequest.SystemPrompt` a nullable `string?`? (verify against IUserAgentRuntime.cs)
- If SystemPrompt is nullable and BuildSystemPrompt returns `""`, does this cause any issue?

### I3: finally block correctness
- Does the `finally` block in `SendMessage`:
  - Set `_isStreaming = false`?
  - Append the assistant message to `_messages` ONLY if content > 0?
  - Reset `_streamingContent = ""`?
  - Call `StateHasChanged()`?

### I4: HandleKeyDown
- Does it send on `Enter` (not Shift+Enter)?
- Is the guard `e.Key == "Enter" && !e.ShiftKey`?

### I5: MessageBubble alignment
- User messages: `align-self: flex-end` (right-aligned)?
- Assistant messages: `align-self: flex-start` (left-aligned)?

### I6: RenderMarkdown XSS safety
- Does `RenderMarkdown` call `System.Net.WebUtility.HtmlEncode` BEFORE inserting `<br/>` tags?
- Order must be: encode first, THEN replace `\n` with `<br/>`
- If reversed (replace first, then encode), the `<br/>` tags would be HTML-encoded and display as literal text

---

## Nitpicks — note but don't block

### N1: _messagesRef unused
- `_messagesRef` is captured via `@ref` but scroll-to-bottom JS interop is deferred
- Does this generate a compiler warning? The build report claims 0 warnings — verify this is consistent

### N2: @using duplication
- `_Imports.razor` adds `@using FortressAI.V2.Web.Components.Chat` and `@using FortressAI.V2.Web.Models`
- `ChatView.razor` and `MessageBubble.razor` also have per-file `@using` for these
- Not a bug, just redundant — note as nitpick

---

## Consistency audit

### CA1: TurnRequest contract
- `ChatView.razor` constructs `new TurnRequest(Message: userMessage, SystemPrompt: BuildSystemPrompt())`
- `IUserAgentRuntime.cs` defines `record TurnRequest(string Message, string? SystemPrompt = null, string? SessionId = null)`
- Verify: positional record construction matches — `Message` first, `SystemPrompt` second (optional)
- Verify: `SystemPrompt` is nullable `string?` — so passing `""` is valid but semantically empty

### CA2: HarnessEvent contract
- ChatView uses `evt.Type`, `evt.Content`
- IUserAgentRuntime.cs defines `record HarnessEvent(string Type, string? Content = null, ...)`
- Verify: `evt.Content != null` guard in ChatView matches the nullable `string?` definition

### CA3: _Imports.razor completeness
- Verify `FortressAI.V2.Web.Components.Chat` is present
- Verify `FortressAI.V2.Web.Models` is present
- Verify `FortressAI.V2.Web.Services` is present (needed for IUserAgentRuntime inject)

---

## Summary output format

For each check, output:
```
[C1] File placement: PASS/FAIL — [one-line finding]
[C2] ChatMessage record: PASS/FAIL — [one-line finding]
...
```

Then at the end:
```
OVERALL VERDICT: PASS / NEEDS-CHANGES / FAIL
Critical issues: N
Important issues: N
Nitpicks: N
```

Be specific. Cite file and line. Don't make up issues that aren't there. Don't miss issues that are there.
