# ADO4570 R2 — Word preview (docx-preview) fix pass

You are fixing 5 issues in the FAIT project (`/home/fredw/projects/fip/fait`). Apply ALL fixes. Do NOT change anything outside these fixes. Zero build errors required.

---

## Fix 1 — DocxPreviewPanel.razor: NavigationManager absolute URL + two-pass OnAfterRenderAsync

**File:** `src/FortressAI.Web/Components/Chat/DocxPreviewPanel.razor`

**Current state:** Uses `HttpClientFactory.CreateClient()` (unnamed, no BaseAddress) with a relative URL. Calls JS immediately after `StateHasChanged()` without waiting for DOM update.

**Full replacement for the entire file:**

```razor
@inject ArtifactPreviewService PreviewSvc
@inject AuthenticationStateProvider AuthStateProvider
@inject IJSRuntime JSRuntime
@inject IHttpClientFactory HttpClientFactory
@inject NavigationManager NavManager
@using FortressAI.Shared.Models
@using FortressAI.Web.Services
@using Microsoft.AspNetCore.Components.Authorization
@implements IAsyncDisposable

<div class="docx-preview-panel">
    @if (_loading)
    {
        <div class="docx-preview-panel__loading">
            <MudProgressCircular Indeterminate="true" />
            <MudText Typo="Typo.body2" Color="Color.Secondary" Class="mt-2">Loading document...</MudText>
        </div>
    }
    else if (_error != null)
    {
        <div class="docx-preview-panel__error">
            <MudIcon Icon="@Icons.Material.Outlined.ErrorOutline" Size="Size.Large" />
            <MudText Typo="Typo.body2" Color="Color.Secondary">@_error</MudText>
        </div>
    }
    else
    {
        <div id="@_containerId" class="docx-preview-panel__content"></div>
    }
</div>

@code {
    [Parameter, EditorRequired] public WorkspaceUpload Artifact { get; set; } = default!;

    private bool _loading = true;
    private string? _error;
    private byte[]? _pendingBytes;
    private string _containerId = $"docx-{Guid.NewGuid():N}";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await FetchBytesAsync();
            return;
        }

        // Second pass: bytes are ready and container div is now in DOM
        if (_pendingBytes != null)
        {
            var bytes = _pendingBytes;
            _pendingBytes = null;
            await JSRuntime.InvokeVoidAsync("docxPreviewInterop.render", bytes, _containerId);
        }
    }

    private async Task FetchBytesAsync()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdStr = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                _error = "Authentication error. Please refresh.";
                _loading = false;
                StateHasChanged();
                return;
            }
            var (token, expires) = PreviewSvc.GenerateToken(Artifact.Id, userId);
            var relativeUrl = $"/api/artifacts/{Artifact.Id}/preview?token={Uri.EscapeDataString(token)}&expires={expires}";
            var absoluteUrl = NavManager.ToAbsoluteUri(relativeUrl).ToString();
            using var http = HttpClientFactory.CreateClient();
            _pendingBytes = await http.GetByteArrayAsync(absoluteUrl);
            _loading = false;
            StateHasChanged(); // triggers second OnAfterRenderAsync pass
        }
        catch (Exception)
        {
            _error = "Failed to load document preview. Try downloading the file.";
            _loading = false;
            StateHasChanged();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

<style>
.docx-preview-panel {
    width: 100%;
    height: 100%;
    overflow: auto;
}
.docx-preview-panel__loading,
.docx-preview-panel__error {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    height: 100%;
    gap: 12px;
    padding: 24px;
    text-align: center;
    color: var(--color-text-secondary);
}
.docx-preview-panel__content {
    padding: 16px;
    background: white;
    min-height: 100%;
}
</style>
```

---

## Fix 2 — XlsxPreviewPanel.razor: NavigationManager absolute URL

**File:** `src/FortressAI.Web/Components/Chat/XlsxPreviewPanel.razor`

**Changes needed:**
1. Add `@inject NavigationManager NavManager` after the existing `@inject IHttpClientFactory HttpClientFactory` line
2. In `LoadAndRenderAsync()`, replace:
   ```csharp
   var url = $"/api/artifacts/{ArtifactId}/preview?token={Uri.EscapeDataString(token)}&expires={expires}";

   using var http = HttpClientFactory.CreateClient();
   _fileBytes = await http.GetByteArrayAsync(url);
   ```
   With:
   ```csharp
   var relativeUrl = $"/api/artifacts/{ArtifactId}/preview?token={Uri.EscapeDataString(token)}&expires={expires}";
   var absoluteUrl = NavManager.ToAbsoluteUri(relativeUrl).ToString();
   using var http = HttpClientFactory.CreateClient();
   _fileBytes = await http.GetByteArrayAsync(absoluteUrl);
   ```

Note: XlsxPreviewPanel already calls JS in `OnAfterRenderAsync` via the `firstRender` path and the `OnTabChanged` path. The existing pattern fires JS after `LoadAndRenderAsync()` sets `_loading = false` and calls `StateHasChanged()`, then calls `RenderSheetAsync(0)` immediately after. The DOM race condition is less severe here because the container is only shown after `_sheetNames.Count > 0` is set, but the same DOM-readiness concern applies. However, the brief only explicitly requires the NavManager fix for XlsxPreviewPanel — do NOT restructure the XlsxPreviewPanel render flow; only fix the URL.

---

## Fix 3 — App.razor: Fix wrong static asset path for xlsx-preview-interop.js

**File:** `src/FortressAI.Web/Components/App.razor`

The file at `wwwroot/js/` contains: `chat.js`, `docx-preview-interop.js`, `xlsx-preview-interop.js`

The script tag for xlsx uses the wrong `/_content/FortressAI.Web/` prefix. Files in the host app's `wwwroot/js/` are served at `js/filename.js` directly.

**Change:**
```html
<!-- Before -->
<script src="/_content/FortressAI.Web/js/xlsx-preview-interop.js"></script>

<!-- After -->
<script src="js/xlsx-preview-interop.js"></script>
```

The `docx-preview-interop.js` script tag is already correct: `<script src="js/docx-preview-interop.js"></script>` — do NOT change it.

---

## Fix 4 — ArtifactPreviewPanel.razor: Remove dead Office Online fallback

**File:** `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`

In `LoadPreview()`, remove the dead `officeapps.live.com` code path. The current code is:

```csharp
if (_canPreview && !_isDocx)
{
    if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        _presignedUrl = await GetProxyPreviewUrlAsync(artifact);
    }
    else
    {
        var encoded = Uri.EscapeDataString(rawUrl);
        _presignedUrl = $"https://view.officeapps.live.com/op/embed.aspx?src={encoded}";
    }
}
```

Replace with (remove the `else` branch entirely):

```csharp
if (_canPreview && !_isDocx)
{
    if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        _presignedUrl = await GetProxyPreviewUrlAsync(artifact);
    }
}
```

Also remove `IHttpClientFactory` from the injections at the top if it's injected but no longer used anywhere in this file. Check first — `ArtifactPreviewPanel.razor` injects `@inject IHttpClientFactory HttpClientFactory` but the file doesn't appear to use it directly (the DOCX/XLSX panels handle their own HTTP). Remove that injection if unused.

---

## Fix 5 — ArtifactSidebarPanel.razor: Remove wasteful presign call for DOCX in SelectArtifact

**File:** `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`

In `SelectArtifact()`, the current flow is:
1. Check `IsXlsxType` → return early (correct, already done)
2. `var rawUrl = await WorkspaceFileSvc.GetPresignedDownloadUrlAsync(...)` — this is called for EVERY non-XLSX previewable artifact including DOCX
3. Then `IsDocxType` check → DocxPreviewPanel handles its own loading — the `rawUrl` is discarded for DOCX

**Fix:** Short-circuit for DOCX *before* the presign call, same as XLSX:

```csharp
if (IsXlsxType(artifact))
{
    // XlsxPreviewPanel handles its own loading
    _previewLoading = false;
    StateHasChanged();
    return;
}

if (IsDocxType(artifact))
{
    // DocxPreviewPanel handles its own loading — no presign needed
    _previewLoading = false;
    StateHasChanged();
    return;
}

var rawUrl = await WorkspaceFileSvc.GetPresignedDownloadUrlAsync(artifact.S3Key, expiryMinutes: 30);
var ext = Path.GetExtension(artifact.Filename);
if (IsTextPreviewable(artifact))
{
    try
    {
        using var http = HttpClientFactory.CreateClient();
        var content = await http.GetStringAsync(rawUrl);
        _textPreviewContent = content.Length > 200_000
            ? content[..200_000] + "\n\n[... truncated at 200KB ...]"
            : content;
    }
    catch (Exception ex)
    {
        _textPreviewContent = null;
        _previewError = "Could not load preview. Try downloading.";
    }
}
else if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
{
    _previewUrl = await GetProxyPreviewUrlAsync(artifact);
}
else
{
    // Office preview removed — Epic 12 will provide native DOCX/XLSX/PPTX previews
    _previewUrl = null;
    _previewError = null;
}
```

Remove the now-dead `else if (IsDocxType(artifact))` block that was previously in the middle.

---

## Build verification

After making all changes, run:
```bash
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj --no-restore 2>&1 | tail -20
```

If there are build errors, fix them before finishing.

---

## Git commit

After confirming 0 build errors:
```bash
cd /home/fredw/projects/fip/fait && git add -A && git commit -m "fix(fait#ADO4570): R2 — NavManager absolute URL, two-pass OnAfterRender, fix script path, remove dead code"
```

---

## Output

After committing, output:
1. The full git commit hash
2. A summary of each file changed and what was done
3. Any build warnings (not errors) that exist
