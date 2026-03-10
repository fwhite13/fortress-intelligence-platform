# Review Report: FAIT Bundle — 3 Items

**Reviewer:** Hawkeye  
**Commit:** `d1eb9e4`  
**Date:** 2026-03-09  
**Review Cycle:** 1 of 2  
**Repo:** `~/projects/fip/fait/`

---

## Verdict: PASS

No Critical issues. Two Nitpicks. All three items are functionally correct.

---

## Consistency Audit

**Files Cross-Referenced:**

| File A | File B | Check | Result |
|--------|--------|-------|--------|
| `KbDocumentService.cs` `.Where(pd => pd.S3Key != null && s3Keys.Contains(pd.S3Key))` | `KbSyncRetryService.cs` `UPDATE project_documents SET IngestionStatus = 'ingested'... WHERE IngestionStatus = 'pending'` | Join column — `S3Key` is set by `DocumentService` when `IngestionStatus` is also set to `"pending"`. The DB query correctly targets the same `project_documents` table that `KbSyncRetryService` updates. | ✅ Correct join column |
| `KbDocumentInfo.IngestionStatus` values (`"pending"`, `"ingested"`, `"failed"`) | `ProjectDocument.IngestionStatus` values in DB (`"none"`, `"pending"`, `"ingested"`, `"failed"`) | Value space compatible. See N1 below for the `"none"` edge case. | ✅ Compatible (with nitpick) |
| `AssistantConfigService.GetPersonalitySystemPrompt` directive | `ChatView.razor` call site (`if (_assistantConfig != null)`) | Directive lives in `AssistantConfigService`, gated on `_assistantConfig != null` in caller. `GetOrCreateConfigAsync` always creates a config if absent — the gating is a DB-failure safety net, not a functional restriction. | ✅ Intentional and correct |
| `chat.js` `dotNetRef.invokeMethodAsync('OnSpeechResult', ...)` | `ChatView.razor` `[JSInvokable] public void OnSpeechResult(...)` | Method name and signature match. | ✅ |
| `chat.js` `dotNetRef.invokeMethodAsync('OnSpeechEnded')` | `ChatView.razor` `[JSInvokable] public void OnSpeechEnded()` | Match. | ✅ |
| `chat.js` `dotNetRef.invokeMethodAsync('OnSpeechError', event.error)` | `ChatView.razor` `[JSInvokable] public void OnSpeechError(string error)` | Match. | ✅ |
| `ChatView.razor` mic button position | Task Brief item 21 (mic LEFT of Send) | Mic button rendered first, Send button second in `chat-input-wrapper`. | ✅ Correct order |

**IDbContextFactory DI Registration:**  
`AddDbContextFactory<AppDbContext>` at Program.cs:51 ✅ — `KbDocumentService` is `AddScoped` ✅ — Singleton factory into Scoped service is correct pattern per team conventions.

---

## Critical Issues — 0

None.

---

## Important Issues — 0

None.

---

## Nitpicks — 2

### N1: `status ?? "pending"` is dead code — doesn't protect against `"none"` from DB

**File:** `KbDocumentService.cs` (line 313)

**Code:**
```csharp
if (statusMap.TryGetValue(doc.S3Key, out var status))
    doc.IngestionStatus = status ?? "pending";
```

**Issue:** `ProjectDocument.IngestionStatus` is a non-nullable `string` (`= "none"` default). When `TryGetValue` succeeds and returns `"none"`, `status ?? "pending"` evaluates to `"none"` — not `"pending"`. The null-coalescing never fires.

**Scenario where this surfaces:** A doc completes S3 upload but the second `db.SaveChangesAsync()` (which sets `"pending"`) fails. The doc is now in S3 *and* in `project_documents` with `IngestionStatus = "none"`. The fix correctly returns the DB value — but `"none"` causes `GetIngestionStatusColor` to show gray instead of amber, and `_hasPendingDocs` (which checks `== "pending"` only) won't start auto-refresh. The chip shows "⏳ Processing" but never auto-resolves.

**Severity:** Nitpick — this is an extreme edge case (partial write failure) and the visible behavior is only slightly wrong (gray chip vs amber chip; no auto-refresh). Not worth blocking for.

**Suggested fix:**
```csharp
doc.IngestionStatus = (status == "none" || status == null) ? "pending" : status;
```
Or add `"none"` to the `_hasPendingDocs` check in `KnowledgeBaseManagement.razor`. Either resolves the edge case.

---

### N2: `OnSpeechError` silently swallows the error string — no user feedback

**File:** `ChatView.razor` (lines 879–884)

**Code:**
```csharp
[JSInvokable]
public void OnSpeechError(string error)
{
    _isDictating = false;
    _interimTranscript = "";
    InvokeAsync(StateHasChanged);
}
```

**Issue:** The `error` parameter is received but never used. Common Speech API errors (`not-allowed`, `no-speech`, `audio-capture`) are silently discarded. The mic icon just stops pulsing — no indication to the user why dictation stopped.

**Severity:** Nitpick — not a correctness bug. The mic state is reset correctly. But UX would be better with a brief snackbar for `not-allowed` (microphone permission denied) at minimum.

**Suggested fix (optional):**
```csharp
[JSInvokable]
public void OnSpeechError(string error)
{
    _isDictating = false;
    _interimTranscript = "";
    if (error == "not-allowed")
        Snackbar.Add("Microphone access denied.", Severity.Warning);
    InvokeAsync(StateHasChanged);
}
```

---

## Acceptance Criteria Verification

### Item 1: KB "Processing" Chip Never Clears Fix

- [x] **#1 — IDbContextFactory injected correctly:** Field declared, assigned in constructor, `IDbContextFactory<AppDbContext>` is registered in DI. ✅
- [x] **#2 — DB query uses S3Key:** Join via `.Where(pd => pd.S3Key != null && s3Keys.Contains(pd.S3Key))`. `KbSyncRetryService` updates by time window (`UploadedAt`), not by `S3Key` — but `S3Key` is always set when `IngestionStatus = "pending"` (by `DocumentService`). The join column is correct. ✅
- [x] **#3 — Null-safety on S3Key:** `pd.S3Key != null` guard in Where clause before `Contains`. ✅
- [x] **#4 — `await using var db` scoping:** `await using var db = await _dbContextFactory.CreateDbContextAsync()` — context disposed when block exits, no leak. ✅
- [x] **#5 — Fallback: docs with no DB row stay `"pending"`:** `TryGetValue` only patches when a matching DB row exists. Docs not in `project_documents` keep `KbDocumentInfo.IngestionStatus = "pending"` (the default). ✅
- [x] **#6 — No N+1:** Single `ToDictionaryAsync` over all S3 keys, not one query per doc. ✅

### Item 2: "Tool Call Limit Reached" System Prompt Fix

- [x] **#7 — Directive text is clear and well-placed:** Appended after personality and display-name personalization. Covers both "generate document" (markdown-in-chat) and "tool calls keep failing" (explain + output inline) scenarios. Doesn't break existing `prefix` logic. ✅
- [x] **#8 — `maxIterations` comment is accurate:** Comment correctly explains the 5-iteration cap and notes it works in tandem with the system prompt directive. ✅
- [x] **#9 — Directive gated on assistant config — intentional?** `GetPersonalitySystemPrompt` is only called when `_assistantConfig != null`. `GetOrCreateConfigAsync` creates a config for every authenticated user if absent. In practice the directive applies to all users; the null-gate is a DB-failure safety net. Confirmed intentional and correct. ✅

### Item 3: Dictation Button

**JS (`chat.js`):**
- [x] **#10 — Feature detection covers both prefixes:** `window.SpeechRecognition || window.webkitSpeechRecognition` in both `isSpeechRecognitionSupported()` and `startDictation()`. ✅
- [x] **#11 — `startDictation` returns `false` gracefully:** `if (!SpeechRecognition) return false;` — no exception thrown. ✅
- [x] **#12 — `onresult` final vs interim:** Iterates from `event.resultIndex`, accumulates `finalText` for `isFinal` results, overwrites `interimText` for non-final. Correct — final text is additive, interim is overwritten. ✅
- [x] **#13 — `onend` fires `OnSpeechEnded`:** Yes — handles browser auto-stop (e.g., silence timeout) gracefully by delegating to `OnSpeechEnded` which resets `_isDictating`. ✅
- [x] **#14 — `onerror` fires `OnSpeechError` with error string:** `event.error` passed. ✅
- [x] **#15 — `stopDictation` clears `_recognition` and resets `_isRecording`:** Both done. Note: `stopDictation` nulls `_recognition` before `stop()` has fired `onend` — `onend` may still fire and call `OnSpeechEnded` a second time after explicit stop. This is harmless (idempotent reset) but technically a double-fire. ✅ (accepted)

**Blazor (`ChatView.razor`):**
- [x] **#16 — `[JSInvokable]` on all 3 callbacks:** `OnSpeechResult`, `OnSpeechEnded`, `OnSpeechError` all decorated. ✅
- [x] **#17 — `OnSpeechResult` appends, stores interim:** `_userInput += finalText` (appends) and `_interimTranscript = interimText` (separate field, not merged into input). ✅
- [x] **#18 — All callbacks use `InvokeAsync(StateHasChanged)`:** Correct JS→Blazor thread marshaling in all three callbacks. ✅
- [x] **#19 — `ToggleDictation` starts/stops and updates `_isDictating`:** Stop path: calls `stopDictation`, sets `_isDictating = false`. Start path: `_isDictating = started` (false if JS returns false). ✅
- [x] **#20 — Mic button only rendered when `_speechSupported`:** `@if (_speechSupported)` wraps the entire `<MudIconButton>`. Fully hidden (not disabled) on unsupported browsers. ✅
- [x] **#21 — Mic button LEFT of Send button:** In `chat-input-wrapper`, mic is rendered inside the `@if` block before the Send `<MudIconButton>`. ✅
- [x] **#22 — Mic button disabled when `isStreaming`:** `Disabled="@isStreaming"` on mic button. ✅
- [x] **#23 — `_dotNetRef` reused, not recreated:** Created once in `OnAfterRenderAsync(firstRender)` at line 289, reused at line 850 in `ToggleDictation`. ✅
- [x] **#24 — Interim transcript display gated correctly:** `@if (_isDictating && !string.IsNullOrEmpty(_interimTranscript))`. ✅
- [x] **#25 — `GetMicButtonStyle()` pulsing red when recording:** `animation: pulse 1.5s infinite; color: #dc2626` when `_isDictating`, muted secondary color at idle. ✅

**CSS (`fortress.css`):**
- [x] **#26 — `@keyframes pulse` correct:** `0%, 100% { opacity: 1; }  50% { opacity: 0.4; }` — matches spec exactly. ✅

**Scope creep:**
- [x] **#27 — No unrelated changes:** 5 source files changed, all within scope of the 3 items. 2 pipeline docs added (deploy report + prior review report). Clean. ✅

---

## Positive Observations

- **Item 1:** The DB lookup placement is clean — outside the S3 try/catch, so a DB failure doesn't hide the S3 listing results. If the DB query fails, the method will throw but that's correct behavior (you'd want to know).
- **Item 1:** `await using` pattern is textbook correct for `IDbContextFactory` in a service method. Consistent with established team convention.
- **Item 1:** The S3 pagination loop (`do { ... } while (response.IsTruncated)`) was already correct from prior work. Not disturbed.
- **Item 2:** The directive handles both the "document generation" and "tool failure fallback" cases in one sentence — tight and complete.
- **Item 3:** `event.resultIndex` in `onresult` is exactly correct — iterating from `resultIndex` (not 0) avoids reprocessing already-final results. Shows knowledge of the edge case.
- **Item 3:** The `_dotNetRef` reuse pattern is correct. Not creating a new reference per dictation session is the right call — avoids memory leaks.
- **Item 3:** Full hide (not disable) for unsupported browsers is the right UX choice.

---

## Summary

| Category | Count |
|----------|-------|
| Critical | 0 |
| Important | 0 |
| Nitpick | 2 |

All 27 checklist items verified. Both nitpicks are edge cases / UX polish — neither blocks functionality or correctness. Recommend **PASS**.
