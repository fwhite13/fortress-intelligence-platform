# Review Report — ADO#3092: Avatar NSFW Check on Upload

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-09
**Cycle:** 1
**Commit Reviewed:** `8743c5a6`
**CC Invocation:** `cat pipeline/review-brief-3092.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Verdict: NEEDS-CHANGES

One critical (blocking) issue, two important issues, four nitpicks. Data layer and moderation core are clean.

---

## Critical Issues (Blocking)

### C1 — Hardcoded Bedrock Model ID (`AvatarModerationService.cs:16`)

```csharp
private const string ModerationModel = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
```

**Violates `security-rules.md`:** "NEVER hardcode Bedrock model IDs in source code. Always read model IDs from `IConfiguration` (appsettings.json / environment override). Pattern: `_config["Bedrock:ModelId"]` or equivalent — never a string literal."

This is a `const` field, not config-driven. Must be replaced with `_config["Bedrock:AvatarModerationModelId"]` (or similar key), injected via `IConfiguration` in the constructor.

---

## Important Issues (Should Fix)

### I1 — S3 Avatar URL hardcodes AWS domain (`Program.cs:946`)

```csharp
var avatarUrl = $"https://{bucket}.s3.amazonaws.com/{s3Key}";
```

Bakes in the AWS S3 public hostname. If the deployment uses CloudFront, a custom domain, or a non-us-east-1 region bucket, this URL will be wrong. Should be config-driven:

```csharp
var baseUrl = config["AWS:AvatarBaseUrl"] ?? $"https://{bucket}.s3.amazonaws.com";
var avatarUrl = $"{baseUrl}/{s3Key}";
```

### I2 — MIME type validation relies solely on client-supplied `Content-Type` (`Program.cs:904`)

```csharp
var mimeType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
if (!allowedTypes.Contains(mimeType))
    return Results.BadRequest(...);
```

`IFormFile.ContentType` is whatever the browser sends — an attacker can upload a malicious file (e.g., an HTML polyglot or SVG with script) with `Content-Type: image/jpeg` and pass this check. The Bedrock moderation call is a best-effort content check, not a security gate. At minimum, validate magic bytes against the declared MIME type (`FF D8 FF` for JPEG, `89 50 4E 47` for PNG, etc.).

---

## Nitpicks (Optional)

### N1 — `UNSAFE` reason extraction is slightly fragile (`AvatarModerationService.cs:99`)

```csharp
var reason = text.Length > 7 ? text[7..].TrimStart(':', ' ') : "Content not appropriate...";
```

`"UNSAFE"` is 6 chars; separator `: ` at 6–7; reason at position 8. If model responds `"UNSAFE: "` (trailing space), result after trim is empty string. Prefer `text.Length > 8 ? text[8..].Trim() : "..."`.

### N2 — No size guard in `AvatarModerationService.CheckImageAsync`

```csharp
using var ms = new MemoryStream();
await imageStream.CopyToAsync(ms, ct);
```

Upstream endpoint validates 2MB before calling this, so tolerable. But direct calls to `CheckImageAsync` with an unbounded stream would load arbitrarily large data into memory. Consider a defensive cap.

### N3 — `S3CannedACL.PublicRead` on avatar objects (`Program.cs:943`)

Avatars are world-readable by URL — likely intentional for profile pictures. Flag as a deliberate design choice that should be documented in a comment.

### N4 — Hardcoded S3 bucket fallback (`Program.cs:933`)

```csharp
var bucket = config["AWS:WorkspaceBucket"] ?? config["AWS:S3Bucket"] ?? "fortress-user-workspaces";
```

String `"fortress-user-workspaces"` is a last-resort fallback. Low risk since config is always set in production, but inconsistent with config-driven pattern.

---

## Data Layer — Clean ✅

| File | Finding |
|------|---------|
| `User.cs` | `string? AvatarUrl` with `[Column("avatar_url")]` and `[MaxLength(1000)]` — correct ✅ |
| `FaitV2DbContext.cs` | `HasColumnName("avatar_url").HasMaxLength(1000)`, nullable (no `.IsRequired()`) — matches DDL ✅ |
| Migration `20260509100000` | `Up()` adds `varchar(1000) nullable`, `Down()` drops it — fully reversible ✅ |
| Snapshot | `AvatarUrl` property present with correct type, length, column name ✅ |

---

## AvatarModerationService — Core Logic Checks

| Check | Result |
|-------|--------|
| Uses `InvokeModelAsync` (not streaming) | ✅ |
| Image sent as base64 in request body | ✅ |
| Parses `SAFE` / `UNSAFE: {reason}` | ✅ |
| Fails open on exception (logs warning, `IsAllowed=true`) | ✅ |
| `AvatarModerationResult` with `IsAllowed` + `Reason` | ✅ |
| Catch-all exception handler | ✅ |
| Model ID from config | ❌ **C1 — BLOCKING** |
| DI registered as `AddScoped` | ✅ |

---

## Endpoint Checks (`POST /api/profile/avatar`)

| Check | Result |
|-------|--------|
| Requires authorization | ✅ |
| MIME type validation (jpeg/png/webp/gif) | ✅ (but header-only — see I2) |
| Max size 2MB enforced | ✅ |
| Rejected → 400 with reason | ✅ |
| S3 path `avatars/{userId}/{uuid}.{ext}` | ✅ |
| DB update with 5-second timeout | ✅ |
| DB update failure does NOT fail request | ✅ |
| 200 with `{ avatarUrl }` on success | ✅ |

---

## Summary

The implementation is well-structured with solid moderation logic, correct fail-open behavior, proper authorization, and a clean data layer. One blocking issue must be fixed: the Bedrock model ID is hardcoded as a `const` in violation of `security-rules.md` — it must be moved to `IConfiguration`. Two important issues (hardcoded S3 URL domain, MIME type header-only validation) should also be addressed before merge. Fix C1, address I1 and I2, then resubmit for Cycle 2.

---

## Required Fixes Before Resubmit

1. **[CRITICAL]** Move `ModerationModel` const to `IConfiguration["Bedrock:AvatarModerationModelId"]` (and add key to `appsettings.json`)
2. **[IMPORTANT]** Drive avatar URL base from `config["AWS:AvatarBaseUrl"]` with fallback
3. **[IMPORTANT]** Add magic-byte MIME validation in addition to Content-Type header check
