# Build Report: FAIT Azure DevOps OAuth Integration

**Task:** FAIT-DEVOPS-OAUTH  
**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-12  

---

## Build Result

✅ **0 Error(s), 31 Warning(s)**  
*(Warnings are pre-existing MUD0002 analyzer warnings unrelated to this change)*

```
Build succeeded.
    31 Warning(s)
    0 Error(s)
Time Elapsed 00:00:02.05
```

---

## Commit SHA

`7c1b7bc` — pushed to `main` on `github.com:fwhite13/fortress-intelligence-platform.git`

Commit message: `feat(devops): Azure DevOps OAuth integration — token storage, Settings UI, /auth/devops-callback`

---

## Files Modified / Created

| File | Action | Description |
|------|--------|-------------|
| `src/FortressAI.Shared/Models/UserDevOpsToken.cs` | **Created** | New model with UserId, AccessToken, RefreshToken?, ExpiresAt, Email?, DisplayName?, ConnectedAt |
| `src/FortressAI.Web/Data/AppDbContext.cs` | **Modified** | Added `UserDevOpsTokens` DbSet + `OnModelCreating` entity config mapping to `user_devops_tokens` |
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | **Modified** | Added `user_devops_tokens` to `extraTables` array (CREATE TABLE IF NOT EXISTS) |
| `src/FortressAI.Web/Services/DevOpsTokenService.cs` | **Created** | Full OAuth service — GetAuthorizationUrl, ExchangeCodeAsync, GetTokenAsync, DeleteTokenAsync, IsConnectedAsync |
| `src/FortressAI.Web/Program.cs` | **Modified** | Registered `DevOpsTokenService` as scoped; added `IMemoryCache` using; added `/auth/devops-callback` endpoint |
| `src/FortressAI.Web/Components/Pages/Settings.razor` | **Modified** | Added Azure DevOps UI card, inject, fields, OnInitializedAsync checks, snackbar feedback, ConnectDevOps/DisconnectDevOps methods |

---

## Architecture Notes

### OAuth Flow
- Uses **Azure AD PKCE flow** (`https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize`) — same endpoint as M365, different scopes and config prefix
- State validated via `IMemoryCache` with 10-minute TTL, cache key: `devops_oauth_state:{state}`
- On successful token exchange, fetches DevOps profile from `https://app.vssps.visualstudio.com/_apis/profile/me?api-version=7.1` to populate `DisplayName` and `Email`

### Differences from MicrosoftTokenService
| Aspect | MicrosoftTokenService | DevOpsTokenService |
|--------|----------------------|--------------------|
| Config prefix | `Azure:` | `AzureDevOps:` |
| Scopes | Graph mail/calendar | `vso.work vso.code vso.build_execute offline_access` |
| Profile API | Graph `/v1.0/me` | VSSPS `/_apis/profile/me?api-version=7.1` |
| Stored email field | `MicrosoftEmail` | `Email` |
| Has `DisplayName` | No | Yes |
| RefreshToken nullable | No (required) | Yes (nullable) |

### Graceful degradation
`DevOpsTokenService.IsConfigured` returns `false` if any of the three config keys are absent. The Settings UI shows an info alert instead of the connect button — app does not crash.

---

## DB Table Created

**Table:** `user_devops_tokens`

```sql
CREATE TABLE IF NOT EXISTS user_devops_tokens (
    user_id CHAR(36) NOT NULL,
    access_token LONGTEXT NOT NULL,
    refresh_token LONGTEXT NULL,
    expires_at DATETIME(6) NOT NULL,
    email VARCHAR(256) NULL,
    display_name VARCHAR(256) NULL,
    connected_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (user_id),
    CONSTRAINT fk_devops_tokens_user FOREIGN KEY (user_id) REFERENCES AspNetUsers (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```

Created via `DatabaseInitializationService` on startup (idempotent — `IF NOT EXISTS`).

---

## ECS Environment Variables Required

These must be set by Rhodey/Fred before the Azure DevOps integration will activate. The app will start normally without them (shows "not configured" in Settings).

| Variable | Value |
|----------|-------|
| `AzureDevOps__ClientId` | Azure app registration client ID |
| `AzureDevOps__ClientSecret` | Azure app registration client secret |
| `AzureDevOps__TenantId` | Azure tenant ID |
| `AzureDevOps__RedirectUri` | `https://fait.dev.fortressam.ai/auth/devops-callback` |

*(Note: Double underscores `__` = nested config key separator in ECS/environment variables)*

---

## Azure App Registration — Action Required

Add the following **Redirect URI** to the Azure app registration used for DevOps:

```
https://fait.dev.fortressam.ai/auth/devops-callback
```

Platform type: **Web**

If a separate app registration is created for DevOps (recommended to isolate scopes), ensure the following API permissions are granted:
- `vso.work` — Read work items
- `vso.code` — Read source code
- `vso.build_execute` — Execute builds
- `offline_access` — Refresh token support

---

## DevOps Profile API Endpoint

Used to retrieve display name and email post-token-exchange:

```
GET https://app.vssps.visualstudio.com/_apis/profile/me?api-version=7.1
Authorization: Bearer <access_token>
```

Response fields used:
- `displayName` → stored in `user_devops_tokens.display_name`
- `emailAddress` → stored in `user_devops_tokens.email`

---

## Self-Review Checklist

- [x] Model created with correct nullable/non-nullable fields
- [x] DbSet registered in AppDbContext
- [x] OnModelCreating config matches column naming convention (`snake_case`)
- [x] DB table DDL added to DatabaseInitializationService extraTables
- [x] DevOpsTokenService clones MicrosoftTokenService pattern faithfully
- [x] Service registered as scoped in Program.cs
- [x] OAuth callback validates state from IMemoryCache (not Redis)
- [x] Callback redirects to `/settings?devops_connected=true` on success
- [x] Callback redirects to `/settings?devops_error=...` on failure
- [x] Settings.razor UI card matches M365 card structure
- [x] IsConfigured check gates connect button
- [x] Query param feedback (`devops_connected`, `devops_error`) wired to snackbars
- [x] ConnectDevOps/DisconnectDevOps methods implemented
- [x] Build: 0 errors
- [x] Committed and pushed to main
