# FAIT Bundle Build Report

**Date:** 2026-03-09
**Branch:** main
**Commit:** d1eb9e4
**Build result:** ✅ 0 errors, 28 warnings (pre-existing)

---

## Item 1: KB "Processing" Chip Never Clears Fix

### Files Changed
- `src/FortressAI.Web/Services/KbDocumentService.cs`

### What Was Done
- Added `using FortressAI.Web.Data;` and `using Microsoft.EntityFrameworkCore;` imports
- Injected `IDbContextFactory<AppDbContext>` into `KbDocumentService` constructor (new field `_dbContextFactory`)
- In `ListDocumentsAsync`, after the S3 do-while loop, added a DB lookup that:
  1. Collects all S3 keys from the listed docs
  2. Queries `AppDbContext.ProjectDocuments` where `S3Key` matches (null-safe)
  3. Builds a `statusMap` dictionary keyed by S3Key
  4. Patches each `doc.IngestionStatus` from the map (falls back to default "pending" if no DB row)

### Key Implementation Decisions
- **DB entity used:** `ProjectDocument` (DbSet name `ProjectDocuments`) in `AppDbContext`
- **Table:** `project_documents` — confirmed this is the same table `KbSyncRetryService.MarkPendingDocumentsAsIngestedAsync` updates via raw SQL
- **S3Key null-safety:** Added `pd.S3Key != null` filter in the Where clause since `S3Key` is `string?` on the model
- **IDbContextFactory is already registered** in DI (used throughout the project in services like `AssistantConfigService`) — no `Program.cs` changes needed
- The spec note "tracks ALL KB doc types" is correct: `KbSyncRetryService` uses a time-based WHERE clause (`UploadedAt <= syncStartedAt`) that catches all pending docs regardless of tier

---

## Item 2: "Tool Call Limit Reached" System Prompt Fix

### Files Changed
- `src/FortressAI.Web/Services/AssistantConfigService.cs`
- `src/FortressAI.Web/Components/Chat/ChatView.razor`

### What Was Done
**AssistantConfigService.cs:**
- Located `GetPersonalitySystemPrompt` — this is where the base personality prompt is constructed for ALL users
- Appended tool-call-limit directive to `prefix` after the `userDisplayName` check (last thing before return):
  > "When asked to create, write, or generate a document or file, output the content directly in your chat response as formatted markdown — do not attempt to use tools to save it. If tool calls are needed but keep failing, explain what you tried and provide the output directly in your response."

**ChatView.razor:**
- Added 3-line comment above `const int maxIterations = 5;` (single declaration at the tool-enabled path entry, around line 580 post-edits):
  ```
  // maxIterations = 5: caps tool call loops to prevent runaway agentic cycles.
  // Primary mitigation for tool-call-limit UX: the system prompt instructs the model
  // to output content directly in the chat response rather than repeatedly calling tools.
  ```

### Key Implementation Decisions
- System prompt is assembled in `AssistantConfigService.GetPersonalitySystemPrompt` and prepended to any project-level system prompt in ChatView
- The directive is always active (not gated on tool availability) — appropriate since the personality prefix is always applied when an assistant config exists
- There is only ONE `const int maxIterations = 5;` declaration in ChatView.razor (the second reference in the original grep was the `if (iteration >= maxIterations)` guard check, not a second `const` declaration)

---

## Item 3: Dictation Button

### Files Changed
- `src/FortressAI.Web/wwwroot/js/chat.js`
- `src/FortressAI.Web/Components/Chat/ChatView.razor`
- `src/FortressAI.Web/wwwroot/css/fortress.css`

### What Was Done

**chat.js:**
- Added `_recognition`, `_isRecording`, `_dotNetRecordingRef` state fields to `window.fortressChat`
- Added `startDictation(dotNetRef)` — creates SpeechRecognition with `continuous: true`, `interimResults: true`, `lang: 'en-US'`; wires up `onresult`, `onerror`, `onend` callbacks to Blazor via `invokeMethodAsync`; returns `true` on success, `false` if API unsupported
- Added `stopDictation()` — stops recognition and clears state
- Added `isSpeechRecognitionSupported()` — feature detection for `window.SpeechRecognition || window.webkitSpeechRecognition`

**ChatView.razor:**
- Added state fields: `_isDictating`, `_speechSupported`, `_interimTranscript`
- Added speech support check in `OnAfterRenderAsync` firstRender block (after scroll listener init)
- Added `ToggleDictation()` method — tap-to-start/tap-to-stop via `fortressChat.startDictation` / `fortressChat.stopDictation`
- Added `GetMicButtonStyle()` — pulsing red when recording, muted color at idle
- Added `[JSInvokable] public void OnSpeechResult(string finalText, string interimText)` — appends final text to `_userInput`, stores interim for display
- Added `[JSInvokable] public void OnSpeechEnded()` — resets dictating state
- Added `[JSInvokable] public void OnSpeechError(string error)` — resets dictating state
- Reused existing `_dotNetRef = DotNetObjectReference.Create(this)` for speech callbacks (no new reference needed)
- Added conditional mic button (`@if (_speechSupported)`) LEFT of Send button in `chat-input-wrapper`
- Added interim transcript display below input wrapper while recording
- **Bug fix during implementation:** CC Sonnet generated the `Title` attribute with backslash-escaped quotes (`\"Stop recording\"`); this caused Blazor compilation errors — manually fixed to use unescaped quotes (`"Stop recording"`) which Razor handles correctly inside attribute expressions

**fortress.css:**
- Appended `@keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }` at end of file

---

## Build Result

```
Build succeeded.
    28 Warning(s)
    0 Error(s)
```

All 28 warnings are pre-existing (null reference warnings, MUD analyzer warnings for `Title` attribute). No new warnings introduced by these changes.

---

## Deviations from Spec

1. **Single `maxIterations` comment location:** The spec said to add comments to "BOTH" occurrences. Investigation confirmed there is only ONE `const int maxIterations = 5;` declaration in ChatView.razor. The second reference the spec mentioned was `if (iteration >= maxIterations)` (a use, not a declaration). Comment added to the single declaration.

2. **Blazor quote escaping fix:** Claude Code generated backslash-escaped quotes in the Razor `Title` attribute expression. Blazor does not accept `\"` in attribute expressions — fixed to use standard double-quote characters which Razor parses correctly inside `@(...)` expressions.

---

## Commit

```
feat(kb): fix processing chip, add dictation button, fix tool-call-limit prompt
commit d1eb9e4 on main
```
