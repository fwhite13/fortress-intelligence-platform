# Fix Brief: ADO#3101 Cycle 2 — Three targeted fixes

## Working directory
/home/fredw/projects/fip/fait-v2

---

## Fix 1: Tighten WRITE_TOOL_PATTERNS in agent-harness/harness-server.js

**File:** `agent-harness/harness-server.js`

**Current line (line 812):**
```js
const WRITE_TOOL_PATTERNS = /create|update|delete|write|send|post|add|remove|modify|set/i;
```

**Problem:** `post` and `set` are bare substring matches causing false positives:
- `get_post_by_id` matches `post` → false 403
- `reset`, `offset`, `preset` match `set` → false 403

**Fix:** Remove `post` entirely (covered by `create`/`write`/`send`) and apply word boundary to `set`:
```js
const WRITE_TOOL_PATTERNS = /create|update|delete|write|send|add|remove|modify|\bset\b/i;
```

Replace ONLY that one const line. Do not touch anything else in this file.

---

## Fix 2: ToggleActive clears AllowedRoles in AgentPlugins.razor

**File:** `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor`

**Problem:** In the `ToggleActive` method (around line 204), the `UpdatePluginAsync` call passes `new List<string>()` for `allowedRoles`, silently wiping any existing role restrictions.

**Current code in ToggleActive (the UpdatePluginAsync call, around line 218-224):**
```csharp
await PluginAgentService.UpdatePluginAsync(
    plugin.Id, plugin.Name, plugin.Description,
    plugin.SkillsDirectory,
    existingMcpPerms,
    new List<string>(),
    newValue,
    plugin.AllowKbWrite);
```

**Fix:** Deserialize `AllowedRoles` from `plugin.AllowedRoles` the same way `existingMcpPerms` is built from `plugin.AllowedMcpServers`. Then pass that through instead of `new List<string>()`.

Add the deserializing logic right after the `existingMcpPerms` try/catch block:
```csharp
var existingAllowedRoles = new List<string>();
try
{
    existingAllowedRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
        plugin.AllowedRoles,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new();
}
catch { }
```

Then change `new List<string>()` in the `UpdatePluginAsync` call to `existingAllowedRoles`.

The resulting ToggleActive method body should look like:
```csharp
private async Task ToggleActive(AgentPlugin plugin, bool newValue)
{
    try
    {
        var existingMcpPerms = new List<McpServerPermission>();
        try
        {
            existingMcpPerms = System.Text.Json.JsonSerializer.Deserialize<List<McpServerPermission>>(
                plugin.AllowedMcpServers,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new();
        }
        catch { }

        var existingAllowedRoles = new List<string>();
        try
        {
            existingAllowedRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                plugin.AllowedRoles,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? new();
        }
        catch { }

        await PluginAgentService.UpdatePluginAsync(
            plugin.Id, plugin.Name, plugin.Description,
            plugin.SkillsDirectory,
            existingMcpPerms,
            existingAllowedRoles,
            newValue,
            plugin.AllowKbWrite);
        plugin.IsActive = newValue;
        Snackbar.Add($"{plugin.Name} {(newValue ? "enabled" : "disabled")}.", Severity.Success);
        StateHasChanged();
    }
    catch
    {
        Snackbar.Add($"Failed to update {plugin.Name}.", Severity.Error);
    }
}
```

---

## Fix 3: Filter Read=false servers from context envelope §5

**File:** `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs`

**Problem:** Around line 109, `mcpPermLines` is built from ALL `pluginMcpServers` including those with `Read=false` and `Write=false`. These appear as "read-only" in the system prompt even though they're disabled.

**Current code (around line 109):**
```csharp
var mcpPermLines = pluginMcpServers.Select(s =>
    $"- {s.ServerId}: {(s.Write ? "read+write" : "read-only")}");
```

**Fix:** Add a `.Where(s => s.Read)` filter:
```csharp
var mcpPermLines = pluginMcpServers.Where(s => s.Read).Select(s =>
    $"- {s.ServerId}: {(s.Write ? "read+write" : "read-only")}");
```

Also update the count check on the line above (around line 107) to only show the section if there are actually readable servers. The `if (pluginMcpServers.Count > 0)` check will naturally produce an empty section if all are Read=false, so the simplest fix is just the Where clause on mcpPermLines. But ideally also update the condition to:
```csharp
if (pluginMcpServers.Any(s => s.Read))
```

---

## After all fixes
Do NOT run any commands after editing. Just make the three edits as described above and stop. The calling script will handle verification and commits.
