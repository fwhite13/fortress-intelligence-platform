# Build Report — ADO#2848: Per-user Fargate task lifecycle

**Date:** 2026-05-06
**Engineer:** Tony Stark (software-engineer)
**Commit:** c97fc00

---

## What was built

`IUserAgentRuntime` interface + `FargateUserAgentRuntime` implementation providing full per-user Fargate task lifecycle: launch, stop, health check, session retrieval, and SSE turn streaming.

---

## Files changed

- `src/FortressAI.V2.Web/Services/FargateUserAgentRuntime.cs` — New. Full implementation of all 5 interface methods. ECS RunTask/StopTask/DescribeTasks wired. Aurora `user_sessions` via `IDbContextFactory<FaitV2DbContext>`. SSE streaming via `IAsyncEnumerable<HarnessEvent>`.
- `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` — New. Interface definition (committed in 2042049 alongside ADO#2849 scope, present in tree).
- `src/FortressAI.V2.Web/Program.cs` — Registered `IAmazonECS`, `IUserAgentRuntime` (as `FargateUserAgentRuntime`), `IDbContextFactory<FaitV2DbContext>`, `IHttpClientFactory`.
- `src/FortressAI.V2.Web/appsettings.json` — Added `Fargate:*` config section (ClusterArn, TaskDefinition, SubnetIds, SecurityGroupIds, ContainerName, HarnessPort).
- `src/FortressAI.V2.Web/Data/Migrations/20260507032637_AddFargateColumnsToUserSession.cs` — EF migration adding `TaskArn`, `PrivateIp`, `FargateStatus`, `FargateSessionId` columns to `user_sessions`.

---

## Build errors resolved (4 total)

| # | Error | Fix |
|---|-------|-----|
| 1 | CS0104 `KeyValuePair` ambiguous (Amazon vs System) | Fully qualified as `Amazon.ECS.Model.KeyValuePair` |
| 2 | CS0104 `KeyValuePair` ambiguous (second occurrence) | Same — both env var lines qualified |
| 3 | CS1621 `yield return` inside catch block | Captured error to `HarnessEvent? errorEvent` outside try/catch; yield after |
| 4 | CS0104 `Task` ambiguous (`Amazon.ECS.Model.Task` vs `System.Threading.Tasks.Task`) | `StopAsync` return type fully qualified as `System.Threading.Tasks.Task` |

---

## Parallelization used

No — single CC session (linear file build).

---

## CC sessions run

1 — Claude Code Sonnet, fix-and-verify pass on `FargateUserAgentRuntime.cs`

---

## Acceptance criteria verification

- [x] `IUserAgentRuntime.cs` exists in `Services/` — confirmed
- [x] `FargateUserAgentRuntime.cs` exists in `Services/` — confirmed
- [x] All 5 methods implemented — `EnsureRunningAsync`, `StopAsync`, `GetSessionAsync`, `IsHealthyAsync`, `SendTurnAsync`
- [x] `SendTurnAsync` is `IAsyncEnumerable<HarnessEvent>` — confirmed
- [x] Aurora `user_sessions` accessed via `FaitV2DbContext` — confirmed
- [x] `UserSession` model has `TaskArn`, `PrivateIp`, `FargateStatus`, `FargateSessionId` — migration present
- [x] EF migration `AddFargateColumnsToUserSession` created — confirmed
- [x] AWSSDK.ECS package added to csproj — confirmed
- [x] Registered in Program.cs — `IAmazonECS`, `IUserAgentRuntime`, `IDbContextFactory` all registered
- [x] `dotnet build` = 0 errors, 0 warnings — **CONFIRMED**

---

## Known edge cases / things Clint should scrutinize

- `GetPrivateIpFromTask` parses the ENI attachment for `privateIPv4Address` — relies on ECS attachment structure; worth verifying against actual RunTask response shape in staging.
- `StopAsync` return type is `System.Threading.Tasks.Task` (fully qualified) to avoid `Amazon.ECS.Model.Task` ambiguity — interface declares bare `Task`, compiler resolves correctly but worth a read.
- 90s startup poll (30 × 3s) is hardcoded — no config knob yet; acceptable for MVP.
- SSE streaming stops on `"done"` or `"error"` event type — harness must emit one of these to terminate the stream.

---

## How to test locally

```bash
# Build
cd ~/projects/fip/fait-v2/src/FortressAI.V2.Web
dotnet build

# Migration (requires local Aurora or docker MySQL)
dotnet ef database update

# Integration: inject IUserAgentRuntime in a test controller or Blazor page,
# call EnsureRunningAsync("test-user") — requires real Fargate config in appsettings.
```

---

## Build result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Commit:** `c97fc00` — `feat(fait-v2#2848): IUserAgentRuntime + FargateUserAgentRuntime for per-user task lifecycle`

---

## Build Cycle 2 — I1 Fix

**Date:** 2026-05-07
**Commit:** `dda9573`
**Reviewer finding:** I1 — Duplicate `AddDbContext<FaitV2DbContext>` registration in `Program.cs`

### Fix applied
Removed `AddDbContext<FaitV2DbContext>(...)` (8 lines, around line 82). Kept `AddDbContextFactory<FaitV2DbContext>(...)` only. In EF Core 8, `AddDbContextFactory` covers both factory-based and direct `DbContext` injection — the `AddDbContext` call was redundant and would cause double-registration warnings.

### File changed
- `src/FortressAI.V2.Web/Program.cs` — Removed 8 lines (the `AddDbContext` block)

### Build result
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Commit:** `dda9573` — `fix(fait-v2#2848): remove duplicate AddDbContext registration, keep only AddDbContextFactory`
