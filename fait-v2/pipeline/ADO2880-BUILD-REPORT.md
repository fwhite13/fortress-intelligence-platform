# Build Report — ADO#2880: FAIT v2 Marketing Agent Seed

## What was built
Seeded the first three launch agents (Marketing, Finance, Legal) into FAIT v2 — skills markdown files, EF Core data migration, and updated `PluginAgentService` to read skills from the wwwroot filesystem.

## Commit
`2324abe` — `feat(fait-v2#2880): seed Marketing/Finance/Legal agent plugins — skills markdown, EF migration, PluginAgentService wwwroot read`

## Files changed
- `src/FortressAI.V2.Web/wwwroot/claude/agents/marketing.md` — Marketing agent skills (brand positioning, content strategy, campaign planning, product marketing, materials)
- `src/FortressAI.V2.Web/wwwroot/claude/agents/finance.md` — Finance agent skills (budget modeling, analysis, reporting, forecasting)
- `src/FortressAI.V2.Web/wwwroot/claude/agents/legal.md` — Legal agent skills (contract review, compliance docs, research support, drafting)
- `src/FortressAI.V2.Web/Data/Migrations/20260507210000_SeedInitialAgentPlugins.cs` — EF data migration: `InsertData()` for 3 rows with fixed GUIDs `000...001/002/003`; `DeleteData()` in Down(). No raw SQL.
- `src/FortressAI.V2.Web/Services/PluginAgentService.cs` — Injected `IWebHostEnvironment`; `GetSkillsContentAsync` reads local filesystem for `wwwroot/` paths, falls back to blob stub for other paths

## Parallelization
No — single CC session, tasks were sequential (migration depends on knowing the table schema, service update depends on knowing the file paths).

## CC sessions run
1 — CC Sonnet, single run, no interventions needed.

## Acceptance criteria verification
- [x] `wwwroot/claude/agents/marketing.md` exists with Marketing agent skills
- [x] `wwwroot/claude/agents/finance.md` exists with Finance agent skills
- [x] `wwwroot/claude/agents/legal.md` exists with Legal agent skills
- [x] EF migration `SeedInitialAgentPlugins` inserts 3 rows using `InsertData()` — no raw SQL
- [x] `PluginAgentService.GetSkillsContentAsync` reads from wwwroot/ for local paths
- [x] `IWebHostEnvironment` injected in PluginAgentService constructor
- [x] dotnet build 0 errors, 0 warnings

## Dotnet build result
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.41
```

## Known edge cases / things Clint should scrutinize
- **Migration timestamp**: `20260507210000` — manually set (not `dotnet ef migrations add`). The Designer file and ModelSnapshot were not updated since `InsertData`-only migrations don't require them for MySQL/Pomelo. Verify EF doesn't complain at runtime.
- **Fixed GUIDs**: `00000000-0000-0000-0000-000000000001/002/003` are deterministic seeds. If the table already has rows with these IDs from a prior run, the migration will fail (unique constraint). Confirm DB is clean before deploying to staging.
- **`AllowedRoles = "[]"`**: Empty array = all users. This matches spec intent but Clint should confirm the `GetAvailablePluginsAsync` logic handles this correctly (it does — line: `allowedRoles.Count == 0`).

## How to test locally
1. `dotnet ef database update` — applies `SeedInitialAgentPlugins` migration
2. Check `agent_plugins` table: should have 3 rows with GUIDs `000...001/002/003`
3. Open FAIT v2, navigate to any chat — agent selector should show Marketing, Finance, Legal
4. Select Marketing agent — verify skills content loads (not the fallback stub)
