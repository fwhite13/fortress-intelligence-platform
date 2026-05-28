# ADO#4053 — Cycle 2 Review Brief

You are performing an adversarial code review of fixes committed in `efa0a41c` (and the base commit `632d07f6`) for ADO#4053: Memory Import flow.

## Files to Review

1. `fait/agent-harness/harness-server.js` — the `/import-memory` endpoint (lines ~1273–1330)
2. `fait/src/FortressAI.Web/Services/MemoryFileService.cs` — ImportMemoryAsync method
3. `fait/src/FortressAI.Web/Components/Pages/Memory.razor` — CopyImportPromptAsync method (lines ~535–551)
4. `fait/src/FortressAI.Web/Program.cs` — HarnessClient registration

## What Was Fixed (Cycle 1 Findings)

| # | File | Fix |
|---|------|-----|
| C1 | `harness-server.js` | GUID regex guard on `userId` before pgvector schema construction |
| I1 | `harness-server.js` | 50,000-char content size cap — rejects with HTTP 400 if exceeded |
| I2 | `harness-server.js` | `upsertMemoryChunks` wrapped in non-fatal try/catch; `pgvectorWarning` field in response |
| I3 | `MemoryFileService.cs` | `CreateClient()` → `CreateClient("HarnessClient")` |
| I4 | `Memory.razor` | `_importPromptCopied = true` moved inside `try` block in `CopyImportPromptAsync` |

## Verification Tasks

### C1: GUID Regex Guard
File: `fait/agent-harness/harness-server.js`

1. Read the `/import-memory` endpoint handler
2. Verify the GUID regex is: `/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i`
3. Verify the GUID check happens BEFORE any use of `userId` in schema construction (i.e., before the `try` block that calls `upsertMemoryChunks`)
4. Verify it returns `res.status(400).json({ error: 'Invalid userId' })` clearly
5. Check: does `provisionUserSchema` also use `userId` to build a schema name like `user_${userId.replace(/-/g, '_')}`? If the GUID guard fires before the `try` block, is `upsertMemoryChunks` ever called with an invalid `userId`?

### I1: 50K Content Cap
File: `fait/agent-harness/harness-server.js`

1. Verify the content size check is BEFORE the chunking loop (CHUNK_SIZE/OVERLAP calculation)
2. Verify it uses `content.length > 50_000` (or similar — 50,000 chars)
3. Verify it returns HTTP 400 with a clear message
4. Check: could `content` arrive as null/undefined here? Is there a prior null check?

### I2: pgvector Non-Fatal Try/Catch
File: `fait/agent-harness/harness-server.js`

1. Read the full `/import-memory` endpoint handler
2. Verify `upsertMemoryChunks` call is inside a `try { ... } catch (pgErr) { ... }` block
3. Verify the catch block logs the error and sets a `pgvectorWarning` variable (does NOT re-throw)
4. Verify the success response includes `pgvectorWarning` when pgvector failed (conditional inclusion)
5. Verify the outer `try/catch` for the whole handler (S3 write failure path) is SEPARATE from the pgvector try/catch
6. Check: if S3 write fails but pgvector hadn't been called yet, does the pgvector try/catch wrap prevent incorrect success responses?

### I3: Named HTTP Client
File: `fait/src/FortressAI.Web/Services/MemoryFileService.cs`

1. Verify `_httpClientFactory.CreateClient("HarnessClient")` is used (not the unnamed `CreateClient()`)
2. File: `fait/src/FortressAI.Web/Program.cs` — Verify `"HarnessClient"` is registered with `builder.Services.AddHttpClient("HarnessClient", ...)` 
3. Check what configuration is applied to the HarnessClient (timeout, base URL, etc.)

### I4: Clipboard Try-Guard
File: `fait/src/FortressAI.Web/Components/Pages/Memory.razor`

1. Read `CopyImportPromptAsync` method
2. Verify `_importPromptCopied = true` is ONLY set INSIDE the `try` block (after the await JS call succeeds)
3. Verify `_importPromptCopied = false` reset and `StateHasChanged()` are also inside `try` (after the delay)
4. Verify the `catch` block does NOT set `_importPromptCopied = true`
5. Verify there is NO code setting `_importPromptCopied = true` in a `finally` block or outside the try/catch entirely
6. The intended behavior: if clipboard write fails silently, user sees NO "Copied!" feedback (correct)

### Quick Re-Check: Regressions
1. Verify all 5 original ACs from ADO#4053 are still met:
   - AC1: Import button visible on Memory page
   - AC2: Two-step modal (step 1 = paste, step 2 = confirm with prompt copy)
   - AC3: Harness `/import-memory` endpoint writes to S3 via `memory/write` API
   - AC4: pgvector upsert runs after S3 write (non-fatal)
   - AC5: UI shows success/failure feedback

2. Check: is the outer catch in the endpoint handler correct? Does `res.json({ success: false, error: err.message })` return 200 with an error body or does it return a 4xx/5xx? This could be a pre-existing issue.

## Pass Criteria
- All 5 fixes are correctly implemented as described
- No regressions introduced
- GUID regex covers the full UUID format (8-4-4-4-12)
- Content cap check precedes all processing
- pgvector failure is gracefully handled with warning in response
- Named client is registered and used
- Clipboard state only updated on success

## Fail Criteria
- GUID regex is wrong (e.g., missing anchors, wrong group lengths)
- GUID check placed AFTER schema construction can be reached with invalid input
- Content cap placed after chunking begins
- pgvector try/catch re-throws instead of continuing
- `pgvectorWarning` missing from response
- `_importPromptCopied = true` outside try block
- `HarnessClient` not registered in Program.cs

## Output Format
For each check (C1, I1, I2, I3, I4):
- Status: ✅ PASS or ❌ FAIL
- Evidence: the relevant code lines
- Any issues found

Then give an overall verdict: PASS / NEEDS-CHANGES / FAIL
