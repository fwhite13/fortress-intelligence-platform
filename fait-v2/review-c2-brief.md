# Hawkeye Cycle 2 Spot-Check — ADO#2968

You are Hawkeye (Clint Barton), code reviewer. This is a targeted cycle 2 spot-check on commit `adad621`.

## What Was Fixed (Three Changes Only)

1. `wwwroot/claude/rules/forge-kb.md` — rewritten to describe direct Bedrock KB context injection (no MCP references)
2. `Program.cs` mcp_servers seed block — removed dead FipMcp:BaseUrl config read; EndpointUrl now seeded as empty string (rows kept for ConnectorService UI)
3. `appsettings.json` — entire `FipMcp` block removed

## Your Review Tasks

### 1. Check `wwwroot/claude/rules/forge-kb.md`
- Read the full file
- Confirm: NO references to "fip-mcp", "FipMcp", "mcp", or any MCP server in the forge-kb instructions
- Confirm: Instructions describe direct Bedrock KB context injection
- Flag any remaining MCP references as NEEDS-CHANGES

### 2. Check `Program.cs` — mcp_servers seed block
- Search for the mcp_servers seed block (likely a SeedMcpServers or similar method, or inline seeding near ConnectorService)
- Confirm: NO read of `FipMcp:BaseUrl` or `FipMcp:*` configuration
- Confirm: EndpointUrl is seeded as empty string `""` (not removed entirely — rows kept for UI)
- Flag any FipMcp config reads as NEEDS-CHANGES

### 3. Check `appsettings.json`
- Read the file (or search it)
- Confirm: The entire `FipMcp` JSON section is gone
- Flag any remaining FipMcp keys as NEEDS-CHANGES

### 4. Regression Check
- Quick scan of files adjacent to these changes for any obvious regressions
- Check that no other file still references FipMcp:BaseUrl or expects it to exist

## Output Format

Produce a structured review report:

```
## Hawkeye Cycle 2 Review — ADO#2968

### Verdict: PASS | NEEDS-CHANGES

### Check 1: forge-kb.md
[findings]

### Check 2: Program.cs seed block
[findings]

### Check 3: appsettings.json
[findings]

### Check 4: Regression scan
[findings]

### Summary
[One paragraph summary of findings and verdict rationale]
```

Be precise. Cite exact line content or grep results for each check. If anything is wrong, state exactly what and why.
