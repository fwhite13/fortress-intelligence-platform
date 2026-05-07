# REVIEW BRIEF — ADO#2865 — Google Stitch Design Agent
**Clint Barton (Hawkeye) — Code Review | Cycle 1**

## Task
Review WI #2865 — Google Stitch Design Agent service layer and UI components for FAIT v2 Sprint 3.

## Repo
`~/projects/fip/fait-v2/` — branch `main`
Relevant commit: `aa91a57`

## What Was Built (Tony's summary)

**New files:**
- `Services/IDesignAgentService.cs` + `Services/DesignAgentService.cs`
- `Data/Models/DesignAgentSession.cs` + `Data/Models/DesignAgentArtifact.cs`
- `Models/ActiveAgent.cs`
- `Components/Agent/AgentPluginBadge.razor`
- `Components/Agent/DesignArtifactCard.razor`
- `Components/Agent/DesignAgentView.razor`

**Modified files:**
- `Services/IUserAgentRuntime.cs` — added `DispatchToolCallAsync`
- `Services/FargateUserAgentRuntime.cs` — stubbed `DispatchToolCallAsync`
- `Data/FaitV2DbContext.cs` — registered new models
- `Program.cs` — DI registration for `IDesignAgentService`
- `Components/Chat/ChatView.razor` — agent selector (MainAssistant | DesignAgent)

**Note:** `design_agent_sessions` and `design_agent_artifacts` DB tables were already created in the Lane 1 (#2887) migration `AddMcpTables`. No new migration needed here.

## Spec References
- `memory/projects/fait-v2-spec-2026-04-27.md` §6.3 (Design Agent)

## Tony's Flagged Concerns for Clint

1. **`DesignAgentService.GenerateFallbackHtmlAsync`** — strips markdown fences from CC output. Edge cases if CC returns unusual formatting. Check the stripping logic is robust.
2. **`DesignArtifactCard.razor` `HandleDownload`** — uses `JS.InvokeVoidAsync("downloadBase64", ...)`. Verify whether `downloadBase64` JS function exists in `app.js` / `_Host.cshtml`. If it doesn't exist, this is a Critical runtime error (JS interop call to undefined function).
3. **`Stitch:GcpCredentialsConfigured`** is a string config key — `"true"` triggers live Stitch, anything else → CC fallback. The string comparison approach is somewhat fragile; Clint should flag if a strongly-typed bool binding would be safer.

## FAIT v2 Mandatory Rules to Check

- `GuidFormat = MySqlGuidFormat.None` on ALL MySQL connection string builders
- varchar(36) for all GUID columns in any new EF models
- No hardcoded colors/fonts/sizes in .razor files — CSS variables ONLY
- No Cognito references
- No `@{ var x = ... }` inside Razor `@if/@else` blocks with markup — use `@code` properties
- MudBlazor: no icon variants with suffixes (`Rounded`, `Sharp`, etc.) — base icons only
- `IHttpClientFactory` named clients — no raw `HttpClient`
- EF DateTime columns use `datetime(6)` column type

## Key Interface to Verify

`DispatchToolCallAsync` was added to `IUserAgentRuntime` — this is a NEW interface method. Verify:
- All implementations of `IUserAgentRuntime` have it (any stubs/mocks besides `FargateUserAgentRuntime`?)
- The method signature is consistent with how `DesignAgentService` calls it

## Review Format
Use CC CLI: `cat review-2865-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

Produce Review Report at: `~/projects/fip/fait-v2/pipeline/ADO2865-REVIEW-REPORT.md`

Verdict: PASS / NEEDS-CHANGES / FAIL

## ADO Comment (MANDATORY after review)
`mcporter call devops.add_comment --args '{"project":"Fortress","id":2865,"text":"**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. {summary}"}'`
