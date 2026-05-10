# Build Report — ADO#3154

## What was built
Added `BuildSystemPromptAsync` to `AssistantConfigService` — reads `SOUL.md` and `USER.md` from the user's S3 workspace and builds the assistant system prompt from them. Falls back to the existing DB-field-based `GetPersonalitySystemPrompt` if S3 files are missing or unreadable. Updated `ChatView.razor` to call the new async method.

## Files changed
- `src/FortressAI.Web/Services/AssistantConfigService.cs`
  - Added `using Amazon.S3;` and `using Microsoft.Extensions.Configuration;`
  - Added `IAmazonS3 _s3` and `IConfiguration _config` fields
  - Updated constructor to accept `IAmazonS3 s3` and `IConfiguration config`
  - Added `BuildSystemPromptAsync(UserAssistantConfig, Guid, string?, string?)` — S3-first, DB fallback
  - Added `ReadS3FileAsync(string bucket, string key)` — private helper, throws on failure (callers catch)
  - `GetPersonalitySystemPrompt` — **untouched**

- `src/FortressAI.Web/Components/Chat/ChatView.razor` — line 580
  - Replaced `ConfigSvc.GetPersonalitySystemPrompt(...)` with `await ConfigSvc.BuildSystemPromptAsync(_assistantConfig, Session.UserId, ...)`

## Parallelization used
No — single-file dependency (service change drives razor change).

## CC sessions run
1 — CC Sonnet

## Acceptance criteria verification
- [x] `BuildSystemPromptAsync` exists and is async — verified in file
- [x] Reads S3 `{userPrefix}assistants/SOUL.md` and `USER.md` — verified in implementation
- [x] Falls back to `GetPersonalitySystemPrompt` if both S3 files missing — verified (lines ~150-153)
- [x] S3 read failure = LogWarning + continue, never throws to caller — verified in try/catch blocks
- [x] `GetPersonalitySystemPrompt` unchanged — verified via grep (no modifications)
- [x] `ChatView.razor` calls `BuildSystemPromptAsync` — verified at line 580
- [x] Build: 0 errors, 32 pre-existing warnings — confirmed by CC

## Known edge cases / things Clint should scrutinize
- Partial fallback: if `SOUL.md` is missing but `USER.md` is present, we use a DB-preset string + USER.md content. The preset string omits the full personality nuance of the DB path — this is intentional per spec (partial fallback degrades gracefully).
- `ReadS3FileAsync` will throw `AmazonS3Exception` with `NoSuchKey` when the file doesn't exist — this is caught by the caller's try/catch and logs a warning. Verify that this warning is acceptable vs. expected-absent (no SOUL.md = first-time user).
- `WORKSPACE_S3_PREFIX` may have a trailing slash or not — the `userPrefix` construction assumes no trailing slash from config (adds its own `/`). If the env var has a trailing slash, keys would be `//workspaces/...`. Worth confirming config convention.
- `IAmazonS3` is registered as Singleton in Program.cs; `AssistantConfigService` is Scoped — this is fine (singleton injected into scoped is valid).

## How to test locally
1. Deploy with `WORKSPACE_S3_BUCKET` and `WORKSPACE_S3_PREFIX` env vars set
2. Upload a `SOUL.md` to `workspaces/{userId}/assistants/SOUL.md` in the bucket
3. Start a chat — system prompt should reflect SOUL.md content, not DB preset
4. Remove/rename the SOUL.md — system prompt should revert to DB-field-based prompt

## Commit
`ba30f846` — `feat(fait#3154): add BuildSystemPromptAsync to AssistantConfigService with S3 SOUL.md/USER.md support`
