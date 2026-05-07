# Review Report — ADO#2868

**WI:** FAIT v2: Convert Entra auth to FIP shared cookie consumer pattern  
**Commit:** `380e2dd`  
**Reviewer:** Clint Barton (Hawkeye)  
**Review cycle:** 1 of 2  
**Date:** 2026-05-06

---

## Verdict: NEEDS-CHANGES

One Important issue — `appsettings.Development.json` was not cleaned up and still contains the stale `AzureAd` block (including a real `TenantId` and placeholder `ClientId`/`ClientSecret`). All Critical checks pass cleanly.

---

## Spec Compliance Check

**§2 Files Changed (per build report):**
- `FortressAI.V2.Web.csproj` — ✅ modified as specified
- `Program.cs` — ✅ modified as specified  
- `Data/SharedKeyRingDbContext.cs` — ✅ created as specified
- `appsettings.json` — ✅ modified as specified
- `appsettings.Development.json` — ❌ **NOT in scope but needed cleanup** — see I1

**§6 Out of Scope:** ✅ No unauthorized out-of-scope changes detected.

**§7 Acceptance Criteria:**
- [x] No `OpenIdConnect` / `AddMicrosoftIdentityWebApp` in Program.cs ✅
- [x] `DefaultScheme` + `DefaultChallengeScheme` = Cookie ✅
- [x] `SharedKeyRingDbContext` registered and wired to `AddDataProtection` ✅
- [x] `DisableAutomaticKeyGeneration()` present ✅
- [x] `/auth/redirect-to-login` endpoint exists → redirects to `FIP__LoginUrl` ✅
- [x] `appsettings.json` has no `AzureAd` block ✅ *(Development file is separate — see I1)*
- [x] `Data/SharedKeyRingDbContext.cs` exists, namespace `FortressAI.V2.Web.Data` ✅
- [x] `.csproj` has no `Microsoft.Identity.Web` references ✅
- [x] `.csproj` has `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` ✅

**Spec compliance verdict:** ✅ COMPLIANT on all AC bullets — but see Important issue I1 which must be resolved before deploy.

---

## Consistency Audit

**Cross-file checks:**
- `Program.cs` `Auth:CookieName` ↔ `appsettings.json` `Auth.CookieName` — ✅ `.FortressAI.Session` exact match
- `Program.cs` `DataProtection:ApplicationName` default ↔ `appsettings.json` `DataProtection.ApplicationName` — ✅ both `"FortressAI"`
- `Program.cs` `FIP__LoginUrl` fallback ↔ `appsettings.json` `FIP__LoginUrl` value — ✅ both `https://fip.dev.fortressam.ai`
- `SharedKeyRingDbContext` `ToTable("DataProtectionKeys")` ↔ FIRM reference pattern — ✅ exact match
- `SetApplicationName("FortressAI")` ↔ FIRM reference — ✅ matches (FIRM reads same config key, same default)

**FIRM reference pattern delta (positive):**
- FAIT v2 adds `GuidFormat = MySqlConnector.MySqlGuidFormat.None` to `keyRingCsb` — FIRM's `keyRingCsb` omits this. FAIT v2 is more correct.

---

## Critical Issues — 0

All 13 critical checklist items pass.

---

## Important Issues — 1

### I1: `appsettings.Development.json` still contains stale `AzureAd` block

- **File:** `src/FortressAI.V2.Web/appsettings.Development.json`
- **Category:** Correctness / cleanup
- **Issue:** This file was created in ADO#2842 (Entra SSO commit `598ee54`) and was not cleaned up in this commit. It contains:
  ```json
  {
    "AzureAd": {
      "TenantId": "7152ea12-c930-44b0-bb52-069152161c5b",
      "ClientId": "PLACEHOLDER_NEEDS_REAL_ENTRA_APP_REGISTRATION",
      "ClientSecret": "PLACEHOLDER_DEV"
    },
    "Auth": {
      "CookieDomain": ""
    }
  }
  ```
- **Impact:**
  1. The `AzureAd` config block is dead code now — OIDC middleware no longer loads it — but its presence is misleading to future engineers.
  2. `TenantId` is a real value (`7152ea12-...`). Stale Entra config values should not persist in tracked files once the auth method changes.
  3. The file is **not gitignored** — it will persist in git history and be visible in any future review or audit of this repo.
  4. If ASP.NET Configuration system sees `AzureAd` keys in the dev environment, any future developer who tries to re-enable OIDC "temporarily" will find a half-configured file that looks like it should work.
- **Fix:**
  ```diff
  // appsettings.Development.json — remove AzureAd block entirely, keep Auth override
  {
  -  "AzureAd": {
  -    "TenantId": "7152ea12-c930-44b0-bb52-069152161c5b",
  -    "ClientId": "PLACEHOLDER_NEEDS_REAL_ENTRA_APP_REGISTRATION",
  -    "ClientSecret": "PLACEHOLDER_DEV"
  -  },
    "Auth": {
      "CookieDomain": ""
    }
  }
  ```

---

## Nitpicks — 2

**N1: `uint.Parse` without fallback** (`Program.cs:55`)
`uint.Parse(keyRingDbPort)` will throw `FormatException` if `FORTRESS_DB_PORT` is set to a non-numeric value in ECS. The `?? "3306"` default protects against unset, not malformed. FIRM has the same pattern — this is a shared debt, not a regression. Not blocking.

**N2: Comment cross-reference wording** (`Program.cs:60`)
`// MANDATORY — matches existing FIRM pattern` — accurate as intent documentation. Consider adding the ADO# for traceability, e.g., `// GuidFormat=None: matches FIRM/FIP pattern — see ADO#2868`. Not blocking.

---

## Positive Observations

- **GuidFormat on keyRingCsb** — FAIT v2 correctly applies `GuidFormat = None` to the key ring connection string builder. FIRM omits this on its own `keyRingCsb` (though FIRM has it on `DefaultConnection`). FAIT v2 is stricter and more correct.
- **Comment clarity** — The `// fait-v2 is a consumer — FIP portal creates keys` comment on `DisableAutomaticKeyGeneration()` is exactly right and helps future engineers understand the topology.
- **FallbackPolicy = DefaultPolicy** — AuthZ is airtight. All routes require auth by default; only `/health` and `/auth/redirect-to-login` are explicitly `AllowAnonymous`. Clean.
- **No `app.MapControllers()`** — Correctly removed with the OIDC middleware. No leftover route registrations.

---

## What to Fix

**Tony — one fix required before this ships:**

Remove the `AzureAd` block from `appsettings.Development.json`. The `Auth.CookieDomain` override can stay. File should be:

```json
{
  "Auth": {
    "CookieDomain": ""
  }
}
```

That's it. Single file, four lines removed. Build will still pass (that config is now unused).

---

## CC Review Summary

CC (Claude Code, Sonnet, run via `cat /tmp/review-2868-brief.md | claude --model sonnet --print --dangerously-skip-permissions`) reviewed all four files plus the FIRM reference. CC returned PASS on all items. The `appsettings.Development.json` finding was caught by an independent grep sweep of the full project directory (not just the files in the build report) after CC completed. CC reviewed only the files Tony listed as changed; the untouched file was outside that scope.

---

_Hawkeye — code-reviewer pipeline stage_

---

## Review Cycle 2 — ADO#2868

**Commit:** `d42f070`  
**Date:** 2026-05-06  
**Reviewer:** Clint Barton (Hawkeye)

### Verdict: PASS

**I1 verification:**
- `appsettings.Development.json` diff at `d42f070` confirms the `AzureAd` block (TenantId, ClientId, ClientSecret — 5 lines) is fully removed.
- File now contains exactly `{ "Auth": { "CookieDomain": "" } }` — nothing more.
- `git grep` scan of `src/` for `AzureAd`, `OpenIdConnect`, `AddMicrosoftIdentityWebApp`, `SignedOutCallbackPath`, `CallbackPath` → **zero hits** in tracked source files.
- Build gate: clean (no new compilation units touched; this was a config-only change).

**CC confirmation:** `cat review-c2-2868-brief.md | claude --model sonnet --print --dangerously-skip-permissions` returned PASS.

**ADO comment:** Posted (comment ID 781714).

All issues from Cycle 1 resolved. This WI is clear to proceed.

---

_Hawkeye — code-reviewer pipeline stage (cycle 2 final)_
