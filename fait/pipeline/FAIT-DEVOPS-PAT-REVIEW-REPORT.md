# FAIT DevOps PAT Connection — Code Review Report

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `ed6ae0c`
**Review Cycle:** 1 of 2
**Date:** 2026-03-12

---

## Verdict: ✅ PASS (with one Important finding)

All 32 checklist items verified. No critical issues. One important finding (#16-ext: unregistered named HttpClient). All focus items from Maria confirmed clean.

---

## Checklist Results

### Model + DB (items 1–7)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | `UserDevOpsConnection` has `UserId`, `OrgUrl`, `PatEncrypted` — no raw PAT field | ✅ PASS | Model has exactly these three fields + `CreatedAt`/`UpdatedAt`/nav property. No raw PAT field. |
| 2 | `user_devops_connections` DDL uses `CREATE TABLE IF NOT EXISTS` | ✅ PASS | `DatabaseInitializationService` line confirmed. |
| 3 | FK references `users (Id)` — not `AspNetUsers` | ✅ PASS | `REFERENCES users (Id) ON DELETE CASCADE` — correct table name. Focus item clear. |
| 4 | `PRIMARY KEY (user_id)` — one record per user | ✅ PASS | DDL has `PRIMARY KEY (user_id)`. `SaveAsync` does find-then-add-or-update (upsert semantics). |
| 5 | `AppDbContext` `OnModelCreating` maps to `user_devops_connections` with snake_case columns | ✅ PASS | `.ToTable("user_devops_connections")`, `user_id`, `org_url`, `pat_encrypted`, `created_at`, `updated_at` all mapped. |
| 6 | `UserDevOpsConnections` DbSet added to `AppDbContext` | ✅ PASS | `public DbSet<UserDevOpsConnection> UserDevOpsConnections => Set<UserDevOpsConnection>();` present. |
| 7 | No `EnsureCreated()` or `MigrateAsync()` calls added | ✅ PASS | Grepped entire `src/` — neither call present. |

---

### DevOpsConnectionService (items 8–18)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 8 | `IDataProtectionProvider.CreateProtector("DevOpsPat")` — no custom AES | ✅ PASS | Constructor: `_protector = dataProtectionProvider.CreateProtector("DevOpsPat")` — correct. |
| 9 | `SaveAsync` encrypts PAT via protector, stores in `PatEncrypted` | ✅ PASS | `var encryptedPat = _protector.Protect(pat)` before DB write. Raw `pat` never touches the entity. |
| 10 | `GetDecryptedPat` has try/catch around `Unprotect()` | ✅ PASS | `try { return _protector.Unprotect(row.PatEncrypted); } catch (Exception ex) { ... return null; }` — focus item clear. |
| 11 | `GetDecryptedPat` returns `null` on failure (not throws) | ✅ PASS | Catch block returns `null`. Focus item clear. |
| 12 | `IsConnectedAsync` returns `false` when no row exists | ✅ PASS | `AnyAsync(c => c.UserId == userId)` — returns false cleanly if no row. |
| 13 | `DisconnectAsync` safe when no row exists | ✅ PASS | `if (row is null) return;` guard present. |
| 14 | `TestConnection` builds `Authorization: Basic base64(":{PAT}")` | ✅ PASS | `Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"))` — colon-prefixed, correct convention. Focus item clear. |
| 15 | `TestConnection` calls `GET {orgUrl}/_apis/projects?api-version=7.1` | ✅ PASS | `$"{normalizedOrg}/_apis/projects?api-version=7.1"` — exact URL. |
| 16 | `TestConnection` returns meaningful result for 200/401/403/network error | ✅ PASS | 200 returns project count, 401/403 returns invalid PAT message, 404 returns org-not-found, `HttpRequestException` returns DNS/network message, `TaskCanceledException` returns timeout message. |
| 17 | `DevOpsConnectionService` registered in DI (`Program.cs`) | ✅ PASS | `builder.Services.AddScoped<DevOpsConnectionService>();` present. |
| 18 | Raw decrypted PAT not logged at any level | ✅ PASS | Only `userId` and `orgUrl` are logged. No PAT in any log statement. |

**⚠️ IMPORTANT — Item 16 Extension: Unregistered named HttpClient**

`TestConnectionAsync` calls `_httpClientFactory.CreateClient("devops-test")` (line 139), but **no named client `"devops-test"` is registered in `Program.cs`**. The registered named clients are only `"mcp-transport"`.

`IHttpClientFactory.CreateClient("devops-test")` will fall back to creating an unnamed/default client — it won't throw, and the test call will work. However:
- No timeout is configured on this client (the default `HttpClient` timeout is 100 seconds)
- Long DNS timeouts on a bad org URL will block the UI for up to 100 seconds before the `TaskCanceledException` path fires

**Fix:** Register the named client in `Program.cs`:
```csharp
builder.Services.AddHttpClient("devops-test", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

This is **not a blocking issue** (the feature works), but it's a user-experience gap — a bad org URL will leave the Test Connection button spinning for a long time.

---

### Settings.razor (items 19–25)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 19 | `DevOpsConnectionService` injected (not old `DevOpsTokenService`) | ✅ PASS | `@inject DevOpsConnectionService DevOpsConnSvc` — correct. No reference to `DevOpsTokenService` anywhere in the file. |
| 20 | When connected: shows org URL + Disconnect button; PAT field hidden | ✅ PASS | `@if (_devOpsConnected)` branch shows `MudAlert` with `_devOpsOrgUrl` and a Disconnect button only. No PAT field in this branch. Focus item clear. |
| 21 | When disconnected: shows org URL field, PAT password field, Save and Test Connection buttons | ✅ PASS | `else` branch renders both `MudTextField` inputs and both buttons. `InputType="InputType.Password"` on PAT field. |
| 22 | Save button disabled when either field is empty | ✅ PASS | `Disabled="@(string.IsNullOrWhiteSpace(_devOpsOrgUrl) \|\| string.IsNullOrWhiteSpace(_devOpsPat))"` on Save button. |
| 23 | Test Connection calls service, displays result (project count or error) | ✅ PASS | `TestDevOpsConnection()` calls `DevOpsConnSvc.TestConnectionAsync(...)`, sets `_devOpsTestMessage`/`_devOpsTestSuccess`. Result rendered in `MudAlert` beneath the fields. |
| 24 | After disconnect: clears `_devOpsOrgUrl` from UI | ✅ PASS | `DisconnectDevOps()`: `_devOpsOrgUrl = string.Empty;` — focus item clear. |
| 25 | PAT field cleared from component state after successful Save | ✅ PASS | `SaveDevOpsConnection()`: `_devOpsPat = string.Empty;` immediately after `SaveAsync` completes. Focus item clear. |

---

### OAuth Removal (items 26–29)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 26 | `/auth/devops-callback` MapGet fully removed from `Program.cs` | ✅ PASS | Searched `Program.cs` — no `devops-callback` string present. |
| 27 | `DevOpsTokenService` DI registration removed from `Program.cs` | ✅ PASS | No `DevOpsTokenService` reference in `Program.cs`. `DevOpsConnectionService` takes its place. |
| 28 | `UserDevOpsTokens` DbSet and entity config fully removed from `AppDbContext` | ✅ PASS | No `UserDevOpsTokens` DbSet or entity config block in `AppDbContext.cs`. |
| 29 | `user_devops_tokens` entry removed from `extraTables` | ✅ PASS | Not present in `DatabaseInitializationService` extraTables array. Commit message notes the DB table itself is left (non-destructive), just removed from code. |

---

### Security (items 30–32)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 30 | Raw PAT never written to DB (only `PatEncrypted`) | ✅ PASS | `SaveAsync` encrypts before constructing the entity. Only `encryptedPat` touches the DB. |
| 31 | Raw PAT not returned to frontend after save — Settings page masks or omits it | ✅ PASS | After `SaveAsync`, component transitions to `_devOpsConnected = true` branch which shows only the org URL. PAT field is in the disconnected branch only (never re-shown). |
| 32 | `TestConnection` uses decrypted PAT only in-memory, not stored in a field | ✅ PASS | PAT is a local parameter passed directly to `TestConnectionAsync`. It's used to build the `Authorization` header inline and never written to any field or property. |

---

## Focus Item Summary

| Focus Item | Status |
|---|---|
| **#3** — FK must reference `users`, not `AspNetUsers` | ✅ CLEAN — `REFERENCES users (Id)` confirmed |
| **#10/#11** — Decryption failure returns `null`, not throws | ✅ CLEAN — try/catch returns null |
| **#14** — Basic auth header `base64(":" + PAT)` | ✅ CLEAN — colon-prefix confirmed |
| **#20** — PAT never re-displayed after save | ✅ CLEAN — connected branch shows only org URL |
| **#25** — PAT cleared from component state after save | ✅ CLEAN — `_devOpsPat = string.Empty` on save |

---

## Issues Found

### ⚠️ Important (non-blocking)

**[DEVOPS-PAT-01] `devops-test` named HttpClient not registered**

- **File:** `src/FortressAI.Web/Program.cs`
- **Service:** `DevOpsConnectionService.cs`, line 139
- **Problem:** `_httpClientFactory.CreateClient("devops-test")` references an unregistered named client. Falls back to default (100s timeout). A bad org URL will leave Test Connection spinning for ~100 seconds before timing out.
- **Fix:**
  ```csharp
  // In Program.cs, near the mcp-transport registration:
  builder.Services.AddHttpClient("devops-test", client =>
  {
      client.Timeout = TimeSpan.FromSeconds(10);
  });
  ```
- **Impact:** UX only — feature functions correctly, just with a long timeout on DNS failure.

---

## Summary

32/32 checklist items pass. All five focus items confirmed clean. One important (non-blocking) finding: the `devops-test` named HttpClient should be registered with a short timeout to prevent a 100-second hang on bad org URLs.

**This can ship as-is.** The HttpClient registration fix is small enough to include in the same PR or as a fast-follow. Recommending PASS with the fix applied before merge.

---

*Reviewed by Hawkeye — cycle 1 of 2*
