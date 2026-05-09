# Build Report — ADO#3122: Chat UI Full v1 Visual Parity

**Date:** 2026-05-09
**Commit:** 1bb5e191
**Branch:** main
**Agent:** Rhodey (Claude Sonnet 4.6)

## Summary

Chat UI rebuilt to match FAIT v1 visual layout. MessageBubble restructured with avatar+body layout, Markdig wired for markdown rendering, mic button placeholder added.

## Changes

### `MessageBubble.razor` — Full rewrite
- Added `@using Markdig` + static `MarkdownPipeline` with `UseAdvancedExtensions()`
- Replaced flat `message-bubble` div with v1 `message / message-avatar / message-body / message-content` structure
- User messages: gold-circle avatar showing user initial (or Person icon fallback), plain text `<p>` content
- Assistant messages: Shield icon avatar (gold), `RenderMarkdown()` output, streaming cursor
- Token meta display preserved below message-body for assistant messages
- Added `[Parameter] string? UserInitial`
- `RenderMarkdown` now uses Markdig with HtmlEncode fallback on exception
- `OnAfterRenderAsync` calls `fortressChat.highlightCode` for syntax highlighting

### `ChatView.razor`
- Passes `UserInitial="@_userInitial"` to `<MessageBubble>`
- Added `private string _userInitial = "U"` field
- Sets `_userInitial` from first char of `_userDisplayName` after auth resolution
- Added mic button placeholder between run-as-task and send buttons

### `fortress.css`
- Replaced `message-bubble` block (lines 2581–2618) with comment + mic button CSS
- `.message-user .message-content` retains `var(--color-primary-light)` — matches v1
- Added `.chat-mic-btn` styles (surface bg, border, gold hover, disabled opacity)

### `FortressAI.V2.Web.csproj`
- Added `<PackageReference Include="Markdig" Version="1.1.3" />`

## Build Gate

```
dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
Build succeeded. 0 Error(s). 2 Warning(s) (pre-existing).
```

## ADO Comment
Comment ID: 784228 posted to ADO#3122.
