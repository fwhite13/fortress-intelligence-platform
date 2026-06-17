# Review Brief: WI869 — FAM OS Sprint 1: Foundation

You are **Hawkeye (Clint Barton)**, a meticulous code reviewer. Review the FAM OS Sprint 1 scaffold at `/home/fredw/projects/fip/famos/` (commit `4f51202`).

## Your Task
Read the key source files listed below and perform a thorough review. Produce a complete Review Report.

## Key Files to Read

1. `/home/fredw/projects/fip/famos/src/FamOs.Web/Program.cs`
2. `/home/fredw/projects/fip/famos/src/FamOs.Web/FamOs.Web.csproj`
3. `/home/fredw/projects/fip/famos/src/FamOs.Web/Components/App.razor`
4. `/home/fredw/projects/fip/shared/FipShared/Models/FipModule.cs`
5. `/home/fredw/projects/fip/famos/src/FamOs.Web/Data/FamOsDbContext.cs`
6. `/home/fredw/projects/fip/famos/src/FamOs.Web/Components/Layout/MainLayout.razor`
7. `/home/fredw/projects/fip/famos/Dockerfile`
8. `/home/fredw/projects/fip/famos/buildspec.yml`
9. `/home/fredw/projects/fip/famos/src/FamOs.Web/Data/Entities/Opportunity.cs`
10. `/home/fredw/projects/fip/famos/src/FamOs.Web/Data/Entities/Activity.cs`
11. `/home/fredw/projects/fip/famos/src/FamOs.Web/Data/Entities/FamOsTask.cs`
12. `/home/fredw/projects/fip/famos/src/FamOs.Web/Services/OutboxProcessorService.cs`
13. `/home/fredw/projects/fip/famos/src/FamOs.Web/Services/SignalRecomputeService.cs`
14. `/home/fredw/projects/fip/famos/src/FamOs.Web/Domain/LifecycleCommandService.cs`
15. `/home/fredw/projects/fip/famos/src/FamOs.Web/Domain/SignalResolver.cs`

## Pre-Verified Findings (grep results already run — confirm/contextualize these in your review)

All high-priority mechanical checks already passed via direct grep:

### DataProtection (lines 100-103 of Program.cs)
```
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();
```
✅ All three: PersistKeysToDbContext, SetApplicationName("FortressAI"), DisableAutomaticKeyGeneration() on same chain.

### Blazor Script (App.razor line 16)
```
<script src="/_framework/blazor.server.js"></script>
```
✅ Correct: blazor.server.js (not blazor.web.js)

### FipModule.cs
```
FipModule.FAMOS = 4  ✅
FullName() has FipModule.FAMOS => "FAM OS"  ✅
ShortName() has FipModule.FAMOS => "FAM OS"  ✅
Url() has FipModule.FAMOS => "https://famos.fortressam.ai"  ✅
```

### MainLayout.razor (line 15)
```
<FipNavBar ActiveModule="FipModule.FAMOS"
```
✅ Correct ActiveModule set.

### Auth (Program.cs)
```
Cookie.Name = ".FortressAI.Session"  ✅ line 33
LoginPath = "/auth/redirect-to-login"  ✅ line 29
options.FallbackPolicy = options.DefaultPolicy  ✅ line 42
/health AllowAnonymous  ✅ lines 181-185
/auth/redirect-to-login AllowAnonymous  ✅ line 193
/auth/logout AllowAnonymous  ✅ line 200
```

### Dockerfile (lines 1, 5)
```
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
```
✅ .NET 9 confirmed.

### buildspec.yml (line 12)
```
docker build -f famos/Dockerfile -t famos-web:$IMAGE_TAG .
```
✅ Monorepo root context (trailing `.`).

### DB Init (Program.cs lines 124-153)
```
_ = Task.Run(async () =>
{
    await Task.Delay(5000);
    ...
    await creator.CreateTablesAsync();
```
✅ Background Task with 5-second delay — does not block health probe.

### EF Migrations
No Migrations/ directory found.
✅ CLEAN — no migrations.

### Commit Scope (git show 4f51202 --name-only)
Only `famos/` and `shared/FipShared/Models/FipModule.cs` — no FAIT/FIRM/FORMS files touched.
✅ Clean commit scope.

## Your Review Job
Read the source files and look for:
1. **Correctness issues** in domain entities, services, DbContext
2. **Security concerns** (auth, data exposure, input handling)
3. **Pattern consistency** with FIP ecosystem (e.g. cookie domain, auth flow, db patterns)
4. **Code quality** (nullable handling, error handling in background services, dispose patterns)
5. **Any issues in the files not yet checked** — DbContext, entities, services, domain logic

## Output Format

Produce your review findings as a structured analysis with:
- Summary of each file reviewed
- Any Critical issues (blocking)
- Any Important issues (should fix)
- Any Nitpick issues (minor)
- Overall verdict: PASS / NEEDS-CHANGES / FAIL
- Reasoning for the verdict

Be specific: file path + line number for every finding.
