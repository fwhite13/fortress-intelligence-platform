# Review Report — ADO#3090

### Verdict: PASS

---

### Spec Compliance Check

**Brief from task:** Agent Plugin Admin Panel + Seed Migration — seed 3 agents, admin page at `/admin/agents`, nav entry in MainLayout.

**Files changed:**
- `src/FortressAI.V2.Web/Data/Migrations/20260509080000_SeedDefaultAgentPlugins.cs` — ✅ created
- `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — ✅ created (note: committed in 69fd41a8, not 0d61a640 — see note below)
- `src/FortressAI.V2.Web/Components/Layout/MainLayout.razor` — ✅ modified (nav entry added)

**Note on commit attribution:** The migration file and AgentPlugins.razor were committed in 69fd41a8 (`feat(fait#3108)`) rather than 0d61a640 (`feat(fait#3090)`). Both commits are by Fred (same session). All three files are present and correct in HEAD. Functional review unaffected — this is a housekeeping note for pipeline traceability.

**Acceptance Criteria:**
- [x] Migration seeds 3 agents: Marketing Assistant, Finance Assistant, Legal Assistant — ✅ Verified: all 3 present in Up() SQL
- [x] Idempotent seeding — ✅ Verified: uses `INSERT IGNORE ... WHERE NOT EXISTS` (doubly guarded — see below)
- [x] Admin page at `/admin/agents` with `[Authorize]` — ✅ `@page "/admin/agents"` + `@attribute [Authorize]` at L1–2
- [x] `IDbContextFactory` pattern — ✅ `await using var db = await DbFactory.CreateDbContextAsync()` in LoadAgents; not stored as field
- [x] `MudDialog` outside `@if` block — ✅ Dialog at L79+, after `@if/_error/else` block closes
- [x] Active toggle calls `UpdatePluginAsync` — ✅ ToggleActive() calls service, no direct DB
- [x] CSS variables only — ✅ No inline styles, no hardcoded hex/px
- [x] Nav entry added in MainLayout — ✅ Admin MudNavGroup after Connectors link
- [x] `dotnet build` 0 errors — ✅ Verified (0 errors, 0 warnings)

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Files Cross-Referenced:**

| Pair | Check | Result |
|------|-------|--------|
| `AgentPlugins.razor` ToggleActive call ↔ `IPluginAgentService.UpdatePluginAsync` signature | `(pluginId, name, description, skillsDirectory, List<McpServerPermission>, List<string>, isActive)` | ✅ Exact match |
| `AgentPlugins.razor` SaveForm call ↔ `IPluginAgentService.UpdatePluginAsync` signature | Same params | ✅ Exact match |
| `AgentPlugins.razor` SaveForm call ↔ `IPluginAgentService.CreatePluginAsync` signature | `(name, description, skillsDirectory, List<McpServerPermission>, List<string>, createdBy)` | ✅ Exact match |
| `SeedDefaultAgentPlugins` names ↔ `agent_plugins.name` unique index | `ix_agent_plugins_name` unique — confirmed in `AddAgentPlugins` migration | ✅ INSERT IGNORE will fire on name collision |
| `SeedInitialAgentPlugins` seeded names ↔ `SeedDefaultAgentPlugins` seeded names | "Marketing"/"Finance"/"Legal" vs "Marketing Assistant"/"Finance Assistant"/"Legal Assistant" | ⚠️ Different names — see Advisory |

**Undocumented dependencies found:**
- `SeedInitialAgentPlugins` (20260507210000) — already seeded bare-name variants. Not a conflict, but see Advisory.

---

### CC Review Summary

CC Sonnet verified all hard pass/fail criteria for the migration, the Razor page, the nav update, and service signature alignment. All passed. One advisory raised on seeding overlap (non-blocking).

---

### Issues Found

| Severity | File | Issue | Fix |
|----------|------|-------|-----|
| None | — | — | — |

---

### Advisory (non-blocking)

**Duplicate-purpose agent overlap:**

`SeedInitialAgentPlugins` (migration 20260507210000) already seeded:
- "Marketing" — `wwwroot/claude/agents/marketing.md`
- "Finance" — `wwwroot/claude/agents/finance.md`
- "Legal" — `wwwroot/claude/agents/legal.md`

`SeedDefaultAgentPlugins` (this migration) seeds:
- "Marketing Assistant", "Finance Assistant", "Legal Assistant" (UUID-keyed, no skills_directory)

Both sets will exist in the DB post-deploy. No crash, no unique constraint violation (different names). However, users will see **6 agents** in the admin list and the agent selector, with 3 bare-name and 3 "Assistant"-suffix duplicates of overlapping purpose. Confirm with WI author whether the original 3 should be deprecated/removed via a follow-up cleanup WI.

---

### Detailed Technical Verification

| Check | Result | Evidence |
|-------|--------|----------|
| Migration idempotency | ✅ | `INSERT IGNORE ... SELECT ... WHERE NOT EXISTS (SELECT 1 FROM agent_plugins WHERE name = v.name)` — two independent guards |
| Unique index on `name` | ✅ | `ix_agent_plugins_name unique: true` in `AddAgentPlugins` migration |
| Down() safety (won't touch user rows) | ✅ | `AND created_by = 'system'` guard; unique name index prevents user rows with same name |
| `await using var db = await DbFactory.CreateDbContextAsync()` | ✅ | LoadAgents L142 |
| DbFactory not stored as field | ✅ | Injected via `@inject`, used inside methods only |
| MudDialog at root (not inside `@if`) | ✅ | Line 79 — after main conditional block |
| `@bind-Visible="_showForm"` pattern | ✅ | Correct MudBlazor dialog binding |
| ToggleActive → UpdatePluginAsync (no direct DB) | ✅ | L162 |
| StateHasChanged() after in-place `plugin.IsActive = newValue` | ✅ | L170 — required because Blazor won't detect mutation of reference type property |
| StateHasChanged() in LoadAgents finally — needed? | ✅ Not needed | Called only from OnInitializedAsync and EventCallback; auto-renders |
| MudSwitch `Value`/`ValueChanged` API (v7) | ✅ | Matches codebase pattern (TaskEditDialog.razor uses identical `@bind-Value` API) |
| `[Authorize]` on page | ✅ | L2: `@attribute [Authorize]` |
| CSS variables only | ✅ | No inline style attributes or hardcoded values found |
| Nav: Admin group after Connectors | ✅ | MainLayout.razor L58–60 |
| dotnet build | ✅ | 0 errors, 0 warnings |

---

### Spec Fidelity

Admin page implements full CRUD (list, toggle, create, edit). Migration is idempotent. `[Authorize]` present. `IDbContextFactory` correctly used. `MudDialog` correctly placed outside `@if`. Service calls match interface signatures. Nav entry in correct position.

The only open question is the intent behind seeding "Assistant" suffix agents when bare-name variants already exist — functional correctness is not impacted but a follow-up cleanup ticket is recommended.

---

_Reviewed by Hawkeye — Commit `0d61a640` + `69fd41a8` (files split across commits)_
