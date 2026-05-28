# Security Review Brief: ADO4570 — Word Preview (docx-preview)

You are performing an adversarial security review of a new DOCX preview feature added to a Blazor Server app (ASP.NET 8, MudBlazor).

## Scope: Changed Files (Low-Medium Risk — UI feature, no backend/DB/harness changes)

### File 1: `src/FortressAI.Web/Components/Chat/DocxPreviewPanel.razor`
This is the new Razor component. Key behaviors:
1. Calls `AuthStateProvider.GetAuthenticationStateAsync()` to get the current user's ID
2. Calls `PreviewSvc.GenerateToken(Artifact.Id, userId)` server-side
3. Constructs a relative URL: `/api/artifacts/{Artifact.Id}/preview?token={Uri.EscapeDataString(token)}&expires={expires}`
4. Converts to absolute URL via `NavManager.ToAbsoluteUri(relativeUrl).ToString()`
5. Uses `HttpClientFactory.CreateClient()` to fetch bytes from that absolute URL
6. Passes bytes to JS interop: `docxPreviewInterop.render(bytes, containerId)`
7. `_containerId` is generated as `$"docx-{Guid.NewGuid():N}"` — random, not user-controlled

### File 2: `wwwroot/js/docx-preview-interop.js`
```javascript
window.docxPreviewInterop = {
    render: async function (arrayBuffer, containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        await docx.renderAsync(arrayBuffer, container, null, {
            className: 'docx-preview-content',
            inWrapper: true,
            ignoreWidth: false,
            ignoreHeight: false,
            ignoreFonts: false,
            breakPages: true,
            useBase64URL: false
        });
    }
};
```

### File 3: `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
Modified to:
- Add `IsDocxType()` detection based on file extension / MIME type
- Route DOCX to `<DocxPreviewPanel Artifact="@_selectedArtifact" />` instead of the old Office Online iframe
- Short-circuit `SelectArtifact()` for DOCX (DocxPreviewPanel handles its own loading)

### File 4: `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`
Modified to:
- Add `_isDocx` flag and `_currentArtifactUpload` state
- Route DOCX to `<DocxPreviewPanel Artifact="@_currentArtifactUpload" />`
- All `officeapps.live.com` references previously present have been REMOVED

### File 5: `src/FortressAI.Web/Components/App.razor`
Changed:
- Fixed script path from `/_content/FortressAI.Web/js/xlsx-preview-interop.js` to `js/xlsx-preview-interop.js`
- Loads CDN scripts:
  - `https://cdn.jsdelivr.net/npm/docx-preview@latest/dist/docx-preview.min.js`
  - `https://cdn.jsdelivr.net/npm/xlsx@latest/dist/xlsx.full.min.js`

### File 6: `src/FortressAI.Web/Services/ArtifactPreviewService.cs` (read-only context)
```csharp
public (string token, long expires) GenerateToken(Guid artifactId, Guid userId)
{
    var expires = DateTimeOffset.UtcNow.AddSeconds(TokenValiditySeconds).ToUnixTimeSeconds();
    var payload = $"{artifactId}:{userId}:{expires}";
    var token = ComputeHmac(payload);
    return (token, expires);
}
// Uses HMAC-SHA256, constant-time comparison, 15-minute expiry, base64url encoding
// Secret sourced from config["PREVIEW_TOKEN_SECRET"] — throws if not configured
```

---

## Security Questions to Answer

### Q1: HMAC Token Exposure
Is the HMAC token generated entirely server-side and NOT exposed beyond the fetch URL?
- Confirm: is the token ever written to a JS variable, rendered into HTML markup, or exposed in a way the user could copy/reuse beyond the fetch?
- In `DocxPreviewPanel.razor`: the token is used in `relativeUrl` which is passed to `http.GetByteArrayAsync(absoluteUrl)` — does this URL appear in any rendered HTML or JS scope accessible to the end user?

### Q2: docx-preview-interop.js — HTML injection risk
- Is rendered HTML sandboxed within the `container` div only?
- Is there any `eval()`, `document.write()`, dynamic `<script>` injection, or `innerHTML` assignment outside of what `docx.renderAsync()` controls?
- Is the `arrayBuffer` coming from a server-controlled source only?
- Is `containerId` user-controlled in any way?

### Q3: Source of bytes
Confirm: the bytes rendered by `docx.renderAsync()` originate from the server-side proxy fetch (`/api/artifacts/{id}/preview`) and NOT from any user-supplied URL, query param, or input field.

### Q4: NavManager.ToAbsoluteUri() — open redirect / path injection
- Is the relative URL fully server-constructed with no user-supplied path segments?
- `Artifact.Id` is a `Guid` — confirm it cannot be manipulated to inject path traversal or extra query params
- `token` is HMAC output, URL-escaped — confirm no injection vector
- `expires` is a `long` Unix timestamp — confirm no injection vector

### Q5: CSRF
The preview fetch is a GET using a short-lived HMAC token. Does the preview endpoint make any state changes? If it's read-only, CSRF via GET is low risk. Confirm.

### Q6: CDN @latest floating versions
Both `docx-preview@latest` and `xlsx@latest` use floating version tags on jsdelivr.net. This means any new release can change the behavior without a code change.
- Rate this as a supply chain risk
- Recommend pinning to specific versions

### Q7: ArtifactPreviewPanel.razor — dead code cleanup
Confirm all `officeapps.live.com` references (or any external Office Online / viewer service redirect URLs) have been removed. Old code used Office Online iframe which would have been an open redirect risk.

### Q8: WorkspaceUpload object constructed server-side
In `ArtifactPreviewPanel.razor`, a `WorkspaceUpload` object is constructed:
```csharp
_currentArtifactUpload = new WorkspaceUpload
{
    Id = artifact.Id,
    S3Key = artifact.S3Key,
    Filename = artifact.Filename,
    MimeType = artifact.MimeType
};
```
The `artifact` here comes from `LayoutState.CurrentArtifact` (an `ArtifactRef`). Confirm this is server-controlled state, not directly deserialized from a user-supplied body.

### Q9: Error handling — information leakage
The catch block in `FetchBytesAsync()`:
```csharp
catch (Exception)
{
    _error = "Failed to load document preview. Try downloading the file.";
```
Generic error message — confirm no exception details leaked to UI.

---

## Pass Criteria
- CLEAR: No critical or high issues; low/medium findings documented
- WARN: Medium issues present that should be fixed before broad deployment  
- BLOCK: Critical or high issues; do not ship until resolved

## Files to Read
Read these files directly (they are in the current repository):
1. `src/FortressAI.Web/Components/Chat/DocxPreviewPanel.razor`
2. `src/FortressAI.Web/wwwroot/js/docx-preview-interop.js`
3. `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
4. `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`
5. `src/FortressAI.Web/Components/App.razor`
6. `src/FortressAI.Web/Services/ArtifactPreviewService.cs`

Read each file in full before answering. Answer each numbered question explicitly. Report any additional findings not covered by the questions above.
