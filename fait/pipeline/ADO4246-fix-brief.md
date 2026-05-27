# Fix Brief: ADO#4246 — Review Cycle 1 Fixes

## Repository
`/home/fredw/projects/fip/fait/`

## Context
This is a fix pass after Clint's review of commit `2de97a86`. Two issues must be fixed.

---

## Fix 1 (C1 — Critical): Remove `allow-same-origin` from iframe sandbox

**File:** `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`

The iframe sandbox attribute currently reads:
```
sandbox="allow-scripts allow-same-origin allow-popups allow-forms allow-popups-to-escape-sandbox"
```

Remove `allow-same-origin`. The corrected sandbox attribute must be:
```
sandbox="allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox"
```

There is exactly one iframe in this file (around line 67). Change only that attribute value. Do not touch any other code.

---

## Fix 2 (I1 — Important): Wire component through `WorkspaceFileService.GetFilePreviewUrlAsync` (Option A)

**Goal:** Remove the direct `ICloudFrontSignedUrlService` dependency from the Razor component. The component should call `WorkspaceFileSvc.GetFilePreviewUrlAsync(s3Key)` instead of calling `CloudFrontSvc.GetSignedUrlAsync` directly. The service already encapsulates the CF vs S3 routing logic.

### Files to change:

#### A. `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`

1. **Remove** the `@inject ICloudFrontSignedUrlService CloudFrontSvc` injection line (top of file).
2. **Replace** the Office Online branch in `SelectArtifact` (in the `@code` block). The current logic is:

```csharp
else
{
    // Office Online requires a publicly accessible URL.
    // Use CloudFront signed URL (publicly resolvable) if available; otherwise store for fallback link.
    if (CloudFrontSvc.IsConfigured)
    {
        var signedUrl = await CloudFrontSvc.GetSignedUrlAsync(artifact.S3Key);
        if (signedUrl != null)
        {
            var encodedSrc = Uri.EscapeDataString(signedUrl);
            _previewUrl = $"https://view.officeapps.live.com/op/embed.aspx?src={encodedSrc}";
            _officeOnlineRawUrl = null;
        }
        else
        {
            _officeOnlineRawUrl = rawUrl;
            _previewUrl = null;
        }
    }
    else
    {
        _officeOnlineRawUrl = rawUrl;
        _previewUrl = null;
    }
}
```

Replace it with:

```csharp
else
{
    // Route through the service — it handles CF signed URL vs S3 presigned fallback.
    // GetFilePreviewUrlAsync returns a CloudFront signed URL when configured,
    // falling back to S3 presigned. Office Online requires a publicly accessible URL,
    // so only use inline embed when CloudFront is configured.
    var previewUrl = await WorkspaceFileSvc.GetFilePreviewUrlAsync(artifact.S3Key, expirySeconds: 3600);
    // Determine if the URL is a CF URL (not an S3 presigned URL) by checking for the S3 domain.
    // If the service returned a CloudFront URL, it will NOT contain ".s3." in the host.
    bool isCfUrl = !previewUrl.Contains(".s3.", StringComparison.OrdinalIgnoreCase)
                   && !previewUrl.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase);
    if (isCfUrl)
    {
        var encodedSrc = Uri.EscapeDataString(previewUrl);
        _previewUrl = $"https://view.officeapps.live.com/op/embed.aspx?src={encodedSrc}";
        _officeOnlineRawUrl = null;
    }
    else
    {
        // S3 presigned URLs are not publicly accessible; show fallback link instead.
        _officeOnlineRawUrl = rawUrl;
        _previewUrl = null;
    }
}
```

3. **Remove** the `@using FortressAI.Web.Services` using directive ONLY IF it is no longer needed after removing the CloudFrontSvc injection. Check — `IWorkspaceFileService` is also in `FortressAI.Web.Services`, so the using directive must stay. Do NOT remove it.

#### B. `src/FortressAI.Web/Services/IWorkspaceFileService.cs`

No changes needed — `GetFilePreviewUrlAsync` is already in the interface. Leave the file as-is.

#### C. `src/FortressAI.Web/Services/WorkspaceFileService.cs`

No changes needed — `GetFilePreviewUrlAsync` is already implemented. Leave the file as-is.

---

## What NOT to change

- Do NOT modify PDF preview logic
- Do NOT modify the download flow
- Do NOT modify the `CloudFrontSignedUrlService` or `ICloudFrontSignedUrlService`
- Do NOT modify `Program.cs`
- Do NOT modify any other files

---

## Acceptance Criteria

1. The iframe `sandbox` attribute does NOT contain `allow-same-origin`
2. `ArtifactSidebarPanel.razor` does NOT inject or reference `ICloudFrontSignedUrlService` or `CloudFrontSvc` anywhere
3. The Office Online preview path calls `WorkspaceFileSvc.GetFilePreviewUrlAsync` instead of `CloudFrontSvc.GetSignedUrlAsync`
4. Build compiles with 0 errors
5. PDF preview path unchanged (still uses `GetPresignedDownloadUrlAsync` directly → `_previewUrl = rawUrl`)

---

## Output instructions

After making all changes, run:
```bash
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj 2>&1 | tail -20
```

Report the build result (errors count). Do NOT commit.
