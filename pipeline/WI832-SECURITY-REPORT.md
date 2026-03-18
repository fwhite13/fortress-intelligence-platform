# Security Report: WI832
## Verdict: PASS
## Scan Scope: Full (high risk — new service with JWT auth, Agent SDK, bash tool)

---

## Summary

**JWT auth:** No hardcoded secret in either container. `InternalTokenService.cs` throws on missing config; `auth.ts` throws at module load time on missing env var. Token is short-lived (5 min), scoped to `cowork-web` issuer / `cowork-agent` audience.

**iframe sandbox:** `allow-scripts` only — `allow-same-origin` absent. Prevents rendered HTML from accessing FIP session cookies or parent DOM.

**bash in allowedTools:** Explicitly approved by Fred (2026-03-17 10:11). Risk acknowledged: shell access + network egress. Sprint 1 is pre-production only (Sprint 2 gates first). Noted for Sprint 2 security review: consider restricting to file tools only or adding a command allowlist.

**DataProtection:** `SetApplicationName("FortressAI")` + `DisableAutomaticKeyGeneration()` both present. Shared key ring pattern correct — cannot generate its own keys.

**SSE stream:** Close handler prevents indefinite Agent SDK execution on disconnect.

**Advisory (non-blocking):** `DataProtection.EntityFrameworkCore` `8.0.*` pin in net9.0 project — NuGet compat works; bump to `9.0.*` recommended in follow-up.

## Verdict: PASS — pipeline may advance to DEPLOY.
