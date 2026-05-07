# CC Adversarial Review Brief — ADO#2848: IUserAgentRuntime + FargateUserAgentRuntime

You are performing an adversarial code review. Your job is to find real bugs, not to validate that the code looks correct.

## Files to read (read ALL of them)
1. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs`
2. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Services/FargateUserAgentRuntime.cs`
3. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Program.cs`
4. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/appsettings.json`
5. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/Models/UserSession.cs`
6. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/FaitV2DbContext.cs`
7. `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/Data/Migrations/20260507032637_AddFargateColumnsToUserSession.cs`

## What to look for (be skeptical, be adversarial)

### CRITICAL checks
1. **Interface completeness**: Does `IUserAgentRuntime` define exactly these 5 methods: `EnsureRunningAsync`, `StopAsync`, `GetSessionAsync`, `IsHealthyAsync`, `SendTurnAsync`? Verify `SendTurnAsync` returns `IAsyncEnumerable<HarnessEvent>`.
2. **Implementation completeness**: Does `FargateUserAgentRuntime` implement all 5 methods? Any missing or mismatched signatures?
3. **EnsureRunningAsync polling**: Does it poll DescribeTasks max 90s before throwing `TimeoutException`? Verify the math: 30 polls × 3s = 90s. Does it actually throw `TimeoutException` (not `InvalidOperationException`) on timeout?
4. **StopAsync**: Does it look up the Aurora session first, then call ECS StopTask? Or does it directly call StopTask?
5. **IsHealthyAsync**: Does it do HTTP GET to `/health` with exactly 5s timeout? Is the timeout set on the `HttpClient` or the request? NOTE: `client.Timeout` on a named client created via `CreateClient()` returns a NEW instance each time — setting `Timeout` on a factory-created client is fine for one-off use, but verify nothing else re-uses the same modified client.
6. **SendTurnAsync SSE parsing**: 
   - Does it skip blank/whitespace lines?
   - Does it skip `:` keep-alive/comment lines?
   - Does it parse `data: {json}` lines correctly?
   - Is `yield return` inside a catch block anywhere? (C# compiler error CS1621 — should have been fixed per build report, but verify the fix is correct)
7. **Aurora access**: Is `FaitV2DbContext` used via `IDbContextFactory` (no raw ADO.NET)? Every method must use `await using var db = await _dbFactory.CreateDbContextAsync(ct)`.
8. **DI Registration**: 
   - Is `IAmazonECS` registered as **Singleton**?
   - Is `IUserAgentRuntime` registered as **Scoped**?
   - Is `IDbContextFactory<FaitV2DbContext>` registered?
9. **Fargate config section**: Does `appsettings.json` have all required keys: ClusterArn, TaskDefinition, SubnetIds, SecurityGroupIds, ContainerName, HarnessPort?
10. **No ambiguous type names**: Are `Amazon.ECS.Model.KeyValuePair` and `System.Threading.Tasks.Task` fully qualified everywhere they could be ambiguous?
11. **Schema alignment**: Do `UserSession.cs` C# property names, `[Column]` annotations, EF fluent config in `FaitV2DbContext.cs`, and migration column names all match for the 4 new Fargate fields: TaskArn, PrivateIp, FargateStatus, FargateSessionId?

### IMPORTANT checks
12. **SSE cancellation**: Is the `CancellationToken` threaded through all async calls in `SendTurnAsync`? (`PostAsJsonAsync`, `ReadLineAsync`, `EnsureRunningAsync`)
13. **EnsureRunningAsync idempotency**: If a task is already RUNNING in ECS, does it return the existing session without launching a new one?
14. **ENI private IP extraction**: Does `GetPrivateIpFromTask` look for attachment type `"ElasticNetworkInterface"` and detail name `"privateIPv4Address"`? Are these the correct ECS API field names?
15. **Scoped vs Singleton conflict**: `IUserAgentRuntime` is registered as Scoped, but it takes `IDbContextFactory` (Singleton-safe) and `IAmazonECS` (Singleton). No captive dependency issues here. Confirm.
16. **`AddDbContext` vs `AddDbContextFactory` double registration**: Program.cs registers BOTH `AddDbContext<FaitV2DbContext>` AND `AddDbContextFactory<FaitV2DbContext>`. Is this safe? Can it cause conflicts?
17. **`client.Timeout` on HttpClient from factory**: In `IsHealthyAsync`, `_httpClientFactory.CreateClient("HarnessClient")` returns a new `HttpClient` instance each time. Setting `.Timeout = TimeSpan.FromSeconds(5)` on the returned instance is valid for that call but means there's no default timeout on the named client configuration. Is there any risk here (e.g. what if `client.Timeout` has already been used/sent request)?

### NITPICK checks
18. **HttpClient lifecycle**: `SendTurnAsync` calls `_httpClientFactory.CreateClient("HarnessClient")` and uses it for streaming. `HttpClient` instances from factory should be disposed. Is there a `using` or `await using` on the `client`? Check both `IsHealthyAsync` and `SendTurnAsync`.
19. **Poll interval configurability**: The 3000ms delay and 30 poll max are hardcoded constants. Are they extractable from config? Not a blocker for MVP but flag it.
20. **`ReadLineAsync` with CancellationToken**: The call `reader.ReadLineAsync(ct)` — verify this overload exists in .NET 7+. (StreamReader.ReadLineAsync(CancellationToken) was added in .NET 7.)
21. **`EnsureRunningAsync` called from `SendTurnAsync`**: If the task is Starting (not yet Running), `SendTurnAsync` calls `EnsureRunningAsync` which will block up to 90s. Is this intentional? Is the caller expected to handle this timeout?

## Pass/Fail criteria
- FAIL if: any `yield return` inside catch block, missing interface method, wrong DI lifetime, raw ADO.NET used instead of EF, schema mismatch on the 4 new columns
- NEEDS-CHANGES if: important issues found that don't hard-break functionality but should be fixed
- PASS if: all critical checks pass, only nitpicks remain

Report findings grouped by Critical / Important / Nitpick. For each finding: file, line/section, what's wrong, what the impact is, what the fix should be.
