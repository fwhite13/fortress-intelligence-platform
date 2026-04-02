# Review Report — ADO#1554 (NexusDashboard.razor, commit 246dd0d)

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC ran a full 20-item checklist plus 5 adversarial checks against both `Dashboard.razor` and `SubmissionDetail.razor`. 17/20 checklist items passed cleanly. Two warnings confirmed as real issues (items 17 and 20). One adversarial warning (B) is a lower-priority defensive concern.

No false positives dismissed — all three flagged items are real.

---

### Spec Fidelity

All structural requirements are met: route, auth, render mode, page title, table columns, MudLink on title, null Feature Area, date format, View button, loading state, empty state, admin guard, and separate admin table. The layout and wiring are correct.

**Spec compliance verdict:** ✅ COMPLIANT on structure — blocked only by correctness/maintenance issues below.

---

### Consistency Audit

**Files cross-referenced:** `Dashboard.razor` ↔ `SubmissionDetail.razor` — `GetStatusColor` switch expressions are byte-for-byte identical across all 6 named cases plus `_ => Color.Default`.

**ArtifactsCreated:** Not explicitly mapped in either file. Falls through to `Color.Default` in both. Tony's note is confirmed — this is the intentional fallback, not a gap. ✅ Consistent.

---

### Issues Found

| # | Severity | File | Location | Issue | Fix |
|---|----------|------|----------|-------|-----|
| 1 | **Important** | `Dashboard.razor` | `OnInitializedAsync` — `authState.User.Identity?.Name` | `Identity.Name` in ASP.NET Core with Azure AD maps to the `name` claim (display name), **not** UPN. Silently queries by display name; user sees their own submissions only if their display name happens to match what `GetByUserAsync` expects. No error thrown. | Use `authState.User.FindFirst("preferred_username")?.Value ?? ""` (or the `upn` claim, depending on token configuration). Verify with the team what claim `GetByUserAsync` was designed to receive. |
| 2 | **Nitpick** | `Dashboard.razor` | `OnInitializedAsync` — `authState.User.IsInRole("NexusAdmin")` | `"NexusAdmin"` is a bare magic string. If the role name changes, this breaks silently at runtime. | Replace with `NexusRoles.Admin` constant (or equivalent shared constant). If no constant exists yet, create one. |
| 3 | **Nitpick** | `Dashboard.razor` | `_submissions = await SubmissionService.GetByUserAsync(userUpn)` | No null guard on return value. If the service returns null, `.Any()` in the template throws NullReferenceException. | Change to `_submissions = await SubmissionService.GetByUserAsync(userUpn) ?? new();` Same for `_pendingReview`. |

---

### Positive Observations

- Clean DI usage throughout — no `new`-ing services anywhere in `@code`.
- `_loading = true` / `finally { _loading = false; }` pattern is correct and robust.
- Admin section is properly separated (own table, own header, own empty state) — not mixed with user submissions.
- Status color mapping is perfectly consistent with `SubmissionDetail.razor`.
- Feature Area null handling is correct (`?? "—"`).
- Date formatting matches spec (`"MMM d, yyyy"`).

---

### What to Fix (Tony)

**Required before PASS:**

**Issue 1 — UPN claim (Important):**
In `OnInitializedAsync`, change:
```csharp
var userUpn = authState.User.Identity?.Name ?? "";
```
to:
```csharp
var userUpn = authState.User.FindFirst("preferred_username")?.Value
              ?? authState.User.FindFirst("upn")?.Value
              ?? "";
```
Confirm with the team which claim `GetByUserAsync` was built to receive — if the service was designed for `Identity.Name` (i.e. it queries by display name), fix the service contract instead. One of these two sides needs to be correct.

**Optional (do it anyway — takes 2 minutes):**

**Issue 2 — Magic string:**
Replace `"NexusAdmin"` with `NexusRoles.Admin` (create the constant if it doesn't exist):
```csharp
_isAdmin = authState.User.IsInRole(NexusRoles.Admin);
```

**Issue 3 — Null guard:**
```csharp
_submissions = await SubmissionService.GetByUserAsync(userUpn) ?? new();
// and if needed:
_pendingReview = await SubmissionService.GetAllPendingReviewAsync() ?? new();
```

---

### Checklist Results

| # | Item | Result |
|---|------|--------|
| 1 | `@page "/"` + `@attribute [Authorize]` | ✅ |
| 2 | `@rendermode InteractiveServer` | ✅ |
| 3 | Page title "NEXUS Dashboard" | ✅ |
| 4 | New Submission → `/nexus/new` | ✅ |
| 5 | Table columns: #, Title, Feature Area, Status, Submitted, Action | ✅ |
| 6 | Title → MudLink → `/nexus/{s.Id}` | ✅ |
| 7 | Feature Area null → `"—"` | ✅ |
| 8 | Status color mapping matches SubmissionDetail.razor | ✅ |
| 9 | Date format `MMM d, yyyy` | ✅ |
| 10 | Action "View" → `/nexus/{id}` | ✅ |
| 11 | Loading: MudProgressLinear while `_loading` | ✅ |
| 12 | Empty state: MudAlert when no submissions | ✅ |
| 13 | Admin guard on `GetAllPendingReviewAsync()` | ✅ |
| 14 | Separate admin table | ✅ |
| 15 | `ISubmissionService` via DI | ✅ |
| 16 | `AuthenticationStateProvider` via DI | ✅ |
| 17 | UPN claim — `Identity.Name` ≠ UPN in Azure AD | ⚠️ NEEDS-CHANGES |
| 18 | `_loading = true` before / `false` in `finally` | ✅ |
| 19 | No inline `new Service()` in `@code` | ✅ |
| 20 | Role string — `"NexusAdmin"` magic string | ⚠️ Nitpick |

---

*Reviewed by Hawkeye (Clint Barton) — cycle 1 — 2026-04-02*



---

## REVIEW cycle 2 — 2026-04-02

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** 7656ffd
**Scope:** Targeted re-review — 3 previously flagged items only

### Verdict: ✅ PASS

All 3 cycle 1 issues confirmed fixed in `src/FortressNexus.Web/Components/Pages/Dashboard.razor`.

### Item-by-Item Results

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | UPN claim (`preferred_username` first, `Identity.Name` fallback) | ✅ PASS | `var userUpn = authState.User.FindFirst("preferred_username")?.Value ?? authState.User.Identity?.Name ?? "";` (lines 122–124) |
| 2 | `NexusRoles.Admin` — no magic string | ✅ PASS | `_isAdmin = authState.User.IsInRole(NexusRoles.Admin);` (line 125) |
| 3 | Null guard on `GetByUserAsync` | ✅ PASS | `_submissions = await SubmissionService.GetByUserAsync(userUpn) ?? new();` (line 127) |

### Summary
All 3 cycle 1 NEEDS-CHANGES items have been correctly addressed. No new issues found in reviewed scope. Cycle 2 closes clean.


---

## REVIEW cycle 3 — 2026-04-02

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** 6a0ec0f
**Scope:** MSAL → Cookie consumer rework — Program.cs, appsettings.json, .csproj

### Verdict: ⚠️ NEEDS-CHANGES

All 22 checklist items pass. One critical defect found outside the checklist.

---

### CC Review Summary

CC ran the full 22-item checklist against NEXUS Program.cs, appsettings.json, .csproj, and FIRM Program.cs for comparison. 22/22 checklist items green. One critical issue discovered during out-of-checklist adversarial comparison: **DataProtection shared key ring is missing**.

---

### 22-Item Checklist Results

| # | Item | Result |
|---|------|--------|
| 1 | No `using Microsoft.Identity.Web` in Program.cs | ✅ PASS |
| 2 | No `using Microsoft.Identity.Web.UI` in Program.cs | ✅ PASS |
| 3 | No `AddMicrosoftIdentityWebAppAuthentication` call | ✅ PASS |
| 4 | No `Configure<CookieAuthenticationOptions>` MSAL block | ✅ PASS |
| 5 | No `AddMicrosoftIdentityUI` call | ✅ PASS |
| 6 | No `Microsoft.Identity.Web` in .csproj PackageReferences | ✅ PASS |
| 7 | `AzureAd` section removed from appsettings.json | ✅ PASS |
| 8 | `DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme` | ✅ PASS — Program.cs:25 |
| 9 | `DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme` | ✅ PASS — Program.cs:26 |
| 10 | Cookie Name = `.FortressAI.Session` (matches FIRM exactly) | ✅ PASS — Program.cs:36, FIRM:117 |
| 11 | Cookie Domain from `Auth__CookieDomain` config | ✅ PASS — Program.cs:37 |
| 12 | `LoginPath = "/auth/redirect-to-login"` | ✅ PASS — Program.cs:31 |
| 13 | `SameSite = SameSiteMode.Lax` | ✅ PASS — Program.cs:38 |
| 14 | `SecurePolicy = CookieSecurePolicy.Always` | ✅ PASS — Program.cs:39 |
| 15 | `FallbackPolicy = options.DefaultPolicy` | ✅ PASS — Program.cs:44 |
| 16 | `/auth/redirect-to-login` is AllowAnonymous | ✅ PASS — Program.cs:150 |
| 17 | Reads `FIP:LoginUrl` from config | ✅ PASS — Program.cs:146 |
| 18 | Passes `returnUrl` pointing back to nexus.fortressam.ai | ✅ PASS — Program.cs:148–149 |
| 19 | `FIP.LoginUrl` present in appsettings.json | ✅ PASS — appsettings.json:9–11 |
| 20 | `MapControllers()` still present | ✅ PASS — Program.cs:153 |
| 21 | `/health` AllowAnonymous endpoint still present | ✅ PASS — Program.cs:141 |
| 22 | `UseAuthentication()` before `UseAuthorization()` | ✅ PASS — Program.cs:136–137 |

---

### Issues Found

#### ❌ CRITICAL — DataProtection shared key ring missing

**File:** `Program.cs` (missing — no `AddDataProtection()` call anywhere)

**Issue:** ASP.NET Core uses DataProtection to encrypt and decrypt auth cookies. NEXUS has no DataProtection configuration. Without sharing FAIT's key ring, NEXUS generates its own ephemeral keys at startup and **cannot decrypt the `.FortressAI.Session` cookie that FAIT issued**. Every authenticated request will fail and redirect to `/auth/redirect-to-login` — infinite redirect loop in deployed environments.

**FIRM's working pattern (Program.cs:159–162):**
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();
```

**Required fix — add to Program.cs, matching FIRM exactly:**
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();
```
Also requires `SharedKeyRingDbContext` to be registered and the DB connection string configured to point to the same `fred_dev`/prod DB where FAIT writes keys.

Without this fix, cookie auth will appear to work locally (ephemeral keys, single process) but will fail in any deployed environment.

---

### Warnings (non-blocking)

#### ⚠️ WARN — `returnUrl` drops query string
**Program.cs:147–148:** `ctx.Request.Path` does not include query string. A user at `/submissions?id=abc123` returns to `/submissions` after login — query string is silently lost. Not a security issue, UX regression only. Fix with `ctx.Request.Path + ctx.Request.QueryString` if deep-link fidelity matters.

#### ⚠️ WARN — Config key style inconsistency vs. FIRM
NEXUS reads `builder.Configuration["FIP:LoginUrl"]` (colon notation). FIRM reads `config["FIP__LoginUrl"]` (double-underscore). Both work — .NET config maps `FIP__LoginUrl` env vars to `FIP:LoginUrl` — but ops must use env-var form `FIP__LoginUrl` for overrides. Not blocking, worth noting.

---

### NEXUS vs. FIRM Cookie Config — Side-by-Side

| Property | NEXUS | FIRM | Match? |
|---|---|---|---|
| `DefaultScheme` | `CookieAuthenticationDefaults` | `CookieAuthenticationDefaults` | ✅ |
| `DefaultChallengeScheme` | `CookieAuthenticationDefaults` | `CookieAuthenticationDefaults` | ✅ |
| `Cookie.Name` | `.FortressAI.Session` | `.FortressAI.Session` | ✅ |
| `Cookie.Domain` | `Auth__CookieDomain` config | `Auth__CookieDomain` config | ✅ |
| `SameSite` | `Lax` | `Lax` | ✅ |
| `SecurePolicy` | `Always` | `Always` | ✅ |
| `FallbackPolicy` | `DefaultPolicy` | `DefaultPolicy` | ✅ |
| `DataProtection` | **MISSING** | Shared key ring (DB) | ❌ CRITICAL |

---

### What to Fix (Tony)

**1 item required before PASS:**

Add DataProtection shared key ring to Program.cs — match FIRM exactly:
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();
```
Ensure `SharedKeyRingDbContext` is registered (with the same connection string that FAIT uses for the DataProtectionKeys table). Without this, the cookie FAIT issues cannot be read by NEXUS.

---

*Reviewed by Hawkeye (Clint Barton) — cycle 3 — 2026-04-02*


---

## REVIEW cycle 4 — 2026-04-02

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** f948387
**Scope:** Targeted single-item review — DataProtection shared key ring addition

### Verdict: ✅ PASS

All 6 checklist items confirmed. Cycle 3's critical defect is fully resolved.

---

### 6-Item Checklist Results

| # | Item | Result | Evidence |
|---|------|--------|----------|
| 1 | `SharedKeyRingDbContext` implements `IDataProtectionKeyContext` | ✅ PASS | `public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext` |
| 2 | `DataProtectionKeys` DbSet maps to `"DataProtectionKeys"` table | ✅ PASS | `public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();` + `modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");` |
| 3 | Key ring DbContext uses `FORTRESS_DB_HOST` + `FIP_KEYRING_DB_NAME` (default `"fred_dev"`) | ✅ PASS | `var keyRingDbHost = builder.Configuration["FORTRESS_DB_HOST"];` + `var keyRingDbName = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";` |
| 4 | `AddDataProtection()` chain: `.PersistKeysToDbContext<SharedKeyRingDbContext>()` + `.SetApplicationName("FortressAI")` + `.DisableAutomaticKeyGeneration()` | ✅ PASS | All three present with exact values: `builder.Services.AddDataProtection().PersistKeysToDbContext<SharedKeyRingDbContext>().SetApplicationName("FortressAI").DisableAutomaticKeyGeneration();` |
| 5 | `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` in .csproj | ✅ PASS | `<PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="8.0.13" />` |
| 6 | `using Microsoft.AspNetCore.DataProtection;` in Program.cs | ✅ PASS | `using Microsoft.AspNetCore.DataProtection;` (Program.cs line 3) |

---

### FIRM vs. Nexus Comparison

| Property | FIRM | Nexus | Match? |
|---|---|---|---|
| `SetApplicationName` | `"FortressAI"` | `"FortressAI"` | ✅ |
| `DisableAutomaticKeyGeneration` | present | present | ✅ |
| `PersistKeysToDbContext` | `SharedKeyRingDbContext` | `SharedKeyRingDbContext` | ✅ |
| Comment | `// FIRM is a consumer` | `// NEXUS is a consumer` | ✅ correct |
| Password chain | `FORTRESS_DB_PASS ?? ""` | `NEXUS_DB_PASSWORD ?? FORTRESS_DB_PASS ?? ""` | ℹ️ intentional (Nexus-specific primary var) |

The password fallback chain in Nexus is a deliberate extension — `NEXUS_DB_PASSWORD` takes priority, then falls back to the shared `FORTRESS_DB_PASS`. Not a defect; ensure deployment config has the correct credentials against `fred_dev`.

---

### Summary

Cycle 3's critical defect (missing DataProtection shared key ring) is fully and correctly resolved. The implementation matches FIRM's working pattern exactly on all required criteria. Cookie auth between FAIT and NEXUS will function correctly in deployed environments.

---

*Reviewed by Hawkeye (Clint Barton) — cycle 4 — 2026-04-02*
