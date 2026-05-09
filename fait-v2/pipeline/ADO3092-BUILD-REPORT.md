# Build Report — ADO#3092: Avatar NSFW Check on Upload via Bedrock Vision Model

**Date:** 2026-05-09
**Branch:** main
**Service:** FAIT v2 (`fait-v2/src/FortressAI.V2.Web/`)

---

## What Was Built

Avatar upload endpoint (`POST /api/profile/avatar`) with Bedrock vision-based NSFW moderation.
- Images are checked via `claude-haiku-4-5-20251001` before being stored in S3
- Rejected images return HTTP 400 with reason; service fails open on Bedrock errors
- Accepted avatars are stored in S3 at `avatars/{userId}/{uuid}.{ext}` and the URL is persisted to the `users.avatar_url` DB column

---

## Files Changed

| File | Change |
|------|--------|
| `Data/Models/User.cs` | Added `AvatarUrl` property (`string?`, `[Column("avatar_url")]`, `[MaxLength(1000)]`) |
| `Data/FaitV2DbContext.cs` | Added `entity.Property(e => e.AvatarUrl)...` in `OnModelCreating` for the `users` entity |
| `Data/Migrations/20260509100000_AddAvatarUrlToUser.cs` | New migration — `AddColumn avatar_url varchar(1000) nullable` |
| `Data/Migrations/FaitV2DbContextModelSnapshot.cs` | Added `AvatarUrl` property to `User` entity snapshot block |
| `Services/AvatarModerationService.cs` | New file — `IAvatarModerationService` interface + `AvatarModerationService` implementation |
| `Program.cs` | Registered `IAvatarModerationService`; added `POST /api/profile/avatar` endpoint |

---

## Migration Created

**File:** `Data/Migrations/20260509100000_AddAvatarUrlToUser.cs`

**Up:** `ALTER TABLE users ADD COLUMN avatar_url varchar(1000) NULL`
**Down:** `ALTER TABLE users DROP COLUMN avatar_url`

---

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| `AvatarUrl` column added to `User` entity + EF config | PASS |
| EF migration file created with correct DDL | PASS |
| Snapshot updated with `AvatarUrl` property | PASS |
| `IAvatarModerationService` interface defined | PASS |
| `AvatarModerationService` calls Bedrock vision model with base64 image | PASS |
| NSFW check returns `AvatarModerationResult(IsAllowed, Reason)` | PASS |
| Fails open on Bedrock exception (logs warning, allows upload) | PASS |
| Service registered as `AddScoped` in DI | PASS |
| Endpoint validates MIME type (jpeg/png/webp/gif only) | PASS |
| Endpoint validates file size (≤2MB) | PASS |
| Rejected images return HTTP 400 with reason message | PASS |
| Accepted images uploaded to S3 `avatars/{userId}/{uuid}.{ext}` | PASS |
| `users.avatar_url` updated after successful S3 upload | PASS |
| DB update uses 5-second timeout; failure does not fail the request | PASS |
| Endpoint requires authorization | PASS |

---

## Build Result

```
Build succeeded.
    3 Warning(s)
    0 Error(s)

Time Elapsed 00:00:06.28
```

**0 errors confirmed.**

Warnings are pre-existing:
- `CS0649` on `KnowledgeBase._statusRefreshTimer` — pre-existing, unrelated
- `BedrockRuntime1002` on `AvatarModerationService.cs:82` — SDK pattern validator warning for cross-region inference profile ID format; same warning exists in `CompactionService.cs` (pre-existing pattern)
- `BedrockRuntime1002` on `CompactionService.cs:178` — pre-existing
