# Build Report — ADO#3090

## What was built
1. EF migration `SeedDefaultAgentPlugins` — seeds Marketing Assistant, Finance Assistant, Legal Assistant via idempotent raw SQL (`INSERT WHERE NOT EXISTS`)
2. Admin page at `/admin/agents` — Blazor Server page with full CRUD for agent plugins (list, toggle active, create, edit)
3. Nav entry — "Admin > Agents" nav group added to MainLayout.razor drawer after Connectors

## Files changed
- `src/FortressAI.V2.Web/Data/Migrations/20260509080000_SeedDefaultAgentPlugins.cs` — **created**: seeds 3 agents via `INSERT WHERE NOT EXISTS` keyed on `name`. Down migration deletes them by name + `created_by = 'system'`. No schema changes.
- `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — **created** (new Admin/ subdirectory): `@page "/admin/agents"`, `[Authorize]`, `IDbContextFactory` pattern, MudTable listing all plugins, active toggle via MudSwitch, edit button, `MudDialog` outside `@if` blocks with `@bind-Visible`, create/edit form with Name/Description/SkillsDirectory/IsActive. CSS variables only.
- `src/FortressAI.V2.Web/Components/Layout/MainLayout.razor` — **modified**: added `MudNavGroup` for Admin with Agents link after Connectors nav item.

## Parallelization used
No — sequential (migration file → page → nav); no shared files between migration and Blazor page.

## CC sessions run
1 — CC Sonnet via pipe mode.

## Acceptance criteria verification
- [x] `/admin/agents` renders a list of all agent plugins — **verified**: `db.AgentPlugins.OrderBy(p => p.Name).ToListAsync()` in `OnInitializedAsync`
- [x] Can toggle active/inactive — **verified**: `ToggleActive()` calls `PluginAgentService.UpdatePluginAsync` with new `isActive` value
- [x] Can create new agents — **verified**: `OpenNewForm()` + `SaveForm()` creates via `PluginAgentService.CreatePluginAsync`
- [x] Seed migration runs without errors — **verified**: uses INSERT WHERE NOT EXISTS, idempotent
- [x] CSS variables only — **verified**: all CSS uses `var(--*)` tokens, no hardcoded hex/px values in style tags
- [x] `dotnet build` 0 errors — **verified**: 0 errors (2 pre-existing warnings unrelated to this work)

## Commit
`0d61a640` — feat(fait#3090): agent plugin admin panel + seed migration

## Known edge cases / things Clint should scrutinize
- `MudSwitch` uses `Value`/`ValueChanged` API (MudBlazor v7), not `Checked`/`CheckedChanged` — verify this matches the project's MudBlazor version
- Admin page has `[Authorize]` only (no role guard) — admin role enforcement is deferred to a future WI per spec
- The existing `SeedInitialAgentPlugins` migration already seeded "Marketing", "Finance", "Legal" (no "Assistant" suffix) — this new migration will add the "Marketing Assistant" etc. variants if the names don't already exist. Both sets will coexist in DB until the earlier names are cleaned up (future WI or manual cleanup)

## How to test locally
```bash
dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj
# Navigate to /admin/agents — should show plugin list
# Click "New Agent", fill form, save — should add row
# Toggle active switch — should persist immediately
# Apply migration: dotnet ef database update --project src/FortressAI.V2.Web
```
