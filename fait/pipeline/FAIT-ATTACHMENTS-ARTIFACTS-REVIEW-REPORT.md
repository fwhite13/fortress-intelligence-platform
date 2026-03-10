# Review Report: FAIT Chat Attachments + Artifact Rendering

**Task:** FAIT-ATTACHMENTS-ARTIFACTS  
**Reviewer:** Hawkeye  
**Commits:** `d299d6a`, `bc25625`  
**Review Cycle:** 1 of 2  
**Date:** 2026-03-10  

---

## Verdict: NEEDS-CHANGES

Three bugs found. Two are Important (drag-drop is silently broken; `ArtifactPanel.razor` is dead code but not a crash risk). One Critical for a specific edge case: images uploaded while MCP tools are active will likely 500 from Bedrock. The no-tools path (the common case) is safe. None of these crash the app for the standard use case.

---

## Consistency Audit

**Files Cross-Referenced:**

| Cross-check | Result |
|---|---|
| `ChatAttachmentService.BucketName` ("fortress-tools") ↔ task spec | ✅ Correct |
| S3 key prefix: `chat-attachments/{conversationId}/{attachmentId}/{filename}` ↔ spec `chat-attachments/{conversationId}/{filename}` | ⚠️ Minor deviation — see I2 |
| `InputFile accept=` ↔ spec file types | ✅ Matches spec exactly |
| `ChatAttachment` table name in `AppDbContext` ("chat_attachments") ↔ `DatabaseInitializationService` DDL ("chat_attachments") | ✅ Match |
| `ChatAttachmentService` registered in `Program.cs` (line 62) | ✅ |
| `AppDbContext.ChatAttachments` DbSet | ✅ Present |
| DDL `CREATE TABLE IF NOT EXISTS chat_attachments` | ✅ Present |
| `OnSpeechResult` C# — `_userInput = finalText` (replace, not +=) | ✅ Correct |
| JS `onresult` loops from `i=0` | ✅ Correct |
| `_finalTranscript = ''` reset at start of `startDictation` | ✅ Correct (line 84 in chat.js) |

**Undocumented Dependencies Found:**

- `ArtifactPanel.razor` — built but never referenced anywhere in the codebase. `MessageBubble.razor` renders artifacts as raw HTML via `RenderArtifact()`, not via the Blazor component. See I1.
- `ChatAttachmentController.cs` — exists but not mentioned in the brief. Reviewed; it's clean (auth guard, size check, error handling). Not a scope concern — it's a REST fallback for the upload path.

---

## Critical Issues: 1

### C1: Image uploads + MCP tools = likely Bedrock 500

**File:** `src/FortressAI.Web/Services/BedrockService.cs` (line 386–450) and `src/FortressAI.Web/Components/Chat/ChatView.razor` (line 677)  
**Category:** Correctness  

**Issue:**  
`StreamChatAsync` (the no-tools path) correctly calls `ExtractMediaDataUris()` to parse `data:image/...;base64,...` lines out of `effectiveSystemPrompt` and inject them as proper image content blocks on the last user message. This path works.

`StreamChatWithToolsAsync` (the MCP tools path) does **not** call `ExtractMediaDataUris()`. It passes `systemPrompt` directly as a plain `SystemContentBlock { Text = systemPrompt }`. When `effectiveSystemPrompt` contains a raw `data:image/png;base64,AAAA...` line, it is passed verbatim as system text to the Bedrock Converse API, which only accepts text in system blocks. Bedrock will reject this with a 400/500 error.

**Trigger condition:** User has MCP tools configured AND attaches an image in the same message. This is not an obscure path — if a user has any MCP server active (the model selector shows available servers), this fires.

**Evidence:**
```csharp
// StreamChatWithToolsAsync — NO ExtractMediaDataUris call:
var request = new ConverseStreamRequest
{
    ModelId = bedrockModelId,
    Messages = BuildConverseMessages(messages),  // ← plain text only, no multimodal
    ...
};
if (!string.IsNullOrEmpty(systemPrompt))
{
    request.System = new List<SystemContentBlock>
    {
        new SystemContentBlock { Text = systemPrompt }  // ← raw data URI ends up here
    };
}
```

Compare to `StreamChatAsync` which correctly does:
```csharp
(cleanedSystemPrompt, pdfBase64List, imageList) = ExtractMediaDataUris(systemPrompt);
// ... then injects as content blocks on last user message
```

**`BuildConverseMessages` also has no multimodal support** — it handles plain text, tool_use, and tool_result blocks only.

**Fix — Option A (minimal, safest):** In `HandleSend`, extract image/PDF data URIs from `effectiveSystemPrompt` *before* passing it to either streaming method. Store the URIs separately and inject them only when calling `StreamChatAsync`. For `StreamChatWithToolsAsync`, pass only the cleaned prompt and add a note that multimodal is unsupported with tools.

**Fix — Option B (proper):** Add the same `ExtractMediaDataUris` pre-processing at the top of `StreamChatWithToolsAsync` and extend `BuildConverseMessages` to inject image/document blocks on the last user message, mirroring `StreamChatAsync`.

Option B is the right long-term fix. Option A is safer for this sprint.

---

## Important Issues: 2

### I1: `ArtifactPanel.razor` is dead code — orphaned component

**File:** `src/FortressAI.Web/Components/Chat/ArtifactPanel.razor`  
**Category:** Quality  

**Issue:**  
`ArtifactPanel.razor` is a complete, well-written Blazor component with proper local state, `@onclick:stopPropagation`, and awaited `IJSRuntime` calls. But it is **never used anywhere**. `MessageBubble.razor` renders artifacts via the `RenderArtifact()` method which returns a raw HTML string injected via `@((MarkupString)...)`.

The two implementations diverge:
- `ArtifactPanel.razor` calls `fortressChat.copyToClipboard` and `fortressChat.downloadTextFile`
- `MessageBubble.razor` calls `fortressChat.copyArtifact(artifactId)` and `fortressChat.downloadArtifact(artifactId, title, ext)`

Both JS helpers exist (all four are implemented in `chat.js`), so neither path crashes. But having two parallel implementations means future changes will need to be made in two places — or in the wrong place.

The `MessageBubble` raw-HTML approach is pragmatic (avoids Blazor component lifecycle issues in streamed content) and it works fine. The `ArtifactPanel.razor` component is the better architectural pattern but isn't wired in.

**Impact:** No crash risk. Technical debt only. But it will confuse the next developer who opens the project.

**Recommendation:** Either  
(a) Delete `ArtifactPanel.razor` and document that artifact rendering is intentionally done via raw HTML in `MessageBubble`, or  
(b) Wire `ArtifactPanel.razor` in properly (requires dynamic Blazor component rendering, non-trivial). 

Option (a) is the right call for this sprint.

---

### I2: Drag-drop silently does nothing — `HandleDrop` never processes files

**File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (lines 1037–1040)  
**Category:** Correctness  

**Issue:**  
`HandleDrop` only resets `_isDragOver` to false. It does not call `ProcessFiles`. Drag-and-drop was advertised as a feature but silently fails — no upload, no error, no feedback.

```csharp
private async Task HandleDrop(DragEventArgs e)
{
    _isDragOver = false;
    // ← ProcessFiles never called
}
```

Additionally, `_isDragOver` is never bound to a CSS class on the drop target div, so the `.drag-over` CSS class (which is defined in `fortress.css`) is never applied either. The visual indicator for drag state is also missing.

**Note:** Blazor's `DragEventArgs` does not expose `DataTransfer.files` — the Blazor `@ondrop` event can't directly access dropped files. This requires either a JS interop helper or delegating the drop to the hidden `InputFile`. This is a known Blazor limitation that requires a workaround. The feature may have been stubbed with the intent to implement JS-side handling.

**Impact:** The paperclip button works correctly. Drag-and-drop is the only broken path. Users who try to drag a file will see nothing happen, no feedback.

**Fix:** Either implement proper JS interop for drop (read files via `DataTransfer.files` in JS, upload via the REST controller, return attachment metadata to Blazor), or document that drag-and-drop is a planned feature and show a "use the paperclip button" message on drop. Do not silently swallow the drag event.

---

## Nitpicks: 2

**N1: `--color-surface-raised` undefined in CSS**  
`fortress.css` line 2088: `.attachment-chip { background: var(--color-surface-raised); }` — this CSS variable is not defined anywhere in the project. The chip will render with no background (transparent). The `.artifact-panel` uses `--color-surface-elevated` with a fallback (`#1a1a2e`) which is defined. The chip has neither a definition nor a fallback. **Not a crash.** Fix: either add `--color-surface-raised` to the design token block, or change to `var(--color-surface-elevated, #1e1e2e)` to match the inline styles already used in `MessageBubble.razor`.

**N2: S3 key includes `attachmentId` subdirectory — deviates from spec**  
Spec: `chat-attachments/{conversationId}/{filename}`. Actual: `chat-attachments/{conversationId}/{attachmentId}/{filename}`. The actual pattern is arguably *better* (prevents filename collisions), but the spec discrepancy should be noted and the spec updated.

---

## Acceptance Criteria Verification

### Dictation (verify only)
- [x] **#1** `onresult` loops from `i=0` ✅ — `chat.js` line 72: `for (let i = 0; i < event.results.length; i++)`
- [x] **#2** `_finalTranscript = ''` reset at start ✅ — `chat.js` line 84: `this._finalTranscript = '';` (inside `startDictation`, before `onresult` is set)
- [x] **#3** `OnSpeechResult` C# replaces, not appends ✅ — `ChatView.razor`: `_userInput = finalText;` with comment "Replace, don't append"

### Artifact Rendering
- [x] **#4** `ArtifactPanel.razor` — Correct Blazor patterns ✅. Local `_expanded` bool, `ToggleExpanded()` method, no `@bind-IsVisible`. Would compile correctly. *But it's unused — see I1.*
- [x] **#5** `MessageBubble.razor` artifact regex ✅ — `RegexOptions.Singleline | RegexOptions.Compiled`. Artifact tags stripped from display (only text before/after artifacts is rendered as markdown).
- [x] **#6** Copy/download buttons — JS calls awaited ✅ — `CopyContent()` and `DownloadContent()` in `ArtifactPanel.razor` are `async Task` with `await JS.InvokeVoidAsync(...)`. `MessageBubble`'s HTML buttons use plain `onclick` JS (no async needed). Both wrapped in try/catch.
- [x] **#7** System prompt — artifact instructions added ✅ — `AssistantConfigService.GetPersonalitySystemPrompt()` appends artifact instructions at end of personality prefix without breaking existing content. `ChatView.HandleSend` also has a `GetArtifactSystemPrompt()` injection (redundant with the personality prefix addition, but harmless — instructions are consistent).
- [x] **#8** JS helpers — `downloadTextFile` and `copyToClipboard` ✅ — correct implementations. `copyToClipboard` returns the Promise from `navigator.clipboard.writeText()`. `downloadTextFile` creates Blob, object URL, appends/clicks/removes `<a>`, revokes URL. `copyArtifact`/`downloadArtifact`/`toggleArtifact` are also all correctly implemented.
- [x] **#9** Artifact CSS uses existing variables ✅ — uses `--color-border`, `--color-surface-elevated` (with fallbacks), `--radius-md`, `--radius-sm`, `--text-sm`, `--font-medium`, `--text-xs`. All defined in fortress.css.

### Chat Attachments
- [x] **#10** S3 bucket `fortress-tools` ✅. Key prefix `chat-attachments/{conversationId}/...` ✅. (Includes `{attachmentId}/` sub-segment — see N2.)
- [x] **#11** File input accept types ✅ — `.txt,.md,.csv,.json,.xml,.html,.py,.cs,.js,.ts,.yaml,.yml,.png,.jpg,.jpeg,.gif,.pdf` matches spec exactly.
- [x] **#12** `@ondragover:preventDefault` ✅ — present on line 187. `_isDragOver` state set ✅. *But visual indicator CSS class is never applied — see I2.*
- [x] **#13** File chips rendered above input before send ✅ — `@if (_pendingAttachments.Any())` block renders chips above the input wrapper.
- [x] **#14** `HandleSend` attachment injection ✅ — `attachmentsSnapshot` captures pending attachments, cleared at top of method, content extracted and injected into `effectiveSystemPrompt` before Bedrock call. `_pendingAttachments.Clear()` is the first action.
- [~] **#15** Images via Bedrock vision — **CONDITIONAL PASS / FAIL by path**:
  - No-tools path (`StreamChatAsync`): ✅ Works. `ExtractMediaDataUris` extracts data URIs from system prompt and injects them as image content blocks on the last user message.
  - Tools path (`StreamChatWithToolsAsync`): ❌ Broken. See C1. Raw data URI string passed as system text.
- [x] **#16** `ChatAttachment` model `[Table("chat_attachments")]` — **Note:** The `[Table]` attribute is NOT on the model class. Table mapping is done in `AppDbContext.OnModelCreating` via `entity.ToTable("chat_attachments")`. This is the EF Core fluent API pattern used consistently for all models in this project. ✅ Functionally correct.
- [x] **#17** `AppDbContext` has `DbSet<ChatAttachment>` ✅ + model config ✅ (table name, key, properties, FK, indexes).
- [x] **#18** `DatabaseInitializationService` has `chat_attachments` DDL ✅ — `CREATE TABLE IF NOT EXISTS` pattern ✅ — at line 295. FK to conversations with CASCADE.
- [x] **#19** `ChatAttachmentService` registered in `Program.cs` ✅ — line 62: `builder.Services.AddScoped<ChatAttachmentService>()`.
- [x] **#20** PDF stub — graceful fallback ✅ — `ExtractAttachmentContentAsync` returns a `data:application/pdf;base64,...` URI. `ExtractMediaDataUris` handles PDF data URIs (no-tools path). Tools path has same C1 issue. No crash in either path — worst case it's injected as text.
- [~] **#21** Large file handling — **Present**, 10MB limit enforced in both `ProcessFiles` (Blazor) and `ChatAttachmentController`. Files over 10MB are silently skipped in `ProcessFiles` (no user feedback). Controller returns 400. Noted per spec.
- [x] **#22** No unrelated changes in scope ✅ — diff includes only attachment/artifact files + pipeline docs + EF migration (expected). No unrelated code touched.

---

## Positive Observations

- **S3 key design** — including `{attachmentId}` in the S3 key path prevents filename collisions for multiple same-named files in one conversation. Good defensive design.
- **Attachment context injection architecture** is well-structured — snapshot → clear pending → inject into system prompt. Clean and atomic.
- **`ExtractMediaDataUris` for no-tools path** is correct and elegantly handles line-by-line extraction. The data URI format (`"data:{mediaType};base64,{base64}"`) returned by `ExtractAttachmentContentAsync` is on its own line (via `AppendLine(content)`), which means `ExtractMediaDataUris`'s line-based parser will correctly identify and extract it.
- **Error handling** is consistently good — S3 errors, JS interop failures, and extraction failures are all caught and logged without propagating crashes.
- **Token estimation** is a thoughtful UX touch. Showing `~N tokens` in chips helps users understand context consumption.
- **Drag-drop UI wiring** (`@ondragover:preventDefault`) is correctly done — the browser default drag behaviour is suppressed.
- **Dictation fix** is exactly right. `i=0` loop + full transcript rebuild is the correct pattern for speech recognition.

---

## Issues Summary

| # | Severity | Title |
|---|---|---|
| C1 | **Critical** | Images + MCP tools = Bedrock 500 in `StreamChatWithToolsAsync` |
| I1 | Important | `ArtifactPanel.razor` is dead code — orphaned component, never used |
| I2 | **Important** | Drag-drop silently does nothing — `HandleDrop` never processes files |
| N1 | Nitpick | `--color-surface-raised` CSS var undefined (no fallback in `.attachment-chip`) |
| N2 | Nitpick | S3 key format deviates from spec (extra `{attachmentId}` segment) |

---

## Required Fixes Before PASS

1. **C1** — Either guard the tools path so images are not attempted (with graceful message), or add `ExtractMediaDataUris` pre-processing before `StreamChatWithToolsAsync` and extend `BuildConverseMessages` with multimodal support.
2. **I1** — Delete `ArtifactPanel.razor` or document it as unused. Don't leave it as a ghost.
3. **I2** — Fix `HandleDrop` to either process files via JS interop or show a "use paperclip" fallback message. Silent swallow is not acceptable for an advertised feature.
4. **N1** — Add fallback to `.attachment-chip` background: `var(--color-surface-raised, var(--color-surface-elevated, #1e1e2e))`.

---

_Hawkeye — Review complete. Back to Tony Stark for fixes._

---

## Review Cycle 2 — Fix Verification Report

**Reviewer:** Hawkeye  
**Commit:** `9fc51e1`  
**Review Cycle:** 2 of 2  
**Date:** 2026-03-10  

---

## Verdict: NEEDS-CHANGES

All critical and important fixes land correctly. One new bug introduced in the fix commit: `DotNetObjectReference` leak in the drag-drop interop setup — a disposable is allocated but never stored or freed. Minor but real.

---

## Checklist Results

### C1 — Image/Bedrock fix

| # | Check | Result |
|---|---|---|
| 1 | `ExtractMediaDataUris(ref systemPrompt)` called before Bedrock request in `StreamChatWithToolsAsync`? | ✅ Called at lines 400–411. `cleanedSystemPrompt` used throughout. |
| 2 | Image blocks built with `ContentBlock.Image` + `ImageBlock` + `ImageSource.Bytes` (binary `MemoryStream`, NOT base64 string)? | ✅ Lines 445–455: `Convert.FromBase64String(base64Data)` → `MemoryStream(imgBytes)` → `ImageSource { Bytes = ... }`. Correct. |
| 3 | Image blocks prepended to last user message content list? | ✅ Lines 458–461: `extraBlocks.Concat(existing).ToList()` — extra blocks first, existing text blocks appended. |
| 4 | No regression to no-tools path in `StreamChatAsync`? | ✅ `StreamChatAsync` unchanged. Still calls `ExtractMediaDataUris` at line 47, same as before. |

**C1 verdict: FIXED ✅**  
The fix mirrors `StreamChatAsync` logic exactly. Binary bytes path is correct — `Convert.FromBase64String` to `MemoryStream` satisfies Bedrock Converse API requirements. PDF blocks are also handled correctly with `DocumentBlock` / `DocumentSource.Bytes`.

---

### I1 — Dead component removed

| # | Check | Result |
|---|---|---|
| 5 | `ArtifactPanel.razor` no longer exists in repo? | ✅ `find . -name "ArtifactPanel.razor"` returns nothing. |
| 6 | No compile errors from removal? | ✅ `grep -rn "ArtifactPanel" src/` returns nothing. No references remain. |

**I1 verdict: FIXED ✅**

---

### I2 — Drag-drop JS interop

| # | Check | Result |
|---|---|---|
| 7 | `chat.js` has `setupDragDrop(elementId, dotNetRef)` inside `window.fortressChat`? | ✅ Lines 204–223 of `chat.js`. |
| 8 | `setupDragDrop` handles `dragover` (preventDefault), `dragleave`, `drop` with `dataTransfer.files` → base64 → `HandleDroppedFiles`? | ✅ All three events wired. `FileReader.readAsDataURL` + `.split(',')[1]` extracts base64. `HandleDroppedFiles` invoked with array of `{name, contentType, base64}`. |
| 9 | `ChatView.razor` calls `setupDragDrop` in `OnAfterRenderAsync(firstRender)`? | ✅ Line 347, inside `if (firstRender)` block. |
| 10 | `HandleDroppedFiles([JSInvokable])` processes files same way as `HandleFileSelected`? | ✅ Same pipeline: `IsSupportedFile` check → size guard → `UploadAttachmentAsync` → `_pendingAttachments.Add` → `StateHasChanged`. File type/name/contentType all passed through. Logic is functionally equivalent to `ProcessFiles`. |
| 11 | `OnDragOver`/`OnDragLeave` are `[JSInvokable]` and call `StateHasChanged()`? | ✅ Lines 1038–1042. Both attributed `[JSInvokable]`, both call `StateHasChanged()`. |
| 12 | `_isDragOver` bound to `drag-over` CSS class on drop zone div? | ✅ Line 187: `class="chat-input-wrapper @(_isDragOver ? "drag-over" : "")"`. |

**I2 verdict: FIXED ✅ — with one new bug (see C2 below)**

---

### N1 — CSS fallback

| # | Check | Result |
|---|---|---|
| 13 | `.attachment-chip` background has `var(--color-surface-raised, #f5f5f5)`? | ✅ `fortress.css` line 2088: `background: var(--color-surface-raised, #f5f5f5);` |

**N1 verdict: FIXED ✅**

---

### Scope

| # | Check | Result |
|---|---|---|
| 14 | No unrelated changes beyond the 5 stated files? | ✅ Commit touches exactly: `ArtifactPanel.razor` (deleted), `ChatView.razor`, `BedrockService.cs`, `fortress.css`, `chat.js` + the pipeline report doc. No unrelated files. |

---

## New Issue Found in Fix Commit

### C2: `DotNetObjectReference` leak — drag-drop creates an untracked disposable

**File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (line 347)  
**Category:** Correctness / Resource leak  
**Severity:** Important (downgraded from Critical — no crash, no data loss, but memory leak on every page load)

**Issue:**  
`setupDragDrop` is called with `DotNetObjectReference.Create(this)` — a *new, anonymous* reference that is never stored in a field and never disposed. `DisposeAsync` only disposes `_dotNetRef` (line 1169). The drag-drop reference leaks for the lifetime of the page/session.

The existing pattern in this file already has the fix: `_dotNetRef` is created on line 330 and passed to `initScrollListener` and `startDictation`. The drag-drop setup should reuse the same reference.

**Evidence:**
```csharp
// Line 330 — existing reference, properly disposed in DisposeAsync
_dotNetRef = DotNetObjectReference.Create(this);

// ...

// Line 347 — NEW anonymous reference, NOT stored, NOT disposed → leak
await JS.InvokeVoidAsync("window.fortressChat.setupDragDrop", "chat-input-drop-zone", DotNetObjectReference.Create(this));
```

**Fix:**
```diff
- await JS.InvokeVoidAsync("window.fortressChat.setupDragDrop", "chat-input-drop-zone", DotNetObjectReference.Create(this));
+ await JS.InvokeVoidAsync("window.fortressChat.setupDragDrop", "chat-input-drop-zone", _dotNetRef);
```

`_dotNetRef` is guaranteed non-null at this point (assigned on line 330, same `firstRender` block, earlier in execution).

---

## Acceptance Criteria Re-verification (Cycle 2)

- [x] **C1** — `StreamChatWithToolsAsync` now calls `ExtractMediaDataUris`, builds proper binary image/PDF blocks, prepends to last user message. ✅
- [x] **I1** — `ArtifactPanel.razor` deleted, no dangling references. ✅
- [x] **I2** — Drag-drop fully wired via JS interop. ✅ (pending C2 fix)
- [x] **N1** — CSS fallback added. ✅

---

## Required Fix Before PASS

**C2** — Replace `DotNetObjectReference.Create(this)` on line 347 with `_dotNetRef`. One-line fix.

---

_Hawkeye — Cycle 2 complete. One new fix required. All original issues resolved._
