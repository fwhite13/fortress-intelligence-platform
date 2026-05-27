# ADO#4246 — CloudFront Signed URLs — Review Brief (Cycle 2)

You are performing an adversarial code review for **Cycle 2** of ADO#4246. This is a targeted fix verification: two issues were found in Cycle 1 (C1 and I1). Tony applied fixes in commit `65699baa`. Your job is to:

1. Verify the C1 fix is correct (no regressions, attribute string is exactly right)
2. Verify the I1 fix is correct and sound (the URL detection heuristic, no dead code, no regressions)
3. Quick re-check the full 8-file changeset for anything Cycle 1 missed
4. Confirm no regressions introduced by the Cycle 2 changes

---

## Background: What Changed in Cycle 2

**Only one file changed:** `fait/src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`

**C1 fix:**
- Removed `allow-same-origin` from the Office Online iframe sandbox attribute
- New sandbox: `sandbox="allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox"`

**I1 fix (Option A):**
- Removed `@inject ICloudFrontSignedUrlService CloudFrontSvc` from the component
- Component now calls `WorkspaceFileSvc.GetFilePreviewUrlAsync(artifact.S3Key, expirySeconds: 3600)`
- CF URL detection heuristic: `bool isCfUrl = !previewUrl.Contains(".s3.", StringComparison.OrdinalIgnoreCase) && !previewUrl.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase)`
- If `isCfUrl` is true → inline Office Online iframe embed
- If `isCfUrl` is false → fallback link only

---

## Files to Read

1. `fait/src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor` — PRIMARY: verify C1 and I1 fixes
2. `fait/src/FortressAI.Web/Services/WorkspaceFileService.cs` — verify `GetFilePreviewUrlAsync` logic, confirm no dead code
3. `fait/src/FortressAI.Web/Services/IWorkspaceFileService.cs` — confirm interface is correct
4. `fait/src/FortressAI.Web/Services/CloudFrontSignedUrlService.cs` — verify CF service is still correct; confirm it's still registered
5. `fait/src/FortressAI.Web/Services/ICloudFrontSignedUrlService.cs` — interface check
6. `fait/src/FortressAI.Web/Program.cs` — confirm `ICloudFrontSignedUrlService` still registered (needed by `WorkspaceFileService`)

---

## Specific Checks Required

### C1 Verification
1. Read the current iframe in `ArtifactSidebarPanel.razor`. Confirm the sandbox attribute is exactly: `allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox`
2. Confirm `allow-same-origin` is completely absent from the file
3. Confirm no other iframe tags exist in the file that might still have `allow-same-origin`

### I1 Verification
1. Confirm `@inject ICloudFrontSignedUrlService CloudFrontSvc` is NO LONGER in the component
2. Confirm `CloudFrontSvc.` is NO LONGER referenced anywhere in the component
3. Confirm `WorkspaceFileSvc.GetFilePreviewUrlAsync(artifact.S3Key, expirySeconds: 3600)` is called in the Office Online branch
4. Confirm `WorkspaceFileService.GetFilePreviewUrlAsync` exists and correctly:
   - Returns a CF signed URL when `_cloudFront.IsConfigured` is true (and `GetSignedUrlAsync` succeeds)
   - Falls back to `GetPresignedDownloadUrlAsync` (S3 presigned) when CF is not configured or fails
5. Confirm `IWorkspaceFileService` declares `GetFilePreviewUrlAsync` — no orphaned implementation

### URL Detection Heuristic Analysis (I1 — CRITICAL TO VERIFY)
The component uses this heuristic to decide inline embed vs fallback:
```csharp
bool isCfUrl = !previewUrl.Contains(".s3.", StringComparison.OrdinalIgnoreCase)
               && !previewUrl.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase);
```

Answer the following with evidence from the code:
- **Q1:** Can a CloudFront signed URL ever contain `.s3.` or `amazonaws.com`? (Expected: NO — CF URLs use `*.cloudfront.net` domain. Verify by checking AmazonCloudFrontUrlSigner usage and the `_distributionDomain` config.)
- **Q2:** Can an S3 presigned URL be a CloudFront URL? (Expected: NO — `GetPresignedDownloadUrlAsync` calls `_s3.GetPreSignedURL` which always returns an S3 domain URL.)
- **Q3:** False positive risk — can any non-S3, non-CF URL be returned by `GetFilePreviewUrlAsync`? Look at all return paths in the method.
- **Q4:** What happens if `GetFilePreviewUrlAsync` throws? Is there exception handling in the component? (Check the `try/catch` block wrapping the Office Online branch.)
- **Q5:** What happens if `GetSignedUrlAsync` returns null (e.g., Secrets Manager failure)? Does the service fall back to S3 presigned? Does this cause a null reference in the component?

### Regression Check (Cycle 2 changes only)
1. Does the I1 fix change behavior when CF is configured and working? (Should be equivalent — same CF signed URL is returned, same encoding happens.)
2. Does the I1 fix change behavior when CF is NOT configured? (Before: fallback used `rawUrl` which is an S3 presigned URL. After: `GetFilePreviewUrlAsync` returns S3 presigned URL but assigns it to `_officeOnlineRawUrl = rawUrl`. Is `rawUrl` still the correct value? Verify what `rawUrl` is set to in context.)
3. Is there any case where `previewUrl` could be null (causing NullReferenceException in the `.Contains` call)?

### Cycle 1 Remaining Items (Quick Re-check)
The following were NOT fixed by Tony (they were Nitpicks from Cycle 1). Confirm they are still present as-was:
- N1: `_cachedPem` not `volatile` — should still be present, not blocking
- N2: No partial-config warning log — should still be present, not blocking
- N3: PEM in managed heap — inherent limitation, not actionable

### Full Changeset Re-check
Re-read all 8 originally modified files for anything Cycle 1 missed. Specifically look for:
- `Program.cs` — `ICloudFrontSignedUrlService` registration still present? (Required because WorkspaceFileService depends on it now via constructor injection)
- `appsettings.json` / `appsettings.Development.json` — no real keys/domains introduced since Cycle 1
- Any remaining references to `CloudFrontSvc` in any `.razor` or `.cs` file (other than the service itself)

---

## Pass/Fail Criteria

**PASS conditions (both must be true):**
1. C1 fix is correct — `allow-same-origin` is gone, sandbox string is exactly right, no other iframes affected
2. I1 fix is correct — CF injection removed, service encapsulates routing, URL detection heuristic is sound, no null-ref risk, fallback behavior preserved

**FAIL / NEEDS-CHANGES conditions:**
- `allow-same-origin` still present anywhere in the component
- `CloudFrontSvc` still injected or referenced in component
- `GetFilePreviewUrlAsync` has a null-return path that would cause NullReferenceException in component
- URL heuristic can false-positive (treat a valid CF URL as S3 or vice versa) in a way that breaks preview
- `ICloudFrontSignedUrlService` no longer registered in DI (would break `WorkspaceFileService` constructor)

---

## Output Format

Provide:
1. **C1 Verification Result** — Pass or Fail, with the exact sandbox string found
2. **I1 Verification Result** — Pass or Fail, with answers to Q1-Q5
3. **Regression Check Result** — Pass or Fail, with any issues found
4. **Full Re-check Result** — Anything new found that Cycle 1 missed
5. **Overall verdict** — PASS, NEEDS-CHANGES, or FAIL with brief justification

Be adversarial. Look for edge cases. Do not assume correctness — verify it.
