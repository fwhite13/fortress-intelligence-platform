# WI#901 — FAM OS: Natasha QA Auth Bypass

## Priority: CRITICAL — blocks all visual QA
## Type: Infrastructure / Security

## Context
FAM OS dev is protected by Entra SSO (FallbackPolicy = DefaultPolicy). Natasha's browser QA agent cannot authenticate via Entra and is blind to all rendered page content. FAIT solves this with AllowAnonymous bypass routes + a dev-mode stub identity. FAM OS needs the same pattern.

## Reference Implementation
FAIT Program.cs lines 334-548: AllowAnonymous on /health, static files, MCP adapters.
Key pattern: `app.MapGet("/qa-bypass", ...).AllowAnonymous()` returns stub user context.

## Requirements

### 1. Dev-only QA bypass middleware
In `Program.cs`, add BEFORE the auth middleware (order matters):

```csharp
// QA bypass — dev environment only
if (app.Environment.IsDevelopment() || 
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Headers.ContainsKey("X-QA-Bypass") &&
            context.Request.Headers["X-QA-Bypass"] == "natasha-qa-token-famos-dev")
        {
            // Inject stub identity — bypasses Entra redirect
            var claims = new[]
            {
                new System.Security.Claims.Claim("preferred_username", "qa@fortressam.ai"),
                new System.Security.Claims.Claim("name", "QA Tester"),
                new System.Security.Claims.Claim("oid", "00000000-0000-0000-0000-000000000001"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "QA Tester"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "qa-bypass-user"),
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, "QABypass");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}
```

### 2. ECS task definition environment variable
Add to famos-dev task definition:
```
FAMOS_QA_BYPASS=true
```
This enables the bypass in the deployed dev container without modifying app logic.

### 3. QA health/status endpoint (AllowAnonymous)
```csharp
app.MapGet("/qa/status", () => Results.Ok(new { 
    qaBypass = true, 
    environment = "dev",
    timestamp = DateTime.UtcNow,
    message = "QA bypass active"
})).AllowAnonymous();
```
Natasha should hit this first to confirm bypass is active before testing pages.

### 4. Natasha test pattern
With bypass active, Natasha should:
1. GET `/qa/status` — confirm 200 + qaBypass=true
2. All subsequent requests include header: `X-QA-Bypass: natasha-qa-token-famos-dev`
3. Pages will render with stub "QA Tester" identity — no Entra redirect

## Files to modify
- `famos/src/FamOs.Web/Program.cs` — add bypass middleware + /qa/status endpoint
- ECS task def famos-dev — add FAMOS_QA_BYPASS=true env var (Rhodey handles this)

## Security notes
- Header token is dev-only hardcoded value — NOT a secret, just a guard against accidental bypass
- FAMOS_QA_BYPASS env var is NOT set in prod task def — bypass is physically absent in production
- Pattern matches FAIT precedent exactly

## Acceptance Criteria
- [ ] GET /qa/status returns 200 without auth header → { qaBypass: true }
- [ ] GET / without X-QA-Bypass header → 302 redirect to Entra (auth still works normally)
- [ ] GET / with X-QA-Bypass header → 200, page renders with "QA Tester" user
- [ ] GET /pipeline with X-QA-Bypass header → 200, pipeline board renders
- [ ] GET /tasks with X-QA-Bypass header → 200, task center renders
- [ ] Dashboard shows "QA Tester" in user avatar area
- [ ] No bypass behavior when FAMOS_QA_BYPASS env var is absent
