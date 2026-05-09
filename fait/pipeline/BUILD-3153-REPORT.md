# Build Report — ADO#3153

## What was built
`UserProvisioningService` — atomic S3 workspace seeding called from AssistantSetup wizard on completion. Writes 4 files (SOUL.md, USER.md, AGENTS.md, MEMORY.md) under `workspaces/{userId}/` with rollback on partial failure.

## Files changed
- `src/FortressAI.Web/Services/UserProvisioningService.cs` — **new file** (230 lines). Handles idempotency check, 4 S3 PutObject writes, rollback on failure, AccessDenied halt-and-report, and sets `OnboardingCompletedAt` on success.
- `src/FortressAI.Web/Program.cs` — added `builder.Services.AddScoped<UserProvisioningService>();` at line 97 (after `AssistantConfigService`)
- `src/FortressAI.Web/Components/Pages/AssistantSetup.razor` — added `@inject UserProvisioningService ProvisioningSvc` (line 10) and `await ProvisioningSvc.ProvisionAsync(Session.UserId)` call after `SaveChangesAsync` in `HandleSubmit` (line 374)

## Parallelization used
No — single CC session, sequential (all three changes are interdependent)

## CC sessions run
1 CC Sonnet session

## Acceptance criteria verification
- [x] `UserProvisioningService.cs` exists — verified (230 lines, matches spec)
- [x] `Program.cs` registration — verified at line 97
- [x] `AssistantSetup.razor` inject + call — verified at lines 10 and 374
- [x] `dotnet build` — **0 errors, 32 warnings** (all pre-existing warnings)

## Commit
`81075e5a` — `feat(fait#3153): add UserProvisioningService with atomic S3 workspace seeding`

## Known edge cases / things Clint should scrutinize
1. **Rollback completeness** — Rollback only deletes S3 files written before the failure. If `DeleteObjectAsync` throws during rollback, it logs a warning and continues (best-effort). This is acceptable per spec.
2. **AccessDenied handling** — `AmazonS3Exception` with `ErrorCode == "AccessDenied"` is caught separately, logs an error, and re-throws without rollback attempt (no files were written if the first write fails with AccessDenied). This matches spec intent.
3. **`memory_topics` DB step** — Skipped entirely per spec (table doesn't exist in v1 schema). The comment in code makes this explicit.
4. **`S3Prefix` default** — Empty string. Key format: `workspaces/{userId}/assistants/SOUL.md`. If `WORKSPACE_S3_PREFIX` is set, it prepends. This matches the harness pattern.
5. **`HandleSubmit` exception handler** — The `ProvisionAsync` call is inside the existing try/catch in HandleSubmit. If provisioning throws, the user sees the generic "Something went wrong" error. This is acceptable per spec — the wizard won't navigate to `/chat` on failure.

## How to test locally
1. Complete the AssistantSetup wizard for a new user
2. Check CloudWatch logs for `[Provision]` log entries
3. Verify S3 objects exist at `fortress-user-workspaces/workspaces/{userId}/assistants/SOUL.md` etc.
4. Verify `users.onboarding_completed_at` is set in the DB
5. Re-submit the wizard — should log "already provisioned — skipping" and proceed normally
