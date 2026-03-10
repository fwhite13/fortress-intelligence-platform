# FAIT Build Report: Dictation Fix + Chat Attachments + Artifact Rendering

**Date:** 2026-03-10
**Commit:** `d299d6a`
**Branch:** main
**Build status:** ✅ 0 errors, 0 warnings

---

## Summary

All three parts of the sprint are implemented and committed. Build is clean.

---

## Part 1: Dictation Stutter Fix ✅ (P0)

**File:** `src/FortressAI.Web/wwwroot/js/chat.js`

**Root cause fixed:** The `onresult` handler was looping from `event.resultIndex` and *appending* to `this._finalTranscript`, causing previously finalized words to be re-appended with each new `isFinal` event.

**Fix:** Loop starts at `i = 0` on every event. `finalText` is rebuilt from scratch by iterating ALL `event.results`. `this._finalTranscript` is assigned (not appended). No more duplication.

```javascript
// Before (broken): appended from resultIndex
for (let i = event.resultIndex; i < event.results.length; i++) {
    if (event.results[i].isFinal) this._finalTranscript += transcript + ' ';
}

// After (fixed): rebuild from all results each time
let finalText = '';
for (let i = 0; i < event.results.length; i++) {
    if (event.results[i].isFinal) finalText += transcript + ' ';
}
this._finalTranscript = finalText;
```

---

## Part 2: Inline Chat Attachments ✅

### New Files
- `src/FortressAI.Shared/Models/ChatAttachment.cs` — EF model
- `src/FortressAI.Web/Services/ChatAttachmentService.cs` — S3 upload, content extraction, token estimation
- `src/FortressAI.Web/Controllers/ChatAttachmentController.cs` — `POST /api/chat-attachments/upload`
- `src/FortressAI.Web/Migrations/20260310064054_AddChatAttachments.cs` — DB migration

### Modified Files
- `src/FortressAI.Web/Data/AppDbContext.cs` — DbSet + EF config added
- `src/FortressAI.Web/Program.cs` — `AddScoped<ChatAttachmentService>()` registered
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — attachment UI + logic
- `src/FortressAI.Web/Components/Chat/MessageBubble.razor` — attachment chips display

### What it does
1. **Paperclip button** in the chat input bar opens a file picker
2. **Supported types:** `.txt`, `.md`, `.csv`, `.json`, `.xml`, `.html`, `.py`, `.cs`, `.js`, `.ts`, `.yaml`, `.yml`, `.png`, `.jpg`, `.jpeg`, `.gif`, `.pdf`
3. **File size limit:** 10MB per file
4. **Staged files** appear as chips above the input with filename, size, token estimate, and a remove button
5. **On send:** Files are uploaded to S3 under `chat-attachments/{conversationId}/{attachmentId}/{filename}`, then content is extracted and injected into the `effectiveSystemPrompt` before the Bedrock call
6. **Content injection strategy:**
   - Text/code files: extracted as UTF-8 text with `[File: filename]` header
   - Images: base64 data URI (`data:image/...;base64,...`) — BedrockService routes these to vision content blocks
   - PDFs: base64 data URI (`data:application/pdf;base64,...`) — BedrockService routes these to document blocks
7. **Message chips:** After send, attached file names appear as small chips under the user message bubble
8. **Send button** is enabled when attachments are staged even if text input is empty (sends `"(see attached files)"` as message text)

### DB Migration
Run on next deploy:
```bash
cd src/FortressAI.Web && dotnet ef database update
```

Table created: `chat_attachments` with cascade delete on `conversation_id`.

---

## Part 3: Artifact Output Rendering ✅

### Modified Files
- `src/FortressAI.Web/Components/Chat/MessageBubble.razor` — full artifact parsing and panel rendering
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — artifact system prompt injection
- `src/FortressAI.Web/wwwroot/css/fortress.css` — artifact panel CSS
- `src/FortressAI.Web/wwwroot/js/chat.js` — copy/download/toggle JS helpers

### System Prompt
Every chat message now includes this instruction in the system prompt:
```
When creating documents, code, or structured output that the user might want to save or reuse,
wrap the content in artifact tags:

<artifact type="markdown" title="Document Title">
content here...
</artifact>

Types: markdown, text, code (use language="python" etc for code)
You cannot save files or write to the Knowledge Base — produce artifacts inline.
```

### Frontend Parsing
- `RenderContent()` checks for `<artifact` in message content
- Artifact regex splits message into text segments and artifact blocks
- Each artifact renders as a collapsible panel card
- Text outside artifacts is rendered as normal Markdown

### Artifact Panel Features
- **Type icon:** 📄 markdown, 💻 code, 📃 text
- **Title** shown in header
- **Copy button:** Copies raw artifact content to clipboard
- **Download button:** Downloads as file with appropriate extension (`.md`, `.py`, `.txt`, etc.)
- **Toggle button:** Collapse/expand the artifact body
- **Markdown artifacts:** Rendered as HTML via Markdig
- **Code artifacts:** Raw code in `<pre><code class="language-X">` for highlight.js syntax highlighting

---

## Known Limitations / Future Work

1. **Drag-and-drop:** The drag-over handler is wired but actual file-from-drag isn't implemented in Blazor Server without JS interop for `DataTransfer`. Files can be attached via the paperclip button. Full drag-drop would need a JS interop bridge.

2. **Attachment display on message reload:** The `Attachments` parameter on `MessageBubble` is populated from `_pendingAttachments` at send time. On conversation reload from DB, attachments are stored in DB but the component doesn't query them for display (they'd need a service call per message). This is acceptable for Phase 1 — attachments are visible in the message chips immediately after sending, and the content is in the conversation context.

3. **S3 lifecycle:** No expiration policy on `chat-attachments/` prefix. Consider adding an S3 lifecycle rule for 90-day expiry.

4. **Token budget:** Attachment content is prepended to `effectiveSystemPrompt`. Large files consume system prompt space (20K reserve). The token estimate shown to users helps them make informed choices.

---

## Test Checklist

- [ ] **Dictation:** Say a full sentence, verify no word duplication in the transcript
- [ ] **Attach text file:** Click paperclip → select a `.md` file → chip appears → send → model receives file content
- [ ] **Attach image:** Select `.png` → send → model describes image (vision)
- [ ] **Attach PDF:** Select `.pdf` → send → model reads PDF content
- [ ] **Remove attachment:** Click X on chip before sending → chip disappears
- [ ] **Artifact rendering:** Ask model to "write a markdown document about X" → artifact panel appears with Copy/Download/Toggle
- [ ] **Code artifact:** Ask model to "write a python script" → code panel with syntax highlighting
- [ ] **Copy artifact:** Click Copy → content in clipboard
- [ ] **Download artifact:** Click Download → file downloads with correct extension
- [ ] **Collapse artifact:** Click ▲ → content collapses → click ▼ → expands again
- [ ] **DB migration:** `dotnet ef database update` completes without error
