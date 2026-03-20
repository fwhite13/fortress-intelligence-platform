# FIP Cowork — Specialist Agents Architecture Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-19 (updated 2026-03-19 — corrections from Fred)  
**Status:** Strategic design — pre-sprint  
**Context:** FAM internal tooling. Fred wants the specialist agent model as the FIP Cowork plugin architecture.  
**Phase 1 roster (Fred-defined):** Marketing, Financial Analyst, Researcher, Insurance Underwriter, Risk Manager, Tech Writer, Graphic Designer  
**Audience:** FAM employees only — internal tooling, not client-facing  
**Research:** `claude-cowork-plugins-research.md` (Bruce, March 2026)  
**Existing codebase:** `fip/cowork/` — Redis, Claude Agent SDK, SSE streaming, S3, approval gates

---

## 1. The Strategic Bet

Anthropic's Cowork Plugin model has a fundamental ceiling: **it dies when the laptop sleeps.** It's personal productivity software masquerading as enterprise automation. Session-based, single-user, desktop-required.

FIP Cowork's specialist agents are a **genuinely different product**:
- Server-side, always-on (ECS Fargate — no "app must be open" constraint)
- Multi-user (each specialist agent is shared across the team, not per-person)
- Purpose-built workspace UI per agent — not a generic chat window
- Agent-scoped KB (FORGE) — the agent knows everything about its domain
- MCP-connected to the real data sources (HubSpot, Klaviyo, Ahrefs, etc.)
- Approval-gated execution for consequential actions (our existing gate model)

**The positioning:** Claude Cowork Plugins are slash commands in a chat window for one person. FIP Cowork Specialist Agents are enterprise-grade, always-running AI staff who know Fortress AM's business, have access to Fortress AM's data, and never lose context between conversations.

This is what Fred's description maps to exactly: "Nick is our Project Manager plugin. Phil is our tech writer plugin." Those OpenClaw agents ARE the reference model — persistent, context-rich, purposeful. We're building that concept for Fortress AM as a productized, multi-user platform.

---

## 2. Architecture Overview

### 2.1 How Specialist Agents Extend FIP Cowork

The existing FIP Cowork architecture:

```
CoworkWeb (Blazor Server)
    ↕ HTTP + SSE
CoworkAgent (Node.js/TypeScript)
    ↕ Claude Agent SDK
    ↕ FORGE KB (ForgeClient)
    ↕ S3 (file outputs)
    ↕ Redis (task queue, pub/sub)
```

Specialist agents add a **second tier** alongside the existing generic task runner:

```
CoworkWeb (Blazor Server)
  ├── Generic Chat (existing)          → CoworkAgent /tasks
  └── Specialist Agent Workspace UI   → CoworkAgent /agents/:agentId/tasks
          ↕
CoworkAgent (Node.js/TypeScript)
  ├── Generic task runner (existing)   → runner.ts
  └── Specialist agent runners         → agents/<agentId>/runner.ts
          ↕ Agent-specific system prompt
          ↕ Agent-scoped FORGE KB
          ↕ Agent-specific MCP servers
          ↕ Claude Agent SDK (shared)
```

**Key architectural principle:** Specialist agents are NOT separate services. They run inside the same `CoworkAgent` container, using the same Claude Agent SDK + Redis + S3 infrastructure. What changes per agent:
1. The system prompt (agent identity, domain expertise, behavioral rules)
2. The FORGE KB context injected before each task (agent's own KB, not the generic corp KB)
3. The approved MCP servers available to that agent
4. The workspace UI component that renders in CoworkWeb

### 2.2 Agent Definition Format

Each specialist agent is a **typed configuration + code bundle** registered in a central manifest:

```typescript
// fip/cowork/src/CoworkAgent/src/agents/registry.ts

export interface AgentDefinition {
  id:          string;           // 'marketing' | 'analyst' | 'techwriter' | ...
  name:        string;           // 'Marketing Agent'
  description: string;           // Short description for agent selector UI
  icon:        string;           // MudBlazor icon name
  color:       string;           // CSS hex for agent accent color

  // System prompt for this agent (replaces generic FAIT Cowork system prompt)
  systemPromptPath: string;      // relative to agents/<id>/

  // FORGE KB IDs for this agent's knowledge base
  kbConfig: {
    kbId:         string;        // Agent's own FORGE KB ID
    dataSourceIds: string[];     // Data sources in that KB
    fallbackToCorpKb?: boolean;  // Also search corp KB? (default: true)
  };

  // MCP server definitions this agent is allowed to use
  // Subset of all configured MCP servers; agent cannot use servers not listed here
  allowedMcpServers: string[];

  // Optional: tool approval overrides (which tools require human approval)
  approvalOverrides?: {
    require: string[];    // tool names that always require approval
    skip:    string[];    // tool names that skip approval even if destructive
  };

  // Name of the Blazor component in CoworkWeb that renders this agent's workspace
  workspaceComponent: string;    // 'MarketingWorkspace' | 'AnalystWorkspace' | ...
}
```

**Registered agents:**

```typescript
// fip/cowork/src/CoworkAgent/src/agents/registry.ts

export const AGENT_REGISTRY: Record<string, AgentDefinition> = {
  marketing: {
    id:          'marketing',
    name:        'Marketing Agent',
    description: 'Content creation, campaign planning, brand voice, competitive analysis, performance reporting',
    icon:        'Campaign',
    color:       '#7C3AED',
    systemPromptPath:  'agents/marketing/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_MARKETING_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_MARKETING_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  true,
    },
    allowedMcpServers:   ['hubspot', 'klaviyo', 'ahrefs', 'notion', 'slack'],
    approvalOverrides: {
      require: ['klaviyo_send_campaign', 'hubspot_create_deal'],
      skip:    [],
    },
    workspaceComponent:  'MarketingWorkspace',
  },

  'financial-analyst': {
    id:          'financial-analyst',
    name:        'Financial Analyst',
    description: 'Investment memos, earnings analysis, financial models, research library',
    icon:        'ShowChart',
    color:       '#0369A1',
    systemPromptPath:  'agents/financial-analyst/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_FINANCIAL_ANALYST_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_FINANCIAL_ANALYST_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  true,
    },
    allowedMcpServers:   ['brave_search', 'notion', 'slack'],
    approvalOverrides:   { require: [], skip: [] },
    workspaceComponent:  'FinancialAnalystWorkspace',
  },

  researcher: {
    id:          'researcher',
    name:        'Researcher',
    description: 'Deep research synthesis, competitive intelligence, fact-finding, literature review',
    icon:        'Search',
    color:       '#0891B2',
    systemPromptPath:  'agents/researcher/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_RESEARCHER_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_RESEARCHER_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  true,
    },
    allowedMcpServers:   ['brave_search', 'notion', 'slack'],
    approvalOverrides:   { require: [], skip: [] },
    workspaceComponent:  'ResearcherWorkspace',
  },

  underwriter: {
    id:          'underwriter',
    name:        'Insurance Underwriter',
    description: 'Submission evaluation, carrier appetite, underwriting checklists, eligibility assessment',
    icon:        'Policy',
    color:       '#DC2626',
    systemPromptPath:  'agents/underwriter/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_UNDERWRITER_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_UNDERWRITER_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  false,  // Underwriting is domain-specific; corp KB not relevant
    },
    allowedMcpServers:   ['slack'],
    approvalOverrides:   { require: [], skip: [] },
    workspaceComponent:  'UnderwriterWorkspace',
  },

  'risk-manager': {
    id:          'risk-manager',
    name:        'Risk Manager',
    description: 'Enterprise risk register, compliance calendar, regulatory monitoring, risk assessments',
    icon:        'Shield',
    color:       '#B45309',
    systemPromptPath:  'agents/risk-manager/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_RISK_MANAGER_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_RISK_MANAGER_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  true,
    },
    allowedMcpServers:   ['brave_search', 'notion', 'slack'],
    approvalOverrides:   { require: [], skip: [] },
    workspaceComponent:  'RiskManagerWorkspace',
  },

  techwriter: {
    id:          'techwriter',
    name:        'Technical Writer',
    description: 'Platform docs, API reference, runbooks, style guide enforcement, release notes',
    icon:        'Article',
    color:       '#059669',
    systemPromptPath:  'agents/techwriter/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_TECHWRITER_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_TECHWRITER_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  false,  // Technical docs are self-contained; corp KB adds noise
    },
    allowedMcpServers:   ['notion', 'slack', 'github'],
    approvalOverrides:   { require: [], skip: [] },
    workspaceComponent:  'TechWriterWorkspace',
  },

  designer: {
    id:          'designer',
    name:        'Graphic Designer',
    description: 'Brand asset management, design briefs, visual direction, image generation',
    icon:        'Palette',
    color:       '#7C3AED',
    systemPromptPath:  'agents/designer/system-prompt.md',
    kbConfig: {
      kbId:              process.env.COWORK_DESIGNER_KB_ID ?? '',
      dataSourceIds:     (process.env.COWORK_DESIGNER_DS_IDS ?? '').split(','),
      fallbackToCorpKb:  false,  // Brand assets are domain-specific
    },
    allowedMcpServers:   ['slack', 'notion'],
    approvalOverrides:   { require: [], skip: [] },
    workspaceComponent:  'DesignerWorkspace',
  },
};
```

### 2.3 Specialist Agent Task Routing

New route in `CoworkAgent`:

```
POST /agents/:agentId/tasks       → creates a specialist task (same as /tasks but agent-scoped)
GET  /agents/:agentId/tasks       → list tasks for this agent (all users)
GET  /agents/:agentId/tasks/:id/stream → SSE stream (same protocol as generic tasks)
POST /agents/:agentId/tasks/:id/approve
POST /agents/:agentId/tasks/:id/reject
GET  /agents                      → list available agent definitions (for UI agent selector)
```

The `agentId` is looked up in `AGENT_REGISTRY`. If not found → 404.

Task metadata in Redis gains an `agentId` field:
```typescript
interface TaskMeta {
  userId:    string;
  userEmail: string;
  prompt:    string;
  agentId?:  string;      // null for generic tasks
  createdAt: string;
  // ...existing fields
}
```

### 2.4 Agent-Scoped FORGE KB Architecture

**Decision: one FORGE KB per specialist agent.** Not shared KB with agent-scoped data sources.

Rationale:
- Different agents need different chunking strategies (marketing brand docs = medium chunks; analyst research = large chunks; tech writer code/API docs = large chunks)
- FORGE KB metadata filtering would add complexity with minimal benefit
- Each agent's KB is independently indexable, auditable, and maintainable by domain experts
- Cost is low: additional KB = ~$0.10/GB/month

**KB structure:**

| Agent | KB Name | Contents | Who maintains |
|-------|---------|----------|---------------|
| Marketing | `cowork-marketing` | Brand guidelines, campaign history, personas, competitive intel, product messaging, past content examples | Marketing team uploads via FAIT admin |
| Analyst | `cowork-analyst` | Market research reports, investment memos, competitor profiles, industry data | Tom/analysts upload |
| Tech Writer | `cowork-techwriter` | Style guides, API docs, process templates, doc standards | Engineering uploads |
| Ops | `cowork-ops` | Process playbooks, vendor docs, SOP library, workflow templates | Ops team uploads |

**KB injection in agent runner** (same pattern as existing `forgeClient.ts`):

```typescript
// agents/marketing/runner.ts extends base runner
// Before task execution, inject agent KB context:

const agentKbContext = await queryForgeContextCached(
  params.prompt,
  agentDef.kbConfig.kbId,        // marketing KB, not corp KB
  agentDef.kbConfig.dataSourceIds
);

// Then optionally also query corp KB
let corpKbContext = '';
if (agentDef.kbConfig.fallbackToCorpKb) {
  corpKbContext = await queryForgeContextCached(params.prompt, CORP_KB_ID);
}

// Combined context injected into system prompt
const agentContext = [agentKbContext, corpKbContext].filter(Boolean).join('\n\n---\n\n');
```

### 2.5 Agent Access Control (First-Class Feature)

Agent visibility is **role/permission-gated with admin oversight**. This is not bolted on later — it's part of the data model from Sprint 3.

**Rules:**
- No user sees any specialist agent by default
- A system admin explicitly assigns agents to users or roles
- An agent is only visible and usable by users who have been granted access
- Admins manage this via a Cowork admin UI (`/admin/agent-access`)

**Data model** — new Redis hash per agent (extended to DB if Cowork gains relational storage in Sprint 4+):

```typescript
// Redis key: cowork:agent-access:<agentId>  →  Set of userId strings
// Redis key: cowork:user-agents:<userId>     →  Set of agentId strings (inverse index)

interface AgentAccessGrant {
  agentId:   string;   // 'marketing' | 'financial-analyst' | etc.
  userId:    string;   // FAM user ID
  grantedBy: string;   // userId of admin who granted
  grantedAt: string;   // ISO timestamp
}
```

**API routes added to CoworkAgent:**
```
GET  /admin/agent-access              → list all grants (admin only)
POST /admin/agent-access              → grant { agentId, userId }
DELETE /admin/agent-access            → revoke { agentId, userId }
GET  /agents                          → returns only agents the calling user can access
```

**`GET /agents`** is filtered by calling user's access set — the CoworkWeb `AgentIndex.razor` uses this endpoint. Users literally cannot see agents they haven't been granted.

**Admin access:** Users with `IsAdmin` claim from FAIT auth automatically see all agents and can manage grants. No separate Cowork admin role needed.

**Phase 1 bootstrap:** Admin manually grants access to pilot users via the API (or a seed script) until the admin UI is built in Sprint 4.

**Reference from Section 8:** Access control is resolved — this section implements that decision.

### 2.6 Workspace UI Architecture (CoworkWeb)

The Blazor CoworkWeb app gains an agent workspace section alongside the existing generic chat:

**Navigation change:**

```
Left nav (MainLayout):
  ├── My Tasks       (existing generic chat)
  ├── ─── Agents ───
  ├── Marketing      → /agents/marketing
  ├── Research       → /agents/analyst
  ├── Tech Writer    → /agents/techwriter
  └── Operations     → /agents/ops
```

**Component structure:**

```
CoworkWeb/Components/
├── Pages/
│   ├── Tasks/            (existing)
│   └── Agents/
│       ├── AgentIndex.razor          → /agents (lists all agents)
│       ├── AgentPage.razor           → /agents/:agentId (container)
│       └── Workspaces/
│           ├── MarketingWorkspace.razor
│           ├── AnalystWorkspace.razor
│           ├── TechWriterWorkspace.razor
│           └── OpsWorkspace.razor
└── Shared/
    ├── AgentChatPanel.razor          (shared chat UI — same across all agents)
    └── AgentTaskHistory.razor        (task list for an agent)
```

**`AgentPage.razor` layout:**

```
┌─────────────────────────────────────────────────────────┐
│  [Agent Name + Icon]   [Status: Online]                  │
├───────────────────────────────┬─────────────────────────┤
│                               │                         │
│   Agent Workspace Panel       │   Chat Interface        │
│   (purpose-built for domain)  │                         │
│                               │   [Task history]        │
│   e.g. for Marketing:         │   [Active task stream]  │
│   - Campaign tracker          │   [Input box]           │
│   - Content calendar          │                         │
│   - Brand asset library       │                         │
│   - Performance dashboard     │                         │
│                               │                         │
└───────────────────────────────┴─────────────────────────┘
```

The workspace panel is agent-specific (loaded dynamically based on `agentId`). The chat interface is shared — same `AgentChatPanel.razor` for all agents.

---

## 3. Marketing Agent — Full Spec

This is the Phase 1 reference implementation. Marketing Agent first because: clearest scope, most immediate business value for FAM.

### 3.1 What the Marketing Agent Owns

The Marketing Agent maintains proactive awareness of:

| Domain | What It Tracks |
|--------|----------------|
| **Brand** | Brand voice guide, messaging pillars, visual identity rules, terminology do/don't list |
| **Campaigns** | Active campaigns, content calendar, channel mix, status (draft/live/complete) |
| **Content** | Published content library (blog, email, social), performance by piece |
| **Competitive** | Competitor positioning, messaging, content cadence, recent moves |
| **Performance** | Campaign metrics pulled from HubSpot/Klaviyo at request time |
| **Audience** | Target personas, segmentation definitions, ICP (ideal customer profile) |

Unlike Cowork's marketing plugin (which loads fresh context every chat session), the Marketing Agent's KB persists all of this across sessions. Any FAM marketing team member can ask "what's our current positioning vs. [competitor]?" and get an answer grounded in documented intel — not a hallucination.

### 3.2 Marketing Workspace UI — Screen Design

```
/agents/marketing
```

**Left panel — Marketing Workspace:**

```
Campaign Tracker
┌────────────────────────────────────────┐
│ Q1 Product Launch    [Live]  [HubSpot]│
│ Email Nurture Series [Draft] [Klaviyo]│
│ Blog Content Push    [Active]          │
│ + New Campaign                        │
└────────────────────────────────────────┘

Content Calendar (this week)
┌─────┬──────────────────────────────────┐
│ Mon │ Blog: AI in wealth management    │
│ Wed │ Email: Product update newsletter │
│ Fri │ LinkedIn: case study post        │
└─────┴──────────────────────────────────┘

Brand Quick Reference
┌────────────────────────────────────────┐
│ Voice: [Authoritative] [Approachable]  │
│ Avoid: [Jargon] [Passive voice]        │
│ Colors: ■ #1B2E4B ■ #C8A96E           │
└────────────────────────────────────────┘

Performance Snapshot (last 30d)
[Pull from HubSpot] button
│ Email open rate:  28.4%               │
│ Click rate:        4.1%               │
│ Blog traffic:    +12% MoM             │
```

**Right panel — Chat:**

```
[Task history list — previous 10 tasks]

[Active SSE stream when task running]
  Step 1: Retrieving brand guidelines...
  Step 2: Drafting email subject lines...
  Step 3: Checking against brand voice...
  ✅ Complete. Output: email-draft.html

[Input box]
> [type here...] [Send] [Attach file]
```

**Workspace components (Blazor):**

```
MarketingWorkspace.razor
  ├── CampaignTracker.razor         (Sprint 1: manual list; Sprint 2: HubSpot pull)
  ├── ContentCalendar.razor         (Sprint 1: static display; Sprint 2: live)
  ├── BrandQuickRef.razor           (Sprint 1: loaded from agent KB; static)
  └── PerformanceSnapshot.razor     (Sprint 1: button → agent task; Sprint 2: live)
```

### 3.3 Marketing Agent KB Contents

**What goes in the `cowork-marketing` FORGE KB:**

| Document | Source | Update frequency |
|----------|--------|-----------------|
| Brand voice guide | Marketing team uploads | Quarterly or on change |
| Messaging pillars / positioning | Marketing team uploads | Semi-annual or on change |
| Target persona profiles | Marketing team uploads | Annual or on change |
| Content library index | Generated from published content audit | Monthly |
| Competitive positioning snapshot | Analyst research / marketing uploads | Monthly |
| Product feature descriptions | Uploads from PM/marketing | On product update |
| Past campaign briefs (sanitized) | Marketing team uploads | Per campaign |
| Email templates (approved) | Marketing team uploads | On change |
| SEO keyword list / content map | Generated from Ahrefs export | Quarterly |

**S3 prefix for KB documents:** `kb-docs/cowork-agents/marketing/<filename>`

**Who uploads:** Marketing team via FAIT admin Dev KB upload section (same `KbTier.Developer` UI built in `DEVOPS-KB-SPEC.md`, but a different KB ID). Or direct S3 upload + manual FORGE sync.

### 3.4 MCP Connectors for Marketing Agent

**Phase 1 (stub interfaces, no live integration):**
- HubSpot — campaign data, contact lists, email analytics
- Klaviyo — email campaign performance, subscriber lists
- Ahrefs — keyword research, site audit, competitor analysis
- Slack — post outputs to `#marketing` channel

**Phase 2 (live integrations):**
All four wired to real API credentials. Uses existing `mcporter` pattern (read `CC-MEMORY-SPEC.md` for MCP server pattern) or direct MCP server via `@hubspot/mcp-server` npm package etc.

**Phase 1 approach:** Each MCP connector is a stub. HubSpot integration focuses on **marketing automation** (campaign workflows, email sequences, triggers) — not contact/deal CRM management. When a user asks "what's our email open rate?", the agent produces a formatted template and asks the user to paste the data export — or analyzes an uploaded export. Phase 2 wires live APIs.

**Connector configuration in `CoworkAgent`:**

```typescript
// agents/marketing/mcp-servers.ts

export const MARKETING_MCP_SERVERS = {
  hubspot: {
    command: 'npx',
    args:    ['@hubspot/mcp-server'],
    env:     { HUBSPOT_API_KEY: process.env.HUBSPOT_API_KEY ?? '' },
  },
  klaviyo: {
    command: 'npx',
    args:    ['klaviyo-mcp-server'],
    env:     { KLAVIYO_API_KEY: process.env.KLAVIYO_API_KEY ?? '' },
  },
  ahrefs: {
    command: 'npx',
    args:    ['@ahrefs/mcp-server'],
    env:     { AHREFS_API_KEY: process.env.AHREFS_API_KEY ?? '' },
  },
  slack: {
    // Existing Slack MCP from main CoworkAgent config (shared)
    command: 'npx',
    args:    ['@slack/mcp-server'],
    env:     { SLACK_BOT_TOKEN: process.env.SLACK_BOT_TOKEN ?? '' },
  },
};
```

Phase 1: if API key is empty, the MCP server isn't started. Agent operates KB-only mode. Task descriptions tell the agent which tools are unavailable and to work with provided data instead.

### 3.5 Marketing Agent System Prompt

Stored at `agents/marketing/system-prompt.md`:

```markdown
# Marketing Agent — Fortress Asset Management

You are the Marketing Agent for Fortress Asset Management. You are a senior marketing specialist
embedded in the Fortress AM Cowork platform.

## Your Domain
You own: content creation, campaign planning, brand voice enforcement, competitive analysis,
SEO, email marketing, and performance reporting for Fortress AM.

## Your Knowledge
You have access to Fortress AM's brand guidelines, messaging pillars, target personas, campaign
history, and competitive intelligence via your knowledge base. Always use this knowledge when
generating content or analysis. Do not improvise brand voice or positioning — use the documented
definitions in your KB.

## What You Do
- Draft blog posts, emails, social content, press releases, case studies, landing page copy
- Build campaign briefs with objectives, channel mix, content calendar, KPIs
- Review content against Fortress AM's brand voice and style guidelines
- Research competitors and generate positioning analysis or messaging comparisons
- Design email sequences (nurture flows, onboarding, event-driven drips)
- Build performance reports from provided data or live connector data

## Behavioral Rules
1. Brand voice FIRST: Every piece of content must reflect Fortress AM's documented voice — authoritative, approachable, clear. Flag deviations from the style guide.
2. No fabrication: Do not invent campaign metrics, competitor data, or persona attributes. Use only what's in your KB or provided by the user. If you don't know, say so.
3. Ask before sending: Any action that posts to live channels (HubSpot email send, Klaviyo campaign launch, Slack post to external channels) requires explicit user approval.
4. Output format: Written deliverables go in Markdown (.md) files. HTML for landing pages / email previews. CSV for data/metrics. Always name files clearly.
5. Attribution: When citing KB knowledge (brand guide, persona, competitor data), mention where it comes from ("Per your brand guide...").

## Output Quality Standard
Fortress AM serves institutional investors and sophisticated clients. Content must:
- Be precise and credible — no buzzword-laden vagueness
- Lead with value, not product
- Pass the "would a CFO take this seriously?" test

## Available Tools
{AVAILABLE_TOOLS_INJECTED_AT_RUNTIME}
```

### 3.6 What Marketing Agent Does That Anthropic's Plugin Cannot

| Capability | Anthropic Marketing Plugin | FIP Marketing Agent |
|-----------|---------------------------|---------------------|
| **Always-on KB access** | Loads fresh each session | Persistent FORGE KB — always current |
| **Cross-session memory** | No (each session starts fresh) | Yes — KB contains campaign history, past decisions |
| **Multi-user collaboration** | Single user only | FAM marketing team shares the same agent |
| **Background task** | Requires desktop app open | Runs on ECS — close laptop, task keeps running |
| **Approval gates** | No equivalent | Explicit approval for consequential actions (email sends) |
| **Fortress-specific brand voice** | Requires manual setup per session | Pre-loaded in persistent KB |
| **Audit trail** | No | Full task audit log per agent |
| **Data residency** | Anthropic cloud (required) | Fortress AM's own AWS (Bedrock) |
| **Long-running tasks** | Limited by session | Queue-backed; survives connection drops |

---

## 4. Agent Roster — Phase 1 (7 Agents, Fred-Defined)

### 4.1 Marketing Agent
**Priority: 1 (Tom's ask) — Reference Implementation**  
Full deep-dive in Section 3 above. Summary:
- **Owns:** Brand voice, campaigns, content calendar, competitive intel, performance data
- **Workspace:** Campaign tracker, content calendar, brand quick-ref, performance snapshot
- **KB:** Brand guidelines, personas, messaging pillars, campaign history, SEO keyword map
- **MCP:** HubSpot, Klaviyo, Ahrefs, Slack
- **Edge over generic chat:** Never loses brand context; pre-loaded with Fortress AM voice; shares campaign history across team; approval-gated before any live send

---

### 4.2 Financial Analyst Agent
**Priority: 2**

> ⚠️ **TODO — Pending input from Tom and Caleb.** Infrastructure (KB, workspace shell) is specced here, but specific capabilities, coverage domains, and output formats are TBD until Tom/Caleb define scope and requirements. Do not over-implement this agent before that input is received.

Provisional description: financial analysis support for FAM's investment team. Drafts investment memos, analyzes fund data, synthesizes earnings, models scenarios, and maintains the research library.

**What it owns/tracks:**
- Active research coverage list (which securities, funds, or sectors are under analysis)
- Investment memo drafts and finalized memos
- Earnings call transcripts and summaries
- Fund performance data snapshots
- Financial model templates (DCF, comps, scenario analysis)
- Regulatory filing abstracts (10-K, 10-Q, 8-K)
- Internal investment thesis library

**Workspace UI:**
- **Coverage list panel** — active securities/funds under review, with last-updated timestamp
- **Memo workspace** — in-progress memo drafts; click to continue in chat
- **Earnings tracker** — upcoming earnings for covered names, recent transcript summaries
- **Quick actions** — "Start new memo", "Summarize 10-K", "Run comps"
- **Task history** — recent completed analysis tasks with output links

**KB contents (`cowork-financial-analyst`):**
- Investment policy statement and mandate constraints
- Internal valuation frameworks and model templates (uploaded as PDFs/XLSX descriptions)
- Past investment memos (sanitized, key reasoning preserved)
- Sector background documents (e.g., trucking insurance market, municipal bond structures)
- Regulatory context (SEC, NAIC, relevant guidance)
- Risk tolerance parameters and concentration limits

**MCP connectors:**
- `brave_search` — live company/market research, news
- `notion` — memo library and draft storage
- `slack` — distribute analysis to investment team channels
- Future Phase 2: Bloomberg/Refinitiv data feed MCP (if licensed)

**System prompt focus:** CFA-level rigor. Every claim sourced. Quantified estimates flagged as estimates. Model assumptions stated explicitly. Output format: investment memo structure (executive summary, thesis, risks, valuation, recommendation). Never speculate on price targets without flagging as illustrative.

**What makes it better than generic chat:**
- KB carries institutional context (mandate constraints, past thesis history, sector knowledge) — no re-explaining every session
- Memo template library ensures consistent output format across all analysts
- Coverage tracker surfaces what's in-flight without manual status checks
- Multi-user: multiple analysts can query the same KB and pick up each other's memo drafts

---

### 4.3 Researcher Agent
**Priority: 3**

Deep-research specialist. Long-form research synthesis, literature review, competitive intelligence, and fact-finding for FAM. Handles research requests that require multi-source synthesis and rigorous sourcing — not surface-level search.

**What it owns/tracks:**
- Active research request queue
- Research brief library (completed research with key findings)
- Source library (curated reference documents)
- Research methodology templates (market sizing, competitive landscape, literature review)
- Open research questions / outstanding requests

**Workspace UI:**
- **Research queue** — open requests (from any user), status (queued/in-progress/done), requestor name
- **Brief library** — completed research briefs, searchable by topic/date
- **Source uploader** — drag-and-drop PDFs/docs to add to the agent's KB
- **New research form** — structured form: topic, depth (quick/standard/deep), output format (brief/memo/slides outline)
- **Task history** — recent completed research tasks

**KB contents (`cowork-researcher`):**
- Reference library: industry reports, market studies, academic papers (uploaded by team)
- Research methodology guides (primary vs. secondary research, source quality ratings)
- Competitive intelligence snapshots (updated quarterly by the researcher or analysts)
- Glossaries: insurance industry, financial markets, asset management terminology
- Past research briefs (institutional memory)

**MCP connectors:**
- `brave_search` — primary live web research tool
- `notion` — brief storage and sharing
- `slack` — deliver research results to requestor channels
- Future Phase 2: PubMed or specialized research databases via MCP (for regulated-industry research)

**System prompt focus:** Epistemic rigor. Distinguish between confirmed facts, consensus estimates, and minority views. Rate source reliability. Explicitly state what was NOT found. Avoid narrative-building that outruns the evidence. Output format: executive summary + sourced findings + confidence level per claim + limitations.

**What makes it better than generic chat:**
- Accumulated research library means agent learns Fortress AM's domain over time — less starting-from-scratch per request
- Multi-user research queue: anyone can submit a request, researcher handles asynchronously
- Source library in KB means agent can cross-reference new findings against prior research without re-uploading
- Depth modes (quick/standard/deep) give users control over turnaround vs. thoroughness tradeoff

---

### 4.4 Insurance Underwriter Agent
**Priority: 4**

> ⚠️ **TODO — KB seeding pending.** Fred will locate underwriting guidelines. Mark KB seeding as blocked until Fred provides UW guidelines docs. Infrastructure can be built; agent is not useful until KB is seeded.

Specialist underwriting agent for FAM's affinity program operations. Evaluates submissions, checks coverage eligibility, applies underwriting rules, and supports the underwriting workflow across affinity program clients.

**What it owns/tracks:**
- Active submission queue (opportunities in UNDERWRITING_PREP stage — can read from FAM OS)
- Underwriting rule library (eligibility criteria, loss ratio triggers, coverage limits)
- Carrier appetite guides (what each carrier will and won't write)
- Submission templates (trucking app, ACORD forms guidance)
- Underwriting checklists (what's required before RouteToMarket)
- Past submission decisions and their outcomes (win/loss/modification)

**Workspace UI:**
- **Submission queue** — active submissions requiring underwriting review, sorted by urgency/signal
- **Rule browser** — searchable underwriting rules by program, carrier, coverage type
- **Submission review panel** — paste or upload a submission; agent produces eligibility assessment
- **Checklist tracker** — for a given opportunity, what's complete vs. missing
- **Carrier matrix** — appetite table: for this risk profile, which carriers are viable?
- **Quick actions** — "Evaluate submission", "Check carrier appetite", "Generate underwriting narrative"

**KB contents (`cowork-underwriter`):**
- Underwriting guidelines per program (trucking, specialty, etc.)
- Carrier appetite guides (uploaded from carrier portals / producer manuals)
- Loss ratio analysis and historical performance by program
- Coverage definitions and exclusion language
- Regulatory requirements by state (surplus lines rules, admitted carrier requirements)
- ACORD form guides and submission data requirements
- Past submission outcomes with key decision factors

**MCP connectors:**
- `slack` — notify producers and internal team of underwriting decisions
- Future Phase 2: Epic/AMS MCP (pull existing policy data for renewal underwriting)
- Future Phase 2: FAM OS API (read opportunity submission data directly)

**System prompt focus:** Precision in coverage language — never paraphrase policy language loosely. All eligibility decisions must cite the specific rule. Flag ambiguous situations for human review rather than guessing. Output: structured eligibility assessment with YES/NO/CONDITIONAL per coverage item + rationale + required documentation.

**What makes it better than generic chat:**
- Carrier appetite matrix in KB means agent can answer "will Great West write this risk?" without the producer manually consulting 10 carrier portals
- Underwriting rules are codified and persistent — agent applies the same standards every time, not whatever Claude guesses
- Integration path to FAM OS submission queue makes this the cognitive co-pilot for the ER during UNDERWRITING_PREP stage
- Prevents common errors (missing coverage requirements, wrong ACORD form version) that cause carrier declines

---

### 4.5 Risk Manager Agent
**Priority: 5**

Enterprise risk management specialist for FAM. Monitors portfolio risk exposures, evaluates new risks, supports regulatory compliance, and maintains the risk register.

**What it owns/tracks:**
- Enterprise risk register (operational, financial, regulatory, reputational risks)
- Risk appetite statement and tolerance thresholds
- Regulatory change log (SEC, NAIC, state insurance dept updates)
- Compliance calendar (filing deadlines, exam schedules, certification renewals)
- Incident log (past risk events, root cause, remediation status)
- Risk assessment templates (new vendor onboarding, product launch, operational change)

**Workspace UI:**
- **Risk register** — live view of tracked risks by category, severity, and owner; filter by status (open/mitigated/accepted)
- **Compliance calendar** — upcoming deadlines and regulatory filings, color-coded by urgency
- **Regulatory alerts panel** — recent regulatory changes flagged as relevant to Fortress AM
- **Assessment launcher** — structured form: risk type, description → agent produces risk assessment memo
- **Incident tracker** — log an incident, track remediation
- **Quick actions** — "Assess new vendor", "Draft risk memo", "Review compliance calendar"

**KB contents (`cowork-risk-manager`):**
- Enterprise risk framework and appetite statement
- Regulatory requirement summaries (SEC, NAIC, state insurance, ERISA if applicable)
- Past risk assessments and their conclusions
- Compliance checklist library (by regulation type)
- Vendor due diligence criteria
- Insurance program risk parameters (concentration limits, loss ratio triggers — per affinity program)
- Incident response playbooks

**MCP connectors:**
- `brave_search` — regulatory news, enforcement actions, industry guidance
- `slack` — risk alerts to leadership channels
- `notion` — risk register and assessment storage
- Future Phase 2: Regulatory monitoring feed (e.g., Wolters Kluwer MCP for compliance updates)

**System prompt focus:** Risk language is precise — likelihood, impact, and velocity are always separated. Mitigation recommendations are specific and actionable (not "monitor the situation"). Regulatory citations include specific rule numbers. Never understate risk to avoid uncomfortable conversations.

**What makes it better than generic chat:**
- Risk register in KB means agent tracks enterprise exposures longitudinally — not just answering one-off questions
- Compliance calendar surfaced proactively in workspace — no relying on someone remembering a filing deadline
- Regulatory change monitoring: agent can be prompted to check for recent SEC/NAIC guidance and flag what's new
- Risk assessment template library produces consistent, auditable outputs — important for regulatory examination readiness

---

### 4.6 Technical Writer Agent
**Priority: 6**

Documentation specialist for Fortress AM's platform (FIP, FAM OS, Cowork, internal tools). Phil-equivalent for the engineering and operations team — handles API docs, process guides, release notes, and knowledge base articles.

**What it owns/tracks:**
- FIP platform documentation library
- API reference docs (endpoints, schemas, authentication)
- Process and runbook library (deployment guides, incident response, onboarding)
- Style guide and doc standards
- Changelog / release notes archive
- Open documentation requests / gaps

**Workspace UI:**
- **Doc tree** — organized by product area (FAIT, FIRM, FORMS, FAM OS, Cowork, Infrastructure)
- **Doc request queue** — open documentation gaps and requests
- **Style checker** — paste content → agent checks against Fortress AM doc standards
- **Changelog manager** — draft release notes for a version, review against prior releases
- **Quick actions** — "Document an API endpoint", "Write a runbook", "Draft release notes", "Explain a feature"

**KB contents (`cowork-techwriter`):**
- Fortress AM documentation style guide
- FIP platform architecture overview (for accurate technical context)
- API schemas and OpenAPI specs (uploaded from codebase)
- Past documentation examples (good references)
- Audience profiles (who reads each doc type: end-user, developer, ops, executive)
- Glossary (Fortress AM-specific terms and their definitions)

**MCP connectors:**
- `notion` — doc storage and publishing
- `github` — access source code for accurate API documentation
- `slack` — doc review requests and notifications

**System prompt focus:** Accuracy over elegance — when uncertain about a technical detail, say so and flag for engineer review. Match the established doc style exactly. Write for the documented audience (non-technical users get different treatment than developers). Never document behavior that hasn't been confirmed in source or by a subject matter expert.

**What makes it better than generic chat:**
- Platform architecture KB means agent understands FIP's data model, service boundaries, and naming conventions — no constant re-explaining
- Style guide enforcement is consistent — every doc output follows the same standard
- Doc tree awareness: agent knows what already exists and can cross-reference rather than re-explaining things covered elsewhere
- GitHub MCP (Phase 2): agent can read actual source code to produce accurate API docs instead of relying on descriptions

---

### 4.7 Graphic Designer Agent
**Priority: 7**

Creative direction, brand asset management, design brief writing, and visual production assistance for Fortress AM's marketing and communications. Not a raster/vector design tool — a strategic design thinking partner that produces design specs, briefs, copy for design, and can generate images via AI image tools.

**What it owns/tracks:**
- Brand asset library (logos, color palette, typography specs, approved imagery)
- Design brief library (past briefs for recurring asset types)
- Active design requests and their status
- Visual identity guidelines
- Asset naming conventions and file organization standards

**Workspace UI:**
- **Brand asset reference** — color palette display, typography specimens, logo usage rules (loaded from KB)
- **Design request queue** — open design requests with brief status
- **Brief builder** — structured form: asset type, dimensions, copy, tone, deadline → agent produces a complete design brief
- **Image generator** — chat-driven image generation requests (via FAIT's existing Bedrock image models or Stability AI MCP)
- **Asset upload** — upload new approved assets to the KB data source
- **Quick actions** — "Write a design brief", "Generate concept image", "Check brand compliance", "Resize spec"

**KB contents (`cowork-designer`):**
- Brand style guide (colors, typography, logo, imagery direction, spacing rules)
- Approved asset library index (what assets exist, where they live, when they were last approved)
- Design brief templates (email header, social post, presentation deck, PDF report)
- Competitive visual analysis (how Fortress AM compares to peer firms visually)
- Past campaign visual direction notes
- Image generation prompt templates (proven prompts for Fortress AM's visual style)

**MCP connectors:**
- `slack` — share design briefs and concepts with stakeholders
- `notion` — brief library and asset documentation
- Future Phase 2: Canva MCP (create assets directly in Canva from brief)
- Future Phase 2: Stability AI / Adobe Firefly MCP (image generation with style-locked prompts)

**System prompt focus:** Design language is precise — color values are hex codes, not "warm blue." Dimensions are specified in pixels AND print units where relevant. Copy written for design is tight and display-format aware (line breaks matter, character counts matter). All generated concepts are explicitly labeled as concepts requiring designer execution — agent never claims final production output.

**What makes it better than generic chat:**
- Brand style guide in KB means "use our brand colors" actually works — agent knows the exact hex values, not approximations
- Design brief template library produces professionally structured briefs that designers can execute without back-and-forth clarification
- Image generation with brand-locked prompts (stored in KB) produces more on-brand results than ad-hoc generation
- Cross-agent coordination: Marketing Agent can request design briefs from the Designer agent for campaign assets

---

## 5. Technical Gaps — What Needs to Be Built

### 5.1 Current Cowork State

The existing `fip/cowork/` codebase (as of March 2026) has:

**CoworkAgent (Node.js/TypeScript):**
- ✅ Claude Agent SDK integration (runner.ts)
- ✅ Redis task queue + pub/sub (taskQueue.ts, taskStore.ts)
- ✅ SSE streaming to frontend (tasks.ts)
- ✅ Approval gate (waitForApproval, setApprovalDecision)
- ✅ FORGE KB context injection (forgeClient.ts)
- ✅ S3 file output upload (fileService.ts)
- ✅ Audit logging (audit.ts)
- ✅ User auth middleware (auth.ts)
- ❌ Agent registry (no multi-agent concept)
- ❌ Agent-scoped routing (/agents/:agentId/tasks)
- ❌ Agent-scoped KB selection
- ❌ Per-agent MCP server configuration
- ❌ Per-agent system prompts

**CoworkWeb (Blazor Server):**
- ✅ Generic chat UI (existing)
- ✅ SSE task stream rendering
- ✅ File upload/download
- ✅ Approval gate UI
- ❌ Agent workspace UI framework
- ❌ Agent-specific workspace components (MarketingWorkspace, etc.)
- ❌ Agent-scoped task history
- ❌ Agent navigation in left drawer

### 5.2 Gaps to Fill — By Sprint

#### Cowork Sprint 3: Agent Infrastructure (CoworkAgent)

**New files:**
```
src/CoworkAgent/src/agents/
├── registry.ts                    ← AgentDefinition type + AGENT_REGISTRY
├── marketing/
│   ├── system-prompt.md           ← Marketing agent system prompt
│   └── mcp-servers.ts             ← Marketing MCP server configs
├── analyst/
│   └── system-prompt.md
├── techwriter/
│   └── system-prompt.md
└── ops/
    └── system-prompt.md

src/CoworkAgent/src/routes/
└── agents.ts                      ← New /agents router (listing + task routing)
```

**Modified files:**
```
src/CoworkAgent/src/server.ts      ← Mount /agents router
src/CoworkAgent/src/agent/runner.ts ← Accept optional agentId param; use agent system prompt + KB
src/CoworkAgent/src/services/forgeClient.ts ← Accept kbId param (not hardcoded)
```

**Key engineering change in `runner.ts`:**

```typescript
// Current: uses single SYSTEM_PROMPT constant + single FORGE KB ID from env
// New: accepts agentDef, loads agent system prompt, queries agent KB

export async function* runTask(params: TaskParams & { agentId?: string }): AsyncGenerator<SseChunk> {
  const agentDef = params.agentId ? AGENT_REGISTRY[params.agentId] : null;

  // Resolve system prompt
  const systemPrompt = agentDef
    ? await fs.readFile(agentDef.systemPromptPath, 'utf-8')
    : GENERIC_SYSTEM_PROMPT;

  // Query agent KB (or corp KB for generic tasks)
  const kbContext = agentDef
    ? await queryForgeContextCached(params.prompt, agentDef.kbConfig.kbId)
    : await queryForgeContextCached(params.prompt, CORP_KB_ID);

  // Resolve allowed MCP servers for this agent
  const mcpServers = agentDef
    ? buildAgentMcpServers(agentDef.allowedMcpServers)
    : buildCorpMcpServers();

  // Inject available tools list into system prompt
  const finalSystemPrompt = systemPrompt.replace(
    '{AVAILABLE_TOOLS_INJECTED_AT_RUNTIME}',
    buildToolsList(mcpServers)
  );

  // ... rest of runner proceeds as today, using finalSystemPrompt + mcpServers
}
```

#### Cowork Sprint 4: Agent Workspace UI (CoworkWeb)

**New Blazor files:**
```
Components/Pages/Agents/
├── AgentIndex.razor               ← /agents — agent selection page
├── AgentPage.razor                ← /agents/:agentId — container page
└── Workspaces/
    ├── MarketingWorkspace.razor   ← Marketing-specific left panel
    ├── AnalystWorkspace.razor
    ├── TechWriterWorkspace.razor
    └── OpsWorkspace.razor

Components/Shared/
├── AgentChatPanel.razor           ← Shared chat + SSE stream (works for all agents)
└── AgentTaskHistory.razor         ← Shared task list component

Components/Layout/
└── NavMenu.razor                  ← Add agents section to nav
```

**`AgentPage.razor` pattern:**

```razor
@page "/agents/{AgentId}"
@inject CoworkAgentClient AgentClient

<div class="agent-workspace-layout">
    <div class="agent-workspace-panel">
        @switch (AgentId)
        {
            case "marketing":
                <MarketingWorkspace OnTaskCreated="StartTask" />
                break;
            case "analyst":
                <AnalystWorkspace OnTaskCreated="StartTask" />
                break;
            // ...
        }
    </div>
    <div class="agent-chat-panel">
        <AgentChatPanel AgentId="@AgentId"
                        ActiveTaskId="@_activeTaskId"
                        OnSubmit="StartTask" />
        <AgentTaskHistory AgentId="@AgentId"
                          OnSelect="@(id => _activeTaskId = id)" />
    </div>
</div>
```

#### Cowork Sprint 5: Marketing Agent Live (MCP + KB seeded)

- Marketing FORGE KB created (Rhodey provisions)
- Brand guidelines, personas, messaging docs uploaded
- HubSpot MCP wired to Tom's HubSpot instance (real API key)
- Klaviyo MCP wired (real API key)
- Ahrefs MCP wired (real API key)
- Marketing workspace shows live campaign data from HubSpot
- Performance snapshot panel pulls real email metrics

---

## 6. New Environment Variables Needed

```
# Per-agent FORGE KB IDs (Rhodey provisions each KB — 7 new Bedrock KBs)
COWORK_MARKETING_KB_ID=<bedrock KB ID>
COWORK_MARKETING_DS_IDS=<comma-separated data source IDs>

COWORK_FINANCIAL_ANALYST_KB_ID=<bedrock KB ID>
COWORK_FINANCIAL_ANALYST_DS_IDS=<comma-separated data source IDs>

COWORK_RESEARCHER_KB_ID=<bedrock KB ID>
COWORK_RESEARCHER_DS_IDS=<comma-separated data source IDs>

COWORK_UNDERWRITER_KB_ID=<bedrock KB ID>
COWORK_UNDERWRITER_DS_IDS=<comma-separated data source IDs>

COWORK_RISK_MANAGER_KB_ID=<bedrock KB ID>
COWORK_RISK_MANAGER_DS_IDS=<comma-separated data source IDs>

COWORK_TECHWRITER_KB_ID=<bedrock KB ID>
COWORK_TECHWRITER_DS_IDS=<comma-separated data source IDs>

COWORK_DESIGNER_KB_ID=<bedrock KB ID>
COWORK_DESIGNER_DS_IDS=<comma-separated data source IDs>

# MCP API keys (Phase 2 only — leave empty for Phase 1 stub mode)
HUBSPOT_API_KEY=
KLAVIYO_API_KEY=
AHREFS_API_KEY=
BRAVE_SEARCH_API_KEY=
SLACK_BOT_TOKEN=       # existing from CoworkAgent
NOTION_API_KEY=
GITHUB_TOKEN=          # for Tech Writer GitHub MCP (Phase 2)
```

**Infrastructure note (Rhodey):** 7 new Bedrock KBs required. Each uses the same OpenSearch Serverless collection (within quota). S3 prefix per agent: `kb-docs/cowork-agents/<agentId>/`. Standard fixed-size chunking (300 tokens, 10% overlap) for all agents except Financial Analyst and Researcher (use 1500-token chunks — longer analytical documents benefit from larger chunks).

---

## 7. Why This Wins vs. Anthropic's Cowork Plugins

| Dimension | Anthropic Cowork Plugin | FIP Specialist Agent |
|-----------|------------------------|---------------------|
| **Persistence** | Dies when laptop sleeps | ECS Fargate — always on |
| **Multi-user** | One person, one machine | Shared across Fortress AM team |
| **Brand/KB memory** | Configure fresh each session | Persistent FORGE KB |
| **Data residency** | Anthropic cloud required | Fortress AM's own Bedrock |
| **Audit trail** | None | Full task audit log |
| **Consequential action gates** | No approval mechanism | Explicit approval gate before any send/post |
| **Workspace UI** | Generic chat window | Purpose-built domain UI per agent |
| **Customization** | Fork the plugin files | KB contents + system prompt per agent |
| **Cost** | $20–100/user/month (Claude subscription) | Existing Bedrock per-token pricing; no per-seat SaaS |
| **Distribution** | Per-machine install | Web app — any user with FIP access |

**The killer differentiator:** FIP Specialist Agents are **shared AI staff for Fortress AM** — not personal productivity assistants. Tom doesn't install a plugin on his laptop. He opens `cowork.dev.fortressam.ai/agents/marketing` and there's the Marketing Agent, already loaded with Fortress AM's brand guidelines and campaign history, accessible from any device, running whether Tom's laptop is open or not.

---

## 8. Open Questions and Pending TODOs

### Resolved (from Fred, 2026-03-19)
- ✅ **HubSpot scope:** Marketing automation only (campaign workflows, email sequences, triggers) — not CRM contact/deal management.
- ✅ **Audience:** All specialist agents are internal FAM employee tools only. Not client-facing.
- ✅ **Access control:** Role/permission-gated with admin oversight. First-class feature — see Section 2.5.
- ✅ **Operations Agent audience:** FAM internal operations (not affinity program clients).

### Pending — Fred to Provide
- 📋 **Brand guidelines docs** — Fred will locate and provide. Marketing KB seeding is blocked until received. Do not block sprint build on this; infrastructure proceeds.
- 📋 **Underwriting guidelines docs** — Fred will locate and provide. Underwriter KB seeding is blocked until received.

### Pending — Tom and Caleb to Define
- 📋 **Financial Analyst scope and capabilities** — Tom and Caleb will define requirements. Infrastructure (KB, workspace shell) can be built; do not implement specific analysis workflows until input received.
- 📋 **Financial Analyst investment policy source** — Does a formal investment policy statement / mandate constraints document exist? PDF, SharePoint, regulatory filing, or institutional knowledge? This is the foundational KB document; without it, agent cannot flag out-of-mandate investments.
- 📋 **Phase 2 MCP priority for Marketing Agent** — Which comes first after Phase 1: HubSpot automation workflows, Klaviyo sequences, or Ahrefs SEO? Depends on which marketing workflow Tom wants to automate first.

---

## 9. WI Candidates

### Sprint 3 — Agent Infrastructure (CoworkAgent backend)
| WI# | Title | Owner | Priority |
|-----|-------|-------|---------|
| TBD | Cowork S3: Agent registry, per-agent routing, per-agent system prompts + KB selection | Tony | High |
| TBD | Cowork S3: Per-agent MCP server binding + stub mode (empty API key → skip) | Tony | High |

### Sprint 4 — Agent Workspace UI (CoworkWeb)
| WI# | Title | Owner | Priority |
|-----|-------|-------|---------|
| TBD | Cowork S4: AgentPage layout, AgentChatPanel, AgentTaskHistory, nav update | Tony | High |
| TBD | Cowork S4: MarketingWorkspace component (Phase 1 — static KB data) | Tony | High |
| TBD | Cowork S4: FinancialAnalystWorkspace, ResearcherWorkspace components | Tony | Medium |
| TBD | Cowork S4: UnderwriterWorkspace, RiskManagerWorkspace components | Tony | Medium |
| TBD | Cowork S4: TechWriterWorkspace, DesignerWorkspace components | Tony | Low |

### KB Provisioning (Rhodey + domain owners)
| WI# | Title | Owner | Priority |
|-----|-------|-------|---------|
| TBD | Provision 7 Bedrock KBs (one per agent) + S3 prefixes | Rhodey | High |
| TBD | Seed Marketing KB — brand guidelines, personas, messaging, campaign history | Marketing — **📋 BLOCKED: Fred to provide brand guidelines docs** | High |
| TBD | Seed Financial Analyst KB — investment policy, frameworks, past memos | Tom/Caleb — **📋 BLOCKED: pending Tom/Caleb scope + investment policy source** | Medium |
| TBD | Seed Researcher KB — methodology guides, reference library | Research team | Medium |
| TBD | Seed Underwriter KB — carrier appetite guides, UW rules, program guidelines | Fred — **📋 BLOCKED: Fred to provide UW guidelines docs** | Medium |
| TBD | Seed Risk Manager KB — risk framework, regulatory summaries, compliance checklists | Risk team | Medium |
| TBD | Seed Tech Writer KB — doc standards, FIP architecture, past docs | Engineering | Low |
| TBD | Seed Designer KB — brand style guide, asset library index, brief templates | Marketing/Design | Low |

### Sprint 5 — Marketing Agent Live (MCP integration)
| WI# | Title | Owner | Priority |
|-----|-------|-------|---------|
| TBD | Cowork S5: HubSpot MCP live integration (Marketing Agent) | Tony + Rhodey | Medium |
| TBD | Cowork S5: Klaviyo + Ahrefs MCP live integration (Marketing Agent) | Tony + Rhodey | Medium |
| TBD | Cowork S5: Brave Search MCP live integration (Analyst + Researcher agents) | Tony | Medium |

---

## 10. One-Page Summary

> **FIP Cowork Specialist Agents are FAM's always-on AI staff.** Internal tools for FAM employees — not client-facing.
>
> Instead of a chat window where you type commands, each agent has a purpose-built workspace: the Marketing Agent shows your campaign tracker, content calendar, and brand reference. The Financial Analyst displays your coverage list and memo drafts. The Underwriter shows the submission queue and carrier matrix. The Risk Manager keeps your compliance calendar and risk register front and center.
>
> Unlike Claude Cowork's plugins — which die when your laptop sleeps and work only for one person — FIP Agents run on FAM's own AWS, persist across sessions, and are shared across the team. Start a campaign brief request, close your laptop, and find the draft waiting when you open Cowork on any device.
>
> **Access is permission-gated.** Admins assign which agents each employee can see. The Financial Analyst and Underwriter agents are not visible to everyone by default.
>
> **Phase 1 roster: 7 specialists.**
> - **Marketing** — brand voice, campaigns, content, competitive analysis, HubSpot marketing automation + Klaviyo + Ahrefs
> - **Financial Analyst** — investment memos, financial models, research library *(scope: Tom/Caleb TBD)*
> - **Researcher** — deep research synthesis, competitive intelligence, multi-source fact-finding
> - **Insurance Underwriter** — submission evaluation, carrier appetite, underwriting checklists *(KB: Fred TBD)*
> - **Risk Manager** — enterprise risk register, compliance calendar, regulatory monitoring
> - **Tech Writer** — platform docs, API reference, runbooks, style guide enforcement
> - **Graphic Designer** — brand asset management, design briefs, visual direction, image generation
>
> The Marketing Agent launches first. Pre-loaded with FAM brand guidelines, personas, and campaign history. Before it sends any email or triggers any campaign workflow, it asks for approval.
>
> It's not a plugin. It's a member of the team.

---

_Architecture by Reed Richards | FIP Cowork Specialist Agents. Phase 1: 7 agents (Marketing, Financial Analyst, Researcher, Insurance Underwriter, Risk Manager, Tech Writer, Graphic Designer). Two build sprints (S3: infrastructure, S4: UI) + one live integration sprint (S5: Marketing MCP). 7 new Bedrock KBs; Financial Analyst + Researcher use 1500-token chunking._
