# Build Report — ADO#3101 Cycle 2 Addendum
**Agent:** Tony Stark  
**Date:** 2026-05-09  
**Commit:** ca8aaea6  
**CC Invocation:** `cat pipeline/tony-c2-fix-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Changes Applied (Cycle 2 Fixes Only)

### Fix 1 — `WRITE_TOOL_PATTERNS` false-positive (`agent-harness/harness-server.js`)

**Problem:** Bare `post` and `set` substrings caused false 403s on tool names like `get_post_by_id`, `reset`, `offset`.

**Fix:**
```diff
- const WRITE_TOOL_PATTERNS = /create|update|delete|write|send|post|add|remove|modify|set/i;
+ const WRITE_TOOL_PATTERNS = /create|update|delete|write|send|add|remove|modify|\bset\b/i;
```
- Removed `post` (write intent covered by `create`/`write`/`send`)
- Applied `\bset\b` word boundary to eliminate `reset`/`offset`/`preset` false positives

**Verification:** `node --check agent-harness/harness-server.js` → PASS

---

### Fix 2 — `ToggleActive` clears `AllowedRoles` (`AgentPlugins.razor`)

**Problem:** `ToggleActive` passed `new List<string>()` for `allowedRoles`, silently wiping existing role restrictions on every enable/disable toggle.

**Fix:** Added `existingAllowedRoles` deserialization block (parallel to existing `existingMcpPerms` pattern) and passed through to `UpdatePluginAsync`:
```diff
+ var existingAllowedRoles = new List<string>();
+ try
+ {
+     existingAllowedRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
+         plugin.AllowedRoles,
+         new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new();
+ }
+ catch { }
+
  await PluginAgentService.UpdatePluginAsync(
      plugin.Id, plugin.Name, plugin.Description,
      plugin.SkillsDirectory,
      existingMcpPerms,
-     new List<string>(),
+     existingAllowedRoles,
      newValue,
      plugin.AllowKbWrite);
```

---

### Fix 3 — Context envelope §5 includes `Read=false` servers (`ContextEnvelopeService.cs`)

**Problem:** `mcpPermLines` built from all `pluginMcpServers` including disabled ones (Read=false, Write=false), making them appear as "read-only" in the system prompt.

**Fix:**
```diff
- if (pluginMcpServers.Count > 0)
+ if (pluginMcpServers.Any(s => s.Read))
  {
-     var mcpPermLines = pluginMcpServers.Select(s =>
+     var mcpPermLines = pluginMcpServers.Where(s => s.Read).Select(s =>
          $"- {s.ServerId}: {(s.Write ? "read+write" : "read-only")}");
```
- Section now only renders when at least one readable server exists
- Disabled servers no longer appear as "read-only" in the agent context

---

## Verification Results

| Check | Result |
|-------|--------|
| `node --check harness-server.js` | ✅ PASS |
| `dotnet build` | ✅ 0 errors, 0 warnings |

---

## Files Changed
- `agent-harness/harness-server.js` — Fix 1
- `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — Fix 2
- `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — Fix 3

## Scope Compliance
- ADO#3106 files: **NOT touched** ✅
- Changes scoped strictly to three specified fixes ✅
