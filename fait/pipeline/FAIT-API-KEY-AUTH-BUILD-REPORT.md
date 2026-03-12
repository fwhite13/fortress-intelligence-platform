# Build Report: FAIT API Key Authentication + Haven Chat Endpoint

**Task:** FAIT-API-KEY-AUTH
**Builder:** Tony Stark (software-engineer)
**Date:** 2026-03-12
**Repo:** `~/projects/fip/fait/`
**Branch:** main

---

## ✅ Build Result: SUCCESS — 0 errors

---

## Generated API Key

```
ed9b529c93fd15a56cc060fa5de37f33d1e8fcb9248c98c37574a0c3deaa5623
```

**ECS Environment Variable:**
```
AppKeys__Haven=ed9b529c93fd15a56cc060fa5de37f33d1e8fcb9248c98c37574a0c3deaa5623
```

Add this to the `fred-dev` ECS task definition environment variables.

---

## Endpoint

```
POST https://fait.dev.fortressam.ai/api/haven/chat
```

### Request Headers
```
Content-Type: application/json
x-api-key: ed9b529c93fd15a56cc060fa5de37f33d1e8fcb9248c98c37574a0c3deaa5623
```

### Request Body
```json
{
  "message": "What can Holly eat at Onda?",
  "projectId": "08de7de6-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "conversationId": null
}
```

- `message` (string, required) — The user's question
- `projectId` (Guid, optional) — FAIT project whose KB docs should be searched first
- `conversationId` (Guid, optional) — Reserved for future conversation history (not used in v1)

### Response
```json
{
  "answer": "At Onda by Scarpetta, Holly can safely order...",
  "sources": ["02-gf-dining-guide.md", "onda-menu.md"]
}
```

- HTTP 200 — answer generated successfully
- HTTP 400 — missing `message` field
- HTTP 499 — client cancelled
- HTTP 502 — Bedrock/AI service error

---

## Implementation Notes

### Response Type
**Simplified JSON response** (v1) — not SSE streaming.

The Haven app receives a single JSON response containing the full answer and source list. SSE streaming is a follow-up enhancement once the basic integration is validated.

### Authentication Flow
1. Haven PWA sends `POST /api/haven/chat` with `x-api-key` header
2. `AppKeyAuthHandler` reads header, compares against `AppKeys:Haven` config value
3. If valid: issues `ClaimsPrincipal` for Fred White (FAIT user `08de7605-3f7d-427d-858a-637777b41018`)
4. `[Authorize(AuthenticationSchemes = "AppKeyAuth")]` on `HavenChatController` ensures only API-key-authenticated requests reach the endpoint
5. Cookie/OIDC flow for Blazor pages is **completely unaffected**

### KB Retrieval Strategy
1. If `projectId` provided → `KnowledgeBaseService.RetrieveProjectAsync()` (project-scoped)
2. Always → `KnowledgeBaseService.RetrieveCorpAsync()` (corp KB)
3. Top 8 chunks by score are included in the system prompt
4. Claude synthesizes an answer with `temperature=0.3` (factual/consistent)

---

## Files Created/Modified

| File | Change |
|------|--------|
| `src/FortressAI.Web/Auth/AppKeyAuthHandler.cs` | **Created** — `AppKeyAuthOptions` + `AppKeyAuthHandler` |
| `src/FortressAI.Web/Controllers/HavenChatController.cs` | **Created** — `POST /api/haven/chat` |
| `src/FortressAI.Web/Program.cs` | **Modified** — registered `AppKeyAuth` scheme in production auth path |

---

---

## Review Cycle 2 Fixes (2026-03-12) — Commit `b4dace7`

### Fix #1 (Critical): AppKeyOnly Policy — Cookie Auth Bypass Closed

**Problem (Clint Item #22):** `[Authorize(AuthenticationSchemes = "AppKeyAuth")]` only controlled which scheme ran `HandleAuthenticateAsync`. When no `x-api-key` header was present, `AppKeyAuth` returned `NoResult()` and the framework fell back to the `DefaultScheme` (Cookie). A logged-in browser user with a valid `.FortressAI.Session` cookie could call `POST /api/haven/chat` without an API key.

**Fix applied:**

1. **Created** `src/FortressAI.Web/Auth/AppKeyRequirement.cs` — `AppKeyRequirement : IAuthorizationRequirement` + `AppKeyAuthorizationHandler` that only calls `Succeed()` when `identity.AuthenticationType == "AppKeyAuth"` and `IsAuthenticated`.

2. **Modified** `Program.cs`:
   - Added `using Microsoft.AspNetCore.Authorization;`
   - Registered `builder.Services.AddSingleton<IAuthorizationHandler, AppKeyAuthorizationHandler>()`
   - Added `"AppKeyOnly"` policy via `options.AddPolicy` inside the existing `AddAuthorization()` lambda

3. **Modified** `HavenChatController.cs`:
   - Changed `[Authorize(AuthenticationSchemes = "AppKeyAuth")]` → `[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]`

**Result:** Cookie-authenticated users are now hard-blocked by the `AppKeyOnly` policy. Only requests carrying a valid `x-api-key` header and authenticated by `AppKeyAuth` can reach `POST /api/haven/chat`.

---

### Finding: `oid` Claim — NameIdentifier is Sufficient

**Clint's concern:** The `oid` claim value `08de7605-3f7d-427d-858a-637777b41018` in `AppKeyAuthHandler` is Fred's FAIT DB user ID, but `oid` in Entra tokens is the **Entra Directory Object ID**, which could be different.

**Investigation:**

Searched the entire `src/FortressAI.Web/` codebase for all usages of `oid`, `FindFirst`, and `NameIdentifier`.

**Findings:**
- `UserManagementService.cs` — resolves users exclusively by `Guid userId` passed as method parameter. No claim lookups.
- `HavenChatController.cs` — uses `User.FindFirstValue(ClaimTypes.NameIdentifier)` for logging only.
- `HavenChatController.cs`, `TasksController.cs`, `ChatAttachmentController.cs`, `McpOAuthController.cs`, `Program.cs` (two endpoints) — all use `ClaimTypes.NameIdentifier` or `email` for user resolution. **Zero code paths read the `"oid"` claim.**
- The `oid` claim in both `AppKeyAuthHandler` and `StubAuthHandler` is set only for pattern consistency with real Entra tokens; it is never consumed by any FAIT service.

**Conclusion:** The `oid` value doesn't matter for FAIT functionality. Setting `oid = NameIdentifier` (both `08de7605-3f7d-427d-858a-637777b41018`) is correct and consistent. **No code change required.** The existing value is already equal to `NameIdentifier`, so the pattern is consistent.

---

## Limitations / Follow-up Items

1. **SSE streaming not implemented in v1** — JSON response is sufficient for Haven. Add streaming in v2 if the app needs it.
2. **No conversation history** — `conversationId` is accepted but not yet used. Implement in v2 by loading prior messages from `Conversations` table.
3. **Single API key** — Only one key is supported (Haven). If more integrations need API key auth, extend to a key→user map in config.
4. **Personal KB not searched** — Only project KB + corp KB. If Haven needs Fred's personal KB documents, add `RetrievePersonalAsync` call.

---

## ECS Deployment Checklist

- [ ] Add `AppKeys__Haven=ed9b529c93fd15a56cc060fa5de37f33d1e8fcb9248c98c37574a0c3deaa5623` to `fred-dev` ECS task definition
- [ ] Deploy new FAIT image (`scripts/fip-deploy.sh fait` or equivalent)
- [ ] Test: `curl -X POST https://fait.dev.fortressam.ai/api/haven/chat -H "x-api-key: ed9b529c93fd15a56cc060fa5de37f33d1e8fcb9248c98c37574a0c3deaa5623" -H "Content-Type: application/json" -d '{"message":"test"}'`
- [ ] Verify Blazor auth still works (login page, SSO)
