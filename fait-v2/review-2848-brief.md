# CC Review Brief — ADO#2848: IUserAgentRuntime + FargateUserAgentRuntime

You are performing an adversarial code review. Your job is to find what's wrong, not validate what's right.

## Files to review (read all of them)

1. `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs`
2. `src/FortressAI.V2.Web/Services/FargateUserAgentRuntime.cs`
3. `src/FortressAI.V2.Web/Program.cs`
4. `src/FortressAI.V2.Web/appsettings.json`
5. `src/FortressAI.V2.Web/Data/Models/UserSession.cs`
6. `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs`
7. `src/FortressAI.V2.Web/Data/Migrations/20260507032637_AddFargateColumnsToUserSession.cs`

## What to look for

### CRITICAL checks (must verify each)

1. **Interface completeness** — Does `IUserAgentRuntime` define exactly these 5 methods?
   - `EnsureRunningAsync(string userId, CancellationToken ct)` → `Task<RuntimeSession>`
   - `StopAsync(string userId, CancellationToken ct)` → `Task`
   - `GetSessionAsync(string userId, CancellationToken ct)` → `Task<RuntimeSession?>`
   - `IsHealthyAsync(string userId, CancellationToken ct)` → `Task<bool>`
   - `SendTurnAsync(string userId, TurnRequest request, CancellationToken ct)` → `IAsyncEnumerable<HarnessEvent>`

2. **`FargateUserAgentRuntime` implements all 5 methods** — check method signatures match the interface exactly.

3. **`EnsureRunningAsync` startup polling** — Must poll DescribeTasks max 90s (30 polls × 3s) before throwing `TimeoutException`. Verify:
   - Is it actually `TimeoutException` and not `InvalidOperationException`?
   - Is the math correct (30 × 3000ms = 90s)?
   - Does it bail out correctly on "STOPPED" or "DEPROVISIONING"?

4. **`EnsureRunningAsync` idempotency** — If an existing RUNNING task exists in Aurora, does it return without launching a new one? Does it actually re-verify the task is still RUNNING in ECS?

5. **`StopAsync`** — Does it look up Aurora session first, then call ECS `StopTaskAsync`? Does it handle ECS failure gracefully (still marks DB as stopped)?

6. **`IsHealthyAsync`** — Does it do HTTP GET to `/health`? Is the 5s timeout set correctly? Does it return false gracefully on exception?

7. **`SendTurnAsync`** — Does it POST to `/turn`? Does it parse SSE line-by-line? Does it correctly:
   - Skip empty/whitespace lines?
   - Skip `:` keep-alive comment lines?
   - Parse `data: {...}` lines by stripping the `data: ` prefix?
   - Yield `HarnessEvent` objects?
   - Stop on `"done"` or `"error"` event type?
   - Handle `yield return` NOT inside a `catch` block (C# compiler constraint)?

8. **Aurora access** — Is `user_sessions` accessed ONLY via `FaitV2DbContext`? No raw ADO.NET, no Dapper, no direct SQL?

9. **Schema alignment** — Trace this chain end-to-end:
   - C# entity properties in `UserSession.cs`: `TaskArn`, `PrivateIp`, `FargateStatus`, `FargateSessionId`
   - `[Column("...")]` attributes on each property
   - EF `OnModelCreating` in `FaitV2DbContext.cs` — do `HasColumnName` calls match the `[Column]` attributes?
   - Migration columns in `AddFargateColumnsToUserSession.cs`
   - Are all column names consistent across all three places?

10. **DI registrations in Program.cs**:
    - `IAmazonECS` registered as **Singleton** (not Scoped/Transient)?
    - `IUserAgentRuntime` registered as **Scoped** (not Singleton)?
    - `IDbContextFactory<FaitV2DbContext>` registered?
    - `IHttpClientFactory` registered (via `AddHttpClient`)?

11. **appsettings.json** — Is there a `Fargate:` section with these keys: `ClusterArn`, `TaskDefinition`, `SubnetIds`, `SecurityGroupIds`, `ContainerName`, `HarnessPort`?

12. **`yield return` inside catch** — Scan `SendTurnAsync` carefully. Is there any `yield return` inside a `catch` block? (CS1621 compiler error)

13. **Ambiguous type names** — Are `Amazon.ECS.Model.KeyValuePair` and `Amazon.ECS.Model.Task` fully qualified wherever they're used alongside their System counterparts?

### IMPORTANT checks

14. **SSE parsing correctness** — Does the line parser correctly skip blank lines AND lines starting with `:`? What about `data:` without a space? Is `line.StartsWith(':')` checking for the colon character correctly?

15. **Cancellation token threading** — Is `ct` passed to: `CreateDbContextAsync`, `DescribeTasksAsync`, `RunTaskAsync`, `Task.Delay`, `StopTaskAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, `ReadLineAsync`, `ReadAsStreamAsync`, `PostAsJsonAsync`, `GetAsync`?

16. **`EnsureRunningAsync` idempotency edge case** — What happens if the existing ECS task check returns no tasks (describeResp.Tasks is empty)? Does it handle null/empty correctly?

17. **ENI private IP extraction** — In `GetPrivateIpFromTask`, does it correctly navigate `task.Attachments` → find `Type == "ElasticNetworkInterface"` → find `Details` entry with `Name == "privateIPv4Address"`? Any null reference risks?

### NITPICK checks

18. **HttpClient lifecycle** — `_httpClientFactory.CreateClient("HarnessClient")` creates a client each call. In `IsHealthyAsync`, is `client.Timeout` being set on a named client (which may share handler)? Could this cause issues?

19. **Poll delay configurability** — `const int PollDelayMs = 3000` and `const int MaxPolls = 30` are hardcoded. Flag as nitpick.

20. **`EnsureRunningAsync` DB context scope** — `db` is opened at the top with `await using`. The session object is tracked by this context. After `await Task.Delay(...)` inside the polling loop, the same `session` is modified and saved. Is there any risk of the context being disposed or detached? (In C# `await using` keeps the context alive until the end of the method, so this should be fine — verify.)

## Output format

For each issue found:
- **Severity:** Critical / Important / Nitpick
- **File and line:** (approximate)
- **Issue:** Clear description
- **Impact:** What breaks
- **Fix:** Exact correction

If something looks suspicious but you've verified it's actually correct, say so explicitly (false positive dismissed + reason).

Be adversarial. Assume the code has bugs until proven otherwise. Report everything you find.
