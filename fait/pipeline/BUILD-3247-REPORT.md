# Build Report — ADO#3247

## What was built
Removed the dead outbound webhook from `FeedbackDispatcher.DispatchToJarvisAsync`. The class no longer POSTs to Jarvis — it's a no-op log statement. Jarvis polls `feedback_submissions` directly via cron.

## Files changed
- `src/FortressAI.Web/Services/FeedbackDispatcher.cs` — Stripped `IHttpClientFactory`, `IConfiguration`, and all HTTP dispatch logic. Removed `using System.Net.Http.Headers`. Method now returns `Task.CompletedTask` after a single log line. Constructor reduced to `ILogger` only.

## Parallelization used
No — single file change.

## CC sessions run
1 (CC Sonnet, single-shot)

## Acceptance criteria verification
- [x] `Feedback:JarvisWebhookUrl` config read removed
- [x] `OpenClaw:ApiToken` read removed
- [x] `HttpClient` dependency removed
- [x] `FIP:FaitBaseUrl` read removed (was only used in webhook payload body)
- [x] `DispatchToJarvisAsync` method signature preserved — call site in Program.cs untouched
- [x] DB write to `feedback_submissions` unaffected (handled in Program.cs, not in this class)
- [x] `/api/feedback/{id}/status` endpoint untouched
- [x] `dotnet build` — 0 errors

## Commit
`d167b445` — `fix(fait#3247): remove dead outbound webhook from FeedbackDispatcher`

## Env vars to remove from fred-dev task def (Rhodey action required)
These env vars are now dead and should be removed from the ECS task definition:
- `FEEDBACK_JARVIS_WEBHOOK_URL`
- `OpenClaw__ApiToken`

Tag Rhodey in the deploy step to clean these up.

## Known edge cases / things Clint should scrutinize
- The call site `_ = feedbackDispatcher.DispatchToJarvisAsync(submission);` (Program.cs ~line 640) is fire-and-forget. It will now resolve immediately since the method returns `Task.CompletedTask`. No behavioral change from the caller's perspective.
- `IHttpClientFactory` is still registered globally (`builder.Services.AddHttpClient()`) for other consumers — we did NOT remove that.

## How to test locally
1. Submit feedback via the FAIT UI
2. Verify no HTTP POST errors in logs
3. Verify `feedback_submissions` row is created with `status = 'pending'`
4. Log should contain: `[feedback] Webhook dispatch removed — Jarvis polls directly`
