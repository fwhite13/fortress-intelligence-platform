# Fix Brief — ADO#2968 Cycle 2 — Three Targeted Fixes

## Working Directory
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

---

## Fix 1 — Update `wwwroot/claude/rules/forge-kb.md`

Current content references `fip-mcp` MCP server for KB access. After ADO#2968, KB access is now via direct AWS Bedrock — no MCP tool call needed. Update this file so the CC agent knows:
- KB context is pre-loaded into the context envelope automatically (no tool call needed to retrieve KB info)
- KBs are made available when enabled for the session; the agent should use the KB content already present in context
- No MCP KB query tools are available or needed — the system handles KB injection at the envelope level

Replace the entire content of `wwwroot/claude/rules/forge-kb.md` with accurate instructions reflecting direct Bedrock KB injection.

**File path:** `wwwroot/claude/rules/forge-kb.md`

---

## Fix 2 — Update `mcp_servers` seed block in `Program.cs`

ConnectorService reads from `mcp_servers` to render the Connectors UI. So rows MUST stay. However, the seed block currently sets real endpoint URLs like `https://mcp.fortressam.ai/mcp/forge-kb` — these are now dead for forge-kb, ado, ms365, web-search because those integrations are now direct (not via MCP proxy).

The `EndpointUrl` field is used for MCP proxy calls. Since these are now direct integrations, we should set `EndpointUrl` to `null` or empty string for rows that are no longer MCP-proxied.

Update the seed block in `Program.cs`:
1. Remove the `var baseUrl = cfg["FipMcp:BaseUrl"]...` line (dead config read)
2. Update each tool group entry so `EndpointUrl` is set to `null` (or empty) instead of `$"{baseUrl}/{tg.Name}"`
3. Update the existing-row logic accordingly — remove the `EndpointUrl` comparison/update since it's no longer relevant

The seed block must still write/update the rows (ConnectorService depends on them), just without live endpoint URLs.

**File path:** `Program.cs` (root of Web project)

---

## Fix 3 — Remove `FipMcp:BaseUrl` from `appsettings.json`

Dead configuration key. Remove the entire `"FipMcp"` block:

```json
"FipMcp": {
  "BaseUrl": "https://mcp.fortressam.ai/mcp"
},
```

**File path:** `appsettings.json`

---

## After All Fixes

Run: `dotnet build` from `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web/`

Must produce 0 errors. Fix any compilation errors before committing.

Commit from `/home/fredw/projects/fip/fait-v2/` with message:
`fix(fait#2968): update forge-kb.md CC instructions, remove dead mcp seed + FipMcp config`

---

## Notes
- Do NOT remove the seed block entirely — ConnectorService queries `mcp_servers` table to render the Connectors UI
- Do NOT touch ConnectorService.cs
- Only the three files above should change: forge-kb.md, Program.cs, appsettings.json
