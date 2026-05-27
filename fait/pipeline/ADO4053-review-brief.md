# ADO#4053 — Adversarial Code Review Brief
## Reviewer: Clint Barton (Hawkeye)
## Commit: 632d07f6

---

## Files to Review

1. `fait/agent-harness/harness-server.js` — new `POST /import-memory` endpoint + refactored `resolveProgressLabel`/`chipTrunc`
2. `fait/src/FortressAI.Web/Components/Pages/Memory.razor` — import button + two-step MudDialog + @code
3. `fait/src/FortressAI.Web/Services/IMemoryFileService.cs` — `ImportMemoryAsync` interface + `ImportMemoryResult` record
4. `fait/src/FortressAI.Web/Services/MemoryFileService.cs` — `IHttpClientFactory` constructor param + `ImportMemoryAsync` implementation
5. `fait/src/FortressAI.Web/Controllers/MemoryController.cs` — pre-existing `POST /api/memory/write` endpoint (already present, called by new endpoint)

---

## Context

This WI adds a memory import flow. Users click "Import" on the `/memory` page, see a two-step modal (step 1: copy an export prompt to paste into another AI, step 2: paste that AI's response), then submit. The Blazor service calls the harness at `POST /import-memory`. The harness then:
1. Calls `POST /api/memory/write` on the Blazor app (internal token auth)
2. Calls `upsertMemoryChunks(userId, 'memory/imported-memory.md', content)` to push into pgvector

---

## AREA 1: harness-server.js — /import-memory endpoint

Read lines 1270-1320 of `fait/agent-harness/harness-server.js`.

**Authentication concern — CRITICAL investigation:**
The harness runs as a Node.js process inside the same ECS task or VPC as Blazor. Other harness endpoints (e.g., `/tools/write_memory`, `/tools/read_memory`, `/turn`) also have no auth guard — they're considered internal. This is the existing security model. Confirm: is `/import-memory` consistent with all other tool endpoints in this file? If all endpoints have no auth, then `/import-memory` follows the established pattern. If other endpoints have an auth check that `/import-memory` lacks, that's a Critical finding.

**Input validation — size limit:**
- `express.json({ limit: '10mb' })` is set at line 365. The harness has a 10MB body cap.
- Blazor's `MemoryFileService.ImportMemoryAsync` uses `_httpClientFactory.CreateClient()` (default client) with no explicit timeout and no size limit on the POST body.
- Is there any validation of `content` length in the harness endpoint beyond `!content.trim()`? What happens if someone POSTs 9.9MB of text? `upsertMemoryChunks` will spin through CHUNK_SIZE=500/OVERLAP=50 iterations — up to ~22,000 chunks — each with a Bedrock embedding call. This is a potential DoS/runaway cost issue. Check: is there any max content length guard on the endpoint?

**Chunk count calculation — consistency check:**
The endpoint pre-calculates `chunkCount` using CHUNK_SIZE=500, OVERLAP=50 before calling `upsertMemoryChunks`. The actual `upsertMemoryChunks` function uses the same constants. Verify they are identical — if they drift, the reported chunk count will be wrong.

Read `upsertMemoryChunks` at approximately line 473-500. Compare the loop logic with the pre-calculation loop in the endpoint.

**Error handling — partial failure:**
If `upsertMemoryChunks` fails after `memory/write` succeeds, what does the endpoint return? The catch block returns `{ success: false, error: err.message }` with HTTP 200. Does `MemoryFileService` handle this correctly? Check: `response.EnsureSuccessStatusCode()` only fires on non-2xx — it will NOT catch `{ success: false }`. The code then checks `result?.Success != true` and throws. This chain is correct IF the harness always returns HTTP 200 (it does — it uses `res.json()` without a status code in both success and error). Confirm this.

**UserId user-scoping — pgvector:**
`upsertMemoryChunks` takes a raw `userId` string and constructs the schema name as `user_${userId.replace(/-/g, '_')}`. The userId comes from `req.body.userId` which is `Session.UserId.ToString()` from Blazor (a Guid string). Verify:
1. Is there any validation that `userId` looks like a Guid (format check)? Or could an attacker inject an arbitrary string into the schema name? Example: if `userId` is `admin"; DROP SCHEMA...`, what does `userId.replace(/-/g, '_')` produce? Check whether schema name construction has injection risk.
2. The `api/memory/write` endpoint uses `Guid.TryParse(request.UserId, out var userId)` which validates the userId is a proper GUID before proceeding. However, `upsertMemoryChunks` does not do this validation — it trusts the raw string.

---

## AREA 2: resolveProgressLabel / chipTrunc refactor — regression risk

Read the diff for `resolveProgressLabel` (before: lines ~259-290 in old version, after: lines 263-360 in new version).

**Specific regression checks — compare old vs. new behavior:**

Old `resolveProgressLabel`:
- Took `toolInput` and converted entire thing to a lowercase string via `JSON.stringify`
- `write_file` → called `extractFilename(toolInput)` → `Saving ${filename}...`
- `read_file` → called `extractFilename(toolInput)` → `Reading ${filename}...`
- `list_files` → `Listing files...`
- No handling for `str_replace_based_edit_tool` or `str_replace_editor`
- fallback: `Working...`

New `resolveProgressLabel`:
- Parses `toolInput` as JSON (with try/catch fallback)
- New: `str_replace_based_edit_tool` / `str_replace_editor` → `Editing: {filename}`
- `write_file` → `Saving: {chipTrunc(fname)}` (note the colon format changed)
- `read_file` → `Reading: {chipTrunc(fname)}` (note the colon format changed)
- bash/computer: now extracts `input.command` or `input.cmd` and shows `Running: {preview}` (new behavior — previously showed generic strings)
- Falls through to `Working...` (same)

**Regression to check:** Old `getBuiltinSummary` had `case 'search_knowledge_base'`, `'search_memory'`, `'read_memory'`, `'write_memory'` etc. — these are NOT in `resolveProgressLabel`, they're in `getBuiltinSummary`. Confirm this separation is intentional and unchanged.

**Check:** does the new `resolveProgressLabel` produce correct output when `toolInput` is already a parsed object (not a JSON string)? The `JSON.parse` on an object would throw — is the catch fallback adequate?

**chipTrunc edge cases:**
- `chipTrunc(null)` → `''` ✓ (the `if (!str) return ''` guard)
- `chipTrunc(undefined)` → `''` ✓
- `chipTrunc('')` → `''` ✓
- `chipTrunc('x'.repeat(100))` → first 57 chars + '...' ✓
- `chipTrunc(123)` → `String(123).trim()` = `'123'` ✓ (the `String(str)` call handles non-strings)

---

## AREA 3: Memory.razor — two-step dialog

Read `fait/src/FortressAI.Web/Components/Pages/Memory.razor` — the import section starts at `<!-- Import Memory Dialog (ADO#4053) -->` and the @code section for import starts at `// Import memory (ADO#4053)`.

**State reset on close/reopen:**
- `OpenImportDialog()` resets: `_importStep = 1`, `_importContent = string.Empty`, `_importLoading = false`, `_importPromptCopied = false`, `_importError = null`
- `CloseImportDialog()` just sets `_showImportDialog = false` — does NOT reset step/content
- On next `OpenImportDialog()` call, all state IS reset (because `OpenImportDialog` explicitly resets). Verify this is correct — there's no path where the dialog reopens without going through `OpenImportDialog()`.

**Copy-to-clipboard — UX when unavailable:**
- `CopyImportPromptAsync()` calls `JS.InvokeVoidAsync("navigator.clipboard.writeText", ...)` inside a try/catch that silently falls through
- On failure, `_importPromptCopied = true` is still set and snackbar still fires. The user gets "Prompt copied" even if clipboard failed silently.
- Is this acceptable? The prompt text is visible in the read-only display box, so user can manually copy. But the feedback is misleading.
- Flag as Important: on clipboard failure, show a different message (e.g., "Copy failed — select the text manually") rather than "Prompt copied."

**Double-submit prevention:**
- "Import" button has `Disabled="@(string.IsNullOrWhiteSpace(_importContent) || _importLoading)"` — correct, prevents double-submit while loading.
- Back button has `Disabled="@_importLoading"` — correct.
- Cancel button has `Disabled="@_importLoading"` — correct.

**Empty content path:**
- `RunImportAsync()` has `if (string.IsNullOrWhiteSpace(_importContent)) return;` guard
- Button is disabled when `_importContent` is whitespace, so this is belt-and-suspenders. ✓

**StateHasChanged() placement in RunImportAsync:**
```csharp
_importLoading = true;
_importError = null;
StateHasChanged();  // ← explicitly called after setting loading=true
try { ... }
catch { _importError = ...; }
finally
{
    _importLoading = false;
    StateHasChanged();  // ← explicitly called in finally
}
```
This is correct. ✓

**After successful import, does the dialog close AND the topic list refresh?**
- `_showImportDialog = false` (dialog closes)
- `await LoadTopicsAsync()` (list refreshes)
- `Snackbar.Add(...)` (user feedback)
- These are called in try block on success, so they only run on success. ✓

---

## AREA 4: MemoryFileService — IHttpClientFactory

**DI Registration:**
- `Program.cs` line 105: `builder.Services.AddHttpClient();` — this registers the default `IHttpClientFactory`. ✓
- `MemoryFileService` is `AddScoped<IMemoryFileService, MemoryFileService>()` — constructor takes `IHttpClientFactory`. ✓
- `_httpClientFactory.CreateClient()` — uses the default (unnamed) client. ✓

**Named vs default client:**
The harness client is registered as `"HarnessClient"` at Program.cs line 335:
```
builder.Services.AddHttpClient("HarnessClient", client => ...);
```
But `ImportMemoryAsync` uses `_httpClientFactory.CreateClient()` (no name — default client). This means the import call does NOT use the configured `"HarnessClient"` with its base address or headers. Check: does `"HarnessClient"` have a different base address or timeout configured? If so, using the default client instead could cause connection issues.

**Timeout — no timeout on HTTP call:**
`ImportMemoryAsync` calls `http.PostAsync(...)` with no timeout. Default `HttpClient` has no built-in timeout by default in .NET (infinite). If the harness is slow (e.g., Bedrock embedding is slow for a large document), this request could hang indefinitely. Flag as Important.

**HARNESS_URL config:**
`_config["HARNESS_URL"] ?? "http://localhost:3000"` — the fallback `localhost:3000` is the harness default PORT. Tony's note says to verify this env var is set in ECS. Check if `"HARNESS_URL"` is documented in any ECS task definition or environment file.

---

## Pass/Fail Criteria

**FAIL on:**
- `/import-memory` has no auth but other comparable harness endpoints DO have auth checks (inconsistency)
- `userId` injection into pgvector schema name without format validation
- Chunk count mismatch between endpoint pre-calculation and `upsertMemoryChunks`
- `resolveProgressLabel` refactor produces incorrect output for previously-working tool names

**NEEDS-CHANGES on:**
- Named `"HarnessClient"` vs default client inconsistency (if HarnessClient has configured base address)
- Missing content size limit on `/import-memory`
- `CopyImportPromptAsync` shows "Copied!" even when clipboard API failed
- Missing HTTP timeout on `ImportMemoryAsync`

**PASS criteria:**
- All AC met
- Auth model consistent with existing harness endpoints
- No pgvector schema injection risk
- chipTrunc/resolveProgressLabel refactor doesn't regress existing tool chips
