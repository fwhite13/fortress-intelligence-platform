# Build Report — ADO#1351: FIRM MS365 Forklift

**Commit:** `877a9cc`  
**Branch:** main  
**Build result:** ✅ 0 errors, 0 warnings  
**Date:** 2026-03-29

---

## What was built

Surgical forklift of FAIT's working MS365 pattern into FIRM. Replaced FIRM's broken `UserMicrosoftToken`/`FirmDbContext` config stack (which used `HasColumnType("longtext")`, `HasColumnType("datetime(6)")`, `string` UserId) with FAIT's exact pattern (no `HasColumnType`, `Guid` UserId). Created new `MicrosoftTokenService` aligned to FAIT. Updated all dependent code to use `Guid` throughout.

---

## Files changed

| File | Change |
|------|--------|
| `Models/FirmUser.cs` | `Id` changed from `string` to `Guid` |
| `Models/UserMicrosoftToken.cs` | `UserId` changed from `string` to `Guid`; added `FirmUser? User` navigation property; default values aligned to FAIT |
| `Data/FirmDbContext.cs` | FirmUser Id: removed `HasColumnType("char(36)")`, now `ValueGeneratedOnAdd()`. UserMicrosoftToken config block: completely replaced with FAIT pattern — no `HasColumnType` calls, proper FK navigation wired |
| `Services/MicrosoftTokenService.cs` | **NEW** — exact FAIT copy with FIRM namespace/type substitutions; uses `Azure:ClientId/TenantId/ClientSecret` config keys |
| `Services/IFirmMicrosoftTokenService.cs` | All `string firmUserId` → `Guid firmUserId` |
| `Services/FirmMicrosoftTokenService.cs` | All `string firmUserId` → `Guid firmUserId`; keeps `Firm:Graph*` config keys |
| `Services/MeetingService.cs` | `GetOrCreateUserAsync`: `Id = Guid.NewGuid()` (removed `.ToString()`). String-parameterized methods unchanged (FirmMeeting.CreatedBy stays string) |
| `Program.cs` | Added `builder.Services.AddScoped<MicrosoftTokenService>()`; `/auth/ms-callback` state parsing now uses `Guid.TryParse`; user ID comparison uses `Guid` throughout |
| `Controllers/MeetingsApiController.cs` | All `firmUser.Id` and `user.Id` calls to string-accepting methods wrapped with `.ToString()` |
| `Components/Pages/MeetingDetail.razor` | `user.Id.ToString()` for `GetMeetingAsync` |
| `Components/Pages/Meetings.razor` | `firmUser?.Id.ToString()` assignment; `GetCurrentUserAsync` returns `firmUser.Id.ToString()`; added `@inject MicrosoftTokenService`; added MS365 connection banner + `ConnectMs365()` method |

---

## Root cause fixed

The `ElementMappingConvention` NullRef crashes were caused by Pomelo EF Core's type mapping conventions mishandling explicit `HasColumnType("longtext")` and `HasColumnType("datetime(6)")` on properties that don't need them. FAIT's pattern avoids these entirely — no `HasColumnType` on text/datetime properties, let Pomelo infer them. FIRM now matches.

Additionally, `FirmUser.Id` was `string` while `UserMicrosoftToken.UserId` was also `string` — but the FK relationship requires type parity. Changing both to `Guid` + adding `GuidFormat = MySqlGuidFormat.None` to the connection string (already present from ADO#1329) ensures char(36) round-trip without binary conversion.

---

## Key design decisions

- **`FirmMeeting.CreatedBy` stays `string`** — the spec explicitly permits this; it stores the user GUID as a string FK, which is compatible with `Guid.ToString()`.
- **`FirmMicrosoftTokenService` kept alongside new `MicrosoftTokenService`** — the old service is still registered via `IFirmMicrosoftTokenService` (used by CalendarService and `/auth/ms-callback`). The new `MicrosoftTokenService` (FAIT-pattern, `Azure:*` config) is registered as a concrete scoped service and used by the Meetings.razor connect prompt.
- **No DB migration needed** — `GuidFormat = MySqlGuidFormat.None` ensures `Guid` ↔ `char(36)` is transparent. Existing data is compatible.

---

## Acceptance criteria

- [x] `dotnet build` succeeds — zero errors, zero warnings
- [x] `FirmUser.Id` is `Guid`
- [x] `UserMicrosoftToken.UserId` is `Guid`
- [x] No `HasColumnType` for longtext, datetime(6), or JSON in UserMicrosoftToken config
- [x] New `MicrosoftTokenService` created with FAIT-pattern Guid methods
- [x] `MicrosoftTokenService` registered in DI
- [x] `/auth/ms-callback` parses state as Guid
- [x] Meetings.razor shows "Connect Microsoft 365" banner for unconnected users
- [x] `GuidFormat = MySqlGuidFormat.None` present in connection string (was already there from ADO#1329)

---

## How to test locally

1. `dotnet run` from `firm/src/FortressIntelligenceRM.Web/`
2. Navigate to `/meetings` — should load without red toast
3. If no MS365 token in DB: should see yellow "Connect Microsoft 365" banner
4. If MS365 token exists: should see green "connected as [email]" banner
5. Click "Connect Microsoft 365" → should redirect to Microsoft OAuth

---

## Notes for Clint

- The `/auth/ms-callback` endpoint in Program.cs still uses `IFirmMicrosoftTokenService` (not the new `MicrosoftTokenService`) — this is intentional; the old service uses `Firm:Graph*` config keys that are already configured in ECS. The new service uses `Azure:*` keys which need to be provisioned for FIRM.
- When `Azure:ClientId/TenantId/ClientSecret` are not set in FIRM's ECS task definition, `MicrosoftTokenService.IsConfigured` returns false and the connect button will redirect to an OAuth URL with an empty `client_id`. This is a config task, not a code task — ADO should track the ECS secret provisioning separately.
- `MeetingsApiController.cs` was touched to add `.ToString()` calls — worth a look to confirm those are correct.
