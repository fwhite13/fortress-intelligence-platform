# Build Report — ADO#3160 (Part 2: Avatar Upload Implementation)

## What was built

Avatar upload feature for FAIT: file picker + S3 upload + DB persist in AssistantSettings, plus conditional avatar image display in MessageBubble (falls back to icon when no AvatarUrl set).

---

## Files changed

- `src/FortressAI.Web/Components/Pages/AssistantSettings.razor`
  - Added `@using Amazon.S3.Model`, `@inject IAmazonS3 S3`, `@inject IConfiguration AppConfig`
  - Added Avatar card UI: file upload picker, 2MB/format validation, preview image, uploading state, error display
  - Added `_avatarUrl`, `_avatarUploading`, `_avatarError` fields
  - Loaded `_avatarUrl` from config in `OnInitializedAsync`
  - Added `HandleAvatarUpload`: validates file, deletes old S3 object if exists, uploads to `workspaces/{userId}/avatar/{guid}{ext}`, persists URL to DB
  - Added `ExtractS3Key` helper for old-key extraction from URL
  - Added `<style>` block with CSS-var-driven preview classes

- `src/FortressAI.Web/Components/Chat/MessageBubble.razor`
  - Replaced unconditional `MudIcon` with conditional: renders `<img class="assistant-avatar-img">` when `AssistantConfig.AvatarUrl` is non-empty, else falls back to icon
  - Added `<style>` block with CSS-var-driven avatar img classes

---

## Migration

Migration `20260510014154_AddAvatarUrlToUserAssistantConfig` applied manually to `fait_dev`.

**Context:** The DB had a consolidated migration `20260509000000_FaitDevConsolidation` applied directly (not via EF), which already contained most columns from this migration file. Only `AvatarUrl` was genuinely missing.

**Resolution:**
1. Inserted historical migration IDs into `__EFMigrationsHistory` (they were already applied via consolidation)
2. Applied `AvatarUrl varchar(512)` column directly via SQL (all other columns from the migration already existed)
3. Inserted `20260510014154_AddAvatarUrlToUserAssistantConfig` into `__EFMigrationsHistory`

**Verified:** `SHOW COLUMNS FROM user_assistant_config LIKE 'AvatarUrl'` → `varchar(512) NULL` ✅

---

## Parallelization used

No — single CC session, sequential changes (AssistantSettings then MessageBubble).

## CC sessions run

1 CC run (sonnet) → commit `9f402032`

---

## Acceptance criteria verification

- [x] File upload picker appears in /assistant-settings after Accent Color card — implemented ✅
- [x] 2MB size limit enforced with error message — implemented in `HandleAvatarUpload` ✅
- [x] Format validation (jpg/jpeg/png/gif/webp) — implemented ✅
- [x] S3 upload to `workspaces/{userId}/avatar/{guid}{ext}` — implemented ✅
- [x] Old avatar deleted from S3 on re-upload — implemented with warning-only on delete failure ✅
- [x] AvatarUrl persisted to DB — EF SaveChangesAsync on `UserAssistantConfig` ✅
- [x] Preview image shown when AvatarUrl is set — conditional `<img>` in avatar card ✅
- [x] MessageBubble shows avatar image when AvatarUrl set — conditional `<img>` vs `MudIcon` ✅
- [x] CSS classes using CSS vars only — no inline styles, `--avatar-size-lg`, `--avatar-size-sm`, `--radius-full`, `--color-accent` ✅
- [x] Build: 0 errors ✅ (32 pre-existing warnings)
- [x] Migration applied to fait_dev ✅

---

## Known edge cases / things Clint should scrutinize

1. **S3 URL format** — Avatar URL stored as `https://{bucket}.s3.amazonaws.com/{key}`. If CloudFront is later added, `ExtractS3Key` parses the bucket-path format correctly but won't handle CloudFront URLs. Worth noting for future.
2. **AccessDenied re-throw** — On S3 `AccessDenied`, the exception is re-thrown after setting `_avatarError`. This will surface as an unhandled exception in Blazor's error boundary. Intentional per spec (hard fail on access denied = misconfiguration).
3. **Migration history repair** — The manual migration history cleanup is a one-time fix. Future migrations using `dotnet ef database update` against fait_dev should work normally now that all history entries are in sync.
4. **WORKSPACE_S3_BUCKET config key** — Reads from `AppConfig["WORKSPACE_S3_BUCKET"]` with fallback to `"fortress-user-workspaces"`. Verify this key is set in the ECS task definition env for production.

---

## How to test locally

1. Navigate to `/assistant-settings`
2. Scroll to "Assistant Avatar" card
3. Click "Upload Avatar" → select a PNG/JPG under 2MB
4. Verify: uploading spinner appears, then success snackbar
5. Verify: preview image renders in the settings page
6. Open a chat conversation — verify the message bubble shows the avatar image instead of the icon
7. Upload a second avatar — verify old S3 object is deleted and new one shows

---

## Commit

`9f402032` — `feat(fait#3160): add avatar upload UI to AssistantSettings and conditional avatar display in MessageBubble`
