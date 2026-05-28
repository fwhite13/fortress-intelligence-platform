# ADO#4053 Review Cycle 1 Fix Brief

Apply ALL of the following fixes exactly as specified. No scope creep.

---

## Fix C1 — harness-server.js: userId GUID validation (Security)

**File:** `/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

**Location:** Inside the `app.post('/import-memory', ...)` handler, after the existing `userId` and `content` checks (around line 1280), BEFORE the `try` block.

**Current code** (after the existing guards):
```js
    const internalToken = process.env.INTERNAL_API_TOKEN || '';
```

**Change:** Add GUID regex guard immediately before `const internalToken`:
```js
    const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!GUID_RE.test(userId)) {
        return res.status(400).json({ error: 'Invalid userId' });
    }

    const internalToken = process.env.INTERNAL_API_TOKEN || '';
```

---

## Fix I1 — harness-server.js: Content size cap

**File:** `/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

**Location:** Inside the `app.post('/import-memory', ...)` handler, immediately after the GUID guard added in C1 (still before the `try` block).

**Add after the GUID guard:**
```js
    const MAX_CONTENT_CHARS = 50_000;
    if (content.length > MAX_CONTENT_CHARS) {
        return res.status(400).json({ error: `Content too large (max ${MAX_CONTENT_CHARS} chars)` });
    }
```

---

## Fix I2 — harness-server.js: pgvector upsert non-fatal

**File:** `/home/fredw/projects/fip/fait/agent-harness/harness-server.js`

**Location:** Inside the `try` block of `app.post('/import-memory', ...)`, the current code is:
```js
        // Upsert into pgvector
        await upsertMemoryChunks(userId, 'memory/imported-memory.md', content);

        res.json({ success: true, chunks: chunkCount });
```

**Replace with** (wrap pgvector in non-fatal try/catch, pass pgvectorWarning if it fails):
```js
        // Upsert into pgvector (non-fatal — S3 write already succeeded)
        let pgvectorWarning = null;
        try {
            await upsertMemoryChunks(userId, 'memory/imported-memory.md', content);
        } catch (pgErr) {
            console.error('[harness] import-memory pgvector upsert failed (non-fatal):', pgErr.message);
            pgvectorWarning = pgErr.message;
        }

        const result = { success: true, chunks: chunkCount };
        if (pgvectorWarning) result.pgvectorWarning = pgvectorWarning;
        res.json(result);
```

---

## Fix I3 — MemoryFileService.cs: Named HTTP client

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/MemoryFileService.cs`

**Location:** ~line 200 inside `ImportMemoryAsync`.

**Current code:**
```csharp
        var http = _httpClientFactory.CreateClient();
```

**Replace with:**
```csharp
        var http = _httpClientFactory.CreateClient("HarnessClient");
```

---

## Fix I4 — Memory.razor: Clipboard success only on try success

**File:** `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Pages/Memory.razor`

**Location:** `CopyImportPromptAsync()` method.

**Current code:**
```csharp
    private async Task CopyImportPromptAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", _importPrompt);
        }
        catch
        {
            // Clipboard API might be blocked — silently fall through
        }
        _importPromptCopied = true;
        Snackbar.Add("Prompt copied — paste into your AI.", Severity.Info);
        await Task.Delay(2000);
        _importPromptCopied = false;
        StateHasChanged();
    }
```

**Replace with** (`_importPromptCopied = true` and snackbar only inside `try`, after clipboard write succeeds):
```csharp
    private async Task CopyImportPromptAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", _importPrompt);
            _importPromptCopied = true;
            Snackbar.Add("Prompt copied — paste into your AI.", Severity.Info);
            await Task.Delay(2000);
            _importPromptCopied = false;
            StateHasChanged();
        }
        catch
        {
            // Clipboard API might be blocked — silently fall through
        }
    }
```

---

## Execution Instructions

Apply all 4 file changes exactly as specified above. Do not modify any other code. After applying changes, output a summary of each change made.
