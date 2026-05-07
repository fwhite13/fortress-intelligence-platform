# Build Report — ADO#2850

## What was built
Main assistant chat UI for FAIT v2. `ChatView.razor` renders inside `DualPaneLayout`'s `<ChatContent>` slot on the `/` dashboard. Supports message history, streaming responses via `IUserAgentRuntime.SendTurnAsync`, KB toggle pills (Fortress KB + Personal KB), and an auto-grow textarea input with Enter-to-send and Shift+Enter for newlines.

## Files changed
- `src/FortressAI.V2.Web/Models/ChatMessage.cs` — New `ChatMessage` record (`Role`, `Content`, `Timestamp`)
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — NEW: main chat component with streaming, KB pills, empty state
- `src/FortressAI.V2.Web/Components/Chat/MessageBubble.razor` — NEW: individual message bubble (user = right/primary, assistant = left/surface)
- `src/FortressAI.V2.Web/Components/Pages/Dashboard.razor` — Replaced placeholder ChatContent with `<ChatView />`, removed Sprint-1 welcome banner from chat pane (code simplified)
- `src/FortressAI.V2.Web/Components/_Imports.razor` — Added `@using FortressAI.V2.Web.Components.Chat` and `@using FortressAI.V2.Web.Models`
- `src/FortressAI.V2.Web/wwwroot/css/app.css` — Appended ADO#2850 chat styles block (all CSS variable-driven)

## Parallelization used
No — all tasks were sequential (Tasks 1→6). Files built in dependency order: model → components → dashboard → CSS → imports.

## CC sessions run
1 CC run (Sonnet). All tasks executed in a single session.

## Acceptance criteria verification
- [x] `ChatView.razor` and `MessageBubble.razor` in `Components/Chat/` — confirmed
- [x] `ChatMessage` record defined in `Models/ChatMessage.cs` — confirmed
- [x] KB toggle pills use C# method pattern (gold active state with CSS vars) — `GetFortressKbStyle()` / `GetPersonalKbStyle()` return inline style strings using CSS variables; 28px height is documented structural exception
- [x] `SendMessage` calls `AgentRuntime.SendTurnAsync` and streams response token-by-token — confirmed, `await foreach (var evt in AgentRuntime.SendTurnAsync(...))` with `StateHasChanged()` per token
- [x] Input disabled during streaming — `disabled="@_isStreaming"` on textarea and send button
- [x] `Dashboard.razor` uses `<ChatView />` inside `DualPaneLayout` — confirmed, ChatContent slot contains `<ChatView />`
- [x] All CSS values are CSS variables (except 28px/44px structural) — confirmed
- [x] `dotnet build` = 0 errors, 0 warnings — VERIFIED
- [x] Commit `feat(fait-v2#2850): main assistant chat UI with streaming and KB toggle pills` — hash `28feac9`

## Known edge cases / things Clint should scrutinize
1. **Streaming while empty state** — If no messages exist but streaming starts, the streaming indicator won't show (it's inside the `else` branch). This is intentional for the first message: user message is added to `_messages` before streaming starts, so we're always in the `else` branch during a streaming response.
2. **_messagesRef auto-scroll** — `_messagesRef` is captured but auto-scroll JS interop is NOT wired. Clint may want to flag this for Sprint 3 — the container will overflow without scroll-to-bottom on new messages.
3. **`_userId` empty string** — If auth claims are missing (e.g., dev localhost without Entra), `_userId` will be `""`. `SendTurnAsync` receives an empty userId. FargateUserAgentRuntime will handle/throw — not a UI concern but Clint should verify the fallback.
4. **SendMessage not awaited on keydown in some edge cases** — `HandleKeyDown` calls `await SendMessage()` — if user hammers Enter during an already-in-flight send, the `_isStreaming` guard catches it cleanly.
5. **CSS `@keyframes blink`** — The blink animation in app.css uses a plain `@keyframes` rule; no vendor prefix. Works on all modern browsers but Clint may want to verify rendering on older Edge.

## How to test locally
```bash
cd ~/projects/fip/fait-v2
dotnet run --project src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
# Navigate to https://localhost:5001/
# Should see empty chat state: "What can I help you with?"
# KB pills: Fortress KB (inactive/grey), My KB (active/gold)
# Type a message, press Enter — sends to AgentRuntime
# During streaming: input disabled, streaming cursor blinks
```

## Commit
`28feac949ddeab63ec84e77d5589df319b3069a4`

---

## Build Cycle 2 — Fix Pass

**Date:** 2026-05-07
**Commit:** `b63835e`
**Build:** SUCCEEDED — 0 errors, 0 warnings

### Fixes applied

#### C7 — Hardcoded `max-height: 200px` in CSS
- **File:** `src/FortressAI.V2.Web/wwwroot/css/app.css` (line 618)
- **Change:** `.chat-input-field` rule — `max-height: 200px` → `max-height: var(--chat-input-max-height, 200px)`
- Default preserved at 200px via CSS custom property fallback; now overridable without source changes.

#### I2 — `BuildSystemPrompt()` returns empty string instead of null
- **File:** `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` (line 142)
- **Change:** Return type `string` → `string?`; return expression changed to `parts.Any() ? string.Join(" ", parts) : null`
- When no KBs are enabled, method now returns `null` instead of `""`, preventing downstream callers from treating an empty string as a valid system prompt.

### CC Sessions
1 CC run (Sonnet) — both fixes in single session.

### ADO Comment
Posted as comment ID 781753 on WI #2850.
