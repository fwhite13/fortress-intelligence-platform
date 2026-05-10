# Review Report — ADO#3201

### Verdict: NEEDS-CHANGES

**CC review:** Confirmed. Two critical issues found. One is a blocking data bug; one is a double-write defect.

---

## CC Review Summary

CC confirmed:
- **CHECK A FAIL** — `ConversationId` is absent from the `TurnRequest` constructor in ChatView.razor. Harness gets empty string → S3 key becomes double-slash path.
- **CHECK B PASS** — `StubDocumentGeneratorService` correctly calls `.ToArray()` after the inner `using` block closes. Valid `.docx` will be returned.
- **CHECK C PASS** — `!conversationId` catches empty string correctly (falsy in JS). The `/tools/create_document` handler returns 400 on empty conversationId.
- **CHECK D WARNING** — Duplicate save: both the harness route AND ChatView.razor call `SaveArtifactAsync` for the same artifact. Two DB rows inserted.
- **CHECK E PASS** — `node --check` exits clean. No syntax errors.

---

## Spec Compliance Check

No developer brief file path was provided for this review dispatch. Checking against the review focus items directly.

**Files changed per commit `6a3a851f` and `b167dc22`:**
- ✅ `IDocumentGeneratorService.cs` — created
- ✅ `StubDocumentGeneratorService.cs` — created
- ✅ `WorkspaceController.cs` — created (located in `Services/`, not `Controllers/` — see Nitpick N1)
- ✅ `IUserAgentRuntime.cs` — `ConversationId` added to `TurnRequest`
- ✅ `Program.cs` — `IDocumentGeneratorService` registered
- ✅ `harness-server.js` — create_document tool, handler, dispatch, system prompt

**ChatView.razor was NOT modified** — and it needs to be (see Critical C1).

---

## Consistency Audit

**Files Cross-Referenced:**
- `IUserAgentRuntime.cs` (TurnRequest record) ↔ `ChatView.razor` (TurnRequest constructor) — ❌ `ConversationId` defined but not passed
- `ChatView.razor` (artifact SSE handler) ↔ `harness-server.js` `/tools/create_document` (save-artifact call) — ❌ double save to DB
- `harness-server.js` (`conversationId` extraction at line 1185) ↔ `harness-server.js` dispatch loop (line 1683) — ✅ `conversationId` flows correctly within harness
- `WorkspaceController.cs` `save-artifact` ↔ `IWorkspaceFileService.SaveArtifactAsync` — ✅ contract matches
- `WorkspaceController.cs` auth ↔ `harness-server.js` `X-Internal-Token` header — ✅ consistent

---

## Critical Issues — 2

### C1: `ConversationId` NOT passed in `TurnRequest` at ChatView.razor
- **File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (line ~890)
- **Category:** Consistency / Correctness
- **Issue:** The `TurnRequest` record has `ConversationId` as a parameter (added in this PR to `IUserAgentRuntime.cs`), but the constructor call at ChatView.razor line 890 does not pass it. The harness receives `conversationId = ''` (empty string). The `/tools/create_document` handler then returns **HTTP 400** (`userId and conversationId are required`) because empty string is falsy in JS.

  This means `create_document` will **always fail** when called from the Bedrock path. Every document creation attempt via the Bedrock ConverseStream path will return `[Document Error]\nuserId and conversationId are required` to the model.

- **Evidence:**
  ```csharp
  // ChatView.razor ~L890 — ConversationId is ABSENT
  var turnRequest = new TurnRequest(
      UserId: Session.UserId.ToString(),
      Message: text.Trim(),
      History: chatHistory.Select(m => new ChatHistoryEntry(m.Role, m.Content)).ToList(),
      TaskMode: _taskMode,
      SystemPrompt: string.IsNullOrEmpty(effectiveSystemPrompt) ? null : effectiveSystemPrompt
      // ConversationId NOT PASSED — defaults to null → harness uses '' → 400 from /tools/create_document
  );
  ```

  ```javascript
  // harness-server.js L1185
  const conversationId = rawBody.ConversationId ?? rawBody.conversationId ?? '';
  // → '' when not sent

  // harness-server.js L775 — in /tools/create_document handler
  if (!userId || !conversationId) {
      return res.status(400).json({ error: 'userId and conversationId are required' });
  }
  // '' is falsy → returns 400 every time
  ```

- **Impact:** `create_document` is completely non-functional in the Bedrock path. Feature does not work.
- **Fix:**
  ```diff
  // ChatView.razor ~L890
  var turnRequest = new TurnRequest(
      UserId: Session.UserId.ToString(),
      Message: text.Trim(),
      History: chatHistory.Select(m => new ChatHistoryEntry(m.Role, m.Content)).ToList(),
      TaskMode: _taskMode,
  -   SystemPrompt: string.IsNullOrEmpty(effectiveSystemPrompt) ? null : effectiveSystemPrompt
  +   SystemPrompt: string.IsNullOrEmpty(effectiveSystemPrompt) ? null : effectiveSystemPrompt,
  +   ConversationId: ConversationId?.ToString()
  );
  ```

---

### C2: Duplicate `SaveArtifactAsync` — two DB rows per artifact
- **File:** `ChatView.razor` (line ~925–942) AND `harness-server.js` `/tools/create_document` handler (line ~818)
- **Category:** Correctness / Data integrity
- **Issue:** When `create_document` succeeds, the artifact is saved to DB **twice**:

  1. **Harness path** — `/tools/create_document` handler calls `POST /api/workspace/save-artifact` → `WorkspaceController.SaveArtifact()` → `_workspaceFileService.SaveArtifactAsync()` → **DB INSERT #1**
  2. **ChatView path** — Bedrock dispatch loop emits `{ type: 'artifact', payload: ... }` SSE → ChatView.razor receives it → calls `WorkspaceFileSvc.SaveArtifactAsync(Session.UserId, conversation.Id, null, artifactPayload)` → **DB INSERT #2**

  Both paths execute in the same successful create_document flow. Two `user_workspace_files` rows are created with the same `s3Key`, resulting in duplicate artifact entries shown to the user.

- **Evidence:**
  ```javascript
  // harness-server.js ~L818 — SAVE #1
  const saveRes = await fetch(`${FAIT_BASE_URL}/api/workspace/save-artifact`, { ... });
  // ↓ then immediately:
  // harness-server.js ~L693 — EMIT artifact SSE
  sendEvent({ type: 'artifact', payload: JSON.stringify({ filename, s3Key, ... }) });
  ```
  ```csharp
  // ChatView.razor ~L934 — SAVE #2
  _pendingArtifact = await WorkspaceFileSvc.SaveArtifactAsync(
      Session.UserId, conversation.Id, null, artifactPayload);
  ```

- **Impact:** Duplicate rows in `user_workspace_files`. Artifact shows up twice in the conversation. Wastes DB space and confuses the UI.
- **Fix (two options):**

  **Option A (preferred):** Remove the `save-artifact` call from the harness `/tools/create_document` handler. Let the Blazor client be the single source of truth for DB writes. The harness still uploads to S3 and returns `{ filename, s3Key, sizeBytes }` — the Blazor client saves it via the SSE handler.

  ```diff
  // harness-server.js — remove the save-artifact call (lines ~817–836)
  -       // 4. Save artifact metadata via Blazor API
  -       const saveRes = await fetch(`${FAIT_BASE_URL}/api/workspace/save-artifact`, { ... });
  -       if (!saveRes.ok) {
  -           console.error(`[harness] create_document: save-artifact failed: ...`);
  -       }
  ```

  **Option B:** Keep the harness save and remove the ChatView.razor `SaveArtifactAsync` call, treating the SSE event as display-only. Requires the harness to send enough info for the UI to display the artifact without calling SaveArtifactAsync.

  Option A is cleaner — harness handles S3, Blazor handles DB.

---

## Important Issues — 3

### I1: `create_document` not in BUILTIN_TOOLS with correct name (verify)
- **File:** `harness-server.js` (line 312–313)
- **Status:** ✅ PASS — `'create_document'` is present in `BUILTIN_TOOLS` set.
  ```javascript
  const BUILTIN_TOOLS = new Set([
      'list_workspace_files', 'search_memory', 'read_memory', 'write_memory', 'create_document'
  ]);
  ```

### I2: Single Bedrock path confirmed — toolConfig present
- **File:** `harness-server.js` (lines 1501–1605)
- **Status:** ✅ PASS — There is exactly ONE Bedrock ConverseStream path (taskMode=false branch). `create_document` toolSpec is present in `toolConfig.tools[]` at line 1577. Task mode uses CC spawn — no toolConfig needed.

### I3: System prompt in both injection locations
- **File:** `harness-server.js`
- **Status:** ✅ PASS — create_document guidance appears in both:
  - Task mode (CC briefContent): lines 1327–1335
  - Bedrock path (systemParts): lines 1473–1478

---

## Full Check Results

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | ConversationId end-to-end | ❌ FAIL | ChatView.razor doesn't pass it → feature broken |
| 2 | Harness extracts conversationId | ✅ PASS | Line 1185, falls back to `''` |
| 3 | Dispatch loop passes conversationId | ✅ PASS | Line 1683, included in POST body |
| 4 | S3 key double-slash protection | ✅ PASS | `!conversationId` catches `''` → returns 400 |
| 5 | Artifact SSE event order | ✅ PASS | `sendEvent(artifact)` → set toolResultText → `sendEvent(text)` |
| 6 | No artifact event on error | ✅ PASS | Only emitted in the `!cdData.error` branch |
| 7 | StubDocumentGeneratorService valid .docx | ✅ PASS | OpenXml, Save(), ToArray() after inner using block |
| 8 | WorkspaceController auth | ✅ PASS | Both endpoints: `[AllowAnonymous]` + `IsInternalAuthorized()` → 401 |
| 9 | IDocumentGeneratorService registered Scoped | ✅ PASS | `AddScoped<IDocumentGeneratorService, StubDocumentGeneratorService>()` at Program.cs line 113 |
| 10 | create_document in toolConfig | ✅ PASS | Present at line 1577 |
| 11 | System prompt both locations | ✅ PASS | Lines 1330–1335 (task) and 1473–1478 (Bedrock) |
| 12 | Filename sanitization | ✅ PASS | lowercase, non-alnum→hyphen, collapse, trim, max 100, timestamp appended |
| 13 | generate-document returns bytes | ✅ PASS | `return File(bytes, mimeType)` — binary response |
| 14 | Duplicate save (artifact) | ❌ FAIL | Harness saves + ChatView saves = 2 DB rows |
| 15 | node --check | ✅ PASS | No syntax errors |
| 16 | ConversationId nullability | ⚠️ NITPICK | `string?` is fine; Guid.TryParse in controller provides validation |

---

## Nitpicks

- **N1:** `WorkspaceController.cs` is placed in `Services/` rather than `Controllers/`. Doesn't break anything but violates the standard ASP.NET controller naming convention. Low priority to move, but worth noting for project hygiene.

- **N2:** `TurnRequest.ConversationId` is `string?` — the controller does `Guid.TryParse(request.ConversationId, ...)` and returns 400 on invalid Guid. The harness sends whatever string it received. If the Blazor-side conversationId is a `Guid?`, passing `.ToString()` is the correct serialization. Non-blocking.

- **N3:** No success log in `/tools/create_document` for S3 upload. The error path logs but the success path is silent. Add: `console.log('[harness] create_document: uploaded to S3:', s3Key)` for debugging.

---

## What to Fix

### Fix 1 — ChatView.razor (BLOCKING)
Pass `ConversationId` to `TurnRequest` in the constructor at `ChatView.razor` ~L890:

```csharp
var turnRequest = new TurnRequest(
    UserId: Session.UserId.ToString(),
    Message: text.Trim(),
    History: chatHistory.Select(m => new ChatHistoryEntry(m.Role, m.Content)).ToList(),
    TaskMode: _taskMode,
    SystemPrompt: string.IsNullOrEmpty(effectiveSystemPrompt) ? null : effectiveSystemPrompt,
    ConversationId: ConversationId?.ToString()
);
```

Also check the briefRequest constructor at line ~1356 and add ConversationId there too if applicable.

### Fix 2 — Remove duplicate save-artifact call from harness (BLOCKING)
In `harness-server.js` `/tools/create_document` handler, remove the `save-artifact` POST to Blazor. The ChatView.razor SSE handler is the correct place for DB persistence. Harness should only: generate bytes, upload to S3, return `{ filename, s3Key, sizeBytes }` to the dispatch loop.

```diff
// harness-server.js ~L817–836
-       // 4. Save artifact metadata via Blazor API
-       const saveRes = await fetch(`${FAIT_BASE_URL}/api/workspace/save-artifact`, {
-           method: 'POST',
-           headers,
-           body: JSON.stringify({
-               userId,
-               conversationId,
-               ...
-           })
-       });
-       if (!saveRes.ok) {
-           console.error(`[harness] create_document: save-artifact failed: ${await saveRes.text()}`);
-       }

        res.json({ success: true, filename, s3Key, sizeBytes });
```

> **Note for Tony:** After this fix, the `save-artifact` endpoint in `WorkspaceController` is still needed — it's used by the ChatView.razor artifact SSE handler path (indirectly via `WorkspaceFileSvc.SaveArtifactAsync`). However, the harness no longer calls it directly. The endpoint can remain for future use but the harness-side call should be removed.

---

## Positive Observations

- The guard `!conversationId` correctly catches empty string in JS — good defensive coding.
- The SSE event order is correct: `artifact` fires before `text`.
- `StubDocumentGeneratorService` is a clean, correct implementation — proper OpenXml usage with flush-on-dispose respected.
- `WorkspaceController` auth is solid — both `[AllowAnonymous]` + `IsInternalAuthorized()` pattern is correct for internal endpoints.
- `node --check` passes clean.
- `IDocumentGeneratorService` registered as `Scoped` — correct choice for per-request MemoryStream.

---

_Review by Hawkeye — Cycle 1 of 2_
