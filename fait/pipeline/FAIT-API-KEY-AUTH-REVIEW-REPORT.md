# Review Report: FAIT API Key Auth + Haven Chat Endpoint

**Reviewer:** Hawkeye (Clint Barton) — code-reviewer  
**Commit:** `5e9ec99`  
**Review Cycle:** 1 of 2  
**Date:** 2026-03-12  

---

## Verdict: ⚠️ NEEDS-CHANGES

One **Important** issue and one **Critical** finding. All other items pass cleanly.

---

## Checklist Results

### AppKeyAuthHandler (items 1–8)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | NoResult when header absent | ✅ PASS | `string.IsNullOrEmpty(apiKey)` → `NoResult()`. Cookie/OIDC unaffected. |
| 2 | Fail when key present but invalid | ✅ PASS | Falls through to the `!string.Equals(...)` branch → `Fail("Invalid API key")` |
| 3 | `StringComparison.Ordinal` used | ✅ PASS | `string.Equals(apiKey, configuredKey, StringComparison.Ordinal)` — exact match |
| 4 | Key from `IConfiguration["AppKeys:Haven"]` | ✅ PASS | Set via `Options.ApiKey` populated in `Program.cs` from config |
| 5 | Null/empty config key handled gracefully | ✅ PASS | `string.IsNullOrEmpty(configuredKey)` check short-circuits to `Fail` before comparison — no NRE possible |
| 6 | Claims match StubAuthHandler structure | ⚠️ SEE BELOW | Structure matches; **values differ from Stub** — expected for production, but see item 7 |
| 7 | Fred White's FAIT user ID `08de7605-...` is NameIdentifier | ✅ PASS | Correctly set. StubAuthHandler uses placeholder `00000000-...-0001` (dev only); AppKeyAuth correctly uses the real production ID |
| 8 | Scheme name `"AppKeyAuth"` consistent | ✅ PASS | Same string in `AddScheme`, `[Authorize(AuthenticationSchemes = ...)]` |

**Item 6 detail:** The claims structure is consistent in shape (same 7 claims, same claim type names). The values differ from Stub (different NameIdentifier, different email domain) — this is correct production vs. dev behavior and is not a defect.

---

### Program.cs Registration (items 9–12)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 9 | `AddScheme` inside production auth block, not stub block | ✅ PASS | Lives in the `else` branch, after OIDC registration |
| 10 | DefaultScheme still Cookie | ✅ PASS | `options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme` unchanged |
| 11 | AppKeyAuth is additive, not replacing Cookie default | ✅ PASS | Added via `authBuilder.AddScheme(...)` — no modification to DefaultScheme |
| 12 | `builder.Configuration["AppKeys:Haven"]` populates `options.ApiKey` | ✅ PASS | Exact match to requirement |

---

### HavenChatController (items 13–21)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 13 | Route is `POST /api/haven/chat` | ✅ PASS | `[Route("api/haven")]` + `[HttpPost("chat")]` → `/api/haven/chat` |
| 14 | Only `[Authorize(AuthenticationSchemes = "AppKeyAuth")]` — no plain `[Authorize]` | ✅ PASS | Single authorize attribute, schema-scoped, no fallback to Cookie |
| 15 | `projectId` read from request body (optional) | ✅ PASS | `Guid? ProjectId` in `HavenChatRequest` — optional nullable |
| 16 | Null `projectId` doesn't crash | ✅ PASS | `if (request.ProjectId.HasValue)` guard — corp-only path executes cleanly |
| 17 | Response shape `{ answer, sources }` | ✅ PASS | `HavenChatResponse { Answer: string, Sources: List<string> }` |
| 18 | Bedrock call wrapped in try/catch | ✅ PASS | Both KB retrievals individually try/catched; Bedrock stream wrapped; returns 502 on failure |
| 19 | Uses `HttpContext.User` for identity | ✅ PASS | `User.FindFirstValue(ClaimTypes.NameIdentifier)` — no hardcoded ID in controller |
| 20 | `conversationId` accepted without crash | ✅ PASS | `Guid? ConversationId` accepted in model binding, never referenced — silent no-op |
| 21 | No raw API key values in source | ✅ PASS | No key literals in any source file; appsettings files have no `AppKeys` section |

---

### Security (items 22–25)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 22 | `/api/haven/chat` NOT accessible via cookie auth | 🔴 ISSUE | **See critical finding below** |
| 23 | `x-api-key` value not logged | ✅ PASS | Handler has zero logging calls. Controller logs `userId` and `projectId` only — not the key |
| 24 | No unauthenticated `/api/haven/*` routes | ✅ PASS | Only one action method on the controller; `[Authorize]` is at class level |
| 25 | Generated key `ed9b529c...` not in committed files | ✅ PASS | Searched all `.cs` and `.json` files in the project — not present |

---

## Issues Found

### 🔴 CRITICAL — Item #22: Cookie auth CAN satisfy `[Authorize(AuthenticationSchemes = "AppKeyAuth")]`

**Severity:** Critical  
**File:** `src/FortressAI.Web/Controllers/HavenChatController.cs`  
**Checklist item:** #22

**The problem:**

`[Authorize(AuthenticationSchemes = "AppKeyAuth")]` in ASP.NET Core specifies which scheme(s) are used to *authenticate* the request (i.e., run `HandleAuthenticateAsync`). It does **not** mean "only this scheme may authorize this request."

What actually happens when a browser with a valid `.FortressAI.Session` cookie hits `POST /api/haven/chat`:

1. The `AppKeyAuth` handler runs → no `x-api-key` header → returns `NoResult()`
2. ASP.NET Core's authorization middleware evaluates `NoResult` as an unauthenticated identity
3. **However**, because `DefaultScheme` is Cookie, the framework *also* evaluates the Cookie scheme's identity for authorization purposes

The net result: a logged-in browser user with a valid session cookie can call `/api/haven/chat` successfully — the API key is not required for browser sessions.

**This is by design in ASP.NET Core's multi-scheme auth model.** `AuthenticationSchemes` on `[Authorize]` only controls which scheme runs authentication for that request, but authorization falls back to any authenticated principal if the specified scheme returns `NoResult`.

**Required fix:**

The controller needs a policy that explicitly requires the API key identity and rejects cookie-authenticated users. The cleanest approach is a custom authorization requirement:

**Option A — Custom `IAuthorizationRequirement` (recommended):**

```csharp
// In a new file, e.g. Auth/AppKeyRequirement.cs
public class AppKeyRequirement : IAuthorizationRequirement { }

public class AppKeyAuthorizationHandler : AuthorizationHandler<AppKeyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AppKeyRequirement requirement)
    {
        // Only succeed if the identity was authenticated by AppKeyAuth
        if (context.User.Identities.Any(i => i.AuthenticationType == "AppKeyAuth"))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
```

Register in `Program.cs`:
```csharp
builder.Services.AddSingleton<IAuthorizationHandler, AppKeyAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AppKeyOnly", policy =>
        policy.AddRequirements(new AppKeyRequirement()));
});
```

Controller:
```csharp
[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]
```

**Option B — `OnlyAllowDelegatedRequests` via `AuthenticationSchemes` + explicit claim check:**

A simpler but less formal approach: keep the current attribute and add a guard at the top of the `Chat` method:

```csharp
// Reject if not authenticated by AppKeyAuth
if (User.Identities.All(i => i.AuthenticationType != "AppKeyAuth"))
    return Unauthorized(new { error = "API key required" });
```

Option A is the correct architectural approach. Option B is acceptable for v1 given time constraints, but should be noted as tech debt.

---

### ⚠️ IMPORTANT — Item #6 (supplementary): `StubAuthHandler` uses placeholder ID; `AppKeyAuthHandler` must match the live Entra object

**Severity:** Important (not a blocker, but a consistency risk)  
**File:** `src/FortressAI.Web/Auth/AppKeyAuthHandler.cs`

The Stub handler uses placeholder identity `00000000-0000-0000-0000-000000000001` + `fred@fortressam.ai`. The AppKeyAuth handler uses `08de7605-3f7d-427d-858a-637777b41018` + `fwhite@refugems.com`.

This is **correct and expected** — the Stub is dev-only and never runs in production. However:

- The comment on `StubAuthHandler` says it "produces the same claims structure as real Entra OIDC." If `AppKeyAuthHandler` is meant to impersonate Fred White's real Entra account, the `oid` claim value `08de7605-3f7d-427d-858a-637777b41018` must be validated against the actual Entra Directory Object ID for `fwhite@refugems.com`.
- This can't be verified from source alone — needs confirmation from Fred or an Entra lookup.
- **Action required:** Fred should confirm `08de7605-3f7d-427d-858a-637777b41018` is his real Entra `oid`. If it's not, any per-user FAIT data lookups keyed on that ID will silently use the wrong user.

---

### 📝 NITPICK — Minor items

1. **`HavenChatRequest.Message` is not `required`** — `public string Message { get; set; } = "";` defaults to empty string. The null/empty guard at the top of `Chat` catches it (`if (string.IsNullOrWhiteSpace(request.Message))`), so this isn't a bug. But marking it `required` or using a `[Required]` attribute would be more expressive.

2. **Error returns 502 instead of 500 for Bedrock failures** — 502 ("Bad Gateway") is actually more semantically correct for a downstream service failure, so this is defensible. But the checklist specified 500. Minor inconsistency worth noting, not fixing.

3. **Bedrock call uses hardcoded model string `"claude-sonnet-4-6"`** — should be a config value to avoid deploys for model upgrades.

---

## Summary Table

| Section | Pass | Needs Changes | Notes |
|---------|------|---------------|-------|
| AppKeyAuthHandler (1–8) | 8/8 | 0 | All pass |
| Program.cs Registration (9–12) | 4/4 | 0 | All pass |
| HavenChatController (13–21) | 9/9 | 0 | All pass |
| Security (22–25) | 3/4 | 1 | Item #22 CRITICAL |
| **Total** | **24/25** | **1** | |

---

## Required Changes Before PASS

1. **[CRITICAL — Item #22]** Fix `[Authorize]` on `HavenChatController` to enforce that only `AppKeyAuth`-authenticated identities can call the endpoint. Cookie auth must not satisfy it.
   - Implement Option A (custom policy + `IAuthorizationHandler`) or Option B (identity type guard in action method)
   - Preference: Option A

2. **[IMPORTANT — Item #6/oid validation]** Confirm with Fred that `08de7605-3f7d-427d-858a-637777b41018` is his real Entra Directory Object ID. Document the confirmation in the Build Report for traceability.

---

*Hawkeye out. One critical hit, one confirm needed. Fix item #22 and we're clear to deploy.*

---

# Review Report — Cycle 2 (Focused Re-check)

**Reviewer:** Hawkeye (Clint Barton) — code-reviewer
**Commit:** `8cdad00`
**Review Cycle:** 2 of 2
**Date:** 2026-03-12

---

## Verdict: ✅ PASS

Both cycle 1 issues are correctly resolved. No regressions detected.

---

## Focused Checklist Results

### Critical Fix — Item #22 (Cookie auth bypass)

| # | Check | Result | Evidence |
|---|-------|--------|----------|
| 1 | `HandleRequirementAsync` calls `Succeed()` only when `AuthenticationType == "AppKeyAuth"` AND `IsAuthenticated == true` | ✅ PASS | `context.User.Identities.Any(i => i.AuthenticationType == "AppKeyAuth" && i.IsAuthenticated)` — both conditions required in a single `.Any()` predicate |
| 2 | Cookie-only identity (no `x-api-key` header) would NOT get `Succeed()` called | ✅ PASS | A Cookie identity has `AuthenticationType == "Cookies"`, not `"AppKeyAuth"` — the `.Any()` predicate returns false, `Succeed()` is never called, request fails policy |
| 3 | `HavenChatController` has `[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]` — both scheme AND policy | ✅ PASS | Class-level attribute reads exactly `[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]` |
| 4 | `AppKeyAuthorizationHandler` registered as `IAuthorizationHandler` in DI | ✅ PASS | `builder.Services.AddSingleton<IAuthorizationHandler, AppKeyAuthorizationHandler>()` — singleton, which is correct for a stateless handler |
| 5 | `"AppKeyOnly"` policy registered inside the **existing** `AddAuthorization()` call, not a second separate call | ✅ PASS | Single `builder.Services.AddAuthorization(options => { options.AddPolicy("AppKeyOnly", ...) })` call at line ~233 — no duplicate `AddAuthorization()` detected in the file |

**Implementation note:** The handler does not call `context.Fail()` on the negative path — it simply does nothing, letting ASP.NET Core's authorization pipeline fail the requirement naturally. This is correct behavior per the framework's design; not calling `Succeed()` is sufficient to deny access when no other handler can satisfy the requirement.

---

### oid Finding — Item #6

| # | Check | Result | Evidence |
|---|-------|--------|----------|
| 6 | FAIT never reads `oid` claim for user resolution — all lookups use `NameIdentifier`/email | ✅ CONFIRMED | Build Report investigation confirmed: `HavenChatController`, `TasksController`, `ChatAttachmentController`, `McpOAuthController`, and `Program.cs` all use `ClaimTypes.NameIdentifier` or email for user resolution. The `"oid"` claim is set in `AppKeyAuthHandler` and `StubAuthHandler` for Entra token shape consistency only — zero code paths consume it. No code change required. |

**Additional verification:** `FirmIntegrationController` uses `entraOid` as a query parameter from FIRM (service-to-service call, not a claim lookup) and resolves users by `db.Users.Where(u => u.IsEntraUser && u.IsActive)` — not by reading an `oid` claim from `HttpContext.User`. Consistent with the finding.

---

## No Regressions

Spot-checked the three modified files against cycle 1 passing items:
- `AppKeyAuthHandler.cs` registration in `Program.cs` — unchanged, still in production `else` block ✅
- `DefaultScheme` still Cookie — unchanged ✅
- `[Authorize]` attribute on `HavenChatController` — correctly updated, old `AuthenticationSchemes`-only form replaced ✅
- `AppKeyRequirement` is a clean empty `IAuthorizationRequirement` — no extraneous logic ✅

---

## Summary

| Issue | Cycle 1 Status | Cycle 2 Status |
|-------|---------------|----------------|
| #22 — Cookie auth bypass | 🔴 CRITICAL | ✅ FIXED |
| #6 — oid claim / user resolution | ⚠️ IMPORTANT (confirm needed) | ✅ CONFIRMED — no code change needed |

**All cycle 1 required changes are implemented correctly. No new issues found. Pipeline may advance to SECURITY.**

---

*Hawkeye out. Target neutralized. Clear to advance.*
