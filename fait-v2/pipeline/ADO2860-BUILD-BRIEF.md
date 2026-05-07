# ADO#2860 — FAIT v2 CC Context Envelope — BUILD Brief

## Spec
`memory/projects/fait-v2-spec-2026-04-27.md §3.5`
Feature: Epic D (#2839)
Sprint: FAIT v2 Sprint 4

## What Exists
`CCContextEnvelope` class already exists in `Services/ICCExecutionService.cs`:
- UserId, UserDisplayName, KbIds, EnabledMcpServers, MemorySummary, TaskInstructions

`FargateCCExecutionService.BuildPrompt()` assembles these into a text prompt passed to CC. Currently it just dumps them inline — no system CLAUDE.md, no `.claude/rules/` files.

## What to Build

### Layer 1: System-wide CLAUDE.md + rules directory

Create the following files in the FAIT v2 repo (committed, versioned separately):

**`src/FortressAI.V2.Web/wwwroot/claude/CLAUDE.md`**
This is the system-wide guardrails document injected into every CC invocation. Content:
```markdown
# FAIT v2 CC Sandbox — System Guardrails

## Identity
You are a CC (Claude Code) sandbox running inside the FAIT v2 platform. You are executing a specific task on behalf of an authenticated user. Your outputs are reviewed by the platform before delivery.

## Hard Rules (non-negotiable)
- Do NOT make external network calls except through approved MCP servers listed in your context
- Do NOT read, log, or exfiltrate credentials, tokens, or secrets from environment variables or files
- Do NOT access any file path outside your assigned work directory (/tmp/cc-workspaces/{userId}/)
- Do NOT execute shell commands that modify system state outside the work directory
- Do NOT fabricate data, citations, or facts — if you don't know something, say so
- Do NOT produce output that the user did not request

## Artifact Standards
- Word documents: use python-docx, save as .docx
- Excel workbooks: use openpyxl, save as .xlsx
- PowerPoint: use python-pptx, save as .pptx
- HTML: valid HTML5, no external dependencies (self-contained)
- All artifacts: save to current working directory, use descriptive filenames

## MCP Tool Usage
- Only call MCP tools listed in your "Enabled MCP Servers" context
- Never invent tool names or call servers not listed
- If a tool returns an error, handle it gracefully — do not retry more than twice

## Progress Signaling
- Print a brief progress update to stdout as you complete each major step
- Format: "STEP: <what you just completed>"
- This is how the platform shows progress to the user

## Completion
- When your task is fully complete, print "DONE: <one-sentence summary of what was produced>"
- Save all artifacts before printing DONE
```

**`src/FortressAI.V2.Web/wwwroot/claude/rules/forge-kb.md`**
```markdown
# FORGE Knowledge Base Access

FORGE KBs are AWS Bedrock Knowledge Bases. You access them via the fip-mcp MCP server.

## Available Tools (via MCP)
- Use the KB query tools listed in your enabled MCP servers
- Each KB has an ID — use only the IDs provided in your context envelope
- Respect read/write permissions listed per KB

## Access Scope
- You can only query KBs whose IDs appear in your "Available Knowledge Bases" context
- You cannot enumerate all KBs — only the ones you were given
```

**`src/FortressAI.V2.Web/wwwroot/claude/rules/environment.md`**
```markdown
# Environment Constraints

## File System
- Work directory: /tmp/cc-workspaces/{userId}/ (your userId is in context)
- You may create subdirectories within this prefix
- You may NOT access /home/, /etc/, /var/, /root/, or any system paths

## Python Environment
- Python 3.x available
- Standard library available
- Key packages: python-docx, openpyxl, python-pptx, requests (for approved APIs only)

## No External Network
- No direct HTTP/HTTPS calls to external services
- All external data access must go through the MCP servers in your context
```

### Layer 2: IContextEnvelopeService

Create `Services/IContextEnvelopeService.cs`:

```csharp
namespace FortressAI.V2.Web.Services;

public interface IContextEnvelopeService
{
    /// <summary>
    /// Builds the full system CLAUDE.md content (Layer 1) — static, versioned.
    /// </summary>
    string GetSystemClaudeMd();

    /// <summary>
    /// Builds the per-user payload (Layer 2) for injection into CC context.
    /// Includes user identity, KB access, MCP tokens, memory summary.
    /// </summary>
    Task<CCContextEnvelope> BuildEnvelopeAsync(
        string userId,
        string userDisplayName,
        string taskInstructions,
        CancellationToken ct = default);
}
```

Create `Services/ContextEnvelopeService.cs`:
- `GetSystemClaudeMd()` — reads `wwwroot/claude/CLAUDE.md` + concatenates all `wwwroot/claude/rules/*.md` files, returns combined string
- `BuildEnvelopeAsync()` — for Sprint 4 MVP, builds a minimal envelope:
  - userId and userDisplayName from parameters
  - KbIds: read from `UserConnector` table in Aurora where userId matches and connector type is "forge-kb" (or return empty list if none)
  - EnabledMcpServers: read from `UserConnector` where userId matches (connector names — ms365, ado, web, forge-kb as applicable), return list of active connector names
  - MemorySummary: null for now (placeholder for future memory integration)
  - TaskInstructions: from parameter

### Layer 3: Update FargateCCExecutionService

Update `BuildPrompt()` in `FargateCCExecutionService.cs` to use the system CLAUDE.md as a preamble:

```csharp
private string BuildPrompt(CCContextEnvelope envelope, string task, string systemClaudeMd)
{
    return $"""{systemClaudeMd}

---

# Per-User Context

## Identity
User ID: {envelope.UserId}
User Name: {envelope.UserDisplayName}

## Available Knowledge Bases
{(envelope.KbIds.Any() ? string.Join("\n", envelope.KbIds.Select(id => $"- {id}")) : "None assigned")}

## Enabled MCP Servers
{(envelope.EnabledMcpServers.Any() ? string.Join("\n", envelope.EnabledMcpServers.Select(s => $"- {s}")) : "None enabled")}

{(envelope.MemorySummary != null ? $"## Memory Context\n{envelope.MemorySummary}\n" : "")}
## Task Instructions
{envelope.TaskInstructions}

## Task
{task}
""";
}
```

Update `DispatchTaskAsync` to inject `_contextEnvelopeService.GetSystemClaudeMd()` into `BuildPrompt`.

Also inject `IContextEnvelopeService` into `FargateCCExecutionService` constructor.

### Registration

In `Program.cs`:
```csharp
builder.Services.AddScoped<IContextEnvelopeService, ContextEnvelopeService>();
```

## Acceptance Criteria
- [ ] `wwwroot/claude/CLAUDE.md` exists with system guardrails content
- [ ] `wwwroot/claude/rules/` contains forge-kb.md and environment.md
- [ ] `IContextEnvelopeService` interface defined
- [ ] `ContextEnvelopeService` implementation reads system CLAUDE.md from wwwroot/claude/
- [ ] `BuildEnvelopeAsync` returns correct userId, displayName, KbIds, EnabledMcpServers
- [ ] `FargateCCExecutionService.BuildPrompt` uses system CLAUDE.md as preamble
- [ ] `IContextEnvelopeService` registered in Program.cs as scoped
- [ ] dotnet build 0 errors

## Rules
- CSS variable rule not applicable (no UI changes)
- No hardcoded user IDs or system paths
- No Cognito references
- Use string type for IDs (not Guid)

## MANDATORY: Use Claude Code CLI
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2860-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

## ADO Comment (add after build)
Project: Fortress, ID: 2860
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: Implemented IContextEnvelopeService + ContextEnvelopeService, system CLAUDE.md + rules/ files, updated FargateCCExecutionService.BuildPrompt with system preamble. Build: SUCCEEDED.
```
