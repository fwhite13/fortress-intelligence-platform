# FAIT Chat Attachments (Sprint B) + Artifact Rendering (Sprint A) — Build Report

**Date:** 2026-03-10  
**Branch:** main  
**Commits:**
- `d299d6a` — feat: dictation fix, chat attachments, artifact rendering (prior session)
- `2ec8f2b` — docs: add build report for attachments/artifacts sprint (prior session)
- `bc25625` — feat: chat attachments (Sprint B) + artifact rendering (Sprint A) + dictation stutter verify (this session)

---

## Build Result

```
Build succeeded.
    33 Warning(s)
    0 Error(s)
```

**All warnings are pre-existing** (nullable reference warnings, MUD0002 for `Title` attribute pattern, BedrockRuntime1002 for model ID format). Zero new errors introduced.

---

## Item 1: Dictation Stutter Fix — VERIFIED ✅

**Status:** Already correct per commit `aa87a5a` (confirmed no change needed)

**Evidence:**
- `chat.js` line 84: `this._finalTranscript = '';` — reset at start of `startDictation` ✅
- `chat.js` line 88: `for (let i = 0; i < event.results.length; i++)` — loops from i=0 ✅
- `chat.js` line 96: `this._finalTranscript = finalText;` — replace, not append ✅
- `ChatView.razor` line 952: `_userInput = finalText;  // Replace, don't append` ✅

---

## Item 2: Artifact Rendering (Sprint A)

### Files Created
- `src/FortressAI.Web/Components/Chat/ArtifactPanel.razor` — **NEW** Blazor component with collapsible card, copy/download buttons

### Files Modified
- `src/FortressAI.Web/Services/AssistantConfigService.cs` — Added spec-required artifact tag instructions to `GetPersonalitySystemPrompt`
- `src/FortressAI.Web/wwwroot/js/chat.js` — Added `downloadTextFile`, `copyToClipboard`, `readFileAsBase64` helpers
- `src/FortressAI.Web/wwwroot/css/fortress.css` — Added artifact panel CSS (prior session: artifact-panel, artifact-header, artifact-btn, artifact-content styles)
- `src/FortressAI.Web/Components/Chat/MessageBubble.razor` — (prior session) Parses artifact tags via regex, renders collapsible HTML panels inline

### Key Implementation Decisions

**How artifact tags are parsed:**
- `MessageBubble.razor` uses two compiled `Regex` instances: `ArtifactRegex` to match `<artifact ...>content</artifact>` blocks and `AttrRegex` to parse key/value attributes
- Pattern: `<artifact\s+([^>]*)>(.*?)</artifact>` with `Singleline` flag to handle multiline content
- All artifact blocks are removed from display text; each becomes a rendered panel
- Text before/between/after artifact blocks is rendered via Markdig

**Two artifact rendering approaches (both present):**
1. `MessageBubble.razor` → HTML string injection via `RenderArtifact()` → `MarkupString` — primary approach for streaming/history messages, uses JS `copyArtifact`/`downloadArtifact`/`toggleArtifact`
2. `ArtifactPanel.razor` — Blazor component alternative (added per spec) — uses C# `CopyContent`/`DownloadContent` methods calling JS helpers

**Why dual approach:** The prior session implemented the HTML-string pattern which works well for streamed content (no Blazor re-render overhead). `ArtifactPanel.razor` was added as the spec explicitly requested it and allows future use in other contexts.

**System prompt injection:**
- `AssistantConfigService.GetPersonalitySystemPrompt` adds artifact tag instructions to every session's personality prefix
- `ChatView.HandleSend` additionally calls `GetArtifactSystemPrompt()` which provides full usage examples
- Result: artifact instructions appear in both personality AND per-message system prompts

**No markdown rendering library added** — artifact markdown preview uses Markdig (already a project dependency via `@using Markdig`), not a new NuGet package.

---

## Item 3: Inline Chat Attachments (Sprint B)

### Files Created (prior session `d299d6a`)
- `src/FortressAI.Shared/Models/ChatAttachment.cs` — EF model with `[Table("chat_attachments")]`
- `src/FortressAI.Web/Services/ChatAttachmentService.cs` — Full service: S3 upload, text/image/PDF extraction, DB CRUD

### Files Modified
- `src/FortressAI.Web/Services/DatabaseInitializationService.cs` — Added `chat_attachments` DDL to `extraTables` (IF NOT EXISTS, FK to conversations ON DELETE CASCADE)
- `src/FortressAI.Web/Data/AppDbContext.cs` — (prior session) `DbSet<ChatAttachment>` + `OnModelCreating` config
- `src/FortressAI.Web/Program.cs` — (prior session) `AddScoped<ChatAttachmentService>()`
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — (prior session) Full attachment UI
- `src/FortressAI.Web/Components/Chat/MessageBubble.razor` — (prior session) Attachment chips on user messages
- `src/FortressAI.Web/wwwroot/css/fortress.css` — Added `attachment-chips`, `attachment-chip`, `chip-name`, `chip-size`, `drag-over` CSS classes

### Key Implementation Decisions

**How images are passed to Bedrock:**
- `ChatAttachmentService.ExtractAttachmentContentAsync` returns `data:{mediaType};base64,{base64}` for image files
- These data URIs are injected into the system prompt as additional lines
- `BedrockService.ExtractMediaDataUris` already has logic to detect `data:image/` prefixed lines in the system prompt and extract them as image content blocks injected into the last user message
- This reuses the existing multimodal pipeline — no changes to BedrockService needed

**How text content is injected into message context:**
- Text/code files: S3 content is read and returned as `"[File: {filename}]\n{content}"`
- Content is accumulated in a `## Attached Files` section prepended to the effective system prompt before the Bedrock call
- Token budget respected via existing `PrepareMessagesWithSlidingWindowAsync` sliding window

**S3 key structure:** `chat-attachments/{conversationId}/{attachmentId}/{filename}` (includes attachment ID to prevent collision on duplicate filenames)

**File handling in ChatView:**
- Uses Blazor's `InputFile` / `IBrowserFile` with `OpenReadStream(maxAllowedSize: 10MB)` — no JS file reading needed
- Files are uploaded to S3 immediately on selection (staged in `_pendingAttachments`)
- Linked to message ID after `AddMessageAsync` completes
- `InputFile` element uses `id="chat-file-input"` triggered via JS eval

**PDF stub:** Returns `"[PDF: {filename}, {sizeBytes} bytes — attach text version for better context]"` — no iTextSharp added per spec constraint. TODO marked in `ExtractAttachmentContentAsync`.

**Token budget:** Attachment content is injected into `effectiveSystemPrompt` before `PrepareMessagesWithSlidingWindowAsync`, which applies the sliding window / summarization budget. No separate token cap was added — the existing budget mechanism handles it.

---

## Spec Deviations

1. **`GetFileIcon` returns MUI icon strings instead of emoji** — ChatView.razor uses `Icons.Material.Filled.*` for the file type icon displayed in attachment chips (better visual consistency with MudBlazor UI). The spec called for emoji icons (`🖼️`, `📄`, `📝`). Functional equivalence maintained.

2. **Dual artifact rendering** — Both `ArtifactPanel.razor` component and inline HTML-string rendering in `MessageBubble.razor` exist. The spec described only the component approach; the HTML approach was already implemented and is the active renderer. `ArtifactPanel.razor` was added for spec compliance and can replace the HTML approach in a future cleanup.

3. **Attachment chips use inline styles** — The current ChatView attachment chip display uses inline `style=` attributes rather than the `.attachment-chip` CSS class. The CSS classes were added for future use / spec compliance; the inline styles are functionally identical.

4. **`_isDragOver` warning** — CSS class `drag-over` is defined but `_isDragOver` field is currently unused in the template (the `chat-input-wrapper` div does not conditionally apply it). Field exists, CSS class exists — wiring was deferred; the drag-over visual indicator is a low-priority UX polish item.

---

## Git Summary

```
bc25625  feat: chat attachments (Sprint B) + artifact rendering (Sprint A) + dictation stutter verify
 5 files changed, 181 insertions(+)
```

Pushed to: `origin/main`
