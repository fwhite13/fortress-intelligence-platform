# Cowork 2.0 — Architecture Brief

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-20  
**Status:** Strategic decision — ready for stakeholder review and sprint planning  
**Audience:** Fred (decision-maker / stakeholder), Tony (implementation), Pipeline team  
**Decision made:** Cowork 2.0 replaces FAIT as the single front door for all Fortress AI capabilities

---

## 1. Product Vision

**Cowork 2.0 is a team AI platform where every employee has a personal AI colleague — their own named agent who knows them, briefs them each morning, handles their requests, and brings in the right specialist for the job.**

Today Fortress operates two products that increasingly overlap: FAIT (chat + projects + KB) and Cowork (specialist agents). Both are good. Neither is the product. The product is the layer above both: a place where the AI is a colleague, not a tool — where it knows you, remembers your context, routes your requests, and surfaces expert help without making you navigate menus to find it.

Cowork 2.0 closes that gap. The personal agent IS the home screen. Specialist agents (Becky, Sally, Nick, Phil) are your colleagues — they have names, domains, and personas because people work better with professionals than with product features. FORGE KB is the shared memory layer that makes it all coherent. Every FAIT capability carries forward; users don't lose anything. They just stop using a chat interface and start working with a team.

This is not a rebuild from scratch. It is a deliberate evolution: Cowork's infrastructure + FAIT's data layer + a new personal-agent interaction model on top.

---

## 2. Three-Tier Agent Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Tier 1 — Personal Agent                       │
│   "Your Jarvis" — one per user, persistent identity, daily driver    │
│                                                                       │
│  • Morning briefing (calendar + email + task queue)                  │
│  • All user requests handled here first                              │
│  • Access to user's FORGE KB (transcripts, summaries, docs)         │
│  • Routes complex/specialist requests to Tier 2                      │
│  • User-configured name, persona, tone preferences                   │
└──────────────────────────┬──────────────────────────────────────────┘
                           │ Handoff with context
         ┌─────────────────┼──────────────────────────┐
         ▼                 ▼                           ▼
┌──────────────┐  ┌──────────────────┐  ┌──────────────────────────┐
│   Tier 2     │  │     Tier 2       │  │         Tier 2            │
│   Becky      │  │      Sally       │  │          Nick             │
│  Design Agent│  │   Underwriter    │  │   Project Manager         │
│              │  │                  │  │                           │
│ HTML/CSS gen │  │ Coverage analysis│  │  Sprint planning          │
│ Blazor comps │  │ Risk assessment  │  │  Status tracking          │
│ Brand system │  │ UW checklist     │  │  Task coordination        │
└──────────────┘  └──────────────────┘  └──────────────────────────┘
         │                 │                           │
         └─────────────────┼───────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Tier 3 — FORGE KB Layer                       │
│      Personal KB | Team KBs | Agent Domain KBs | Meeting Intel      │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.1 Tier 1 — Personal Agent

**One per user. Not shared. Configured by the user.**

The personal agent is the interface the user sees on every login. It is not a chatbot — it is a named colleague the user configures. Fred's agent might be named "Alex." Leslie's might be "Sam." The name, avatar, and tone are user-configured. The underlying model is Claude on Bedrock.

**Capabilities:**

| Capability | How |
|-----------|-----|
| Morning briefing | On login (or explicit ask): calendar events (via Graph), email digests (existing EmailAlertsPanel logic), open tasks from FAM OS / FAIT projects, meeting prep notes from FORGE KB |
| Conversational chat | Same as FAIT's chat today — streaming responses, markdown rendering, code blocks |
| Project context | User selects an active project; agent has that project's KB context injected |
| FORGE KB access | Full personal FORGE KB available via retrieval (meeting transcripts, summaries, uploaded docs) |
| Team KB access | Team KBs surfaced based on user's group membership |
| Specialist routing | Recognizes when a request is specialist work; suggests handoff (see §2.3) |
| Rich artifact generation | Tables, charts, formatted documents — rendered as output artifacts, not just text |
| Preferences memory | Agent remembers user's stated preferences (model preference, tone, how verbose to be) |

**What makes it "theirs":**

Each user has a `PersonalAgent` record in the database:
- `userId` — owner
- `agentName` — what they named it ("Alex", "Quinn", "Sam")
- `agentTone` — formal / casual / direct
- `systemPromptAddendum` — user-written instructions appended to base system prompt
- `activeProjectId` — currently active project for KB context injection
- `morningBriefingEnabled` — bool
- `preferredModel` — override (defaults to Bedrock Sonnet)

**Why this matters:** A user who names their agent builds a mental model of it as a colleague, not a tool. The personalization is shallow technically but meaningful experientially. It is the same reason Becky and Sally have names.

### 2.2 Tier 2 — Specialist Agents

**Shared across all users. Persona-branded. Domain-scoped.**

Specialist agents are not per-user. When Becky helps Leslie with a design task, it's the same Becky that helped Fred yesterday. The persona is consistent. The agent's knowledge base (domain KB) is maintained by whoever owns that function — marketing team maintains Becky's KB, underwriting team maintains Sally's.

Specialist agents inherit full context when called by the personal agent — the last N turns of the conversation are carried into the handoff. The specialist agent knows why they were called.

**Initial roster:**

| Name | Domain | Key MCP Tools | Agent KB |
|------|--------|--------------|----------|
| Becky | Design | None (pure Claude generation) | FIP brand guidelines, component examples, past designs |
| Sally | Insurance Underwriter | None in v1 (KB + Claude) | UW guidelines, coverage checklists, carrier guidelines |
| Nick | Project Manager | (future: Jira/ADO read) | Project templates, sprint playbooks, FIP project history |
| Phil | Tech Writer | None | FIP documentation patterns, API conventions, style guide |
| (future) Alex | Marketing | HubSpot, Klaviyo, Ahrefs | Brand assets, campaign history, personas, competitive intel |
| (future) Morgan | Financial Analyst | (pending Tom/Caleb scoping) | Financial models, deal memos, market data |
| (future) Casey | Researcher | Brave Search | Research methodology, source evaluation patterns |

**Persona spec per agent (to be written as separate doc):**
Each specialist needs a 1-page persona brief: name, domain, communication style, what they're good at, what they always ask before starting, what they won't do. This is not engineering work — it is people work. Fred or the relevant domain expert writes it. Engineering implements it as the agent's system prompt.

### 2.3 Routing Model

**Routing is agent-initiated, not UI-forced.** There is no "navigate to Becky" menu item as the primary path. The personal agent recognizes routing opportunities and surfaces them as suggestions.

```
User: "I need a login page for the new onboarding flow"
Personal Agent: "That sounds like design work. Becky is better at this than I am — 
                 she knows the FIP brand system and can generate the actual HTML. 
                 Want me to bring her in? I'll give her the context from this conversation."
User: "Yes"
→ Personal agent hands off to Becky with last 8 turns of conversation as context
→ Becky's workspace UI renders in the center panel
→ Becky's response acknowledges the handoff: "Got it from [user's agent name]. 
   Here's what I'll build..."
```

Users can also navigate directly to a specialist agent via the left nav panel (same as current Cowork agents list). Direct navigation bypasses personal agent routing — the user goes straight to Becky's workspace. Both paths are valid.

**Handoff mechanism (technical):**

```
Personal agent identifies routing opportunity
  → POST /api/personal-agent/suggest-handoff { agentId, contextSummary, conversationHistory }
  → Returns suggestion card to UI
User accepts
  → POST /agents/{agentId}/tasks { prompt, contextFromPersonalAgent: [...last N turns] }
  → Specialist task starts with injected context
  → UI transitions to specialist workspace component
```

The `contextFromPersonalAgent` field is new. The specialist agent runner prepends this to the task context before calling Claude: `"Context from [user's personal agent]: {summary}\n\nUser's last messages: {history}\n\n---\n\n{user's prompt}"`.

---

## 3. FAIT Feature Migration Map

Every FAIT capability has a defined home in Cowork 2.0. Nothing is retired without a replacement.

| FAIT Feature | Current Location | Cowork 2.0 Home | Notes |
|-------------|-----------------|-----------------|-------|
| Chat UI | `/chat`, `/chat/{id}` | Personal agent home screen | Chat IS the personal agent interface. Sidebar chat history → agent conversation history |
| Projects (user-scoped) | `/projects/*`, `ProjectList.razor`, `ProjectDetail.razor` | Projects section in left nav | Same concept, same data. Project KB context injected into personal agent when project is active |
| Team KBs | `KnowledgeBaseManagement.razor`, `ConversationTeamKbs` | Team KB panel in personal agent sidebar | Surfaced as context selector, same as today — user selects which team KBs to include |
| FORGE KB (personal) | Injected at chat time via `ForgeClient` | Always-on for personal agent; also available to specialist agents on handoff | No change to KB mechanism; personal agent gets full FORGE access by default |
| Morning briefing / Dashboard | `Dashboard.razor`, `BriefingService`, `BriefingGenerationService` | Personal agent capability — triggered on login or "morning briefing" intent | The dashboard IS the personal agent's morning card. Pre-meeting briefs, email digests, task queue — all surfaced by the personal agent |
| Pre-meeting briefs | `PreMeetingBriefCard.razor`, `PreMeetingBriefService` | Personal agent surfaces automatically when meeting is starting soon | Same Graph integration, same logic — delivered via agent message, not as a UI card |
| Post-meeting prompts | `PostMeetingPromptCard.razor`, `PostMeetingService` | Personal agent detects recently ended meetings and prompts for capture | Same as today; FIRM integration carries this forward |
| Email alerts | `EmailAlertsPanel.razor`, `EmailAlertCard.razor`, `EmailController` | Personal agent morning briefing component | Folded into morning briefing message, not standalone UI |
| Weekly rollup | `WeeklyRollupCard.razor`, `WeeklyRollupService` | Personal agent — triggered by "weekly rollup" intent or Friday morning | Becomes an agent-generated summary, not a scheduled UI card |
| Chat history / all chats | `/chats`, `ChatList.razor` | Conversation history in agent sidebar | Agent conversation history replaces raw chat list |
| Briefing history | `BriefingHistoryPage.razor` | Accessible via agent: "show me my past briefings" | Archive view, low priority for MVP |
| Document upload to project | `DocumentUpload.razor` | Project context in personal agent — drag/drop into active project | Same S3 upload mechanism, new UI surface |
| MCP server admin | `Admin/McpServers.razor` | Admin section — unchanged | Admin-only, low traffic |
| User admin | `Admin/*` | Admin section — unchanged | |
| Settings | `Settings.razor` | Cowork settings + personal agent configuration | Expands to include personal agent name/tone/preferences |
| Model selector | `ModelSelector.razor` | Personal agent preferences, not per-conversation | Default model set in agent preferences; overridable per conversation |

**What gets retired (no replacement needed):**

- Raw `/chat` route as primary interface — replaced by personal agent home screen
- `ChatList.razor` as standalone page — conversation history is agent history
- `WeeklyRollupCard.razor` as a scheduled card — becomes agent-triggered output

**What gets retired (replaced with better):**

- Dashboard page (`/dashboard`) → personal agent home IS the dashboard. The dashboard card format becomes an agent morning message with richer content.

---

## 4. FORGE KB as Unified Memory Layer

FORGE KB is not changing. It is the correct architecture. This section defines how Cowork 2.0 surfaces it.

```
                    ┌────────────────────────┐
                    │       FORGE KB          │
                    │   (AWS Bedrock KBs)     │
                    ├────────────────────────┤
                    │  Personal KBs (1/user) │  ← meeting transcripts, summaries,
                    │                        │     uploaded docs, FIRM-pushed content
                    ├────────────────────────┤
                    │   Team KBs (shared)    │  ← team documents, standards, history
                    │                        │
                    ├────────────────────────┤
                    │  Agent Domain KBs      │  ← Becky's brand KB, Sally's UW KB,
                    │  (1/specialist agent)  │     Phil's style guide KB
                    └────────────────────────┘
                              ↑
            ┌─────────────────┼──────────────────┐
            │                 │                  │
     Personal Agent     Specialist Agent    FIRM → push
    (always uses user's  (uses its own       (meetings →
     personal KB + any    domain KB + team    personal KB)
     active team KB)      KB if relevant)
```

**KB access rules:**

| Agent | Personal KB | Team KB | Agent Domain KB | Corp KB |
|-------|------------|---------|----------------|---------|
| Personal agent | ✅ Always | ✅ User-selected | ❌ | ✅ Optional |
| Becky (Design) | ❌ | ❌ | ✅ FIP brand/design KB | ❌ |
| Sally (UW) | ❌ | ❌ | ✅ UW guidelines KB | ❌ |
| Nick (PM) | ❌ | ✅ Team KB for active project | ✅ PM playbooks KB | ❌ |
| Phil (Tech Writer) | ❌ | ✅ Team KB if writing team docs | ✅ Style guide KB | ❌ |

**On handoff from personal agent to specialist:** The specialist does NOT inherit the user's personal KB. The personal agent passes relevant excerpts as inline context. This keeps the specialist's retrieval domain clean — Becky should be searching her design KB, not Fred's meeting transcripts.

**FIRM integration with FORGE KB:** Unchanged. FIRM pushes meeting transcripts and summaries to the user's personal FORGE KB via the existing `PushDocumentAsync` mechanism. The personal agent then has access to those documents for context retrieval. No new integration work required for Cowork 2.0 to benefit from FIRM-captured meetings.

---

## 5. Per-User Personal Agent Model

### 5.1 Data Model

```sql
CREATE TABLE personal_agents (
    id              CHAR(36)     NOT NULL DEFAULT (UUID()) PRIMARY KEY,
    user_id         VARCHAR(200) NOT NULL UNIQUE,   -- one agent per user
    agent_name      VARCHAR(100) NOT NULL DEFAULT 'Assistant',
    agent_tone      ENUM('formal','casual','direct') NOT NULL DEFAULT 'casual',
    system_prompt_addendum TEXT NULL,                -- user's custom instructions
    active_project_id CHAR(36) NULL,                -- FK → projects.id
    preferred_model VARCHAR(100) NULL,               -- NULL = platform default (Bedrock Sonnet)
    morning_briefing_enabled BOOL NOT NULL DEFAULT TRUE,
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE personal_agent_conversations (
    id              CHAR(36)     NOT NULL DEFAULT (UUID()) PRIMARY KEY,
    user_id         VARCHAR(200) NOT NULL,
    agent_id        CHAR(36)     NOT NULL,          -- FK → personal_agents.id
    title           VARCHAR(500) NULL,              -- auto-generated from first message
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_message_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE personal_agent_messages (
    id              CHAR(36)     NOT NULL DEFAULT (UUID()) PRIMARY KEY,
    conversation_id CHAR(36)     NOT NULL,          -- FK → personal_agent_conversations.id
    role            ENUM('user','assistant') NOT NULL,
    content         MEDIUMTEXT   NOT NULL,
    model_used      VARCHAR(100) NULL,
    tool_calls      JSON NULL,                      -- tool invocations made in this turn
    artifacts       JSON NULL,                      -- files/tables/charts generated
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

### 5.2 Personal Agent System Prompt Structure

```
[BASE SYSTEM PROMPT — defined by Fortress, not editable by user]
You are {agentName}, {user.displayName}'s personal AI colleague at Fortress Affinity Group.
You know them well. You have access to their FORGE knowledge base (meeting transcripts,
documents, summaries). You give direct, useful answers.

You can bring in specialist colleagues when the work calls for it:
- Becky (Design) — UI design, HTML/CSS generation, visual mockups
- Sally (Underwriter) — insurance coverage analysis, UW checklists
- Nick (Project Manager) — sprint planning, project coordination
- Phil (Tech Writer) — documentation, user guides, technical writing

When you recognize a request is better handled by a specialist, suggest the handoff.
Don't force it — only suggest when it's genuinely better.

Today is {date}. {morningBriefingContext if triggered}

[USER KNOWLEDGE — from FORGE KB retrieval, injected at query time]
Relevant context from your knowledge base:
{forgeKbRetrieval}

[USER PREFERENCES — from personal_agents record]
{systemPromptAddendum}
Preferred tone: {agentTone}
Active project: {activeProjectName} ({activeProjectKbContext})
```

### 5.3 First-Time Setup Flow

New users (or FAIT migrants) land on a setup screen before reaching the home screen:
1. "What should I call your personal assistant?" → name input with suggestions
2. "How do you prefer to communicate?" → tone selector (formal / casual / direct)
3. "Any standing instructions?" → optional freeform (e.g., "Always give me bullet points first, then explain")

Setup takes under 60 seconds. Can be revisited via Settings → My Agent.

---

## 6. Specialist Agent Roster

### Phase 1 (Build in Cowork v2.0 MVP)

**Becky — Design Agent**

> "I turn descriptions into interfaces."

Domain: UI/UX design, HTML/CSS generation, Blazor component output, brand-compliant design.  
Persona: Collaborative, visual thinker, asks clarifying questions about audience and context before designing. Doesn't guess at requirements.  
MCP tools: None — pure Claude generation.  
Domain KB: FIP brand guidelines, component library examples, past design deliverables, accessibility standards.  
System prompt: See `COWORK-DESIGN-AGENT-SPEC.md` §6.  
Workspace UI: `DesignWorkspace.razor` — three-panel (history / preview / controls).

**Sally — Insurance Underwriter**

> "I evaluate risk so you don't miss anything."

Domain: Insurance coverage analysis, underwriting checklists, carrier submission guidance, risk assessment.  
Persona: Methodical, asks for specifics before rendering opinions. Knows what information is actually needed vs. nice-to-have.  
MCP tools: None in Phase 1 (KB + Claude). Phase 2: carrier portal read APIs.  
Domain KB: UW guidelines, coverage type explainers, carrier appetite guides, FAM OS submission templates.  
Workspace UI: `UwWorkspace.razor` — intake form + analysis output + checklist panel.

**Nick — Project Manager**

> "I keep projects moving."

Domain: Sprint planning, task breakdown, status tracking, retrospectives, dependency identification.  
Persona: Action-oriented, produces concrete deliverables (sprint plans, meeting agendas, status updates). Never vague.  
MCP tools: Phase 2: ADO read (query work items, sprints). Phase 1: KB + Claude.  
Domain KB: FIP project history, sprint planning templates, definition-of-done standards, retrospective patterns.  
Workspace UI: `PmWorkspace.razor` — conversation output + artifact panel (plan documents, task lists).

**Phil — Tech Writer**

> "I make complex things clear."

Domain: Technical documentation, user guides, API references, changelogs, architecture summaries.  
Persona: Precise, asks "who is the reader?" before every piece of writing. Produces structured, navigable docs.  
MCP tools: None.  
Domain KB: FIP documentation conventions, style guide, existing API docs, architecture specs.  
Workspace UI: `TechWriterWorkspace.razor` — prompt + editor-style output with copy/download.

### Phase 2 (Post-MVP)

| Name | Domain | Priority | Blocker |
|------|--------|---------|---------|
| Alex | Marketing | High (Tom's ask) | Brand guidelines KB seeding |
| Morgan | Financial Analyst | Medium | Tom/Caleb scope definition |
| Casey | Researcher | Medium | Brave Search MCP integration |

---

## 7. FIRM Integration Decision

**Decision: FIRM remains a standalone portal in Phase 1. Integration is a data-layer concern, not a UI concern.**

### Rationale

FIRM solves a specific, distinct problem: meeting intelligence for investment/relationship professionals. Its UI — meeting list, transcript viewer, meeting detail, KB push controls — serves a distinct workflow that doesn't benefit from being embedded inside a conversational interface.

**What Cowork 2.0 gets from FIRM without merging:**

- FIRM continues pushing transcripts and summaries to the user's personal FORGE KB via the existing `PushDocumentAsync` mechanism
- The personal agent (Tier 1) has full access to those FORGE KB documents at query time
- Users can ask their personal agent: "What did we discuss in the Acme meeting last week?" — the agent retrieves from FORGE KB and answers
- No new integration code needed

**What a merged FIRM would give:**

- Meeting list visible inside Cowork 2.0 nav
- Post-meeting prompt initiated by personal agent rather than FIRM dashboard card
- FIRM's "KB Push" controls surfaced in agent conversation

None of these are blockers for MVP. The data integration (FIRM → FORGE KB → personal agent) is complete. UI integration is Phase 2.

**Phase 2 FIRM integration (not MVP):**

- Add FIRM-style meeting list to Cowork 2.0 left nav (`/meetings` route)
- Personal agent receives FIRM meeting events via webhook → proactively surfaces post-meeting capture prompt
- FIRM portal remains for power users who want the full meeting detail view

---

## 8. Codebase Impact — What Carries Forward, What's New, What Retires

### 8.1 Carries Forward (no rewrite)

| Component | Location | Notes |
|-----------|---------|-------|
| FORGE KB client | `ForgeClient.ts` (CoworkAgent) | Unchanged — KB query mechanism stays |
| Specialist agent infrastructure | `CoworkAgent` Node.js | Registry, routing, SSE, Redis task queue all carry forward |
| Approval gate | `CoworkAgent` | Unchanged |
| Project service + DB | FAIT `ProjectService.cs` + `projects` table | Migrated to Cowork 2.0 database — same schema |
| Team KB management | FAIT `KnowledgeBaseManagement.razor` + KB service | Migrated to Cowork 2.0 admin section |
| FIRM push pipeline | FIRM `PushDocumentAsync` | Unchanged |
| Auth / OIDC / cookie pattern | FAIT `Program.cs` auth setup | Copied verbatim to Cowork 2.0 (same Entra, same `FIP__LoginUrl` pattern) |
| FipShared / FipNavBar | `fip/shared/FipShared/` | Used in Cowork 2.0 layout (FIP module = new enum value) |
| Graph API integration | FAIT `GraphService.cs`, `MicrosoftTokenService.cs` | Carried forward for calendar + email access in morning briefing |
| Pre-meeting brief logic | FAIT `PreMeetingBriefService.cs` | Adapted — logic becomes personal agent tool call, not Blazor service |
| Email alerts | FAIT `EmailController.cs`, `EmailAlertsPanel.razor` | Logic carried forward, UI surface changes |

### 8.2 Built New

| Component | Description |
|-----------|-------------|
| Personal agent engine | New Node.js module in `CoworkAgent/src/personal/` — streaming Claude chat with FORGE KB injection, morning briefing generation, routing suggestion logic |
| Personal agent Blazor UI | `PersonalAgentPage.razor` + `AgentChatPanel.razor` — the home screen |
| Personal agent settings UI | `AgentSettingsPanel.razor` — name, tone, instructions |
| Handoff flow | UI + API for suggesting and executing personal-agent → specialist routing |
| `personal_agents` DB table | Setup + migration (see §5.1) |
| Morning briefing generator | New service: assembles calendar events + email digest + task queue into morning message |
| Conversation history | Replaces raw `Conversation` / `ChatMessage` FAIT tables with agent-scoped conversation model |
| Per-user personalization API | `PATCH /api/personal-agent/settings` endpoint |
| Sally workspace | `UwWorkspace.razor` |
| Nick workspace | `PmWorkspace.razor` |
| Phil workspace | `TechWriterWorkspace.razor` |
| Becky workspace | Already specced in `COWORK-DESIGN-AGENT-SPEC.md` |

### 8.3 Retired (with migration path)

| Component | Retirement Reason | Migration Path |
|-----------|-----------------|---------------|
| FAIT `Index.razor` + `/chat` route | Replaced by personal agent home | Users redirected to Cowork 2.0 |
| FAIT `ChatView.razor`, `ChatInput.razor`, `ChatList.razor` | Chat IS the personal agent now | New `PersonalAgentPage.razor` replaces |
| FAIT `Dashboard.razor` | Dashboard IS the personal agent morning card | Morning briefing in personal agent |
| FAIT `BriefingHistoryPage.razor` | Low usage; "show my past briefings" intent handled by agent | Optional archive view in Phase 2 |
| FAIT `WeeklyRollupCard.razor` | Agent-triggered instead of scheduled card | Weekly rollup becomes agent capability |

**FAIT remains live** during migration. Cowork 2.0 is deployed as a separate service. Users migrated in cohorts. FAIT is not deprecated until ≥90% of active users are on Cowork 2.0.

---

## 9. Build Sequence — MVP to Full Product

### MVP Definition

**Goal:** Internal Fortress team on Cowork 2.0 for daily AI use. FAIT still live for fallback.

**MVP includes:**
- Personal agent home screen with streaming chat
- Morning briefing (calendar + email digest)
- FORGE KB context injection in personal agent
- Becky and Phil (two specialist agents live, full workspace UI)
- Project context (carry active project's KB into personal agent conversation)
- Conversation history
- Basic routing suggestions ("This looks like design work — want to bring Becky in?")

**MVP excludes:**
- Sally, Nick (Phase 2 specialist agents)
- Full FIRM UI integration (data integration already works via FORGE KB)
- Per-variant spinner UX improvements
- Team KB management UI (admin sets up team KBs; users select via dropdown)

---

### Sprint Plan

#### Cowork 2.0 Sprint 1 — Personal Agent Foundation
*New ECS service or renamed Cowork service; DB schema; personal agent engine*

**Deliverables:**
- `personal_agents` table + `personal_agent_conversations` + `personal_agent_messages`
- Personal agent engine: `CoworkAgent/src/personal/runner.ts` — streaming Claude chat with FORGE KB injection, system prompt construction
- `PersonalAgentPage.razor` — home screen, chat input, streaming response, markdown rendering
- Morning briefing: calendar events (via Graph) + email digest (via EmailController logic) + open FAM OS tasks
- Personal agent settings: name, tone, custom instructions
- Auth: same Entra OIDC pattern as FAIT; cookie domain shared so users stay logged in

*Files: ~20 new, ~5 modified*

#### Cowork 2.0 Sprint 2 — Project Context + KB Layer
*FORGE KB in personal agent; project selection; team KB selector; conversation history*

**Deliverables:**
- FORGE KB retrieval wired into personal agent: `ForgeClient.ts` injected at query time
- Active project selector in agent sidebar — selects project, injects project KB context
- Team KB selector — user picks which team KBs to include (dropdown, same as FAIT KB indicator)
- Conversation history: sidebar shows past conversations with auto-generated titles
- Data migration: existing FAIT conversations importable (optional, not blocking)

*Files: ~10 new, ~8 modified*

#### Cowork 2.0 Sprint 3 — Specialist Agent Integration
*Routing suggestions; handoff flow; Becky + Phil live*

**Deliverables:**
- Routing classifier: personal agent detects specialist-appropriate requests and emits suggestion card
- Handoff API: `POST /api/personal-agent/handoff` — packages context + executes specialist task
- Becky workspace live (from `COWORK-DESIGN-AGENT-SPEC.md`)
- Phil workspace live (`TechWriterWorkspace.razor`)
- Agents index page (`/agents`) — browse all specialists
- `AgentPage.razor` — specialist workspace with back-to-home nav

*Files: ~15 new, ~6 modified*

#### Cowork 2.0 Sprint 4 — Sally + Nick + Polish
*Two remaining Phase 1 specialist agents; artifact rendering; performance*

**Deliverables:**
- Sally workspace + UW analysis system prompt + KB seeding
- Nick workspace + PM playbooks KB
- Rich artifact rendering: tables, structured outputs in personal agent messages
- Pre-meeting briefs: personal agent proactively surfaces when meeting is starting in 60 min
- Post-meeting capture prompt: personal agent detects recently ended meeting, prompts for notes
- Performance: SSE streaming, message pagination, lazy load for conversation history

*Files: ~12 new, ~10 modified*

#### Cowork 2.0 Sprint 5 — FAIT Migration + Go-Live
*Internal user migration; FAIT feature parity; monitoring*

**Deliverables:**
- User import from FAIT (existing projects, KB config, preferences carried over)
- Email: `EmailController` logic fully migrated to personal agent morning briefing
- Admin tools: team KB management, user management, agent access control
- MCP server admin: carried from FAIT admin section
- Feature flag: per-user "Cowork 2.0" toggle for gradual rollout
- Monitoring: session length, agent usage by type, handoff conversion rate

*Files: ~8 new, ~15 modified*

---

### Parallel Track: FAIT Sunset Criteria

FAIT is sunset when all three are true:
1. ≥90% of FAIT's monthly active users have logged into Cowork 2.0 at least 3× in the preceding 30 days
2. Zero P1 bugs open against Cowork 2.0 for 2 consecutive weeks
3. Fred explicitly approves

Until then, FAIT runs. Both are production. No forced migration.

---

## 10. Key Decisions Captured

| Decision | Rationale |
|----------|----------|
| Personal agent is the home screen (not a feature) | The product is the AI colleague relationship, not a feature set. Making the personal agent the shell enforces this design intent through every sprint. |
| Specialist agents have names + personas | Named colleagues build stronger mental models than named features. Behavioral differences (Nick always asks "what's the deadline?") create trust and predictability. |
| Routing is suggestion-based, not menu-based | Users don't want to navigate to find help. The agent should recognize the need and offer routing. Menu navigation remains for direct access. |
| FIRM stays standalone for Phase 1 | The data integration (FIRM → FORGE KB) already works. UI integration is additional complexity with limited Phase 1 benefit. |
| FORGE KB architecture unchanged | FORGE KB is production, scaled, and correct. Cowork 2.0 is a new surface that queries it — not a replacement. |
| No single-repo merge of FAIT + Cowork | Running two separate services during migration is safer than a big-bang merge. Cowork 2.0 gets its own ECS service; FAIT stays on its own until sunset. |
| Personal agent system prompt is partly user-authored | The `systemPromptAddendum` field gives users meaningful control without opening the full system prompt to editing. Advanced users who know what they want can have it. |
| Specialist agent personas need non-engineering input | Sally and Nick need persona briefs written by domain experts. Engineering can build the shell; only underwriters know how Sally should ask questions. |

---

## 11. Open Questions (Require Fred / Stakeholder Input)

1. **Personal agent default names** — Does Fortress ship a default name for new users' agents, or does every user name it themselves on first login? Options: (a) force setup flow, (b) default to "Assistant" until renamed, (c) Fortress picks a single default name for all ("Alex").

2. **Specialist agent persona briefs** — Who writes them? This is not engineering work. Sally's system prompt needs to come from the underwriting team. When is that input available, and who owns it?

3. **FAIT user communication** — When is the migration announcement made? Is it opt-in ("try Cowork 2.0") or opt-out ("we're moving everyone on date X")? Affects Sprint 5 scope.

4. **FIRM UI integration timeline** — Is Phase 1 acceptable (data integration only, FIRM UI stays separate), or does the meeting list need to live inside Cowork 2.0 before go-live?

5. **Morning briefing scope at MVP** — Calendar + email + FAM OS tasks is the spec. Should it also include FAIT project reminders? Open items from conversations?

6. **Personal agent branding** — Does every user see "Your Cowork Agent" until they name it, or is there a Fortress-branded default persona ("Hi, I'm Aria, your Fortress AI assistant")?

7. **Team KB ownership** — Today FAIT admins manage team KBs. In Cowork 2.0, does that stay admin-only, or can team leads manage their own team's KB?

---

_Brief by Reed Richards | Cowork 2.0 = personal agent home screen + specialist agent roster + FORGE KB as unified memory layer. Five-sprint build sequence. FAIT sunset criteria defined. Strategic + buildable._
