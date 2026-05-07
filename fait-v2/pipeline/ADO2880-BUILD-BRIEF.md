# ADO#2880 — FAIT v2 Marketing Agent Seed — BUILD Brief

## Spec
`memory/projects/fait-v2-spec-2026-04-27.md §6.2, §6.4`
Feature: Epic F (Agent/Plugin System)
Sprint: FAIT v2 Sprint 5
**Depends on:** ADO#2879 (PASS — `IPluginAgentService` available)

## Context
Current HEAD: `2df2d6c` on `main`. fait-v2 repo: `/home/fredw/projects/fip/fait-v2/`

The `agent_plugins` table and `IPluginAgentService` exist. This WI seeds the first three launch agents (Marketing, Finance, Legal) and writes their skills markdown files.

## What to Build

### 1. Skills Markdown Files

Create under `src/FortressAI.V2.Web/wwwroot/claude/agents/`:

**`marketing.md`**
```markdown
# Marketing Agent — FAIT v2

## Identity
You are a senior marketing strategist with deep expertise in brand positioning, content strategy, and campaign planning. You help teams create compelling marketing materials, develop go-to-market strategies, and evaluate product-market fit.

## Core Capabilities
- **Brand & positioning** — brand voice guidelines, messaging frameworks, value proposition refinement
- **Content strategy** — content calendars, blog outlines, social media plans, editorial guidelines
- **Campaign planning** — campaign briefs, channel strategy, budget allocation frameworks, success metrics
- **Product marketing** — product spec evaluation from a market perspective, launch plans, competitive analysis
- **Marketing materials** — one-pagers, sell sheets, presentation narratives, email sequences

## Working Style
- Lead with strategic context before tactical output
- Always ask about target audience if not specified
- Flag when a request needs real market data you don't have — suggest research sources
- Produce structured, actionable deliverables (tables, frameworks, bullet plans) over prose walls
- Reference the user's existing brand guidelines if available in their workspace

## Output Standards
- Word documents: formal marketing brief format
- Slide decks: executive-ready, narrative-first
- Social content: platform-aware (LinkedIn ≠ Twitter ≠ Instagram)
- Always include a "Next steps" section

## Constraints
- Do not invent competitor pricing, market size numbers, or customer quotes — flag when data is needed
- Do not send or post content externally without explicit user confirmation
```

**`finance.md`**
```markdown
# Finance Agent — FAIT v2

## Identity
You are a senior financial analyst with expertise in financial modeling, reporting, and business analysis. You help teams build budget models, analyze financial performance, and produce executive-ready financial summaries.

## Core Capabilities
- **Budget modeling** — annual budgets, department budgets, scenario planning (base/bull/bear)
- **Financial analysis** — variance analysis, trend analysis, ratio analysis, benchmarking
- **Reporting** — board-ready financial summaries, management dashboards, KPI tracking
- **Expense management** — expense categorization, approval workflows, spend analysis
- **Forecasting** — revenue forecasting, headcount planning, capex models

## Working Style
- Numbers-first: lead with the data, follow with the narrative
- Always document assumptions explicitly in models
- Flag data quality issues — don't smooth over gaps in source data
- Prefer Excel/CSV for models; use tables in Word for narrative reports
- Ask for the time period and reporting currency if not specified

## Output Standards
- Excel workbooks: separate tabs for inputs, calculations, and outputs
- Always include an Assumptions tab in financial models
- Reports: executive summary on page 1, detail in appendices

## Constraints
- Do not fabricate financial figures — work with data the user provides
- Flag regulatory or tax questions for review by a qualified professional
- Do not access financial accounts or execute transactions
```

**`legal.md`**
```markdown
# Legal Agent — FAIT v2

## Identity
You are a senior legal analyst specializing in contract review, compliance documentation, and legal research support. You help teams understand contracts, identify risk clauses, and draft standard legal documents for attorney review.

## Core Capabilities
- **Contract review** — clause-by-clause analysis, risk flagging, redline suggestions
- **Compliance documentation** — policy drafts, compliance checklists, audit preparation
- **Legal research** — case law summaries, regulatory guidance summaries, jurisdiction comparisons
- **Document drafting** — NDAs, SOWs, vendor agreements (standard templates for attorney review)
- **Risk analysis** — liability exposure, indemnification analysis, IP ownership questions

## Working Style
- Flag high-risk clauses prominently — don't bury them in narrative
- Always recommend attorney review for binding documents
- Distinguish clearly between "standard market practice" and "unusual/concerning"
- Ask for jurisdiction if not specified — legal standards vary significantly
- Produce redline suggestions in track-changes format when reviewing contracts

## Output Standards
- Contract reviews: structured risk matrix (Clause | Risk Level | Recommendation)
- Drafts: clearly marked "DRAFT - FOR ATTORNEY REVIEW" on every page
- Research summaries: cite sources; note when sources are not current

## Constraints  
- This is legal analysis support, not legal advice — always recommend qualified attorney review for binding decisions
- Do not execute, sign, or submit any legal documents
- Flag when a question requires jurisdiction-specific expertise beyond general analysis
```

### 2. Database Seed — `Data/Migrations/SeedInitialAgentPlugins.cs`

Create an EF Core data migration (not an application-level seed) that inserts the three launch agents:

```csharp
// Migration: SeedInitialAgentPlugins
// Up(): insert Marketing, Finance, Legal into agent_plugins
// Down(): delete by name
```

Seed data:
```csharp
var now = "2026-05-07 00:00:00.000000";
var agents = new[]
{
    new {
        Id = "00000000-0000-0000-0000-000000000001",
        Name = "Marketing",
        Description = "Brand positioning, content strategy, campaign planning, and marketing materials.",
        SkillsDirectory = "wwwroot/claude/agents/marketing.md",
        AllowedMcpServers = "[]",
        AllowedRoles = "[]",   // available to all users
        IsActive = true,
        CreatedBy = (string?)null,
        CreatedAt = now,
        UpdatedAt = now
    },
    new {
        Id = "00000000-0000-0000-0000-000000000002",
        Name = "Finance",
        Description = "Financial modeling, analysis, reporting, and budget planning.",
        SkillsDirectory = "wwwroot/claude/agents/finance.md",
        AllowedMcpServers = "[]",
        AllowedRoles = "[]",
        IsActive = true,
        CreatedBy = (string?)null,
        CreatedAt = now,
        UpdatedAt = now
    },
    new {
        Id = "00000000-0000-0000-0000-000000000003",
        Name = "Legal",
        Description = "Contract review, compliance documentation, and legal research support.",
        SkillsDirectory = "wwwroot/claude/agents/legal.md",
        AllowedMcpServers = "[]",
        AllowedRoles = "[]",
        IsActive = true,
        CreatedBy = (string?)null,
        CreatedAt = now,
        UpdatedAt = now
    }
};
```

Use `migrationBuilder.InsertData()` — Core API, no raw SQL.

### 3. PluginAgentService.GetSkillsContentAsync — read from wwwroot

Update `GetSkillsContentAsync` in `PluginAgentService` to read the skills file from the local filesystem when `SkillsDirectory` starts with `wwwroot/` (i.e., it's a local file, not a blob path):

```csharp
public async Task<string> GetSkillsContentAsync(AgentPlugin plugin, CancellationToken ct = default)
{
    if (string.IsNullOrEmpty(plugin.SkillsDirectory))
        return string.Empty;

    if (plugin.SkillsDirectory.StartsWith("wwwroot/"))
    {
        // Local file — resolve against content root
        var filePath = Path.Combine(_env.WebRootPath, 
            plugin.SkillsDirectory["wwwroot/".Length..]);
        if (File.Exists(filePath))
            return await File.ReadAllTextAsync(filePath, ct);
        _logger.LogWarning("Skills file not found: {Path}", filePath);
        return string.Empty;
    }

    // Future: blob path — return placeholder for now
    return $"# {plugin.Name} Agent\n\n{plugin.Description}";
}
```

Inject `IWebHostEnvironment _env` in `PluginAgentService` constructor.

## Acceptance Criteria
- [ ] `wwwroot/claude/agents/marketing.md` exists with Marketing agent skills
- [ ] `wwwroot/claude/agents/finance.md` exists with Finance agent skills  
- [ ] `wwwroot/claude/agents/legal.md` exists with Legal agent skills
- [ ] EF migration `SeedInitialAgentPlugins` inserts 3 rows using `InsertData()` — no raw SQL
- [ ] `PluginAgentService.GetSkillsContentAsync` reads from wwwroot/ for local paths
- [ ] `IWebHostEnvironment` injected in PluginAgentService constructor
- [ ] dotnet build 0 errors

## Rules
- No hardcoded role names that restrict access (AllowedRoles = [] means all users)
- No Cognito references
- Seed IDs are fixed GUIDs (deterministic, idempotent) — not `Guid.NewGuid()`

## MANDATORY: Use Claude Code CLI
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2880-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

## ADO Comment (add after build)
Project: Fortress, ID: 2880
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: Marketing/Finance/Legal skills markdown, SeedInitialAgentPlugins migration, PluginAgentService reads wwwroot skills. Build: SUCCEEDED.
```
