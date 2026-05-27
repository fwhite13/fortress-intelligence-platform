# ADO#4246 — CloudFront Signed URLs Review Brief

You are performing an adversarial code review of commit `2de97a86` in `/home/fredw/projects/fip/fait`.

## Context
FAIT (Fortress AI Toolkit) is an ASP.NET 8 Blazor Server app.
This change adds CloudFront signed URL generation for Office Online file preview (PPTX/XLSX).

## Files to Read (at commit 2de97a86 or current HEAD — same commit)

Read all of the following files in full:

1. `src/FortressAI.Web/Services/ICloudFrontSignedUrlService.cs`
2. `src/FortressAI.Web/Services/CloudFrontSignedUrlService.cs`
3. `src/FortressAI.Web/Services/IWorkspaceFileService.cs`
4. `src/FortressAI.Web/Services/WorkspaceFileService.cs`
5. `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
6. `src/FortressAI.Web/Program.cs`
7. `src/FortressAI.Web/appsettings.json`
8. `src/FortressAI.Web/FortressAI.Web.csproj`

Use `git show 2de97a86 -- <file>` or just read the current file — they are the same commit.

---

## What to Verify

### A. CloudFrontSignedUrlService — Thread Safety & Lazy Init

1. How is the RSA key loading implemented? Look for `Lazy<Task<RSA>>`, `SemaphoreSlim`, `Task`, or a field set in an async method. Is initialization thread-safe under concurrent Blazor Server circuits?
2. Is there a risk of multiple Secrets Manager calls racing to initialize the key?
3. After loading, is the RSA key cached in a field that won't be re-fetched on every `GetSignedUrlAsync` call?
4. If Secrets Manager fails (exception), what happens? Is the exception cached (so every subsequent call also fails) or does it retry?
5. Does `GetCannedSignedURL` (from AWSSDK.CloudFront) accept a file path OR a stream/string for the private key? Verify the service is passing the right type — if it's writing a temp file to disk, that's a security problem.

### B. CloudFrontSignedUrlService — Key Security

1. Is the RSA private key PEM value written to disk anywhere (even as a temp file)?
2. Is the private key or any part of it included in log messages, exception messages, or error returns?
3. Is the RSA key disposed properly or does it leak?

### C. WorkspaceFileService — Fallback Logic

1. Read `GetFilePreviewUrlAsync`. When `IsConfigured = false`, what does it return? Is it an S3 presigned URL, a relative URL, null, or something else?
2. The build report says "graceful fallback" but the task brief says "NOT S3 presigned — verify what the fallback returns." Confirm what the fallback actually returns. If it returns an S3 presigned URL, that's fine (it won't 403 for all file types — just Office Online). But if it returns null and the caller doesn't handle null, that's a bug.
3. Is there proper null/exception handling if CloudFront URL generation throws?

### D. ArtifactSidebarPanel.razor — Security & Correctness

1. Read the full updated component. What is the exact `sandbox` attribute string on the Office Online iframe?
   - Was `allow-popups-to-escape-sandbox` actually added?
   - Is `allow-same-origin` present? (This would break the sandbox entirely — huge security issue)
   - Is `allow-scripts` present? (Required for Office Online to function)
   - Assess whether the sandbox attribute is appropriate for an Office Online embed

2. URL encoding: The Office Online URL pattern is:
   `https://view.officeapps.live.com/op/embed.aspx?src=<signed-url>`
   
   The signed URL contains `?Expires=...&Signature=...&Key-Pair-Id=...` — these are query parameters.
   When the signed URL is passed as the `src=` query parameter value, is it URL-encoded?
   If the signed URL's `?` and `&` are not encoded, the Office Online request will be malformed.
   
   Look for `Uri.EscapeDataString(signedUrl)` or `HttpUtility.UrlEncode(signedUrl)` or similar.
   If the URL is just string-concatenated without encoding, flag as Critical.

3. Extension checks: Verify that `.pptx`, `.xlsx` (and `.xls` per build report) are all correctly included in `PreviewableExtensions`. Check for case sensitivity — are both `.PPTX` and `.pptx` handled, or is there a `.ToLower()` / `.ToLowerInvariant()` before the check?

4. What does the component show when `IsConfigured = false` for a PPTX/XLSX file? Does it show a download link, an error, or nothing? Is it a broken/empty iframe?

5. XSS: The Office Online URL is constructed from a CloudFront signed URL which originates from an S3 key (controlled server-side). However, trace the full path — does any user-controlled input flow into the iframe `src`? Even if the S3 key came from user input (filename), verify it's not reflected directly.

### E. Program.cs — DI Registration

1. Confirm `IAmazonSecretsManager` is registered as singleton (or appropriate lifetime).
2. Confirm `ICloudFrontSignedUrlService` → `CloudFrontSignedUrlService` is singleton.
3. Singleton is correct for a service that caches in-memory state in Blazor Server. Verify there are no scoped dependencies injected into this singleton (captive dependency anti-pattern).
4. Check the order of registrations — does anything depend on something registered after it?

### F. Configuration & Secrets

1. Read `appsettings.json`. Are there any non-placeholder values in the `CloudFront` section that look like real keys, IDs, or domains?
2. Verify `CloudFront:PrivateKeySecretName` stores the *name* of the secret (a string like `"fait/cloudfront/workspace-signing-key"`), NOT an actual PEM value.
3. Is `CloudFront:KeyPairId` a placeholder or does it contain an actual Key Pair ID? (Real Key Pair IDs follow format `KXXXXXXXXXXXXX`)

### G. Overall Error Handling

1. What happens if `CloudFront:DistributionDomain` is set but `CloudFront:PrivateKeySecretName` is not (partial config)?
2. What happens if `IsConfigured = true` but Secrets Manager throws a `ResourceNotFoundException` or `AccessDeniedException` at runtime when the first user tries to preview a file?
3. Are exceptions in the signing path caught and surfaced meaningfully, or do they bubble up as unhandled exceptions to the Blazor circuit?

---

## Acceptance Criteria to Verify

After reading the code, verify each AC from the task brief:

- **AC 1:** CloudFront infrastructure documented — check build report (Tony self-reported ✅, skip code check)
- **AC 2:** CF signed URLs generated instead of S3 presigned — verify `GetCannedSignedURL` is called, not `AmazonS3Client.GetPreSignedUrl`
- **AC 3:** PPTX renders inline via Office Online iframe — verify `.pptx` in PreviewableExtensions AND iframe is rendered for that extension
- **AC 4:** XLSX renders inline via Office Online iframe — verify `.xlsx` added to PreviewableExtensions AND same iframe path applies
- **AC 5:** PDF regression — verify PDF path in ArtifactSidebarPanel is unchanged; PDF should NOT go through CF/Office Online
- **AC 6:** Key pair stored in Secrets Manager — verify service reads from Secrets Manager, not from a file or env var directly
- **AC 7:** Expiry configurable — verify env var `CloudFront__UrlExpirySeconds` is read with a 3600 default

---

## Pass/Fail Criteria

**FAIL** if any of these are true:
- `allow-same-origin` is in the iframe sandbox (breaks sandbox entirely)
- RSA private key is written to disk as a temp file
- Private key value appears in logs or exceptions
- Signed URL is not URL-encoded when passed as Office Online `src=` parameter
- Null reference exception is possible in the preview path
- Scoped service injected into singleton (captive dependency)
- Real secrets/keys in appsettings.json

**NEEDS-CHANGES** if:
- Thread safety of lazy init is questionable
- Fallback behavior is unclear or inconsistent
- Error handling swallows exceptions without logging
- Missing case-insensitive extension check

**PASS** if:
- All AC met, no Critical issues, thread safety is sound, sandbox is correct, URL encoding is correct

---

## Output Format

Report your findings for each section (A through G), then give a verdict.
Be specific: file name, line numbers or code snippets, severity (Critical/Important/Nitpick).
