# ADO4554 — Cycle 2 Re-Review Brief

You are performing an adversarial code review for ADO4554 Cycle 2.
Cycle 1 returned NEEDS-CHANGES with 1 Critical + 5 Important issues.
Tony applied all fixes in commit `1d442ed0`.
Your job: verify each issue from Cycle 1 is actually resolved, and check for any new issues introduced.

---

## Files to Review

Read these files in full:
1. `src/FortressAI.Web/Services/ArtifactPreviewService.cs`
2. `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`
3. `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`

---

## Issues from Cycle 1 — Verify Each

### C1 — No startup guard (ArtifactPreviewService.cs)
**Was:** Constructor silently allowed empty `PREVIEW_TOKEN_SECRET` (set `_secret = ""`).
**Expected fix:** Constructor throws `InvalidOperationException` if `string.IsNullOrWhiteSpace(_secret)`.
**Verify:** Does the constructor check for null/whitespace and throw with a meaningful message?

### I1 — DB lookup before token expiry check (ArtifactPreviewController.cs)
**Was:** Controller queried DB for artifact before checking token expiry — leaked artifact existence via timing/404 vs 401.
**Expected fix:** `now >= expires` check BEFORE any DB access.
**Verify:** Is the expiry check (`now >= expires`) the very first substantive check after token null-check, before any DB query?

### I2 — `Content-Disposition` string interpolation (ArtifactPreviewController.cs)
**Was:** `Content-Disposition` built with string interpolation — broken for filenames with quotes or non-ASCII.
**Expected fix:** Uses `ContentDispositionHeaderValue` with `FileNameStar` property.
**Verify:** Is `ContentDispositionHeaderValue` with `FileNameStar` used instead of string interpolation?

### I3 — Empty MimeType causes runtime exception (ArtifactPreviewController.cs)
**Was:** `Response.ContentType = artifact.MimeType` — runtime exception if MimeType is empty string.
**Expected fix:** Fallback to `application/octet-stream` when MimeType is null or empty.
**Verify:** Is there a null/empty check with fallback to `application/octet-stream`?

### I4 — `CopyToAsync` outside try/catch (ArtifactPreviewController.cs)
**Was:** `await s3Response.ResponseStream.CopyToAsync(Response.Body)` not wrapped — mid-stream failures unlogged.
**Expected fix:** Inner try/catch around `CopyToAsync` with `LogError`.
**Verify:** Is `CopyToAsync` inside its own try/catch with error logging?

### I5 — `allow-scripts allow-same-origin` sandbox anti-pattern (ArtifactPreviewPanel.razor)
**Was:** `sandbox="allow-scripts allow-same-origin"` — dangerous combination allowing XSS escape.
**Expected fix:** `allow-same-origin` removed; `allow-popups-to-escape-sandbox` added.
**Verify:** Does the sandbox attribute NOT contain `allow-same-origin`, and does it contain `allow-popups-to-escape-sandbox`?

### N2 — `GetProxyPreviewUrlAsync` returns `""` on parse failure (ArtifactPreviewPanel.razor)
**Was:** Method returned `""` on Guid parse failure — empty string iframe src is broken.
**Expected fix:** Returns `null` instead of `""` so the caller can handle gracefully.
**Verify:** Does `GetProxyPreviewUrlAsync` return `null` (not `""`) when `Guid.TryParse` fails?

---

## Tony's Known Note — Off-by-one in ValidateToken
Tony noted: `ValidateToken` in `ArtifactPreviewService.cs` uses `now > expires` (strict greater-than), which is technically an off-by-one vs the controller's `now >= expires` pre-check. The controller pre-check short-circuits first so the service check is never the deciding factor for expired tokens. This is acceptable — note it in your analysis but do not flag as a blocking issue.

---

## Additional Adversarial Checks (New Issues)

Beyond verifying the fixes, check for any NEW issues introduced by the changes:

1. **Token validation logic**: Is the HMAC comparison constant-time? Any timing attack vectors?
2. **S3 error handling**: Is the outer S3 catch adequate? What HTTP status if S3 returns a non-existent key?
3. **Response streaming after headers**: Once `Response.Body` starts streaming, headers are committed. Is `EmptyResult` returned correctly after streaming?
4. **FileNameStar encoding**: Does `ContentDispositionHeaderValue.FileNameStar` correctly handle all Unicode filenames?
5. **Authentication**: The controller has no `[Authorize]` attribute — is auth enforced by the token itself, or is there a gap?
6. **iframe sandbox**: Does the current sandbox set (`allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox`) have any remaining issues for PDF display?
7. **GetProxyPreviewUrlAsync return type**: The method signature should return `Task<string?>`. Confirm the return type matches.
8. **Razor null handling**: `_presignedUrl` is only set when `_canPreview` is true. Is there any path where `_presignedUrl` could be non-null but stale from a previous artifact?

---

## Verdict Criteria

- **PASS**: All 7 Cycle 1 issues resolved, no new Critical or Important issues.
- **NEEDS-CHANGES**: All Cycle 1 issues resolved BUT new issues found that need fixing.
- **FAIL**: One or more Cycle 1 issues NOT resolved, OR new critical bugs introduced.

Report findings per-issue in this format:
```
[C1] ✅ RESOLVED / ❌ NOT RESOLVED — <evidence>
[I1] ✅ RESOLVED / ❌ NOT RESOLVED — <evidence>
...
```

Then list any NEW issues found, with severity (Critical/Important/Nitpick), file, evidence, and recommended fix.

Be adversarial. Don't rubber-stamp. Check the actual logic, not just whether the code looks changed.
