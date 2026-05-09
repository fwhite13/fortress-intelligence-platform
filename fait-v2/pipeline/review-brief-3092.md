# Code Review Brief — ADO#3092: Avatar NSFW Check on Upload

You are Hawkeye, a thorough code reviewer. Please carefully read and analyze each of the following files, then produce a detailed code review report.

## Working Directory
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

## Files to Review

Read each file and analyze it thoroughly:

1. `Data/Models/User.cs` — Check `AvatarUrl` property: correct type (string?), correct column attribute ([Column("avatar_url")]), correct MaxLength(1000)
2. `Data/FaitV2DbContext.cs` — Check EF config for `AvatarUrl` in OnModelCreating — property config matches migration DDL
3. `Data/Migrations/20260509100000_AddAvatarUrlToUser.cs` — Check migration Up/Down correctness, column type, nullable, reversibility
4. `Data/Migrations/FaitV2DbContextModelSnapshot.cs` — Check AvatarUrl property added to snapshot correctly
5. `Services/AvatarModerationService.cs` — MOST IMPORTANT: Deep review of moderation logic
6. `Program.cs` — Check DI registration and endpoint implementation

## Key Things to Verify

### AvatarModerationService.cs
- Does it call Bedrock using `InvokeModelAsync` (not streaming)?
- Is the model ID `claude-haiku-4-5-20251001`? (or acceptable variant)
- Is the image sent as base64 in the request body?
- Does it correctly parse `SAFE` / `UNSAFE: {reason}` from the response?
- Does it fail OPEN on exception? (i.e., logs warning, returns IsAllowed=true, never throws)
- Is the return type `AvatarModerationResult` with `IsAllowed` and `Reason` properties?
- Is error handling robust (catch-all exception handler)?
- Are there any obvious security issues?

### Program.cs Endpoint (`POST /api/profile/avatar`)
- Does it require authorization (`.RequireAuthorization()` or `[Authorize]`)?
- Does it validate MIME type — only jpeg/png/webp/gif allowed?
- Does it validate file size — max 2MB?
- Does it return HTTP 400 with reason message on rejection?
- On acceptance: S3 upload → DB update → 200 with `{ avatarUrl }`?
- Is the S3 path `avatars/{userId}/{uuid}.{ext}`?
- Does DB update use a 5-second timeout?
- Does DB update failure NOT fail the whole request (logged, swallowed)?
- Is the userId correctly retrieved from the auth context?

### Data Layer
- Does User.cs have the correct nullable string? property?
- Does EF config in DbContext match the migration DDL (varchar(1000) nullable)?
- Is the migration reversible (has both Up and Down)?
- Is the snapshot consistent with the model?

### General Code Quality
- Any hardcoded values that should be config (S3 bucket name, max size, model ID)?
- Any logging gaps?
- Any potential null reference issues?
- Any duplicate code or missed abstractions?
- Error messages — are they user-friendly and not leaking internal details?
- CSS variable rule: No hardcoded colors/font sizes/spacing (no .razor files expected here)

## Output Format

Produce:
1. **Verdict:** PASS / NEEDS-CHANGES / FAIL
2. **Critical Issues** (blocking)
3. **Important Issues** (should fix)
4. **Nitpicks** (optional improvements)
5. **Summary** (2-3 sentences)

Be specific: cite exact file names, line numbers if possible, and exact code snippets.
