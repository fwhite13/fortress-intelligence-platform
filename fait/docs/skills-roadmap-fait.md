# FAIT Skills Roadmap

## Executive Summary

FAIT currently implements a partial plugin agent model (§6.1 in `agent-harness/harness-server.js`) that handles soul injection and per-agent MCP permission enforcement, but the Blazor-side API endpoint the harness calls (`GET /api/agents/{id}/soul`) does not exist, making the entire feature inert in production. The harness supports two execution paths — Bedrock ConverseStream and CC subprocess spawn — but skill-like context is propagated differently and inconsistently across them, with CC receiving a text brief assembled ad hoc while the Bedrock path uses structured toolConfig injection. The 2025 Anthropic Agent Skills standard (SKILL.md packages with progressive disclosure, executable scripts, and frontmatter-driven invocation control) is directly applicable to FAIT's architecture but has not been adopted. This roadmap provides a surface-by-surface gap analysis and a priority-ordered build plan to close those gaps, covering the main harness assistant, the CC subprocess environment, and the plug-in agent registration model.

---

## Current State

### Architecture Overview

FAIT is a two-tier system:

- **Tier 1** — Blazor Server web app (`fait/src/FortressAI.Web/`) handles auth, chat UI (`ChatView.razor`), conversation/task persistence in MySQL, and lifecycle management of per-user Fargate ECS tasks via `FargateUserAgentRuntime.cs`.
- **Tier 2** — Per-user Fargate harness (`fait/agent-harness/harness-server.js`) runs one ECS task per authenticated user. Each `/turn` request is classified by `classifyRequest()` and routed to either the **Bedrock ConverseStream path** (conversational) or the **CC spawn path** (task/file work). The CC spawn path invokes `claude --model sonnet --print --output-format stream-json --verbose --dangerously-skip-permissions` with a structured brief piped to stdin.

The `TurnRequest` record (`IUserAgentRuntime.cs`, line 57) carries `PluginAgentId`, `EnabledMcpSlugs`, `KbFlags`, `TaskMode`, `ForceTaskMode`, `PersistedWorkingFolderId`, and `IsScheduledTask`. `PluginAgentId` is present but never populated by `ChatView.razor`.

MCP infrastructure is in-process in the Blazor app: `DevOpsMcpAdapter.cs`, `M365McpAdapter.cs`, `McpHttpTransport.cs`, `McpRegistryService.cs`, `McpConnectionService.cs`. The harness receives enabled MCP slugs as a list and builds toolConfig dynamically for the Bedrock path; for the CC path it describes tools in a text section of the brief.

### What Works Today

- **Bedrock path tool dispatch** — The harness correctly dispatches `tool_use` events from Bedrock to named endpoints (`graph_*`, `ado_*`, `web_search`, `web_fetch`, `stitch_*`). Rate limiting, MCP token resolution, and auth delegation all work.
- **CC spawn context assembly** — The harness assembles a rich brief for CC: SOUL.md, USER.md, MEMORY.md/pgvector chunks, tool manifest section, KB context, workspace artifact list, Haiku-generated task brief, CLAUDE.md, and EXECUTE_DIRECTIVE prefix. This is effectively an ad hoc skill system already.
- **S3 workspace sync** — The dirty-file detection algorithm (`buildLocalSnapshot` + `findDirtyFiles`) correctly identifies and syncs changed files post-CC-task.
- **Plugin agent soul injection (§6.1)** — The harness-side code to replace SOUL.md with a fetched agent soul exists and is complete. Permission enforcement for MCP write operations when `pluginAgentId` is set also works.
- **pgvector semantic memory** — Per-user memory isolation exists (user_* schema per user in PostgreSQL), semantic search over MEMORY.md chunks is functional.
- **Intervention gate** — The TASK_PROCEED/TASK_HOLD Bedrock call before CC spawn works and prevents accidental task execution.
- **MCP server registry** — `McpServer` model in MySQL, `McpToolService` loading conversation-level enabled servers, per-server auth type handling all work.

### What's Missing

1. **`/api/agents/{id}/soul` endpoint** — The Blazor app has no controller implementing this. The harness's §6.1 code silently fails (non-200 response → falls back to SOUL.md). No `AgentsController.cs` exists in `FortressAI.Web/Controllers/`.
2. **Agent registry model** — No DB entity, no `AgentDefinition` equivalent, no service for defining named agents with soul content, allowed MCPs, KB scoping, or access control.
3. **`PluginAgentId` never passed from `ChatView.razor`** — The TurnRequest field is never populated in the main chat flow, making the entire plugin agent concept dead code from the UI perspective.
4. **Hardcoded tool manifest** — `buildToolManifestSection()` in `harness-server.js` has hardcoded knowledge of `m365`, `ado`, `brave`, `webfetch` slugs. Adding capabilities requires harness code changes.
5. **No per-agent KB scoping** — Harness uses whatever `KbFlags` the Blazor app sends (user-controlled conversation toggles). A plugin agent cannot specify its own KB ID.
6. **No conversation-level agent binding** — The `Conversation` model has no `AgentId`/`PluginAgentId` field. Agent identity cannot persist across turns of the same conversation.
7. **Scheduled tasks have no agent support** — `ScheduledTaskBackgroundService.cs` never populates `PluginAgentId` in the `TurnRequest`.
8. **No access control for agents** — No concept of per-user agent grants. Any user could invoke any agent if the UI existed.
9. **No agent selector UI** — `ChatView.razor` has no agent picker component.
10. **No agent-scoped memory** — pgvector memory is per-user globally; plugin agents share the same store as the personal assistant.
11. **Stitch tools are CC-only** — `stitch_*` tools work via the CC text context but are not in the Bedrock toolConfig injection, so the Bedrock path cannot invoke them.
12. **No SKILL.md infrastructure** — FAIT does not use the Anthropic Agent Skills standard for any capability packaging. The ad hoc brief assembly in the harness is functionally similar but not composable or versioned.

---

## Research Findings

### Anthropic's Skill Guidance

The Anthropic Agent Skills standard (released December 2025, adopted by Microsoft, OpenAI, Atlassian, GitHub) defines a skill as a **directory**, not a file. The canonical layout is:

```
skill-name/
├── SKILL.md              # Required: YAML frontmatter + instruction body (<500 lines)
├── references/           # Domain docs, API specs — loaded on demand, zero upfront cost
├── scripts/              # Executable Python/bash — only output enters context, not code
├── assets/               # Templates used in output
└── agents/               # Sub-agent definitions (analyzer.md, grader.md, etc.)
```

The **progressive disclosure model** has three layers:
1. YAML `name` + `description` only (~100 tokens) — always loaded at session start
2. Full `SKILL.md` body — loaded only when the LLM routes to this skill
3. `scripts/`, `references/`, `assets/` — loaded on demand via bash; script code never enters context

Key frontmatter fields relevant to FAIT:
- `disable-model-invocation: true` — only user can invoke (correct for deploy/commit skills with side effects)
- `user-invocable: false` — only Claude can invoke (good for background knowledge/domain context skills)
- `context: fork` — runs the skill as an isolated subagent; skill content becomes the subagent prompt, conversation history excluded
- `allowed-tools` — enforces least-privilege tool access per skill (e.g., `"Bash(git *) Read Grep"`)
- `arguments` — named positional args for `$name` substitution

The `!`command`` syntax pre-runs shell commands before Claude sees the skill content, enabling live context injection (current git diff, current workspace state) without Claude having to run the commands itself.

**Key insight for FAIT**: The CC spawn brief assembled in `harness-server.js` is already doing what SKILL.md is designed to do — it assembles domain context, tool manifests, and behavioral constraints. The gap is that it is monolithic, hardcoded, and not composable. Refactoring it to a skill-directory model gives versioning, per-agent customization, and progressive disclosure for free.

### Public Skill Patterns Worth Adopting

From the `anthropics/skills` repository and community libraries:

**1. Reference file pattern (`mcp-builder` skill)**
Large reference material (API schemas, policy manuals, underwriting guidelines) lives in `references/` and is loaded only when needed. For FAIT, the insurance domain knowledge currently baked into SOUL.md could move to `references/underwriting-guidelines.md`, `references/claims-procedures.md`, etc. Zero context cost until accessed.

**2. Script execution pattern (`pdf`, `docx`, `xlsx` skills)**
Deterministic operations (form extraction, data validation, document conversion) are implemented as Python scripts in `scripts/`. Claude calls `bash scripts/extract_fields.py document.pdf` and reads the JSON output — no code generation tokens, guaranteed consistency. FAIT's document processing use cases (ACORD form extraction, policy schedule parsing) are perfect candidates.

**3. `disable-model-invocation: true` for destructive operations**
The `psenger/ai-agent-skills` `git-commit-pr-message` skill gates itself to explicit `/command` invocation only. For FAIT, skills with write-side effects (KB write, scheduled task creation, file write) should carry this flag.

**4. Description quality as a routing signal**
Skill discovery is pure LLM reasoning against description strings — no classifier or embeddings. Poorly written descriptions reduce activation from ~90% to ~20%. FAIT's plugin agents need carefully written `description` fields that include both what the agent does AND explicit trigger phrases.

**5. `context: fork` for isolated specialist work**
When a specialist agent should not see conversation history (underwriter reviewing a submission), `context: fork` isolates the subagent. This is the correct pattern for FAIT's plug-in agents doing document review tasks where the prior chat context would be noise.

**6. Allowed-tools scoping per skill**
Each skill declares which tools it may use. A `PolicyLookupSkill` should only have `Read`, `Bash(psql *)` — not full filesystem access. This is the least-privilege pattern FAIT's plugin agent permission enforcement (§6.1 MCP allowlist) partially implements but does not extend to the CC path.

### Enterprise Architecture Analogues

**Semantic Kernel plugins** are structurally equivalent to FAIT's MCP tool + plugin agent model. The critical lesson: context-scoped plugin loading is mandatory above 10-20 tools — global registration degrades LLM function-selection accuracy measurably. FAIT must not register all MCP tools for all agents; each agent/skill gets only its declared tool set.

**AWS Bedrock Agent aliases** provide instant blue-green deployment and rollback via alias pointer updates. This is directly applicable to FAIT's agent definitions: rather than in-place updates to an agent's soul content, maintain versioned snapshots and an active-version pointer. A buggy soul can be rolled back without redeployment.

**The three-tier insurance agent hierarchy** (micro/macro/meta) maps cleanly to FAIT's current architecture:
- Micro agents — single-responsibility (FNOL extraction, policy lookup, fraud scoring)
- Macro agents — end-to-end workflow orchestration (claims handler, new business underwriter)
- Meta agents — compliance audit, human escalation routing

The meta tier is entirely absent from FAIT today and is the highest-priority gap for a regulated insurance use case.

**Semantic description versioning** — changing a plugin's description text in a way that shifts LLM routing behavior is a MAJOR (breaking) version change even if the underlying code is identical. FAIT's agent soul documents are effectively description+instructions; they must be versioned and changes reviewed for routing impact.

---

## Surface Analysis

### 1. Main Harness Assistant

#### Current Behavior

The main harness assistant is the default persona loaded when no `PluginAgentId` is present. Its identity comes from `assistants/SOUL.md` fetched from S3 at `s3://{bucket}/workspaces/{userId}/assistants/SOUL.md`. The context assembly in `harness-server.js` builds:

- SOUL.md content (system identity)
- USER.md (user profile)
- MEMORY.md or pgvector semantic chunks (relevant memory)
- Tool manifest section (`buildToolManifestSection()` — hardcoded to known slugs)
- KB context sections (Corp/Personal/Team Bedrock KBs)
- Task brief (Haiku-generated from history)
- Workspace artifact list
- CLAUDE.md workspace rules
- EXECUTE_DIRECTIVE prefix

For the Bedrock path, enabled MCP tools are injected as Bedrock `toolConfig`. For the CC path, this entire assembly becomes the stdin brief.

The tool manifest section is assembled in `buildToolManifestSection()` with hardcoded conditionals:
```javascript
if (enabledMcpSlugs?.some(s => ['m365','graph','microsoft365'].includes(s))) { ... }
if (enabledMcpSlugs?.some(s => ['ado','azdo','devops'].includes(s))) { ... }
```

New capabilities require editing this function directly.

#### Recommended Skill Patterns

**Pattern 1: Decompose SOUL.md into a skill directory**

Instead of a flat `SOUL.md` file per user at S3, adopt the skill directory model:

```
s3://bucket/workspaces/{userId}/assistants/
├── SOUL.md                          # Frontmatter + core identity (<200 lines)
├── references/
│   ├── underwriting-guidelines.md  # Loaded only during UW work
│   ├── claims-procedures.md        # Loaded only during claims work
│   └── company-kb-index.md         # Loaded when KB queries needed
└── scripts/
    └── workspace_snapshot.py       # Deterministic workspace state dump
```

The `SOUL.md` body stays under 200 lines (core identity, behavioral constraints, tool call policy). Domain reference material moves to `references/` and the harness loads specific files based on conversation context rather than injecting everything upfront.

**Pattern 2: Drive tool manifest from MCP server registry, not hardcoded slugs**

`buildToolManifestSection()` should query the harness's already-known `enabledMcpSlugs` list against a tool manifest registry (JSON on S3 or in the harness's local config), not hardcoded conditionals. Each MCP server's `McpServer` record in MySQL already has a `tool_manifest` JSON field — use it.

```javascript
// Instead of hardcoded conditionals:
const manifest = await buildDynamicToolManifest(enabledMcpSlugs, mcpToolSpecs);
```

**Pattern 3: Behavioral overlay via conversation-level skill flags**

Conversations should be able to activate domain-specific behavioral overlays without switching to a full plug-in agent. For example, a "UW mode" overlay that appends underwriting guidelines and activates the `PolicyScheduleParserSkill` when documents are attached. This maps to Anthropic's `user-invocable: false` pattern — the skill loads automatically when the LLM detects matching intent, without the user explicitly invoking it.

**Pattern 4: `disable-model-invocation: true` for destructive tool skills**

Skills that write to the KB, create scheduled tasks, or modify workspace files should only activate on explicit user invocation (`/write-kb`, `/schedule`, etc.), not via LLM auto-routing. This prevents the assistant from autonomously making persistent changes.

#### Concrete Examples for FAIT

**Example A: Policy Document Parser skill**

```
.claude/skills/policy-parser/
├── SKILL.md
│   ---
│   name: policy-parser
│   description: "Extracts structured fields from insurance policy PDFs and declarations pages.
│                 Use when the user uploads a policy document or asks to review policy terms."
│   user-invocable: false
│   allowed-tools: "Bash(python3 scripts/extract_policy.py *) Read"
│   ---
│   # Policy Parser
│   When a policy document is present in the workspace:
│   1. Run: python3 scripts/extract_policy.py {filename}
│   2. Review the extracted JSON for completeness
│   3. Report: policy number, insured name, coverage limits, effective dates, exclusions
├── references/
│   └── acord-field-map.md   # ACORD form field definitions — loaded on demand
└── scripts/
    └── extract_policy.py    # Returns structured JSON; never entered into context raw
```

**Example B: FNOL Triage skill**

```
.claude/skills/fnol-triage/
├── SKILL.md
│   ---
│   name: fnol-triage
│   description: "Processes First Notice of Loss submissions. Extracts claim details,
│                 validates coverage, and routes to the correct adjuster queue."
│   disable-model-invocation: true
│   allowed-tools: "Bash(python3 scripts/*) Read"
│   ---
│   # FNOL Triage
│   ## Current workspace state
│   !`python3 scripts/workspace_state.py`
│   [skill instructions follow]
├── references/
│   └── coverage-validation-rules.md
└── scripts/
    ├── workspace_state.py
    └── validate_coverage.py
```

**Example C: Dynamic tool manifest generation**

The harness should generate the tool manifest section from the MCP server registry:

```javascript
async function buildDynamicToolManifest(enabledMcpSlugs, mcpServerRows) {
  const sections = [];
  for (const slug of enabledMcpSlugs) {
    const server = mcpServerRows.find(s => s.slug === slug);
    if (!server?.tool_manifest) continue;
    const tools = JSON.parse(server.tool_manifest);
    sections.push(`### ${server.name} (${slug})\n${tools.map(t => `- **${t.name}**: ${t.description}`).join('\n')}`);
  }
  return sections.join('\n\n');
}
```

This replaces the hardcoded conditionals in `buildToolManifestSection()` and makes every MCP server in the registry self-describing.

---

### 2. CC Subprocess Environment

#### Current Spawn Context

When `classifyRequest()` returns true (or `ForceTaskMode=true`), the harness spawns CC:

```javascript
spawn('claude', ['--model', model, '--print', '--output-format', 'stream-json', '--verbose', '--dangerously-skip-permissions'])
```

The stdin brief assembled for CC contains (from `harness-server.js`):

1. `## Plugin Agent Identity` or `## About {assistantName}` (from SOUL.md or fetched agent soul)
2. `## User Identity` (email, userId)
3. `## About the User` (USER.md from S3)
4. `## Relevant Memory` (pgvector semantic search results or MEMORY.md)
5. `## Memory & Tool Guidance`
6. `## Available Tools` (`buildToolManifestSection()` output)
7. `## Context Awareness`
8. `## Tool Call Policy`
9. `## Recent Workspace Artifacts`
10. `## Recent Context` (Haiku-generated task brief from history)
11. KB context sections (Corp/Personal/Team)
12. `## Workspace Rules` (CLAUDE.md content)
13. `EXECUTE_DIRECTIVE:` prefix

Environment variables passed to CC subprocess:
- `CLAUDE_CODE_USE_BEDROCK=1`
- `AWS_DEFAULT_REGION`
- `AWS_DEFAULT_PROFILE`
- `HARNESS_BASE_URL` (harness HTTP endpoint for tool callbacks)
- `HARNESS_PLUGIN_AGENT_ID` (if `pluginAgentId` set)
- `HARNESS_INTERNAL_TOKEN`
- `FAIT_USER_ID`
- `WORKSPACE_DIR` (local path `/workspace/{userId}/{folderId}/`)
- `WORKSPACE_S3_PREFIX`
- `WORKSPACE_S3_BUCKET`
- `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`
- `CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1`
- `CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30`

CC is given `--dangerously-skip-permissions` and has no `.claude/settings.json` permission boundary enforcement.

#### Gaps in Subprocess Skill Propagation

**Gap 1: Tool call mechanism is text-only, no structured registration**

CC is told about available tools via the `## Available Tools` text section. CC must generate bash commands to call `curl -X POST $HARNESS_BASE_URL/tools/{toolName}` — it cannot validate tool schemas, argument types, or check availability. This is the "skills as prompts" anti-pattern. Any tool invocation error only surfaces at runtime when CC makes the HTTP call.

**Gap 2: No SKILL.md loading in CC subprocess**

CC spawned by FAIT does not have a `.claude/skills/` directory in its working directory (`WORKSPACE_DIR`). The workspace at `/workspace/{userId}/{folderId}/` contains only user files. CC has no access to FAIT-defined skills in the `context: fork` sense — it cannot progressively load reference files or execute bundled scripts.

**Gap 3: `--dangerously-skip-permissions` removes safety boundaries**

CC has no `allowedTools` or per-skill tool restrictions enforced by the CC runtime itself. The only restriction is what the brief says CC can do, plus the FAIT harness rejecting tool calls at the HTTP level when `pluginAgentId` permission checks are set. For the default (no plugin agent) case, CC has unrestricted tool access.

**Gap 4: HARNESS_PLUGIN_AGENT_ID is passed but CC ignores it**

CC receives `HARNESS_PLUGIN_AGENT_ID` as an env var but has no mechanism to load agent-specific skills or behaviors based on it. The agent identity is already injected via the brief, but there is no way for CC to selectively load agent-specific reference material or constrain itself to agent-specific tool sets.

**Gap 5: No workspace-level CLAUDE.md per agent**

FAIT injects a single `CLAUDE.md` (the workspace rules document from S3). There is no per-agent CLAUDE.md that could declare agent-specific constraints, behavioral rules, or allowed tool patterns. All agents running in CC get the same CLAUDE.md.

**Gap 6: Brief is monolithic and non-composable**

The entire brief is assembled in one function in `harness-server.js`. If a plugin agent needs different domain context (underwriting guidelines vs. claims procedures), the brief assembly must be modified in the harness code. There is no mechanism for a plugin agent definition to specify what additional context sections it needs.

**Gap 7: No skill discovery for CC**

CC cannot discover or invoke new skills beyond what was in the brief when it was spawned. If a new capability is added to the harness (a new MCP tool, a new script), CC has no way to know about it unless the brief assembly code is modified.

#### Recommended Improvements

**Improvement 1: Mount skill directories into the CC workspace**

Before spawning CC, the harness should copy or symlink the relevant skill directories into the workspace:

```javascript
// In the CC spawn preparation:
const skillsToMount = await resolveAgentSkills(pluginAgentId, enabledMcpSlugs);
for (const skill of skillsToMount) {
  await fs.cp(skill.sourcePath, path.join(workspaceDir, '.claude/skills', skill.name), { recursive: true });
}
```

Skills at `.claude/skills/` in the workspace are discovered by CC automatically per the Anthropic Agent Skills spec. This gives CC progressive disclosure without modifying the harness's brief assembly logic.

**Improvement 2: Provide a `settings.json` per agent for CC**

Instead of `--dangerously-skip-permissions`, use a per-agent `settings.json` that declares the allowed tool set:

```javascript
// Generate .claude/settings.json in workspace before spawn
const settings = {
  permissions: {
    allow: buildAllowList(agentDef.allowedTools),
    deny: buildDenyList(agentDef.deniedTools)
  }
};
await fs.writeFile(path.join(workspaceDir, '.claude/settings.json'), JSON.stringify(settings));
```

This moves tool restriction enforcement into the CC runtime itself, eliminating reliance on HTTP-level blocking alone. Note: this is compatible with running without `--dangerously-skip-permissions` once the allow list is correct.

**Improvement 3: Per-agent CLAUDE.md overlay**

Agent-specific behavioral rules should be composable with the base workspace CLAUDE.md:

```javascript
const baseClaude = await readFromS3(`workspaces/${userId}/assistants/CLAUDE.md`);
const agentClaude = agentDef?.claudeMdOverride ?? '';
const finalClaude = `${baseClaude}\n\n## Agent-Specific Rules\n${agentClaude}`;
```

**Improvement 4: Structured tool registration via env vars**

Pass tool schemas to CC via structured env vars rather than only via text brief:

```bash
FAIT_AVAILABLE_TOOLS='[{"name":"web_search","description":"...","input_schema":{"q":"string"}}]'
```

This allows future CC versions to validate tool call arguments before making HTTP requests.

**Improvement 5: Skill reference resolution in brief assembly**

Brief assembly should become composable. Each section of the CC brief should be a `BriefSection` with a source (S3 path, DB field, computed) and a `maxTokens` budget:

```javascript
const briefSections = [
  { id: 'identity', source: agentSoul ?? soulMd, maxTokens: 2000 },
  { id: 'user-context', source: userMd, maxTokens: 500 },
  { id: 'memory', source: memoryChunks, maxTokens: 1500 },
  { id: 'tools', source: dynamicToolManifest, maxTokens: 1000 },
  ...agentDef?.additionalSections ?? [],  // Agent-specific context additions
];
```

Plugin agent definitions can then declare additional brief sections (e.g., `{ id: 'underwriting-context', source: 's3://bucket/agents/uw-agent/context.md', maxTokens: 2000 }`) without requiring harness code changes.

---

### 3. Plug-in Agent Model

#### Current Evolution Spec Definition

The current plugin agent model in FAIT is defined by §6.1 in `harness-server.js`. What it does:

1. Accepts `pluginAgentId` on a `/turn` request
2. Fetches `GET /api/agents/{pluginAgentId}/soul` from the Blazor app (returns `{ content: string }`)
3. Uses soul content as `## Plugin Agent Identity` in system prompt, replacing SOUL.md
4. Passes `HARNESS_PLUGIN_AGENT_ID` env var to CC subprocess
5. Enforces MCP server allowlist and KB write restrictions at `/tools/:toolName`

This is **identity injection only**. There is no agent registry, no per-agent KB configuration, no per-agent MCP server set management, no access control, and no UI. The Blazor-side API (`/api/agents/{id}/soul`) does not exist.

The Cowork evolution spec (`/home/fredw/projects/fip/cowork/COWORK-SPECIALIST-AGENTS-SPEC.md`) defines the full `AgentDefinition` TypeScript type:

```typescript
interface AgentDefinition {
  id: string;
  name: string;
  description: string;
  icon: string;
  color: string;
  systemPromptPath: string;           // S3 key for soul content
  kbConfig: {
    kbId: string;                     // Bedrock KB ID
    dataSourceIds: string[];
    fallbackToCorpKb: boolean;
  };
  allowedMcpServers: string[];        // List of MCP slugs
  approvalOverrides: {
    require: string[];                // Tool names requiring explicit approval
    skip: string[];                   // Tool names where approval is bypassed
  };
  workspaceComponent: string;         // Blazor component name
}
```

This spec targets Cowork, not FAIT, but the pattern is directly applicable.

#### Recommended Architecture

**Data model: Add `PluginAgent` entity to FAIT's MySQL schema**

```csharp
// fait/src/FortressAI.Shared/Models/PluginAgent.cs
public class PluginAgent
{
    public string Id { get; set; } = Ulid.NewUlid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SoulS3Key { get; set; } = string.Empty;      // S3 path to soul content
    public string? KbId { get; set; }                           // Bedrock KB ID (nullable)
    public bool FallbackToCorpKb { get; set; } = true;
    public string AllowedMcpSlugsJson { get; set; } = "[]";    // JSON array of slugs
    public string ApprovalOverridesJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public bool RequiresExplicitGrant { get; set; } = true;     // false = all users
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Join table for access control
public class PluginAgentGrant
{
    public int Id { get; set; }
    public string PluginAgentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string GrantedBy { get; set; } = string.Empty;
}

// Conversation binding
// Add to existing Conversation model:
// public string? BoundPluginAgentId { get; set; }
```

**API: Implement `/api/agents` controller**

```csharp
// fait/src/FortressAI.Web/Controllers/AgentsController.cs
[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    // GET /api/agents/{id}/soul — called by harness (internal token auth)
    [HttpGet("{id}/soul")]
    public async Task<IActionResult> GetSoul(string id) { ... }

    // GET /api/agents — list agents accessible to current user
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListAgents() { ... }

    // POST /api/agents — admin: create agent
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> CreateAgent([FromBody] CreateAgentRequest req) { ... }
}
```

**Conversation binding: Add `BoundPluginAgentId` to Conversation**

```csharp
// In AppDbContext.cs migration: add column to Conversations table
// In ChatView.razor: when starting a conversation with an agent, set BoundPluginAgentId
// In FargateUserAgentRuntime.cs: read BoundPluginAgentId from conversation and populate TurnRequest.PluginAgentId
```

**Harness: Extend `/turn` request handling**

When `pluginAgentId` is set and the soul fetch succeeds, the harness should also:
1. Fetch the agent's `AllowedMcpSlugs` from `/api/agents/{id}` (full agent definition, not just soul)
2. Override `enabledMcpSlugs` with the agent's declared set (intersection with conversation-enabled set)
3. Fetch the agent's `KbId` and use it in KB retrieval instead of the conversation's KB flags
4. Expose the agent's `ApprovalOverrides` to the intervention gate

#### Install/Discovery Mechanism

**Phase 1: Admin-managed registry (low complexity, high value)**

Agents are defined by admins via a Blazor admin page (`/admin/agents`). Each agent has:
- Name, description, icon
- Soul content (uploaded as Markdown or entered inline, stored to S3 at `agents/{agentId}/SOUL.md`)
- Allowed MCP slugs (selected from the existing MCP server registry)
- KB configuration (Bedrock KB dropdown)
- Access control (all users, or explicit grant list)

This is a pure Blazor CRUD surface over the `PluginAgent` MySQL table. No harness changes required beyond implementing `/api/agents/{id}/soul`.

**Phase 2: S3-backed agent skill directories (medium complexity)**

Each agent gets a directory in S3:

```
s3://bucket/agents/{agentId}/
├── SOUL.md                        # Core identity (<200 lines)
├── references/
│   ├── domain-guidelines.md       # Loaded on demand
│   └── tool-reference.md
└── scripts/
    └── workspace_init.py          # Run before CC spawn to prep workspace
```

The harness `GetSoul` logic is extended to read the full directory listing and pass `reference` and `script` file keys along with soul content. These are mounted into the CC workspace before spawn.

**Phase 3: Versioned agent definitions with alias pattern (high complexity, production-grade)**

Each agent definition is stored with a version number. An `ActiveVersion` pointer (analogous to Bedrock alias) can be updated without changing the agent ID. Conversations pin to the agent version active at conversation creation time, preventing silent behavior changes from agent soul updates mid-conversation.

```csharp
public class PluginAgentVersion
{
    public int Id { get; set; }
    public string PluginAgentId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string SoulS3Key { get; set; } = string.Empty;
    public string AllowedMcpSlugsJson { get; set; } = "[]";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**Discovery: Conversation-level agent selector**

In `ChatView.razor`, add an agent selector to the conversation start flow:
1. User opens new conversation
2. If the user has agent grants, a picker appears (default: personal assistant)
3. Selected agent ID is written to `Conversation.BoundPluginAgentId`
4. All subsequent turns in that conversation automatically carry `PluginAgentId`

For UX, the selector should show agent name, description, and an icon — same data already in the `PluginAgent` entity.

---

## Gap Analysis vs Evolution Spec

| Gap | Harness Side | Blazor Side | Priority |
|-----|-------------|-------------|----------|
| `/api/agents/{id}/soul` endpoint missing | Complete | **Missing** | P0 — blocks all plugin agent functionality |
| `PluginAgentId` never passed from ChatView | N/A | **Missing** | P0 — no UI hook into existing harness code |
| No `PluginAgent` DB entity or registry | N/A | **Missing** | P0 — prerequisite for all other items |
| `Conversation.BoundPluginAgentId` absent | N/A | **Missing** | P1 — enables persistent agent binding |
| Tool manifest hardcoded in harness | **Hardcoded** | N/A | P1 — blocks new capabilities without code deploy |
| No per-agent KB scoping | Incomplete | Missing | P1 — agents always use user KB flags |
| No CC skill directory mounting | Missing | N/A | P2 — improves CC task quality per agent |
| CC uses `--dangerously-skip-permissions` | Present | N/A | P2 — safety concern for plugin agents |
| No per-agent CLAUDE.md overlay | Missing | N/A | P2 — behavioral constraint isolation |
| No agent-scoped memory namespace | Missing | Missing | P3 — nice to have, complex |
| Scheduled tasks ignore `PluginAgentId` | Missing | Missing | P3 — scheduled agent tasks |
| No access control for agents | Missing | Missing | P1 — security gate before GA |
| No agent admin UI | N/A | Missing | P1 — prerequisite for non-dev agent management |
| Stitch tools not in Bedrock toolConfig | Missing | N/A | P2 — limits Bedrock path capabilities |
| No agent version aliasing | N/A | Missing | P3 — production-grade, deferred |
| No brief section composability | Missing | N/A | P2 — extensibility without code changes |

The Cowork specialist agents spec is fully designed but not implemented on either side. FAIT's plugin agent concept is a subset of the Cowork spec, partially implemented in the harness but entirely absent from the Blazor app. The minimum viable implementation (P0 items) is three pieces: DB entity, AgentsController, and ChatView wiring.

---

## Public Skill Examples Worth Studying

**1. `anthropics/skills/skills/mcp-builder/`**
Demonstrates the `references/` pattern for large external documentation. Directly applicable to FAIT's pattern of domain-specific agent context (underwriting guidelines, ACORD form specs, claims procedures). MCP builder's `reference/node_mcp_server.md` (29 KB) costs zero tokens until Claude needs it — the same budget saving applies to FAIT's insurance domain docs.

**2. `anthropics/skills/skills/skill-creator/`**
Shows the full multi-file skill with sub-agents (`agents/analyzer.md`, `agents/comparator.md`, `agents/grader.md`) and a Python evaluation harness (`scripts/run_eval.py`, `scripts/aggregate_benchmark.py`). FAIT should adopt this evaluation pattern: build test scenarios for each plugin agent, run them with/without the agent soul to measure improvement, and use the results to optimize soul content iteratively.

**3. `psenger/ai-agent-skills/skills/git-commit-pr-message/`**
Best example of `disable-model-invocation: true` + `allowed-tools` scoping in a production skill. The `allowed-tools: "Bash(git *) Bash(gh *) Read Grep Glob"` pattern is directly applicable to FAIT's document processing skills where the agent should only have read access to workspace files and execute specific scripts.

**4. `anthropics/skills/skills/pdf/` and `anthropics/skills/skills/docx/`**
Anthropic describes these as "actively used in production." They demonstrate the script execution pattern for document processing — exactly what FAIT needs for policy PDF extraction and ACORD form parsing. These skills' `scripts/` directories should be studied for the pattern of having the script return structured JSON that Claude interprets, rather than having Claude generate the extraction code on each run.

**5. `terrylica/cc-skills` plugin versioning pattern**
The `plugin.json` with `semver` + `semantic-release` automation is worth adopting for FAIT's agent registry. The metadata manifest (separate from `SKILL.md`) that carries version, author, and dependencies is the right pattern for enterprise governance of agent definitions.

---

## Priority-Ordered Recommendations

### P0 — Unblock Plugin Agents (Required to make §6.1 work at all)

**1. Implement `/api/agents/{id}/soul` endpoint**
- **File:** Create `fait/src/FortressAI.Web/Controllers/AgentsController.cs`
- **Complexity:** Low (2-4 hours)
- **Dependency:** Needs `PluginAgent` entity in DB (item 2)
- **Description:** The harness already calls this endpoint. Without it, every plugin agent turn silently falls back to SOUL.md. The endpoint must accept the internal auth token (`X-Internal-Token` header) used by all other internal harness→Blazor calls. Return `{ content: string }` JSON.

**2. Create `PluginAgent` and `PluginAgentGrant` DB entities**
- **Files:** `fait/src/FortressAI.Shared/Models/PluginAgent.cs`, EF migration in `FortressAI.Web/Data/`
- **Complexity:** Low (3-5 hours)
- **Dependency:** None
- **Description:** Minimum viable schema: Id, Name, Description, SoulS3Key, AllowedMcpSlugsJson, KbId (nullable), FallbackToCorpKb, IsActive, RequiresExplicitGrant. Add `PluginAgentGrant` join table for user access control. Register in `AppDbContext.cs`.

**3. Wire `PluginAgentId` through `ChatView.razor` → `TurnRequest`**
- **Files:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`, `FargateUserAgentRuntime.cs`
- **Complexity:** Low (2-3 hours)
- **Dependency:** Items 1 and 2
- **Description:** `ChatView.razor` must read the conversation's `BoundPluginAgentId` (or a session-level agent selection) and pass it in `TurnRequest`. `FargateUserAgentRuntime.cs` must forward it to the harness `/turn` call.

### P1 — Make Plugin Agents Useful

**4. Add `BoundPluginAgentId` to `Conversation` model**
- **Files:** `fait/src/FortressAI.Shared/Models/Conversation.cs`, EF migration
- **Complexity:** Low (1-2 hours)
- **Dependency:** Item 2
- **Description:** Nullable FK to `PluginAgent.Id`. Enables persistent agent binding across turns. The agent identity is re-specified on every turn today (or rather, never specified from the UI) — binding it to the conversation makes it durable.

**5. Implement agent admin UI (`/admin/agents`)**
- **Files:** New Blazor page in `fait/src/FortressAI.Web/Components/Pages/Admin/`
- **Complexity:** Medium (1-2 days)
- **Dependency:** Items 1 and 2
- **Description:** CRUD for `PluginAgent` records. Allows admins to create agents, edit soul content, configure allowed MCP slugs (multi-select from existing MCP server registry), set KB ID, and manage user grants. Soul content should be editable inline (Markdown textarea) with a preview, stored to S3 by the save action.

**6. Implement per-agent MCP slug enforcement in harness at `/turn` level**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Low (3-5 hours)
- **Dependency:** Item 1 (harness can fetch full agent definition alongside soul)
- **Description:** Extend the `/api/agents/{id}` endpoint to return the full agent definition including `allowedMcpSlugs`. In the harness, when `pluginAgentId` is set, intersect the request's `enabledMcpSlugs` with the agent's `allowedMcpSlugs`. This prevents a plugin agent from accessing MCP tools it was not configured for, even if the conversation has those tools enabled.

**7. Implement per-agent KB routing in harness**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Medium (4-6 hours)
- **Dependency:** Items 1 and 6
- **Description:** When `pluginAgentId` is set and the agent has a `kbId`, replace the harness's KB retrieval call (currently uses conversation-level KB flags → corp/personal/team KBs) with a call to the agent's dedicated Bedrock KB. This is the single biggest quality improvement for specialist agents — a UW agent querying underwriting guidelines rather than the user's personal KB is a fundamentally different and much more useful experience.

**8. Replace hardcoded `buildToolManifestSection()` with registry-driven generation**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Low-Medium (4-6 hours)
- **Dependency:** Existing `McpServer.tool_manifest` field in DB (already present)
- **Description:** The harness receives `MCP_TOOL_SPECS` from the Blazor app on session start. Use this data to build the tool manifest section dynamically rather than the current hardcoded slug conditionals. This makes every MCP server in the registry self-describing without harness code changes.

**9. Implement agent access control check in `FargateUserAgentRuntime.cs`**
- **File:** `fait/src/FortressAI.Web/Services/FargateUserAgentRuntime.cs`
- **Complexity:** Low (2-3 hours)
- **Dependency:** Items 1 and 2
- **Description:** Before sending a `TurnRequest` with `PluginAgentId`, verify the current user has a `PluginAgentGrant` for that agent (or `RequiresExplicitGrant = false`). Return an error if not. This is the security gate that prevents unauthorized agent invocation.

### P2 — Improve CC Subprocess Quality

**10. Mount agent skill directories into CC workspace before spawn**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Medium (1-2 days)
- **Dependency:** Items 1 and 2; agent S3 skill directory structure
- **Description:** Before CC spawn, check S3 at `agents/{agentId}/` for a skill directory. If found, sync it to `workspaceDir/.claude/skills/` so CC discovers it automatically per the Agent Skills spec. This enables progressive disclosure in the CC context without modifying brief assembly code.

**11. Generate per-agent `.claude/settings.json` for CC tool scoping**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Medium (4-6 hours)
- **Dependency:** Item 6 (agent allowed tool list)
- **Description:** Write a `settings.json` to `workspaceDir/.claude/settings.json` before CC spawn, declaring the agent's allowed tool set. This moves tool restriction into the CC runtime rather than relying solely on HTTP-level blocking at the harness `/tools/:toolName` endpoint. Consider whether `--dangerously-skip-permissions` should be retained once this is in place.

**12. Add per-agent CLAUDE.md overlay support**
- **File:** `fait/agent-harness/harness-server.js`; agent S3 structure
- **Complexity:** Low (2-3 hours)
- **Dependency:** Item 10
- **Description:** If `agents/{agentId}/CLAUDE.md` exists in S3, append its content (clearly labeled as Agent-Specific Rules) to the base workspace CLAUDE.md in the brief. This allows agents to declare behavioral constraints (e.g., "Never write to files outside the claims/ subdirectory") without changing the shared workspace CLAUDE.md.

**13. Make brief assembly composable (BriefSection pattern)**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Medium-High (2-3 days)
- **Dependency:** Item 6 (agent definition includes additional sections)
- **Description:** Refactor the monolithic brief assembly into a list of `BriefSection` objects with `{ id, content, maxTokens }`. Agent definitions can include additional sections (e.g., UW guidelines, claims procedures). Token budgeting is enforced per section. This is a significant internal refactor but enables agent-specific context without harness code changes for each new agent.

**14. Add stitch tools to Bedrock toolConfig injection**
- **File:** `fait/agent-harness/harness-server.js`
- **Complexity:** Medium (4-8 hours)
- **Dependency:** None
- **Description:** `stitch_*` tools currently only work via the CC text context. Implement stitch tool invocation in the Bedrock path's tool dispatch loop, routing `tool_use` events for `stitch_*` names to the stitch child process. This makes stitch capabilities available in conversational (non-task) turns.

### P3 — Production-Grade and Advanced

**15. Add agent-scoped memory namespace in pgvector**
- **Files:** `fait/agent-harness/harness-server.js`, PostgreSQL schema
- **Complexity:** High (1 week)
- **Dependency:** Items 1-4
- **Description:** Add an `agent_id` column to the pgvector memory table (or create a parallel `agent_memory_*` schema per user per agent). When `pluginAgentId` is set, semantic memory search should query the agent-scoped namespace first, falling back to user global memory. This isolates specialist agent knowledge from personal assistant memory.

**16. Extend `ScheduledTaskBackgroundService` to support agent-bound tasks**
- **File:** `fait/src/FortressAI.Web/Services/ScheduledTaskBackgroundService.cs`
- **Complexity:** Low-Medium (4-6 hours)
- **Dependency:** Items 1-4 (conversation.BoundPluginAgentId)
- **Description:** When dispatching a scheduled task, check if the task's conversation has a `BoundPluginAgentId` and include it in the `TurnRequest`. This enables scheduled tasks to run as specialist agents (e.g., a daily UW pipeline that runs as the Underwriter agent).

**17. Implement versioned agent definitions with alias pattern**
- **Files:** New `PluginAgentVersion` entity, updated `AgentsController.cs`
- **Complexity:** High (1-2 weeks)
- **Dependency:** Items 1-5
- **Description:** Each save to an agent definition creates a new `PluginAgentVersion` record. An `ActiveVersion` pointer on `PluginAgent` is updated separately. Conversations pin to the version active at creation time. Rollback is an ActiveVersion update. Matches the Bedrock Agent alias pattern for instant rollback without redeployment.

**18. Build agent skill evaluation harness**
- **Files:** New Python scripts in `fait/agent-harness/eval/` or a dedicated eval project
- **Complexity:** High (1-2 weeks)
- **Dependency:** Items 1-9
- **Description:** Implement the two-Claude development loop from Anthropic's guidance: define test scenarios (input, expected behavior) for each agent, run them against the current soul content, score results, iterate. The `anthropics/skills/skills/skill-creator/scripts/run_eval.py` is the reference implementation. This is the quality gate that prevents soul regressions from shipping.

---

## Proposed WI-Ready Items

---

**WI-FAIT-AGENTS-01: Create PluginAgent DB entity and EF migration**

**Scope:** `fait/src/FortressAI.Shared/Models/PluginAgent.cs`, `PluginAgentGrant.cs`, EF migration in `FortressAI.Web/Data/Migrations/`

**Acceptance Criteria:**
- `PluginAgent` table created in Aurora MySQL with columns: Id (ULID string), Name, Description, SoulS3Key, AllowedMcpSlugsJson, KbId (nullable), FallbackToCorpKb, IsActive, RequiresExplicitGrant, CreatedAt, UpdatedAt
- `PluginAgentGrant` table created with columns: Id (int), PluginAgentId (FK), UserId (FK to Users), GrantedAt, GrantedBy
- Both entities registered in `AppDbContext.cs` with correct relationships
- EF migration runs cleanly: `dotnet build` passes with 0 errors in `fait/src/`
- No existing functionality broken (no schema conflicts)

---

**WI-FAIT-AGENTS-02: Implement AgentsController with soul endpoint**

**Scope:** New file `fait/src/FortressAI.Web/Controllers/AgentsController.cs`

**Acceptance Criteria:**
- `GET /api/agents/{id}/soul` returns `{ "content": "<soul markdown>" }` for a valid agent ID
- Soul content is read from S3 at the agent's `SoulS3Key`
- Request authenticated via `X-Internal-Token` header (same mechanism as `/api/internal/*` endpoints)
- Returns HTTP 404 if agent not found or `IsActive = false`
- Returns HTTP 403 if internal token invalid
- `GET /api/agents` returns list of agents accessible to the authenticated user (respects `RequiresExplicitGrant` and `PluginAgentGrant` table)
- `POST /api/agents` creates a new agent (admin role only); soul content is written to S3; S3 key stored in `SoulS3Key`
- `dotnet build` passes with 0 errors

---

**WI-FAIT-AGENTS-03: Wire PluginAgentId from conversation through ChatView to TurnRequest**

**Scope:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`, `FargateUserAgentRuntime.cs`, `Conversation` model (add `BoundPluginAgentId` column)

**Acceptance Criteria:**
- `Conversation.BoundPluginAgentId` nullable string column added via EF migration
- When `ChatView.razor` constructs a `TurnRequest`, it reads `Conversation.BoundPluginAgentId` and populates `TurnRequest.PluginAgentId`
- `FargateUserAgentRuntime.cs` forwards `PluginAgentId` in the HTTP body to the harness `/turn` endpoint
- Access control check: `FargateUserAgentRuntime.cs` verifies the current user has a `PluginAgentGrant` for the bound agent before sending (returns error if not authorized)
- Verified end-to-end: a conversation bound to a test agent (with a soul document in S3) correctly uses that soul in the harness response instead of SOUL.md
- `dotnet build` passes with 0 errors

---

**WI-FAIT-AGENTS-04: Admin UI for agent management (/admin/agents)**

**Scope:** New Blazor pages in `fait/src/FortressAI.Web/Components/Pages/Admin/`

**Acceptance Criteria:**
- `/admin/agents` page lists all `PluginAgent` records (name, description, active status)
- Agent detail/edit page allows: editing name/description, editing soul content (Markdown textarea), selecting allowed MCP slugs from existing `McpServer` records (multi-select), entering Bedrock KB ID, toggling `RequiresExplicitGrant`
- Save action writes soul content to S3 and updates the DB record
- User grants page allows admin to add/remove `PluginAgentGrant` records for a given agent
- Pages are protected by admin authorization policy (consistent with other admin pages in the codebase)
- `dotnet build` passes with 0 errors

---

**WI-FAIT-AGENTS-05: Agent selector UI in new conversation flow**

**Scope:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` or a new conversation-start component

**Acceptance Criteria:**
- When starting a new conversation, if the user has access to one or more plugin agents, an agent picker is displayed
- Picker shows agent name, description, and icon (if configured); default option is "Personal Assistant" (no agent binding)
- Selecting an agent sets `Conversation.BoundPluginAgentId` on the new conversation record
- Personal Assistant option results in null `BoundPluginAgentId` (existing behavior unchanged)
- Agent picker is hidden if user has no agent grants
- Selection is shown in conversation header for the lifetime of the conversation (agent badge)
- `dotnet build` passes with 0 errors

---

**WI-FAIT-AGENTS-06: Replace hardcoded buildToolManifestSection() with registry-driven generation**

**Scope:** `fait/agent-harness/harness-server.js`

**Acceptance Criteria:**
- `buildToolManifestSection()` function removed or replaced
- New `buildDynamicToolManifest(enabledMcpSlugs, mcpToolSpecs)` function generates the Available Tools section from the `MCP_TOOL_SPECS` data already received from the Blazor app on session start
- Each MCP server in the enabled set contributes its tool list from its `tool_manifest` JSON field
- For MCP servers with no `tool_manifest` (legacy), the existing hardcoded fallback text is preserved as a fallback for backward compatibility
- Harness integration tests (if any exist) pass; manual verification that tool manifest section in CC brief correctly reflects enabled servers
- No regressions: M365, ADO, brave, webfetch tool descriptions still appear when those servers are enabled

---

**WI-FAIT-AGENTS-07: Per-agent KB routing in harness**

**Scope:** `fait/agent-harness/harness-server.js`; extend `/api/agents/{id}` to return full agent definition

**Acceptance Criteria:**
- `GET /api/agents/{id}` (no `/soul` suffix) returns full agent definition JSON including `kbId`, `fallbackToCorpKb`, `allowedMcpSlugs`
- Harness fetches full agent definition when `pluginAgentId` is set (one additional fetch alongside the soul fetch)
- When agent has a non-null `kbId`, harness KB retrieval calls target that KB ID instead of the conversation's KB flags
- When agent `fallbackToCorpKb = true` and the agent KB returns no results, harness falls back to corp KB retrieval
- When no plugin agent is set, existing KB retrieval behavior is unchanged
- Verified: a test conversation with an agent bound to a dedicated Bedrock test KB returns results from that KB, not the user's personal KB

---

**WI-FAIT-AGENTS-08: Mount agent skill directories into CC workspace before spawn**

**Scope:** `fait/agent-harness/harness-server.js`; agent S3 directory structure defined and documented

**Acceptance Criteria:**
- S3 path convention documented: `agents/{agentId}/SOUL.md`, `agents/{agentId}/references/`, `agents/{agentId}/scripts/`, `agents/{agentId}/CLAUDE.md`
- Before CC spawn, harness lists `agents/{agentId}/` in S3; if any files exist beyond `SOUL.md`, syncs them to `workspaceDir/.claude/skills/{agentId}/`
- CC subprocess discovers the skill directory automatically (no brief changes required)
- Agent-specific `CLAUDE.md`, if present, is appended to the base workspace CLAUDE.md in the brief under `## Agent-Specific Rules`
- If no agent skill directory exists in S3, behavior is unchanged (backward compatible)
- Verified: a test agent with a `references/guidelines.md` file has that file visible to CC in the workspace at `.claude/skills/{agentId}/references/guidelines.md`

---

**WI-FAIT-AGENTS-09: Generate per-agent .claude/settings.json for CC tool scoping**

**Scope:** `fait/agent-harness/harness-server.js`

**Acceptance Criteria:**
- When `pluginAgentId` is set and the agent has a non-empty `allowedMcpSlugs` list, harness generates `.claude/settings.json` in `workspaceDir/.claude/` before CC spawn
- `settings.json` contains `{ "permissions": { "allow": [...], "deny": [...] } }` derived from the agent's `allowedMcpSlugs` and the harness's knowledge of which bash patterns each MCP slug corresponds to
- If no plugin agent is set, no settings.json is generated (existing `--dangerously-skip-permissions` behavior preserved for personal assistant mode)
- Decision documented: whether to remove `--dangerously-skip-permissions` when settings.json is present (recommended yes, but needs testing)
- Verified: a CC spawn for a plugin agent configured with only M365 slugs cannot make ADO tool calls (CC rejects the bash command before the HTTP request reaches the harness)

---

**WI-FAIT-AGENTS-10: Extend ScheduledTaskBackgroundService to support agent-bound conversations**

**Scope:** `fait/src/FortressAI.Web/Services/ScheduledTaskBackgroundService.cs`

**Acceptance Criteria:**
- When polling for due scheduled tasks, `ScheduledTaskBackgroundService` reads the `BoundPluginAgentId` from the task's associated conversation
- If non-null, populates `TurnRequest.PluginAgentId` when calling `runtime.SendTurnAsync`
- Access control check: verifies the conversation owner still has a grant for the bound agent before dispatching; if grant revoked, logs a warning and dispatches without `PluginAgentId` (falls back to personal assistant)
- No change to existing scheduled task behavior when conversation has no bound agent
- `dotnet build` passes with 0 errors
