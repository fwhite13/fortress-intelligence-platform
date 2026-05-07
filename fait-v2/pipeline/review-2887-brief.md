# REVIEW BRIEF — ADO#2887 — FORGE KB Integration Service
**Clint Barton (Hawkeye) — Code Review | Cycle 1**

## Task
Review WI #2887 — FORGE KB integration service for FAIT v2 Sprint 3.

## Repo
`~/projects/fip/fait-v2/` — branch `main`
Relevant commits: `36aeeb1` (main build), `b95d55d` (build report + residuals)

## What Was Built (Tony's summary)

New service layer in FAIT v2 Blazor app to call FORGE KB search/add via `fip-mcp` over MCP JSON-RPC 2.0:

**New files:**
- `Data/Models/McpServer.cs` — EF model for `mcp_servers` table
- `Data/Models/McpUserToken.cs` — EF model for `mcp_user_tokens` table
- `Data/Migrations/20260507125357_AddMcpTables.cs` — EF migration
- `Services/IFipTokenProvider.cs` + `Services/FipTokenProvider.cs`
- `Services/IForgeKbService.cs` + `Services/ForgeKbService.cs`

**Modified files:**
- `Program.cs` — new DI registrations + idempotent `forge-kb` seed on startup
- `appsettings.json` — `FipMcp:EndpointUrl` config key added
- `Components/Pages/Dashboard.razor` — calls `ListKbsAsync`, renders KB pills
- `Components/Chat/ChatView.razor` — `IForgeKbService` injected

## Spec References
- `memory/projects/fait-v2-spec-2026-04-27.md` §7 (Connectors/MCP)
- `memory/projects/forge-kb-mcp-server-spec-2026-04-27.md` (fip-mcp server spec — MCP JSON-RPC contract)

## Tony's Flagged Concerns for Clint

1. **`FipTokenProvider`** reads `access_token` claim directly from cookie principal. Verify claim name matches what FIP shared cookie actually sets. Clint should check `fred-dev` (FAIT v1) to see how it reads the bearer token — the FIP shared cookie may not contain `access_token` as a named claim. This is important for fip-mcp auth to work.
2. **ForgeKbService error handling** — `CallToolAsync` returns `null` on HTTP errors, `ListKbsAsync` returns empty list on null. Is silent empty-list the right behavior for Dashboard if fip-mcp is unreachable?
3. **Design agent tables in migration** — Tony reports CC bundled `DesignAgentSession` and `DesignAgentArtifact` models into the same migration as the MCP tables. WI #2865 (Design Agent, running in parallel) has its own migration. Check for double-creation issues — if both migrations try to create the same tables, the second deploy will fail.

## FAIT v2 Mandatory Rules to Check

- `GuidFormat = MySqlGuidFormat.None` on ALL MySQL connection string builders
- varchar(36) for all GUID columns
- No hardcoded colors/fonts/sizes in .razor files (CSS variables only)
- No Cognito references
- No `@{ var x = ... }` inside Razor `@if/@else` blocks with markup
- All new `IHttpClientFactory` usage uses named clients — no raw `HttpClient`
- EF migrations use `datetime(6)` for all DateTime columns

## Review Format
Use CC CLI: `cat review-2887-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

Produce Review Report at: `~/projects/fip/fait-v2/pipeline/ADO2887-REVIEW-REPORT.md`

Verdict: PASS / NEEDS-CHANGES / FAIL

If PASS or issues are nitpick-only: reply with full Review Report.
If NEEDS-CHANGES: list specific issues with exact file/line references and what to fix.

## ADO Comment (MANDATORY after review)
`mcporter call devops.add_comment --args '{"project":"Fortress","id":2887,"text":"**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. {summary}"}'`
