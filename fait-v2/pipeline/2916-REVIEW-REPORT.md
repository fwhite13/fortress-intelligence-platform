# ADO#2916 — REVIEW REPORT

**Agent:** Hawkeye (REVIEW cycle 1)
**Commit:** `44d7c75`
**Branch:** `main`
**Verdict:** NEEDS-CHANGES
**Date:** 2026-05-07

---

## Checklist

| # | Check | Result |
|---|-------|--------|
| 1 | Endpoint reads Entra OID from claims correctly | PASS — dual-fallback `objectidentifier` / `oid` (lines 270–271) |
| 2 | `ProvisionAsync` called with correct params | PASS — signature matches `IUserProvisioningService` (userId, entraOid, email, displayName, wizardData?, ct) |
| 3 | `RequireAuthorization()` not `AllowAnonymous` | PASS — line 293 |
| 4 | Error status handled in both timers | PASS — `OnInitialized` (lines 77–84) and `Retry()` (lines 131–138) |
| 5 | No hardcoded CSS values | PASS — no new CSS; all classes |

## Findings

### BLOCKING — Auth cookie not forwarded by server-side HttpClient

**Severity:** Critical
**Files:** `Program.cs:293`, `AssistantLoadingState.razor:66–67`

The previous stub endpoint was `.AllowAnonymous()`. This PR changed it to `.RequireAuthorization()`. However, `AssistantLoadingState` polls `/api/agent/status` using a bare `IHttpClientFactory.CreateClient()` — a server-to-server HTTP call with **no auth cookies attached**. The cookie auth middleware will reject (302 redirect) every poll request. `response.IsSuccessStatusCode` will be `false` (or the redirect lands on the login page returning HTML), so the status is never parsed. The user will always time out after 60 seconds.

**Recommended fix (pick one):**
1. **Simplest:** Change back to `.AllowAnonymous()` — the handler already guards unauthenticated callers by returning `{ status: "Error" }` when `entraOid` is null (line 272–273). No provisioning occurs without a valid OID.
2. **Alternative:** Replace the HTTP poll with a direct DI call to `IUserProvisioningService` from the Blazor component, avoiding the HTTP round-trip entirely.

### Non-blocking — Duplicated poll logic

**Severity:** Low / Observation
**File:** `AssistantLoadingState.razor`

The timer callback bodies in `OnInitialized` (lines 38–91) and `Retry` (lines 100–144) are nearly identical (~30 lines each). Consider extracting a shared `PollStatusAsync()` method to reduce duplication. Non-blocking — does not affect correctness.

### Non-blocking — ProvisioningResult ignored

**Severity:** Info
**File:** `Program.cs:285`

The `ProvisioningResult` return value from `ProvisionAsync` is discarded. This is fine for a status-check endpoint (idempotent, fire-and-forget), but worth noting if future work needs to distinguish first-provision from no-op.

---

## CC Invocation

```
Review performed by direct code read — no CC subprocess invocation required for cycle 1.
```
