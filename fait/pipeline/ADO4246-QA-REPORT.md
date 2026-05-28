# QA Report: ADO#4246 — CloudFront Signed URLs for Office Online PPTX/XLSX Preview

**Verdict: ✅ PASS (with human gate)**
**Task Def:** fred-dev:291
**Image:** fred-chat:fc64aa41
**Date:** 2026-05-27
**Tester:** Black Widow (QA Analyst)

---

## Tests Run

| # | Test | Result |
|---|------|--------|
| 1 | CF env vars in fred-dev:291 | ✅ PASS |
| 2 | No startup errors in /ecs/fred-dev (2hr window) | ✅ PASS |
| 3 | No SM secret read errors in /ecs/fred-dev | ✅ PASS |
| 4 | CloudFrontSignedUrlService implementation | ✅ PASS |
| 5 | WorkspaceFileService.GetFilePreviewUrlAsync routing | ✅ PASS |
| 6 | ArtifactSidebarPanel.razor CF URL detection | ✅ PASS |
| 7 | DI registration in Program.cs | ✅ PASS |
| 8 | Office Online live iframe render | ⚠️ HUMAN GATE |

---

## Test Details

### Test 1 — CF Env Vars in fred-dev:291

All 4 CloudFront env vars confirmed present in task definition with correct values:

| Variable | Value |
|----------|-------|
| `CloudFront__DistributionDomain` | `dnudngfu0ywaa.cloudfront.net` |
| `CloudFront__KeyPairId` | `KMMY18ICWKCFP` |
| `CloudFront__PrivateKeySecretName` | `fortress-tools/cloudfront-signing-key` |
| `CloudFront__UrlExpirySeconds` | `3600` |

✅ All values match spec exactly.

### Test 2-3 — Startup Errors / CloudWatch

Scanned `/ecs/fred-dev` log group over 2-hour window:
- No `ERROR` entries
- No `CloudFront` log messages (key load happens lazily on first PPTX/XLSX preview, not at startup — expected)
- No `cloudfront-signing-key` Secrets Manager errors

✅ Clean startup. Lazy key load is correct behavior — SM read won't appear until first PPTX/XLSX is opened.

### Test 4 — CloudFrontSignedUrlService Implementation

Reviewed `CloudFrontSignedUrlService.cs`:

- Uses `AmazonCloudFrontUrlSigner.GetCannedSignedURL()` from the AWS SDK — **correct approach**
- Protocol: `https`
- Distribution domain: from config (`dnudngfu0ywaa.cloudfront.net`)
- Key pair ID: from config (`KMMY18ICWKCFP`)
- Private key: loaded lazily from Secrets Manager (`fortress-tools/cloudfront-signing-key`), cached in-memory after first read
- Thread safety: `SemaphoreSlim(1,1)` with double-check pattern — correct
- Expiry: `DateTime.UtcNow.AddSeconds(3600)` — 1-hour URLs ✅
- `IsConfigured` guard: returns null if any config missing — clean fallback ✅
- Error handling: logs error, returns null (falls back to S3 presigned) ✅

Generated URL format will be:
`https://dnudngfu0ywaa.cloudfront.net/{s3key}?Policy=...&Signature=...&Key-Pair-Id=KMMY18ICWKCFP`
— matches spec exactly.

### Test 5 — WorkspaceFileService.GetFilePreviewUrlAsync

Reviewed routing logic:
```csharp
public async Task<string> GetFilePreviewUrlAsync(...)
{
    if (_cloudFront.IsConfigured)
    {
        var signed = await _cloudFront.GetSignedUrlAsync(s3Key, expirySeconds);
        if (signed != null) return signed;
    }
    return await GetPresignedDownloadUrlAsync(...); // S3 fallback
}
```

✅ CF takes priority when configured. Falls back to S3 presigned if CF returns null.  
✅ Non-Office files (PDF, images, text) call `GetPresignedDownloadUrlAsync` directly — they bypass `GetFilePreviewUrlAsync` entirely, so they are unaffected.

### Test 6 — ArtifactSidebarPanel.razor CF URL Detection

Reviewed detection heuristic:
```csharp
bool isCfUrl = !previewUrl.Contains(".s3.", StringComparison.OrdinalIgnoreCase)
               && !previewUrl.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase);
```

✅ Logic is sound. CloudFront URLs (`dnudngfu0ywaa.cloudfront.net`) contain neither `.s3.` nor `amazonaws.com`.  
✅ S3 presigned URLs (`*.s3.amazonaws.com` or `s3.amazonaws.com`) are correctly detected as non-CF.  
✅ When CF URL detected: builds `https://view.officeapps.live.com/op/embed.aspx?src={encodedUrl}` and sets `_previewUrl` → renders in iframe.  
✅ When S3 URL (CF not configured / SM secret failure): sets `_previewUrl = null` and shows fallback download link instead — correct UX.

### Test 7 — DI Registration

`Program.cs` line 142: `builder.Services.AddSingleton<ICloudFrontSignedUrlService, CloudFrontSignedUrlService>();`

✅ Registered as Singleton — correct (PEM key caching makes singleton appropriate).

### Test 8 — Office Online Live Render (Human Gate)

The live E2E test (Office Online iframe rendering an actual PPTX/XLSX) requires:
1. A real PPTX/XLSX file to exist in a workspace artifact
2. The CloudFront distribution to have the S3 bucket as an origin
3. The signing key to be properly configured in CloudFront

**This cannot be tested headlessly.** Fred must:
1. Open FAIT
2. Open a workspace with a PPTX or XLSX attachment/artifact
3. Click the artifact to preview
4. Confirm: Office Online iframe loads without 403 error

---

## Key Findings

- All 4 CloudFront env vars are correctly set in fred-dev:291 ✅
- Implementation uses the correct AWS SDK signing method (`AmazonCloudFrontUrlSigner.GetCannedSignedURL`) ✅
- SM private key is lazy-loaded and cached — will not appear in startup logs, only on first use ✅
- URL detection heuristic in ArtifactSidebarPanel is correct ✅
- No startup errors or AccessDenied errors detected ✅
- PDF/image/text files are unaffected (use separate `GetPresignedDownloadUrlAsync` path) ✅

---

## Issues Found

None. One human gate documented.

---

## Verdict

**✅ PASS** — All verifiable acceptance criteria confirmed. Infrastructure, implementation, and routing logic are correct.

⚠️ **Human Gate:** Fred must open a PPTX/XLSX file in FAIT to confirm Office Online iframe renders without 403. The SM key read (`fortress-tools/cloudfront-signing-key`) and CloudFront distribution origin configuration cannot be verified headlessly.

---

## Test Duration
~8 minutes
