# QA Report: ADO#3201 — 5.2-A: Harness create_document tool + artifact SSE emission

**Analyst:** Black Widow (Natasha Romanoff)  
**Date:** 2026-05-10  
**Verdict:** ✅ QA PASS

---

## Summary

All acceptance criteria verified. Both task definitions are ACTIVE at the correct revisions, the Blazor service layer is fully wired, and the harness `create_document` path is correctly implemented. Browser E2E blocked by pre-existing Cloudflare + TestAuth__Secret constraint — documented below.

---

## Acceptance Criteria Results

### Service Health

| Check | Result | Detail |
|-------|--------|--------|
| `fred-dev:167` status | ✅ PASS | ACTIVE, desired=1, running=1 |
| Image matches commit | ✅ PASS | `fred-chat:1a648a4a` |
| CloudWatch clean startup | ✅ PASS | No DI errors, no unhandled exceptions |
| `ScheduledTaskBackgroundService starting` | ✅ PASS | Confirmed in startup logs — regression check clear |
| `fait-v2-agent-harness:13` registered | ✅ PASS | Task def ACTIVE, image=`fait-v2-agent-harness:1a648a4a` |

**CloudWatch notes:** All `fail:` log entries are idempotent schema migration checks (pre-existing pattern — column/table already exists → caught and logged). Not errors. Startup completes normally with `Application started.`

---

### Code-Level: Blazor (fred-dev)

| Check | Result | Detail |
|-------|--------|--------|
| `IDocumentGeneratorService.cs` exists | ✅ PASS | `/src/FortressAI.Web/Services/IDocumentGeneratorService.cs` — `DocumentSection` record + `GenerateAsync` interface |
| `StubDocumentGeneratorService.cs` exists | ✅ PASS | Valid `.docx` via `DocumentFormat.OpenXml` — creates title paragraph + SectionProperties |
| `WorkspaceController.cs` exists | ✅ PASS | `/src/FortressAI.Web/Services/WorkspaceController.cs` |
| `POST /api/workspace/save-artifact` | ✅ PASS | Present, `[AllowAnonymous]` + `IsInternalAuthorized()` guard |
| `POST /api/workspace/generate-document` | ✅ PASS | Present, returns `File(bytes, "application/vnd...wordprocessingml.document")` |
| `IDocumentGeneratorService` registered Scoped | ✅ PASS | `Program.cs` line 113: `AddScoped<IDocumentGeneratorService, StubDocumentGeneratorService>()` |
| `TurnRequest` has `ConversationId` field | ✅ PASS | `IUserAgentRuntime.cs` line 56: `string? ConversationId = null` (nullable, optional) |
| `ChatView.razor` passes `ConversationId` | ✅ PASS | Line 896: `ConversationId: conversation.Id.ToString()` — wired in primary TurnRequest constructor |

---

### Code-Level: Harness (fait-v2-agent-harness:13)

| Check | Result | Detail |
|-------|--------|--------|
| Task def ACTIVE | ✅ PASS | `fait-v2-agent-harness:13` — status=ACTIVE |
| `create_document` in `BUILTIN_TOOLS` | ✅ PASS | Line 313: `'list_workspace_files', 'search_memory', 'read_memory', 'write_memory', 'create_document'` |
| `create_document` toolSpec in `toolConfig.tools[]` | ✅ PASS | Lines 1557–1591: full toolSpec with `type`, `title`, `sections[]` schema |
| Dispatch loop `create_document` branch | ✅ PASS | Line 1656: `else if (toolUseAccumulator.name === 'create_document')` — fetches `/tools/create_document`, then `sendEvent({ type: 'artifact', payload: JSON.stringify({...}) })` before tool result text |
| No `save-artifact` fetch in route handler | ✅ PASS | `/tools/create_document` route handler (lines 769–821): calls `generate-document` → S3 PutObject directly. No `save-artifact` call. Returns `{ success, filename, s3Key, sizeBytes }`. |
| System prompt includes `create_document` guidance | ✅ PASS | Lines 1310, 1315, 1453, 1458: guidance injected in both task-mode and standard-mode system prompt construction |
| Harness live and serving | ✅ PASS | CloudWatch shows harness accepting `/turn` requests, responding with SSE events |

---

### Pre-Existing Blockers

| Item | Status |
|------|--------|
| Browser E2E (full auth flow) | ⚠️ BLOCKED — pre-existing. Cloudflare protection + `TestAuth__Secret` prevents automated login. Not new to this WI. |

*No new blockers introduced by ADO#3201.*

---

## Artifact SSE Flow Verification (Static)

The dispatch loop emits `artifact` SSE event in the correct sequence:

```
1. Tool use resolved → fetch /tools/create_document
2. /tools/create_document → calls Blazor /api/workspace/generate-document
3. /tools/create_document → PutObject to S3
4. Returns { filename, s3Key, sizeBytes }
5. Dispatch loop: sendEvent({ type: 'artifact', payload: JSON.stringify({filename, s3Key, mimeType, sizeBytes}) })
6. Dispatch loop: toolResultText = "Document created: ${filename}"
7. sendEvent({ type: 'text', content: toolResultText })
```

This is the correct emission order — artifact event before text confirmation.

---

## Notes

- The harness log shows `/turn` calls missing `conversationId` — these are all `__resumption_brief__` calls, which is expected (they don't invoke `create_document`). The `conversationId` field is passed from Blazor on real turns.
- Node v20 deprecation warning in harness logs is pre-existing/non-blocking.
- GCP credentials unavailable (Stitch) is pre-existing/non-blocking.

---

## Verdict

**✅ QA PASS**

All code-level acceptance criteria met. Both task definitions deployed at correct revisions with matching image tags. Blazor DI, controller endpoints, `TurnRequest` wiring, and harness tool path all verified. Artifact SSE emission sequence is correct per code review. Service is healthy and accepting requests.
