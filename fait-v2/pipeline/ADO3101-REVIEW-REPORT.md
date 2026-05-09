# Review Report — ADO#3101
## Per-Connector Read/Write Permission Enforcement
**Cycle:** 1  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-09  
**CC invocation:** `cat pipeline/clint-review-brief-3101-3106.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Verdict: NEEDS-CHANGES

---

## Build Gates

| Check | Result |
|-------|--------|
| `node --check harness-server.js` | ✅ PASS |
| `dotnet build` | ✅ PASS (0 errors, 3 pre-existing warnings) |

---

## Issues

### Important

**1. `WRITE_TOOL_PATTERNS` false-positive on `post` and `set` (harness-server.js)**

```js
const WRITE_TOOL_PATTERNS = /create|update|delete|write|send|post|add|remove|modify|set/i;
```

`post` is a bare substring match. A tool named `get_post_by_id` contains `post` and will be classified as a write operation, causing a 403 on a legitimate read call against a read-only MCP server. Similarly `set` matches `reset`, `offset`, `asset`, `preset`, `dataset`.

**Fix:** Use word boundaries — `\bpost\b`, `\bset\b` — or remove `post` entirely (covered by `create`/`write`/`send`) and tighten `set` to `\bset\b` or `\bset_`.

---

**2. `ToggleActive` silently clears `AllowedRoles` (AgentPlugins.razor)**

```csharp
await PluginAgentService.UpdatePluginAsync(
    plugin.Id, plugin.Name, plugin.Description,
    plugin.SkillsDirectory,
    existingMcpPerms,
    new List<string>(),    // ← AllowedRoles unconditionally cleared
    newValue,
    plugin.AllowKbWrite);
```

The MCP permissions are correctly round-tripped (deserializing `plugin.AllowedMcpServers`). `AllowedRoles` is not — it is passed as an empty list on every toggle. `PluginAgentService.GetAvailablePluginsAsync` treats empty `allowedRoles` as "open to all", so this silently grants broader access whenever a plugin's active state is toggled.

This is a latent privilege-escalation bug. Currently AllowedRoles may not be wired in the UI, but the pattern is dangerous.

**Fix:** Deserialize `plugin.AllowedRoles` in `ToggleActive` the same way `AllowedMcpServers` is handled, and pass the existing value through.

---

### Nitpick

**3. Context envelope §5 includes Read=false servers (ContextEnvelopeService.cs)**

```csharp
var mcpPermLines = pluginMcpServers.Select(s =>
    $"- {s.ServerId}: {(s.Write ? "read+write" : "read-only")}");
```

`pluginMcpServers` is the full deserialized list. A server with `Read=false, Write=false` will appear as `read-only` in the system prompt even though it's not enabled. This could confuse the agent about available servers.

**Fix:** Filter to `pluginMcpServers.Where(s => s.Read)` before building `mcpPermLines`.

---

## Items Confirmed Correct

| Item | Status |
|------|--------|
| Write enforcement only fires with `serverId`/`server_id` present | ✅ Correct (graceful degradation appropriate) |
| Context envelope §5 read-only vs read+write labels | ✅ Correct logic |
| Admin UI McpServerPermissionForm add/remove/toggle | ✅ Works correctly; index capture in lambda is safe |
| ToggleActive passes MCP perms correctly | ✅ Yes (AllowedMcpServers round-tripped correctly) |
| No hardcoded colors/font sizes/spacing in AgentPlugins.razor | ✅ All CSS-class-driven |

---

## Required Fixes Before Cycle 2

1. **harness-server.js** — Tighten `WRITE_TOOL_PATTERNS`: word-boundary `post` and `set` or remove `post`.
2. **AgentPlugins.razor** — `ToggleActive`: pass through existing `AllowedRoles` instead of `new List<string>()`.

Fix #3 (nitpick) is non-blocking but should be addressed.

---

## Notes for Tony
- Don't touch ADO#3106 files — that WI passed. Only fix the two items above in 3101.
- No scope creep — don't refactor anything else.
