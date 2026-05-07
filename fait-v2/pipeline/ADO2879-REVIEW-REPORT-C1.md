# Review Report — ADO#2879

**Task:** FAIT v2 Plugin agent framework  
**Commit:** `17ba9e5`  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07  
**Cycle:** 1

---

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

All 12 checklist items from the pipeline brief were verified via Claude Code CLI review.

**§ Acceptance Criteria:**
| # | Criterion | Result |
|---|-----------|--------|
| 1 | `AgentPlugin.Id` and `CreatedBy` are `string` type (`Guid.NewGuid().ToString()`, no format specifier) | ✅ |
| 2 | EF config: `HasMaxLength(36)` on Id/CreatedBy, `HasMaxLength(100)` on Name, `longtext` on AllowedMcpServers/AllowedRoles | ✅ |
| 3 | Migration `Up()` column types match model (varchar(36) PK, varchar(100) name, TEXT description, varchar(500) skills_directory, longtext JSON cols, datetime(6) timestamps, tinyint(1) is_active, correct PK + 2 indexes) | ✅ |
| 4 | Migration `Down()` drops `agent_plugins` table | ✅ |
| 5 | `FaitV2DbContext.cs` has exactly ONE `agent_plugins` entity config block | ✅ |
| 6 | `IPluginAgentService` interface is clean — no implementation details | ✅ |
| 7 | `GetAvailablePluginsAsync` filters `IsActive == true`; allows empty `AllowedRoles` (all users) or matching role | ✅ |
| 8 | `BuildEnvelopeAsync` accepts optional `pluginId`, merges read-enabled MCP servers, appends plugin skills to `MemorySummary` | ✅ |
| 9 | Plugin selector CSS uses `var(--...)` tokens only — no hardcoded colors | ✅ |
| 10 | `IPluginAgentService` registered as `Scoped` in `Program.cs` | ✅ |
| 11 | `dotnet build` — 0 errors, 0 warnings | ✅ |
| 12 | No Cognito references in codebase | ✅ |

**Spec compliance verdict:** ✅ COMPLIANT on all criteria — one code-quality issue blocks PASS (see below)

---

### Consistency Audit

- `AgentPlugin` model ↔ EF `OnModelCreating` config ↔ Migration `Up()` — ✅ column types consistent across all three
- `IPluginAgentService` ↔ `PluginAgentService` ↔ `ContextEnvelopeService` — ⚠️ partial mismatch (see Critical #1)
- `McpServerPermission` type accessible from `IPluginAgentService.cs` — ✅ used correctly in calling code
- CSS variables in ChatView plugin selector — ✅ no leakage to hardcoded values

---

### Critical Issues — 1

#### C1: Concrete cast breaks interface contract in `ContextEnvelopeService.cs`

- **File:** `Services/ContextEnvelopeService.cs` (lines 88–89)
- **Category:** correctness / design
- **Issue:** `BuildEnvelopeAsync` hard-casts `_pluginAgentService` from the interface to the concrete `PluginAgentService` type in order to call `DeserializeMcpServers`, which is not on the interface. This will throw `InvalidCastException` if the implementation is ever swapped (mock, decorator, test double).
- **Evidence:**
  ```csharp
  var pluginService = (PluginAgentService)_pluginAgentService;
  var pluginMcpServers = pluginService.DeserializeMcpServers(plugin.AllowedMcpServers);
  ```
- **Impact:** Runtime `InvalidCastException` in any non-production context (tests, mocks, decorated service). Also exposes an internal utility method as `public` on the concrete class for no reason.
- **Fix — Option A** (add to interface, minimal change):
  ```csharp
  // IPluginAgentService.cs
  List<McpServerPermission> DeserializeMcpServers(string json);
  ```
- **Fix — Option B** (preferred — inline deserialization, keeps interface clean):
  ```diff
  - var pluginService = (PluginAgentService)_pluginAgentService;
  - var pluginMcpServers = pluginService.DeserializeMcpServers(plugin.AllowedMcpServers);
  + var pluginMcpServers = System.Text.Json.JsonSerializer
  +     .Deserialize<List<McpServerPermission>>(plugin.AllowedMcpServers,
  +         new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
  +     ?? new();
  ```
  `McpServerPermission` is already in scope (from `IPluginAgentService.cs`), so no additional using needed.

---

### Important Issues — 0

None.

---

### Nitpicks — 0

None.

---

### Positive Observations

- Migration was manually filled but is accurate — column types match the model exactly, including the two indexes. Clean work given the snapshot race condition.
- Role-based filtering logic in `GetAvailablePluginsAsync` is correct and readable: empty array = all users is the right semantic.
- CSS variables usage in the plugin selector is consistent with the FIP token system — no hardcoded color leakage.
- Zero build warnings. Clean compilation on a non-trivial feature.

---

### What to fix

**One change required in `ContextEnvelopeService.cs`:**

Replace the hard-cast at lines 88–89 with inline JSON deserialization (Option B above). The `DeserializeMcpServers` method on `PluginAgentService` can then be made `private` or removed entirely if it has no other callers.

Tony should be able to fix this in under 5 minutes — no architectural rework needed.

---

_Hawkeye — Cycle 1 — 2026-05-07_
