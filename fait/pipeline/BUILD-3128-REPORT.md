# Build Report — ADO#3128

## What was built
Assistant setup detection + `/assistant-setup` onboarding page. New users with `NULL` `onboarding_completed_at` are redirected from `/chat` to `/assistant-setup`, where they enter their display name. On submit, `DisplayName`, `OnboardingCompletedAt`, and `OnboardingStep` are persisted and the user is sent to `/chat`.

## Files changed
- `src/FortressAI.Shared/Models/AppUser.cs` — added `OnboardingCompletedAt` (`DateTime?`) and `OnboardingStep` (`int?`) after `EntraOid`
- `src/FortressAI.Web/Data/AppDbContext.cs` — added EF column mappings for `onboarding_completed_at` and `onboarding_step` inside the AppUser entity config block
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — added onboarding gate in `OnInitializedAsync` (after auth check, before `EnsureRunningAsync`): queries DB for user, redirects to `/assistant-setup` if `OnboardingCompletedAt == null`, fails open on DB error
- `src/FortressAI.Web/Components/Pages/AssistantSetup.razor` — new page at `/assistant-setup`: collects display name (pre-populated from session if available), saves `DisplayName` + `OnboardingCompletedAt = UtcNow` + `OnboardingStep = 1`, redirects to `/chat`

## Parallelization used
No — tasks were sequential: model first, DbContext second, ChatView gate third, new page fourth.

## CC sessions run
1 CC run (Sonnet). CC also noted that `Session.DisplayName` → `Session.CurrentUser?.DisplayName` since `UserSessionService` doesn't expose `DisplayName` directly (fixed inline).

## Design decisions
- **No role/description field:** `UserAssistantConfig` has no suitable free-text field for role description (has `AssistantName`, `AvatarId`, `ColorHex`, `PersonalityPreset`, `FirmAutoTranscript`, `FirmAutoSummary` only). Per brief: when no suitable existing field exists, collect display name only. Role/description field was dropped.
- **Fail-open gate:** DB error during onboarding check logs a warning and allows chat to load — avoids blocking the app on transient DB issues.
- **`OnboardingStep = 1`** written on completion — step field is populated so future multi-step onboarding flows have a starting reference.

## Acceptance criteria verification
- [x] User with `NULL` `onboarding_completed_at` is redirected to `/assistant-setup` on `/chat` load — gate queries DB and calls `Nav.NavigateTo("/assistant-setup", replace: true)` if null
- [x] Existing users (non-null value) are completely unaffected — gate only redirects when `OnboardingCompletedAt == null`
- [x] `/assistant-setup` completes and redirects to `/chat` — `Nav.NavigateTo("/chat", replace: true)` after `SaveChangesAsync`
- [x] No DB migration needed — columns already exist in `fait_dev.users` (confirmed pre-flight)
- [x] CSS variable rule — all values use `var(--...)`, no hardcoded hex/px/rem in new page

## Build result
```
0 Error(s), 31 Warning(s) — all pre-existing MudBlazor warnings in Settings.razor, McpServers.razor, AdminIndex.razor
```

## Commit
`89329f00` — `feat(fait#3128): assistant setup detection + /assistant-setup onboarding page`

## Known edge cases / things Clint should scrutinize
1. **`Session.CurrentUser?.DisplayName` pre-population** — verify `UserSessionService.CurrentUser` is populated at the time `OnInitialized` fires; if it's lazy-loaded it may be null even for existing users. Fallback is empty string which is fine.
2. **Onboarding gate placement** — gate fires before `EnsureRunningAsync`, so a new user will not trigger Fargate launch before being redirected. This is intentional and correct.
3. **`replace: true` on both navigations** — prevents back-button loop (setup → chat → back → setup). Intentional.
4. **Race condition (minor):** if two tabs are open simultaneously for the same user hitting `/chat` for the first time, both will redirect to `/assistant-setup`. Only the last submit wins — idempotent since it just overwrites the same fields.

## How to test locally
1. Create a test user (or null out `onboarding_completed_at` for an existing user in `fait_dev.users`)
2. Navigate to `/chat` — should redirect to `/assistant-setup`
3. Submit the form with a display name
4. Verify redirect to `/chat` and that `onboarding_completed_at` is now set in DB
5. Navigate to `/chat` again — should NOT redirect (gate should pass)
