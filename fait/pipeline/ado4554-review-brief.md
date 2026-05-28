# CC Adversarial Review Brief — ADO4554: Artifact Preview Proxy Endpoint

## Context

Tony built a stateless HMAC-SHA256-authenticated artifact preview proxy endpoint for FAIT. This is a security-sensitive feature — it replaces [Authorize] auth with a token-based scheme. A flaw here means unauthenticated S3 access.

## Files to Review

Read these files in full:
1. `src/FortressAI.Web/Services/ArtifactPreviewService.cs`
2. `src/FortressAI.Web/Controllers/ArtifactPreviewController.cs`
3. `src/FortressAI.Web/Services/ChatLayoutState.cs`
4. `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
5. `src/FortressAI.Web/Components/Chat/ArtifactPreviewPanel.razor`
6. `src/FortressAI.Web/Program.cs` (lines 90-130 are the registration block)
7. `src/FortressAI.Shared/Models/WorkspaceUpload.cs`

## Spec

**Endpoint:** `GET /api/artifacts/{id}/preview?token=<hmac>&expires=<unix-ts>`

**Token:** HMAC-SHA256 over `{artifactId}:{userId}:{expires}` using env var `PREVIEW_TOKEN_SECRET`. 15-minute expiry. No `[Authorize]` attribute — token IS the auth. Stateless.

**Endpoint behavior:** Validate token → DB lookup → S3 fetch → stream bytes with correct Content-Type.

## Acceptance Criteria (verify each)

1. GET /api/artifacts/{id}/preview endpoint exists and returns file bytes with correct Content-Type
2. HMAC-SHA256 token validated correctly (signature + expiry check)
3. Expired tokens (>15 min) return 401
4. Invalid signature returns 401
5. Valid token fetches from S3 and streams bytes to response
6. No [Authorize] attribute on endpoint
7. PREVIEW_TOKEN_SECRET loaded from env var (config)

## Security-Focused Review Questions

### 1. HMAC Token Validation Order
The controller does DB lookup BEFORE token validation. Analyze:
- Is the 404 vs 401 distinction an information leak? (Attacker learns whether artifact ID exists even with invalid token)
- Could an attacker enumerate artifact IDs using this?
- Is this an acceptable tradeoff vs. the design note (avoiding userId in URL)?
- What's the correct security posture: 401 for everything auth-related, or 404 for not-found?

### 2. HMAC Key with Empty Secret
- `PREVIEW_TOKEN_SECRET` defaults to empty string `""` in appsettings.json
- What happens if the env var is NOT set in production? (HMAC with empty key — is this exploitable?)
- Is there a startup check that prevents the app from running with an empty secret?
- Should there be a guard in ArtifactPreviewService constructor?

### 3. Constant-Time Comparison Correctness
Review `CryptographicEquals` in ArtifactPreviewService:
```csharp
private static bool CryptographicEquals(string a, string b)
{
    var aBytes = Encoding.UTF8.GetBytes(a);
    var bBytes = Encoding.UTF8.GetBytes(b);
    if (aBytes.Length != bBytes.Length) return false;
    return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
}
```
- The early-exit on length mismatch — does this leak timing information? Is this a real concern for base64url tokens of fixed length?
- Is `CryptographicOperations.FixedTimeEquals` the right method here?
- Is comparing UTF8 bytes of base64url strings equivalent to comparing the tokens correctly?

### 4. Token Expiry Check
In `ValidateToken`:
```csharp
var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
if (now > expires)
```
- Is `now > expires` correct, or should it be `now >= expires`? Edge case: what happens at exactly the expiry second?
- Is there any clock skew concern for the 15-minute window?

### 5. ArtifactRef.Id Consistency
ArtifactRef is now `record ArtifactRef(Guid Id, string S3Key, string Filename, string MimeType)`.
- ChatView.razor line 694 constructs `new ArtifactRef(artifact.Id, artifact.S3Key, artifact.Filename, artifact.MimeType)` — does this positional constructor match the record definition exactly?
- ArtifactPreviewPanel.razor uses `artifact.Id` in `GetProxyPreviewUrlAsync` — verify this compiles correctly.
- ArtifactSidebarPanel.razor uses `artifact.Id` in `GetProxyPreviewUrlAsync` — verify this too.
- Are there any other places that construct ArtifactRef that were NOT updated?

### 6. S3 Streaming
In the controller's S3 streaming block:
```csharp
Response.ContentType = artifact.MimeType;
Response.ContentLength = artifact.SizeBytes > 0 ? artifact.SizeBytes : null;
Response.Headers["Content-Disposition"] = $"inline; filename=\"{artifact.Filename}\"";
Response.Headers["Cache-Control"] = "private, no-store";
await s3Response.ResponseStream.CopyToAsync(Response.Body);
return new EmptyResult();
```
- Is `artifact.Filename` safely escaped in the Content-Disposition header? What if it contains quotes, backslashes, or non-ASCII characters?
- Is `Content-Disposition: inline` appropriate for all MIME types (PDF, images) or should it vary?
- What happens if S3 returns a stream but the response headers are already partially written when the CopyToAsync fails mid-stream? Is the error handling adequate?
- Is EmptyResult the correct return type after writing to Response.Body directly?

### 7. ChatLayoutState Scoping
ArtifactPreviewService is registered as `AddScoped<ArtifactPreviewService>()`. ChatLayoutState is also `AddScoped`. 
- In Blazor Server, `AddScoped` = per-circuit (per-connection). Is this the right lifetime for ArtifactPreviewService?
- The controller (MVC) has a different scope than Blazor components. Does the controller get the same scoped ArtifactPreviewService instance as the Blazor components? (They should NOT share an instance — each HTTP request gets its own scope, which is correct for MVC controllers.)
- Is there any state leakage concern with `AddScoped` for the controller path?

### 8. Token Generation in Blazor (Client-Side HMAC)
The preview URL is generated in `GetProxyPreviewUrlAsync` in both ArtifactSidebarPanel.razor and ArtifactPreviewPanel.razor:
```csharp
var (token, expires) = PreviewSvc.GenerateToken(artifact.Id, userId);
return $"/api/artifacts/{artifact.Id}/preview?token={Uri.EscapeDataString(token)}&expires={expires}";
```
- The HMAC secret is in the server-side ArtifactPreviewService. In Blazor Server, this runs server-side. Is there any risk of the secret being exposed to the client?
- If someone requests a preview, the token + expires appear in the iframe src. Can this URL be extracted from the DOM? Is that an acceptable risk given the 15-minute expiry?

### 9. Missing Error Handling in ArtifactPreviewPanel
In `GetProxyPreviewUrlAsync` in ArtifactPreviewPanel.razor:
```csharp
if (!Guid.TryParse(userIdStr, out var userId)) return "";
```
- If userId can't be parsed, returns empty string. The preview URL becomes `/api/artifacts/{id}/preview?token=&expires=0`. What does the controller return for this?
- Should this return null and skip the preview rather than generating a broken URL?

### 10. Content-Type Passthrough vs. MimeType Field
The controller sets `Response.ContentType = artifact.MimeType`. The MimeType comes from the DB (stored at upload time). 
- Should the controller also cross-check against what S3 returns (s3Response.Headers.ContentType)?
- If MimeType is null or empty in the DB, what happens?

## Summary: Report Format

For each issue found, report:
- Severity: Critical / Important / Nitpick
- File and line number
- Clear description of the problem
- Exact fix or recommendation

Flag anything that would: (a) allow unauthenticated file access, (b) cause a runtime exception in prod, (c) create a security vulnerability, or (d) produce incorrect behavior.

Be skeptical. Don't take Tony's comments at face value — check the actual logic.
