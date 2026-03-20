# Build Report: WI901 — FAM OS QA Auth Bypass

**Date:** 2026-03-19  
**Agent:** Tony Stark (software-engineer)  
**Status:** ✅ COMPLETE  
**Commit:** `856448f`  
**Branch:** `main`  

---

## Summary

Implemented QA authentication bypass for FAM OS to unblock Natasha's visual QA testing. Two additions to `Program.cs`:

1. **QA Bypass Middleware** — inserted between `UseRouting()` and `UseAuthentication()`, gated on `IsDevelopment()` OR `FAMOS_QA_BYPASS=true` env var. When `X-QA-Bypass: natasha-qa-token-famos-dev` header is present, injects a `ClaimsPrincipal` with `QABypass` auth scheme, bypassing MSAL/Entra entirely.

2. **`/qa/status` Endpoint** — anonymous endpoint returning bypass status JSON, placed after `/health`.

---

## Claude Code CLI Invocation

```bash
cd ~/projects/fip && cat /tmp/wi901-brief.md | claude --model sonnet --dangerously-skip-permissions -p
```

---

## Files Modified

| File | Change |
|------|--------|
| `famos/src/FamOs.Web/Program.cs` | +32 lines — bypass middleware block + /qa/status endpoint |

**Zero other files touched.**

---

## Self-Review Verification

### Middleware Order (grep output)
```
194: app.UseRouting();
196: // QA bypass — dev/staging only (FAMOS_QA_BYPASS=true env var required)
202:     if (context.Request.Headers.ContainsKey("X-QA-Bypass") &&
203:         context.Request.Headers["X-QA-Bypass"] == "natasha-qa-token-famos-dev")
220: app.UseAuthentication();
221: app.UseAuthorization();
231: app.MapGet("/qa/status", ...
```

**Order:** UseRouting (194) → bypass block (196–219) → UseAuthentication (220) → UseAuthorization (221) ✅

### Diff Scope
```
famos/src/FamOs.Web/Program.cs | 32 ++++++++++++++++++++++++++++++++
1 file changed, 32 insertions(+)
```
Only `Program.cs` — confirmed ✅

---

## Acceptance Criteria

- [x] Bypass middleware between `UseRouting()` and `UseAuthentication()` — NOT after `UseAuthorization()`
- [x] Gated on `IsDevelopment()` OR `FAMOS_QA_BYPASS=true` env var
- [x] Token: `X-QA-Bypass: natasha-qa-token-famos-dev`
- [x] Injected claims: `preferred_username`, `name`, `oid`, `ClaimTypes.Name`, `ClaimTypes.NameIdentifier`
- [x] Auth scheme: `QABypass`
- [x] `/qa/status` endpoint with `AllowAnonymous()`
- [x] No new `using` statements added at top (fully qualified `System.Security.Claims`)
- [x] Only `Program.cs` modified
- [x] No `Dense="true"`, no `$"..."` in onclick, no FipTheme.cs
- [x] Pushed to `main`

---

## Deploy Note for Rhodey

> **REQUIRED:** Add `FAMOS_QA_BYPASS=true` to the `famos-dev` ECS task definition environment variables. Without this, the bypass will only activate in local dev (IsDevelopment). The header token alone is not sufficient — the env var gate must also be satisfied.

---

## Test Instructions for Natasha

Once Rhodey deploys with `FAMOS_QA_BYPASS=true`:

```bash
# Verify /qa/status (no auth required)
curl https://famos.dev.fortressam.ai/qa/status

# Verify bypass works — should return authenticated response
curl -H "X-QA-Bypass: natasha-qa-token-famos-dev" https://famos.dev.fortressam.ai/
```

Expected `/qa/status` response:
```json
{
  "qaBypass": true,
  "environment": "dev",
  "timestamp": "...",
  "message": "QA bypass active"
}
```
