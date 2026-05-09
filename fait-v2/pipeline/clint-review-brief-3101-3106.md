# Code Review Brief — ADO#3101 + ADO#3106 (Cycle 1)

You are Hawkeye (Clint Barton), senior code reviewer. Review two WIs that shipped in shared commits `3741e1bf` and `81c87174`.

## Working directory: /home/fredw/projects/fip/fait-v2

---

## PART 1: ADO#3101 — Per-Connector Read/Write Permission Enforcement

### Files to review:
1. `agent-harness/harness-server.js` — focus on WRITE_TOOL_PATTERNS, KB_WRITE_PATTERNS, isWriteTool(), isKbWriteTool(), the /tools/:toolName handler enforcement block
2. `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — §5 MCP Server Permissions block
3. `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — McpServerPermissionForm inner class, add/remove rows, AllowWrite toggle

### Checklist for ADO#3101:
- [ ] WRITE_TOOL_PATTERNS regex: Is it reasonable? Does the `post` prefix cause false positives? Assess `get_post_by_id` scenario.
- [ ] Write enforcement logic: Does it ONLY fire when `args.serverId` or `args.server_id` is present? Is this graceful degradation correct?
- [ ] Context envelope §5: Does it correctly emit `read-only` vs `read+write` per server? Does it only appear when plugin has MCP servers?
- [ ] Admin UI McpServerPermissionForm: Does add/remove/toggle work correctly? Any state bugs?
- [ ] ToggleActive: Confirm it passes empty `mcpPermissions`. Is this destructive (overwrites existing)?
- [ ] CSS variable rule: Any hardcoded colors, font sizes, or spacing in AgentPlugins.razor?
- [ ] node --check: Confirm harness-server.js is syntactically valid
- [ ] dotnet build: Confirm 0 errors

---

## PART 2: ADO#3106 — G3: KB Write Intent Enforcement

### Files to review:
1. `src/FortressAI.V2.Web/Data/Models/AgentPlugin.cs` — AllowKbWrite field
2. `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs` — entity config
3. `src/FortressAI.V2.Web/Data/Migrations/20260509090000_AddAllowKbWriteToAgentPlugin.cs` — migration
4. `src/FortressAI.V2.Web/Services/IPluginAgentService.cs` + `PluginAgentService.cs` — allowKbWrite parameter
5. `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` — KbWriteAllowed in TurnRequest
6. `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — §7 KB write status
7. `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — KbWriteAllowed in TurnRequest
8. `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — Allow KB Write toggle
9. `agent-harness/harness-server.js` — kbWriteAllowed extraction + KB_WRITE_PATTERNS enforcement

### Checklist for ADO#3106:
- [ ] AgentPlugin.AllowKbWrite: default is `false`? Correct.
- [ ] Migration 20260509090000: correct column (tinyint, default 0)? Up/Down correct?
- [ ] TurnRequest.KbWriteAllowed: default is `true` (main agent always allowed). Verify.
- [ ] Harness KB enforcement: blocks kb_write|kb_upsert|kb_create|knowledge_write when kbWriteAllowed===false AND pluginAgentId is set. Verify AND condition.
- [ ] Context envelope §7: "KB Write Access: allowed / not allowed" present, correct condition.
- [ ] ChatView.razor: passes `KbWriteAllowed: activePlugin?.AllowKbWrite ?? true`. Verify null-coalescing to true.
- [ ] Admin UI: Allow KB Write toggle wired to _formAllowKbWrite, passed to CreatePluginAsync/UpdatePluginAsync correctly.
- [ ] ToggleActive: Does it pass `false` for allowKbWrite, thereby clearing it? Flag if destructive.
- [ ] CSS variable rule: check ChatView.razor and AgentPlugins.razor for hardcoded visual values.
- [ ] node --check passes
- [ ] dotnet build 0 errors

---

## Instructions

For EACH file, read it fully and evaluate the checklist items. Then produce:

### ADO#3101 Review:
- Verdict: PASS / NEEDS-CHANGES / FAIL
- Issues: Critical / Important / Nitpick

### ADO#3106 Review:
- Verdict: PASS / NEEDS-CHANGES / FAIL
- Issues: Critical / Important / Nitpick

Be specific: cite file + line numbers where possible. Focus on correctness bugs, not style preferences.
