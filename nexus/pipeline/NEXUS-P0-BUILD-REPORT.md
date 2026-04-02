# Build Report — NEXUS P0 (WI#1515, #1516, #1517, #1521)

**Date:** 2026-04-01  
**Engineer:** Tony Stark  
**Commit:** fbc0b0d  
**Build:** SUCCEEDED (0 warnings, 0 errors)

---

## What Was Built

NEXUS P0 sprint — config hardening, Azure Key Vault integration, Entra SSO config, cookie domain, security headers, and 10-section SpecGenSystem prompt for Production.

---

## Files Changed

| File | Change |
|------|--------|
| `src/FortressNexus.Web/FortressNexus.Web.csproj` | Added `Azure.Extensions.AspNetCore.Configuration.Secrets` v1.3.2 and `Azure.Identity` v1.12.0 |
| `src/FortressNexus.Web/Program.cs` | Added 3 usings (Azure.Identity, Azure.Extensions.AspNetCore.Configuration.Secrets, Microsoft.AspNetCore.Authentication.Cookies); fixed DB name hardcode → env vars; added cookie domain config; added Key Vault bootstrap before `builder.Build()`; added security headers middleware after `UseStaticFiles()` |
| `src/FortressNexus.Web/appsettings.json` | Removed `ConnectionStrings.DefaultConnection` (hardcoded password); added `KeyVaultSettings` placeholder; added `ArtifactGenSystem` prompt placeholder |
| `src/FortressNexus.Web/appsettings.Production.json` | Added `AzureAd` section (TenantId/ClientId — NO ClientSecret); added full 10-section `Nexus.Prompts.SpecGenSystem` prompt |

---

## Acceptance Criteria Verification

### WI#1517 — Key Vault + DB name fix + remove hardcoded secrets
- [x] `Azure.Extensions.AspNetCore.Configuration.Secrets` v1.3.2 in .csproj
- [x] `Azure.Identity` v1.12.0 in .csproj
- [x] `AddAzureKeyVault` wired in Program.cs (conditional on `KeyVaultSettings:VaultUri`)
- [x] DB name reads `FIP_DB_NAME ?? FRED_DB_NAME ?? "nexus"` — no longer hardcoded `nexus_db`
- [x] `KeyVaultSettings.VaultUri` placeholder in appsettings.json
- [x] `ArtifactGenSystem` placeholder in appsettings.json Prompts section
- [x] `ConnectionStrings.DefaultConnection` (Password=dev) REMOVED from appsettings.json

### WI#1515 — Entra SSO config + security headers + /health
- [x] `AzureAd` section in appsettings.Production.json with TenantId + ClientId (no secret in file)
- [x] Security headers middleware: X-Content-Type-Options, X-Frame-Options, Content-Security-Policy
- [x] `/health` endpoint with `.AllowAnonymous()` — was already present, confirmed unchanged

### WI#1516 — Cookie domain
- [x] `CookieAuthenticationOptions` configured after `AddMicrosoftIdentityWebAppAuthentication`
- [x] Reads `Auth:CookieDomain` config key with default `.fortressam.ai`
- [x] Task def `Auth__CookieDomain=.dev.fortressam.ai` will override at runtime

### WI#1521 — 10-section SpecGenSystem prompt
- [x] Full 10-section SpecGenSystem prompt in appsettings.Production.json under `Nexus.Prompts.SpecGenSystem`
- [x] Prompt includes all required sections: Feature Overview, User Stories, Acceptance Criteria, Data Model, Component Map, Service Layer, API Endpoints, UI Specification, Out of Scope, File/Component Map for Claude Code
- [x] Includes OWASP, auth, error states, logging, scalability notes

---

## CC Sessions
- 1 CC Sonnet run — single-shot, no iterations required
- Sequential (all changes in one pass — shared files Program.cs + appsettings*)

---

## Known Notes for Clint
1. **Key Vault** — `AddAzureKeyVault` uses `DefaultAzureCredential`. In ECS Fargate, this will resolve via the task IAM role. The task role must have `Key Vault Secrets User` on the vault. Ensure that's wired in IAM before deploying to prod.
2. **Cookie domain default** — Hardcoded default `.fortressam.ai` in Program.cs. Task def env var `Auth__CookieDomain=.dev.fortressam.ai` overrides at runtime for dev/staging environments. Correct by environment.
3. **DB name** — `FIP_DB_NAME=nexus` is in the task def. Default `"nexus"` matches — no regression risk.
4. **SpecGenSystem in appsettings.json** (base) — Still has the OLD shorter prompt. Production overlay in `appsettings.Production.json` overrides it. This is intentional — dev gets the shorter version, prod gets the full 10-section version.
5. **ArtifactGenSystem** — Placeholder only. Not wired to any service yet — that's Phase 2 scope.

---

## How to Test Locally
```bash
# Build check
dotnet build src/FortressNexus.Web/FortressNexus.Web.csproj --no-restore

# Run locally (Key Vault will be skipped — VaultUri is empty in appsettings.json)
cd src/FortressNexus.Web && dotnet run
# /health should return "OK" without auth
# Security headers should be present on any response
```

---

## BUILD cycle 2 — 2026-04-01 | Commit 16acb3f

**Engineer:** Tony Stark  
**WI:** ADO#1517  
**Build:** SUCCEEDED (0 warnings, 0 errors)

### What Changed
Tightened `AddAzureKeyVault` guard in `Program.cs` to prevent Fargate SIGSEGV (exit 139).

**Root cause:** `DefaultAzureCredential` crashes when called with a placeholder URI (`https://placeholder.vault.azure.net/`). The original guard `!string.IsNullOrEmpty(vaultUri)` passed for placeholder strings.

**Fix:** Guard now requires all four conditions:
1. `vaultUri` is not null/empty
2. Starts with `https://` (case-insensitive)
3. Contains `.vault.azure.net` (case-insensitive)
4. Does NOT contain `placeholder` (case-insensitive)

Blank URI, placeholder URI, or misconfigured URI → Key Vault silently skipped → app starts normally.

### File Changed
- `src/FortressNexus.Web/Program.cs` — Key Vault guard tightened (4-condition check)
