# Build Brief: ADO#4053 — Memory Import Flow

## Context
FAIT app: Blazor Server, MudBlazor, ASP.NET 8
Repo: `/home/fredw/projects/fip/fait/`
Agent harness: `fait/agent-harness/harness-server.js` (Node.js, Express)
FAIT Blazor app: `fait/src/FortressAI.Web/`

## What to Build

Add a "Import Memory" flow to the `/memory` page. Mirrors the Claude Desktop "Import memory" pattern.

### Flow:
1. User clicks "Import Memory" button on `/memory` page
2. A MudDialog opens with two steps:
   - **Step 1:** Display a copyable prompt with a copy button: `"Export all of my stored memories and any context you've learned about me from past conversations. Preserve my words verbatim where possible, especially instructions and preferences."`
   - **Step 2:** Large textarea to paste the exported content back
3. User clicks "Import" — Blazor calls new harness endpoint `POST /import-memory`
4. Harness chunks the pasted content and upserts into pgvector (same as write_memory path)
5. Success state shows how many chunks were added

---

## Part 1: Harness Changes (`agent-harness/harness-server.js`)

### Add new endpoint `POST /import-memory`

Insert this endpoint after the `write_memory` handler (around line 1220).

The endpoint:
- Accepts `{ userId, content }` in request body
- Validates userId and content are present
- Calls `upsertMemoryChunks(userId, 'memory/imported-memory.md', content)` — reuses existing function
- Also writes an S3 topic via the Blazor API (same pattern as write_memory, calls `${FAIT_BASE_URL}/api/memory/write`) with slug=`imported-memory`, title=`Imported Memory`, content=content
- Returns `{ success: true, chunks: <number of chunks upserted> }`
- On error: returns `{ success: false, error: message }`

To count chunks, replicate the chunking calculation (CHUNK_SIZE=500, OVERLAP=50) to get the count before calling upsertMemoryChunks, OR modify the call to return chunk count. Simplest: calculate chunk count inline:
```javascript
const CHUNK_SIZE = 500, OVERLAP = 50;
let chunkCount = 0;
for (let i = 0; i < content.length; i += CHUNK_SIZE - OVERLAP) {
    chunkCount++;
    if (i + CHUNK_SIZE >= content.length) break;
}
```
Then call `upsertMemoryChunks` and return `{ success: true, chunks: chunkCount }`.

The internal token pattern (X-Internal-Token header) is needed when calling Blazor's `/api/memory/write`:
```javascript
const internalToken = process.env.INTERNAL_API_TOKEN || '';
const resp = await fetch(`${FAIT_BASE_URL}/api/memory/write`, {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json',
        ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
    },
    body: JSON.stringify({ userId, slug: 'imported-memory', title: 'Imported Memory', content }),
});
```

Full endpoint code:
```javascript
// ─── import-memory endpoint (ADO#4053) ───────────────────────────────────────
app.post('/import-memory', async (req, res) => {
    const { userId, content } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!content || !content.trim()) return res.status(400).json({ error: 'content required' });

    const internalToken = process.env.INTERNAL_API_TOKEN || '';

    // Calculate chunk count
    const CHUNK_SIZE = 500, OVERLAP = 50;
    let chunkCount = 0;
    for (let i = 0; i < content.length; i += CHUNK_SIZE - OVERLAP) {
        chunkCount++;
        if (i + CHUNK_SIZE >= content.length) break;
    }

    try {
        // Write to S3 + DB via Blazor API
        const resp = await fetch(`${FAIT_BASE_URL}/api/memory/write`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ userId, slug: 'imported-memory', title: 'Imported Memory', content }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
            const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
            throw new Error(`memory/write failed (${resp.status}): ${safeText}`);
        }

        // Upsert into pgvector
        await upsertMemoryChunks(userId, 'memory/imported-memory.md', content);

        res.json({ success: true, chunks: chunkCount });
    } catch (err) {
        console.error('[harness] import-memory error:', err.message);
        res.json({ success: false, error: err.message });
    }
});
```

---

## Part 2: Blazor UI Changes (`fait/src/FortressAI.Web/Components/Pages/Memory.razor`)

### 2a. Add "Import Memory" button

In the button row (the `div` with `display: flex; gap: 8px; margin-bottom: 8px;` around line 22), ADD a third button after the Export button:

```razor
<MudButton Variant="Variant.Outlined"
           Color="Color.Secondary"
           StartIcon="@Icons.Material.Filled.CloudDownload"
           OnClick="OpenImportDialog">
    Import
</MudButton>
```

### 2b. Add Import Dialog

Add a new overlay/dialog after the Delete Confirmation Dialog section (at the bottom of the HTML, before `@code {`).

The dialog has two steps controlled by `_importStep` (1 or 2):

```razor
<!-- Import Memory Dialog (ADO#4053) -->
@if (_showImportDialog)
{
    <MudOverlay Visible="true" DarkBackground="true" ZIndex="1200">
        <MudCard Style="min-width: 500px; max-width: 700px; padding: 16px;">
            <MudCardHeader>
                <CardHeaderContent>
                    <MudText Typo="Typo.h6">Import Memory</MudText>
                </CardHeaderContent>
            </MudCardHeader>
            <MudCardContent>
                @if (_importStep == 1)
                {
                    <MudText Typo="Typo.body2" Class="mb-3">
                        Copy this prompt and paste it into Claude, ChatGPT, or any other AI to export your memories:
                    </MudText>
                    <MudPaper Elevation="0" Class="pa-3 mb-3" Style="background: var(--mud-palette-background-grey); border-radius: 4px;">
                        <MudText Typo="Typo.body2" Style="font-family: monospace; white-space: pre-wrap;">@_importPrompt</MudText>
                    </MudPaper>
                    <MudButton Variant="Variant.Outlined"
                               Color="Color.Primary"
                               StartIcon="@Icons.Material.Filled.ContentCopy"
                               OnClick="CopyImportPromptAsync">
                        @(_importPromptCopied ? "Copied!" : "Copy Prompt")
                    </MudButton>
                }
                else
                {
                    <MudText Typo="Typo.body2" Class="mb-3">
                        Paste the exported content from your other AI below:
                    </MudText>
                    <MudTextField @bind-Value="_importContent"
                                  Label="Paste exported memory here"
                                  Variant="Variant.Outlined"
                                  Lines="12"
                                  FullWidth="true"
                                  AutoGrow="true"
                                  Disabled="@_importLoading" />
                    @if (_importError != null)
                    {
                        <MudAlert Severity="Severity.Error" Class="mt-2">@_importError</MudAlert>
                    }
                }
            </MudCardContent>
            <MudCardActions>
                @if (_importStep == 1)
                {
                    <MudButton Variant="Variant.Filled" Color="Color.Primary"
                               OnClick="() => _importStep = 2">
                        Next: Paste Content
                    </MudButton>
                }
                else
                {
                    <MudButton Variant="Variant.Filled" Color="Color.Primary"
                               OnClick="RunImportAsync"
                               Disabled="@(string.IsNullOrWhiteSpace(_importContent) || _importLoading)">
                        @if (_importLoading)
                        {
                            <MudProgressCircular Size="Size.Small" Indeterminate="true" Class="mr-1" />
                            <span>Importing...</span>
                        }
                        else
                        {
                            <span>Import</span>
                        }
                    </MudButton>
                    <MudButton Variant="Variant.Outlined"
                               OnClick="() => _importStep = 1"
                               Disabled="@_importLoading">
                        Back
                    </MudButton>
                }
                <MudButton Variant="Variant.Text"
                           OnClick="CloseImportDialog"
                           Disabled="@_importLoading">
                    Cancel
                </MudButton>
            </MudCardActions>
        </MudCard>
    </MudOverlay>
}
```

### 2c. Add @code fields and methods

In the `@code { }` block, add these fields after `private bool _exportLoading = false;`:

```csharp
// Import memory (ADO#4053)
private bool _showImportDialog = false;
private int _importStep = 1;
private string _importContent = string.Empty;
private bool _importLoading = false;
private bool _importPromptCopied = false;
private string? _importError = null;
private const string _importPrompt = "Export all of my stored memories and any context you've learned about me from past conversations. Preserve my words verbatim where possible, especially instructions and preferences.";
```

Add these methods at the bottom of the `@code { }` block (before `DisposeAsync`):

```csharp
private void OpenImportDialog()
{
    _showImportDialog = true;
    _importStep = 1;
    _importContent = string.Empty;
    _importLoading = false;
    _importPromptCopied = false;
    _importError = null;
}

private void CloseImportDialog()
{
    _showImportDialog = false;
}

private async Task CopyImportPromptAsync()
{
    // JS interop would be ideal but for simplicity use Snackbar — the text is shown on screen
    // If JSRuntime is available, inject it and use navigator.clipboard; otherwise fallback
    _importPromptCopied = true;
    Snackbar.Add("Prompt copied — paste into your AI.", Severity.Info);
    await Task.Delay(2000);
    _importPromptCopied = false;
}

private async Task RunImportAsync()
{
    if (string.IsNullOrWhiteSpace(_importContent)) return;
    _importLoading = true;
    _importError = null;
    StateHasChanged();
    try
    {
        var result = await MemoryService.ImportMemoryAsync(Session.UserId, _importContent);
        _showImportDialog = false;
        await LoadTopicsAsync();
        Snackbar.Add($"Import complete — {result.Chunks} chunks added to memory.", Severity.Success);
    }
    catch (Exception ex)
    {
        _importError = $"Import failed: {ex.Message}";
    }
    finally
    {
        _importLoading = false;
        StateHasChanged();
    }
}
```

### 2d. JavaScript clipboard interop

For `CopyImportPromptAsync` to actually copy to clipboard, inject `IJSRuntime` at the top of Memory.razor:

Add this inject after the existing injects:
```razor
@inject IJSRuntime JS
```

Then update `CopyImportPromptAsync`:
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

---

## Part 3: MemoryFileService / IMemoryFileService

Add `ImportMemoryAsync` to the service layer so the Blazor page can call the harness.

### 3a. New model record

In `fait/src/FortressAI.Web/Services/IMemoryFileService.cs`, add to the interface:

```csharp
/// <summary>Sends raw text to the harness import-memory endpoint. Returns chunk count.</summary>
Task<ImportMemoryResult> ImportMemoryAsync(Guid userId, string content, CancellationToken ct = default);
```

Also define a result record (can be in the same file or a shared models file):
```csharp
public record ImportMemoryResult(int Chunks);
```

### 3b. Service implementation in `MemoryFileService.cs`

The implementation must call the harness `POST /import-memory` endpoint. Get the harness URL from config key `HARNESS_URL` (defaulting to `http://localhost:3000` for local dev).

```csharp
public async Task<ImportMemoryResult> ImportMemoryAsync(Guid userId, string content, CancellationToken ct = default)
{
    var harnessUrl = _config["HARNESS_URL"] ?? "http://localhost:3000";
    using var http = new HttpClient();
    var payload = new { userId = userId.ToString(), content };
    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    var response = await http.PostAsync($"{harnessUrl}/import-memory", httpContent, ct);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadAsStringAsync(ct);
    var result = System.Text.Json.JsonSerializer.Deserialize<ImportMemoryResponse>(body);
    if (result?.Success != true)
        throw new InvalidOperationException(result?.Error ?? "Import failed");
    return new ImportMemoryResult(result.Chunks);
}

private record ImportMemoryResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("success")] bool Success,
    [property: System.Text.Json.Serialization.JsonPropertyName("chunks")] int Chunks,
    [property: System.Text.Json.Serialization.JsonPropertyName("error")] string? Error
);
```

**Note:** HttpClient should ideally be injected via IHttpClientFactory. Check if the project uses IHttpClientFactory — if `_httpClientFactory` is available on other services, use it. If not (MemoryFileService doesn't have it in its current constructor), either:
- Add `IHttpClientFactory` to the constructor (preferred), OR
- Use `new HttpClient()` scoped to the method (acceptable for this case since it's a short-lived call)

Look at the current `MemoryFileService.cs` constructor — it has `IDbContextFactory<AppDbContext>`, `IAmazonS3`, `IConfiguration`, `ILogger`. Add `IHttpClientFactory httpClientFactory` as a 5th param and store as `_httpClientFactory`. Update the DI registration if needed (IHttpClientFactory is registered by default via `builder.Services.AddHttpClient()`).

---

## Files to Modify

1. `fait/agent-harness/harness-server.js` — add `POST /import-memory` endpoint (after write_memory handler ~line 1220)
2. `fait/src/FortressAI.Web/Components/Pages/Memory.razor` — add Import button + dialog + code
3. `fait/src/FortressAI.Web/Services/IMemoryFileService.cs` — add `ImportMemoryAsync` method + `ImportMemoryResult` record
4. `fait/src/FortressAI.Web/Services/MemoryFileService.cs` — implement `ImportMemoryAsync` (add `IHttpClientFactory` to constructor)

## Constraints
- NO inline styles — use MudBlazor `Class=` with CSS vars (follow the existing pattern in Memory.razor)
- NO new DB columns needed — pgvector upsert path already exists
- NO migration needed
- Follow the existing dialog pattern (MudOverlay + MudCard) already used for New Topic and Delete Confirmation
- `upsertMemoryChunks` is already defined in harness-server.js — do NOT redefine it, just call it
- The harness `FAIT_BASE_URL` and `INTERNAL_API_TOKEN` env vars are already wired — use them
- HARNESS_URL config key for Blazor → harness communication (check if it exists or use `http://localhost:3000` default)

## Acceptance Criteria
1. Import button visible on /memory page (in the top button row)
2. Modal has two steps: copy prompt → paste result
3. Pasted content is embedded and stored in pgvector via upsertMemoryChunks
4. Upsert (no overwrite of existing topics — imported-memory slug is its own topic)
5. Success confirmation shows chunk count

## Output
When complete, emit a one-line summary: "ADO#4053 DONE — [files changed list]"
Do not commit.
