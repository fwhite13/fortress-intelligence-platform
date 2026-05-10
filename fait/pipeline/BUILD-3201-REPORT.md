# Build Report — ADO#3201

## What was built
create_document tool pipeline end-to-end: Blazor document generation service + WorkspaceController internal API endpoints + harness tool registration, S3 upload, artifact SSE emission, and dispatch loop wiring.

## CC Invocation
```bash
cat /tmp/cc-brief-3201.md | claude --model sonnet --print --dangerously-skip-permissions
```
CC completed all file changes. Tony verified output and committed.

## Files Changed

### Blazor (fait repo — commit `6a3a851f`)
- `src/FortressAI.Web/Services/IDocumentGeneratorService.cs` — NEW: interface + DocumentSection record
- `src/FortressAI.Web/Services/StubDocumentGeneratorService.cs` — NEW: stub returning minimal valid .docx using OpenXml SDK (DocumentFormat.OpenXml v3.4.1, already in csproj)
- `src/FortressAI.Web/Services/WorkspaceController.cs` — NEW: ApiController with:
  - `POST /api/workspace/save-artifact` — inserts UserWorkspaceFile row via IWorkspaceFileService
  - `POST /api/workspace/generate-document` — calls IDocumentGeneratorService, returns raw bytes
  - Both endpoints: `[AllowAnonymous]` + `IsInternalAuthorized()` (X-Internal-Token header)
- `src/FortressAI.Web/Services/IUserAgentRuntime.cs` — Added `string? ConversationId = null` to TurnRequest record
- `src/FortressAI.Web/Program.cs` — Registered `IDocumentGeneratorService` as Scoped (StubDocumentGeneratorService)

### Harness (fait-v2 repo — commit `b167dc22`)
- `agent-harness/harness-server.js`:
  - Added `create_document` to BUILTIN_TOOLS Set (line ~313)
  - Added `const conversationId = rawBody.ConversationId ?? rawBody.conversationId ?? ''` to /turn handler
  - Added `/tools/create_document` route handler (after write_memory handler):
    - Calls `${FAIT_BASE_URL}/api/workspace/generate-document` for bytes
    - Sanitizes filename (lowercase, non-alnum→hyphen, collapse, trim, max 100 chars + timestamp)
    - Uploads to S3: `workspaces/${userId}/artifacts/${conversationId}/${filename}`
    - Calls `${FAIT_BASE_URL}/api/workspace/save-artifact` (non-fatal if fails — file is in S3)
  - Added `create_document` toolSpec to toolConfig.tools[] (Bedrock non-task path)
  - Wired `create_document` into dispatch loop: emits `{ type: 'artifact', payload: JSON.stringify({...}) }` SSE event BEFORE setting toolResultText
  - Added create_document guidance to system prompt in BOTH locations (task mode contextParts + Bedrock systemParts)

## Parallelization used
No — sequential: Blazor first (ConversationId in TurnRequest needed by harness), harness second.

## Acceptance Criteria Verification
- [x] `IDocumentGeneratorService` interface + `DocumentSection` record
- [x] `StubDocumentGeneratorService` returns valid minimal .docx (not empty bytes)
- [x] `DocumentFormat.OpenXml` package referenced (v3.4.1 already in csproj)
- [x] `IDocumentGeneratorService` registered Scoped in Program.cs
- [x] `WorkspaceController` created with `POST /api/workspace/save-artifact` + `POST /api/workspace/generate-document`
- [x] Both endpoints use `IsInternalAuthorized()` (X-Internal-Token)
- [x] `[AllowAnonymous]` on both endpoints
- [x] `create_document` added to `BUILTIN_TOOLS` Set
- [x] `/tools/create_document` route handler added
- [x] `create_document` toolSpec added to `toolConfig.tools[]`
- [x] Dispatch loop: `create_document` branch added with `artifact` SSE event emission
- [x] System prompt updated in both injection locations
- [x] `conversationId` available in harness dispatch loop scope
- [x] Filename sanitization: lowercase, non-alnum→hyphen, collapse, trim, max 100 chars + timestamp
- [x] Tool errors return CC-readable error text, not unhandled exceptions
- [x] Build (Blazor): 0 errors, 0 warnings (`dotnet build` confirmed)
- [x] Harness: no syntax errors (`node --check` confirmed SYNTAX OK)

## Known Edge Cases / Things Clint Should Scrutinize

1. **conversationId as string in harness** — The harness userId is validated as `[a-zA-Z0-9_-]{1,64}` but conversationId is passed as-is (GUID string from Blazor). The WorkspaceController validates it with `Guid.TryParse`. If conversationId is empty string (e.g. user in non-conversation context), the save-artifact call will fail with 400 but the file will still be in S3 (non-fatal path). This is acceptable for 5.2-A.

2. **WorkspaceController file location** — Lives in `Services/` directory (not `Controllers/`) following existing project convention. Namespace is `FortressAI.Web.Controllers` which is correct.

3. **generate-document passes through X-Internal-Token** — The harness passes the token to generate-document. If INTERNAL_API_TOKEN is empty in the harness env, the header is omitted and the Blazor endpoint will return 401 (token is required by IsInternalAuthorized). Ensure INTERNAL_API_TOKEN is set in harness env.

4. **StubDocumentGeneratorService** — Returns a valid .docx with a single "coming soon" paragraph. The `type` parameter is not validated in the stub (it accepts any type). The harness validates `type === 'word'` before calling the endpoint.

## How to Test Locally
1. Ensure `INTERNAL_API_TOKEN` env var matches between harness and Blazor
2. Start FAIT locally with harness pointed at local Blazor
3. In chat: "Create a Word document with a brief report on AI trends"
4. Harness should call `create_document`, generate .docx, upload to S3, emit `artifact` SSE event
5. Verify artifact event received in Blazor SSE handler
6. Verify `user_workspace_files` row inserted in DB
