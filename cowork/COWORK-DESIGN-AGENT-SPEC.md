# FIP Cowork — Design Agent Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-20  
**Status:** Ready for implementation  
**Context:** Specialist agent for text-to-UI generation using Claude (Bedrock) only. Zero external SDK dependencies — no `@google/stitch-sdk`, no third-party design APIs.  
**Reference:** `COWORK-SPECIALIST-AGENTS-SPEC.md`, `stitch-research-2026-03-20.md`  
**Codebase:** `fip/cowork/src/CoworkAgent/` (Node.js TypeScript), `fip/cowork/src/CoworkWeb/` (.NET 9 Blazor Server)

---

## 1. Why No Stitch SDK

Bruce's research recommends the hybrid approach (Claude orchestrates Stitch SDK). Fred's constraint overrides that recommendation: **no external design APIs, no dependency on a Google Labs product that could be deprecated tomorrow**.

The pure-Claude approach is viable. Claude 3.7 Sonnet on Bedrock generates high-fidelity, responsive HTML/CSS from text descriptions. It is not tuned for UI generation the way Stitch is — the output requires more prompt engineering discipline — but it is:

- Under Fortress control (Bedrock, AWS, no Google accounts)
- Blazor-aware (Stitch will never generate `.razor` components)
- Extensible (Fortress brand context is first-class, not bolted on)
- Free of rate limits imposed by a third-party beta product

The Design Agent system prompt is the equivalent of Stitch's model tuning: it encodes the HTML generation rules, component vocabulary, and Fortress brand tokens that shape every output.

---

## 2. Feature Scope

### In Scope (Stitch parity targets)

| Feature | Implementation |
|---------|---------------|
| Text-to-UI generation | Claude generates complete HTML/CSS file per prompt |
| Iterative refinement | Conversation-mode: prior screen injected as context |
| Component variants | 3 parallel Claude calls with divergent creative instructions |
| Brand/style context | Per-org `brand.json` injected into system prompt at task start |
| Asset upload | Reference images uploaded via existing file upload pipeline → passed to Claude as vision input |
| Export — HTML download | S3 presigned GET URL returned as `file_output` SSE chunk |
| Export — copy code | HTML source returned in `result` SSE chunk for clipboard |
| Design history | Per-project S3 prefix + Redis index; `GET /agents/design/projects/:projectId/screens` |
| Blazor component output | Optional conversion pass: Claude converts generated HTML to `.razor` component |

### Out of Scope (Fred explicitly excluded)

- Infinite canvas — not needed
- Voice input — not needed beyond FAIT's existing STT (which can feed text to the agent)
- Figma export — no external APIs
- Interactive prototyping (click-through linking) — post-MVP
- React component output — Blazor is the target; React is not in FIP stack

---

## 3. Architecture

### 3.1 Where It Runs

Design Agent runs **inside the existing `CoworkAgent` container** — same Node.js TypeScript process, same Claude Agent SDK, same Redis/S3 infrastructure. It registers as a new entry in `AGENT_REGISTRY` and gets its own routes under `/agents/design/...`.

No new ECS service. No new Docker image. One new agent definition file, one new routes file, one new system prompt.

```
CoworkWeb (Blazor Server)
  └── DesignWorkspace.razor
          ↕ HTTP + SSE (same pattern as other agents)
CoworkAgent (Node.js)
  └── /agents/design/...
          ├── agents/design/runner.ts          ← design-specific runner
          ├── agents/design/system-prompt.md   ← generation rules + brand context
          └── services/brandService.ts          ← org brand context loader
          ↕ Claude Agent SDK (Bedrock Sonnet)
          ↕ S3 (generated HTML artifacts)
          ↕ Redis (task queue + screen index)
```

### 3.2 Data Flow: Single Screen Generation

```
1. User types "A login page with email/password, Fortress Navy header, company logo top-left"
   CoworkWeb → POST /agents/design/projects/:projectId/screens  { prompt, deviceType, orgId }

2. CoworkAgent:
   a. Load brand context for orgId from S3/cache → BrandContext JSON
   b. Build system prompt (base rules + brand context inline)
   c. Load project history (prior screen HTML snippets for continuity) from Redis
   d. Call Claude Agent SDK with: system prompt + screen generation request
   e. Claude generates complete HTML/CSS file → writes to working dir
   f. Upload HTML to S3: fip-cowork-workspaces/design/{orgId}/{projectId}/{screenId}.html
   g. Upload preview thumbnail (Claude generates inline base64 screenshot via <canvas> 
      trick in the HTML itself — see §6.3)
   h. Return SSE chunks: step progress + file_output (download URL + HTML source)

3. CoworkWeb receives SSE:
   a. Renders iframe preview from download URL
   b. Shows "Download HTML" + "Copy Code" + "Convert to Blazor" buttons
   c. Updates design history panel
```

### 3.3 Data Flow: Iterative Refinement

```
1. User clicks on existing screen, types "Make the button primary blue and add a sidebar"
   CoworkWeb → POST /agents/design/projects/:projectId/screens/:screenId/edit
               { prompt, priorHtml: "<full HTML of prior screen>" }

2. CoworkAgent:
   a. Same brand context load
   b. System prompt + prior HTML injected as context: "Here is the current design:\n```html\n{priorHtml}\n```\n\nApply this change: {editPrompt}"
   c. Claude outputs revised HTML
   d. Save as new S3 object: .../screens/{screenId}_v{N}.html (version increment)
   e. Update Redis screen index: screenId → [v1, v2, v3, ...] version list

3. CoworkWeb:
   a. iframe preview refreshes
   b. Version history strip shows v1 → v2 → v3 with timestamps
   c. User can click prior version to restore
```

### 3.4 Data Flow: Variant Generation

```
1. User clicks "Generate Variants (3)"
   CoworkWeb → POST /agents/design/projects/:projectId/screens { ..., variantCount: 3 }

2. CoworkAgent:
   a. Fire 3 parallel Claude calls with different creative instructions:
      - Variant A: "CONSERVATIVE — clean, minimal, high contrast"  
      - Variant B: "CONTEMPORARY — cards, shadows, brand accent color"
      - Variant C: "BOLD — prominent hero, strong typography, full-bleed imagery"
   b. Wait for all 3 with Promise.all (all share the same brand context)
   c. Upload 3 HTML files to S3 with variant suffix: ..._varA.html, ..._varB.html, ..._varC.html

3. CoworkWeb:
   a. Side-by-side or tab comparison view of 3 iframes
   b. "Select this variant" button locks one as the canonical screen
```

---

## 4. File Layout

### `AgentApiClient.cs` — Add Design Extension Methods

Add these three methods to the existing `AgentApiClient` class in `CoworkWeb/Services/AgentApiClient.cs`:

```csharp
// ── Design Agent ─────────────────────────────────────────────────────────

/// <summary>Start a new design screen generation task. Returns (taskId, screenId).</summary>
public async Task<(string TaskId, string ScreenId)> StartDesignScreenAsync(
    string projectId, string prompt, string deviceTarget,
    int variantCount, bool convertToBlazor, string orgId,
    IEnumerable<(string Name, Stream Data, string ContentType)> refs,
    CancellationToken ct = default)
{
    var client = CreateClient();
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(prompt),                                     "prompt");
    form.Add(new StringContent(deviceTarget),                               "deviceTarget");
    form.Add(new StringContent(variantCount.ToString()),                    "variantCount");
    form.Add(new StringContent(convertToBlazor ? "true" : "false"),         "convertToBlazor");
    form.Add(new StringContent(orgId),                                      "orgId");

    foreach (var (name, data, contentType) in refs)
    {
        var fc = new StreamContent(data);
        fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fc, "refs", name);
    }

    var resp = await client.PostAsync(
        $"/agents/design/projects/{projectId}/screens", form, ct);
    resp.EnsureSuccessStatusCode();

    var body = await resp.Content.ReadFromJsonAsync<DesignScreenResponse>(cancellationToken: ct)
        ?? throw new InvalidOperationException("No response from design API");
    return (body.TaskId, body.ScreenId);
}

/// <summary>Submit an edit to an existing screen. Returns the new taskId.</summary>
public async Task<string> EditDesignScreenAsync(
    string projectId, string screenId, string prompt,
    string priorHtml, string orgId, string deviceTarget,
    CancellationToken ct = default)
{
    var client = CreateClient();
    var resp = await client.PostAsJsonAsync(
        $"/agents/design/projects/{projectId}/screens/{screenId}/edit",
        new { prompt, priorHtml, orgId, deviceTarget }, ct);
    resp.EnsureSuccessStatusCode();

    var body = await resp.Content.ReadFromJsonAsync<DesignScreenResponse>(cancellationToken: ct)
        ?? throw new InvalidOperationException("No response from design edit API");
    return body.TaskId;
}

/// <summary>Open SSE stream for a design task (carries internal JWT same as OpenStreamAsync).</summary>
public async Task<Stream> OpenDesignStreamAsync(string taskId, CancellationToken ct = default)
{
    var client  = CreateClient();
    var request = new HttpRequestMessage(HttpMethod.Get,
        $"/agents/design/tasks/{taskId}/stream");
    request.Headers.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
    var resp = await client.SendAsync(request,
        HttpCompletionOption.ResponseHeadersRead, ct);
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsStreamAsync(ct);
}

private record DesignScreenResponse(string TaskId, string ScreenId);
```

### New Files in `CoworkAgent`

```
fip/cowork/src/CoworkAgent/src/
├── agents/
│   └── design/
│       ├── runner.ts           ← design-specific task runner (wraps generic runner)
│       ├── system-prompt.md    ← generation rules + HTML output conventions
│       └── tools.ts            ← design-specific tool definitions (save_screen, etc.)
├── routes/
│   └── design.ts               ← /agents/design/* route handlers
└── services/
    └── brandService.ts         ← org brand context load/cache
```

### New Files in `CoworkWeb`

```
fip/cowork/src/CoworkWeb/Components/Pages/Agents/
└── Workspaces/
    └── DesignWorkspace.razor   ← full workspace UI component
```

### Modified Files

```
fip/cowork/src/CoworkAgent/src/server.ts                (mount design router — add 2 lines)
fip/cowork/src/CoworkWeb/Services/AgentApiClient.cs     (add 3 design extension methods + GetAgentMetaAsync — see §4 above)
fip/cowork/src/CoworkWeb/wwwroot/js/cowork.js           (add window.triggerElementClick helper — P2 fix)
```

### Files That Must Be CREATED (not modified — they don't exist yet)

> **P1 fix (Clint review):** These files were marked "Modified" in an earlier draft but do not exist in the codebase. Tony must CREATE them.

```
fip/cowork/src/CoworkAgent/src/agents/registry.ts    ← CREATE (full content below)
fip/cowork/src/CoworkAgent/src/routes/agents.ts      ← CREATE (full content below)
fip/cowork/src/CoworkWeb/Components/Pages/Agents/AgentPage.razor  ← CREATE (full content below)
```

#### `agents/registry.ts` — Full File (CREATE)

```typescript
// AGENT REGISTRY
// Maps agentId → agent definition.
// Used by /agents/* routes to look up configuration per agent.

export interface AgentDefinition {
  id:                 string;
  name:               string;
  description:        string;
  icon:               string;
  color:              string;
  systemPromptPath:   string;
  kbConfig: {
    kbId:             string;
    dataSourceIds:    string[];
    fallbackToCorpKb: boolean;
  };
  allowedMcpServers:  string[];
  approvalOverrides:  { require: string[]; skip: string[] };
  workspaceComponent: string;
}

export const AGENT_REGISTRY: Record<string, AgentDefinition> = {
  marketing: {
    id:          'marketing',
    name:        'Marketing Agent',
    description: 'Campaign copy, email sequences, audience targeting, brand voice.',
    icon:        'Campaign',
    color:       '#d4af37',
    systemPromptPath:  'agents/marketing/system-prompt.md',
    kbConfig: {
      kbId:             process.env.COWORK_MARKETING_KB_ID ?? '',
      dataSourceIds:    (process.env.COWORK_MARKETING_DS_IDS ?? '').split(',').filter(Boolean),
      fallbackToCorpKb: true,
    },
    allowedMcpServers:  ['hubspot', 'klaviyo', 'ahrefs'],
    approvalOverrides:  { require: [], skip: [] },
    workspaceComponent: 'MarketingWorkspace',
  },

  analyst: {
    id:          'analyst',
    name:        'Financial Analyst',
    description: 'Investment memos, earnings analysis, financial models.',
    icon:        'BarChart',
    color:       '#0369a1',
    systemPromptPath:  'agents/analyst/system-prompt.md',
    kbConfig: {
      kbId:             process.env.COWORK_ANALYST_KB_ID ?? '',
      dataSourceIds:    (process.env.COWORK_ANALYST_DS_IDS ?? '').split(',').filter(Boolean),
      fallbackToCorpKb: false,
    },
    allowedMcpServers:  ['brave-search'],
    approvalOverrides:  { require: [], skip: [] },
    workspaceComponent: 'AnalystWorkspace',
  },

  techwriter: {
    id:          'techwriter',
    name:        'Tech Writer',
    description: 'Documentation, user guides, API references, changelogs.',
    icon:        'Article',
    color:       '#0891b2',
    systemPromptPath:  'agents/techwriter/system-prompt.md',
    kbConfig: {
      kbId:             process.env.COWORK_TECHWRITER_KB_ID ?? '',
      dataSourceIds:    (process.env.COWORK_TECHWRITER_DS_IDS ?? '').split(',').filter(Boolean),
      fallbackToCorpKb: true,
    },
    allowedMcpServers:  [],
    approvalOverrides:  { require: [], skip: [] },
    workspaceComponent: 'TechWriterWorkspace',
  },

  ops: {
    id:          'ops',
    name:        'Operations Agent',
    description: 'SOPs, process documentation, workflow optimization.',
    icon:        'Settings',
    color:       '#6b7280',
    systemPromptPath:  'agents/ops/system-prompt.md',
    kbConfig: {
      kbId:             process.env.COWORK_OPS_KB_ID ?? '',
      dataSourceIds:    (process.env.COWORK_OPS_DS_IDS ?? '').split(',').filter(Boolean),
      fallbackToCorpKb: true,
    },
    allowedMcpServers:  ['slack'],
    approvalOverrides:  { require: [], skip: [] },
    workspaceComponent: 'OpsWorkspace',
  },

  design: {
    id:          'design',
    name:        'Design Agent',
    description: 'Generate responsive HTML/CSS UI screens from text descriptions. Iterate, create variants, export to HTML or Blazor components.',
    icon:        'Palette',
    color:       '#7C3AED',
    systemPromptPath:  'agents/design/system-prompt.md',
    kbConfig: {
      kbId:             process.env.COWORK_DESIGN_KB_ID ?? '',
      dataSourceIds:    (process.env.COWORK_DESIGN_DS_IDS ?? '').split(',').filter(Boolean),
      fallbackToCorpKb: false,
    },
    allowedMcpServers:  [],
    approvalOverrides:  { require: [], skip: [] },
    workspaceComponent: 'DesignWorkspace',
  },
};
```

#### `routes/agents.ts` — Full File (CREATE)

```typescript
import express from 'express';
import { AGENT_REGISTRY } from '../agents/registry.js';
import type { AuthedRequest } from '../middleware/auth.js';

const router = express.Router();

// GET /agents — list all agents the requesting user has access to
// Phase 1: all registered agents visible to all authenticated users.
// Phase 2: filter by AgentAccessGrant (see COWORK-SPECIALIST-AGENTS-SPEC.md).
router.get('/', (_req, res) => {
  const agents = Object.values(AGENT_REGISTRY).map(a => ({
    id:          a.id,
    name:        a.name,
    description: a.description,
    icon:        a.icon,
    color:       a.color,
  }));
  res.json({ agents });
});

// GET /agents/:agentId — single agent metadata
router.get('/:agentId', (req, res) => {
  const agent = AGENT_REGISTRY[req.params.agentId];
  if (!agent) { res.status(404).json({ error: 'Agent not found' }); return; }
  res.json({
    id:          agent.id,
    name:        agent.name,
    description: agent.description,
    icon:        agent.icon,
    color:       agent.color,
    workspaceComponent: agent.workspaceComponent,
  });
});

export default router;
```

**Mount in `server.ts`:**
```typescript
import agentsRouter from './routes/agents.js';
import designRouter  from './routes/design.js';
// ...
app.use('/agents',        authenticate, agentsRouter);
app.use('/agents/design', authenticate, designRouter);
```

#### `AgentPage.razor` — Full File (CREATE)

```razor
@page "/agents/{AgentId}"
@attribute [Authorize]
@inject AgentApiClient AgentApi
@inject NavigationManager Nav
@using CoworkWeb.Components.Pages.Agents.Workspaces

<PageTitle>@_agentName — Cowork</PageTitle>

<div class="agent-page">

    @switch (AgentId)
    {
        case "design":
            <DesignWorkspace OrgId="@_orgId" />
            break;

        case "marketing":
            <MarketingWorkspace />
            break;

        case "analyst":
            <AnalystWorkspace />
            break;

        case "techwriter":
            <TechWriterWorkspace />
            break;

        case "ops":
            <OpsWorkspace />
            break;

        default:
            <div style="padding:40px; text-align:center; color:#9ca3af;">
                Agent "@AgentId" not found.
                <a href="/agents">← Back to agents</a>
            </div>
            break;
    }

</div>

@code {
    [Parameter] public string AgentId { get; set; } = "";

    private string _agentName = "";
    private string _orgId     = "fortress-am";  // Phase 2: resolve from user session

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var meta   = await AgentApi.GetAgentMetaAsync(AgentId);
            _agentName = meta?.Name ?? AgentId;
        }
        catch
        {
            _agentName = AgentId;
        }
    }
}
```

**Add `GetAgentMetaAsync` to `AgentApiClient.cs`:**
```csharp
public async Task<AgentMeta?> GetAgentMetaAsync(string agentId, CancellationToken ct = default)
{
    var client = CreateClient();
    var resp   = await client.GetAsync($"/agents/{agentId}", ct);
    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadFromJsonAsync<AgentMeta>(cancellationToken: ct);
}

public record AgentMeta(string Id, string Name, string Description, string Icon, string Color);
```

---

## 5. Agent Registry Entry

**In `agents/registry.ts`**, add to `AGENT_REGISTRY`:

```typescript
design: {
  id:          'design',
  name:        'Design Agent',
  description: 'Generate responsive HTML/CSS UI screens from text descriptions. Iterate, create variants, export to HTML or Blazor components.',
  icon:        'Palette',
  color:       '#7C3AED',
  systemPromptPath:  'agents/design/system-prompt.md',
  kbConfig: {
    kbId:             process.env.COWORK_DESIGN_KB_ID ?? '',
    dataSourceIds:    (process.env.COWORK_DESIGN_DS_IDS ?? '').split(',').filter(Boolean),
    fallbackToCorpKb: false,  // Design agent does not query corp KB by default
  },
  allowedMcpServers:  [],    // No MCP servers — pure Claude generation
  approvalOverrides: { require: [], skip: [] },
  workspaceComponent: 'DesignWorkspace',
},
```

**New env vars (ECS task definition):**

```
COWORK_DESIGN_KB_ID     = (optional: FORGE KB ID for design patterns/examples; leave empty for MVP)
COWORK_DESIGN_DS_IDS    = (optional: comma-separated data source IDs)
DESIGN_S3_BUCKET        = fip-cowork-workspaces   (same bucket as other agents)
DESIGN_S3_PREFIX        = design                   (S3 key prefix for generated artifacts)
```

---

## 6. System Prompt

**File: `agents/design/system-prompt.md`**

This is the core of the Design Agent. The brand context section is injected at runtime via template substitution — `{{BRAND_CONTEXT}}` is replaced with the org's `brand.json` fields before the prompt is sent to Claude.

```markdown
# Design Agent — System Prompt

You are a UI/UX design specialist. You generate pixel-perfect, accessible, 
responsive HTML/CSS interfaces from natural language descriptions.

## Output Rules

1. **Always produce complete, self-contained HTML files.**
   - All CSS inline in a `<style>` block in `<head>` — no external stylesheets
   - No CDN links (no Bootstrap, no Tailwind, no Font Awesome)
   - No JavaScript unless the user explicitly requests interactivity
   - The HTML file must render correctly when opened standalone in a browser

2. **Use CSS custom properties for all design tokens.**
   Every design element that could vary (color, spacing, radius, font) 
   must be defined as a CSS variable in `:root`. This enables easy theming.
   ```css
   :root {
     --color-primary:    <brand primary>;
     --color-secondary:  <brand secondary>;
     --color-bg:         <brand background>;
     --color-text:       <brand text>;
     --color-border:     #e5e7eb;
     --font-sans:        <brand font>, system-ui, sans-serif;
     --radius-sm:        4px;
     --radius-md:        8px;
     --radius-lg:        12px;
     --shadow-sm:        0 1px 3px rgba(0,0,0,0.1);
     --shadow-md:        0 4px 12px rgba(0,0,0,0.12);
   }
   ```

3. **Mobile-first responsive layout.**
   Default layout works on 320px width. Use CSS Grid or Flexbox.
   Add `@media (min-width: 768px)` breakpoints for tablet and desktop.
   Include `<meta name="viewport" content="width=device-width, initial-scale=1">`.

4. **Semantic HTML5.**
   Use `<header>`, `<nav>`, `<main>`, `<section>`, `<article>`, `<aside>`, 
   `<footer>`, `<button>`, `<input>` appropriately.
   Every interactive element must have a visible focus state.
   Images must have `alt` attributes.

5. **No Lorem Ipsum unless asked.**
   Use realistic placeholder content relevant to the described interface.
   If the user mentions an industry or company context, use appropriate 
   terminology and realistic data.

6. **File naming convention.**
   Save generated HTML as `screen.html` in the working directory.
   For variants: `screen_varA.html`, `screen_varB.html`, `screen_varC.html`.
   For edits: use the same filename — the task runner handles versioning externally.

7. **Write a brief description before the HTML.**
   Format:
   ```
   DESIGN SUMMARY: [one sentence description of what was generated]
   TOKENS USED: [list the CSS variables defined]
   DEVICE TARGET: [mobile | tablet | desktop | responsive]
   ```
   Then output the complete HTML.

## Brand Context

Apply these brand tokens to all generated designs:

{{BRAND_CONTEXT}}

When brand tokens are not provided, use these Fortress AM defaults:
- Primary color: #1a2332 (Fortress Navy)
- Accent color: #d4af37 (Fortress Gold)
- Background: #ffffff
- Text: #1a2332
- Font: Inter, system-ui, sans-serif
- Border radius: 8px (medium), 12px (card)
- Shadow: 0 1px 3px rgba(0,0,0,0.08) (light), 0 4px 16px rgba(0,0,0,0.12) (card)

## Component Vocabulary

When generating UI components, use these established patterns.
This vocabulary should be consistent across all designs for the same org.

### Buttons
```css
.btn-primary {
  background: var(--color-primary);
  color: #fff;
  padding: 10px 20px;
  border-radius: var(--radius-md);
  border: none;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.15s;
}
.btn-primary:hover { opacity: 0.88; }
.btn-outline {
  background: transparent;
  color: var(--color-primary);
  border: 1.5px solid var(--color-primary);
  padding: 9px 19px;
  border-radius: var(--radius-md);
  font-weight: 600;
  cursor: pointer;
}
```

### Cards
```css
.card {
  background: #fff;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg);
  padding: 20px 24px;
  box-shadow: var(--shadow-sm);
}
```

### Form inputs
```css
.input {
  border: 1.5px solid var(--color-border);
  border-radius: var(--radius-md);
  padding: 9px 12px;
  font-size: 14px;
  width: 100%;
  transition: border-color 0.15s;
}
.input:focus {
  outline: none;
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(26,35,50,0.08);
}
```

### Navigation
```css
.nav {
  background: var(--color-primary);
  color: #fff;
  padding: 0 24px;
  height: 56px;
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.nav-link { color: rgba(255,255,255,0.75); text-decoration: none; font-size: 14px; font-weight: 500; }
.nav-link.active, .nav-link:hover { color: #fff; }
```

## Blazor Conversion Rules

When the user requests Blazor component output (`CONVERT_TO_BLAZOR: true`), apply these rules:

1. **Convert HTML elements to their MudBlazor equivalents** where available:
   - `<button class="btn-primary">` → `<MudButton Variant="Variant.Filled" Color="Color.Primary">`
   - `<input type="text">` → `<MudTextField />`
   - `<select>` → `<MudSelect />`
   - `<input type="checkbox">` → `<MudCheckBox />`
   - Navigation icon → `<MudIcon Icon="@Icons.Material.Outlined.{Name}" />`
   
2. **Keep custom CSS for non-MudBlazor layout** — grid layout, spacing, background colors stay as CSS.

3. **Output format:**
   ```razor
   @namespace CoworkWeb.Generated
   
   <div class="generated-screen">
       @* MudBlazor components and layout HTML *@
   </div>
   
   <style>
       /* Custom CSS that MudBlazor doesn't handle */
       .generated-screen { ... }
   </style>
   
   @code {
       // Component parameters if any were identified
   }
   ```

4. **Parameter extraction:**  
   Any hardcoded text that could reasonably be a parameter 
   (page title, labels, button text) should be extracted as `[Parameter]` properties 
   with sensible defaults.

5. **Do NOT use inline Variant=, Color=, Size= unless specifically required.**
   Prefer CSS class overrides following the FIP design system.

## Variant Generation Instructions

When generating variants (variantCount > 1), treat each variant as a completely 
independent design direction. Do NOT produce subtle tweaks — produce genuinely 
different approaches:

- **Variant A (REFINED):** Clean, minimal, high contrast. Maximum whitespace.
  Typography-led hierarchy. Restrained use of color.

- **Variant B (CONTEMPORARY):** Card-based layouts, soft shadows, brand accent 
  color as highlight. Modern SaaS aesthetic. Subtle gradients acceptable.

- **Variant C (BOLD):** Strong visual hierarchy, prominent hero or header area,
  brand primary color as dominant. High visual impact.

Each variant must be a complete, standalone HTML file. No cross-references between variants.

## What NOT to Generate

- No dark mode variants unless explicitly requested
- No animations that could cause motion sickness (no auto-playing carousels, no constant motion)
- No fixed-position elements that overlap content on mobile without testing
- No placeholder images from external URLs (use CSS background gradients or SVG placeholders instead)
- No external fonts via `<link>` (use system font stack or `@font-face` with base64 if critical)
```

---

## 7. Brand Service

### 7.1 `services/brandService.ts`

Brand context is per-org. In Phase 1, Fortress AM is the only org — brand is loaded from a static S3 file. Phase 2 adds per-org admin UI to manage brand tokens.

```typescript
import { S3Client, GetObjectCommand, PutObjectCommand } from '@aws-sdk/client-s3';

export interface BrandContext {
  orgId:          string;
  primaryColor:   string;     // CSS hex, e.g. "#1a2332"
  secondaryColor: string;
  accentColor:    string;
  backgroundColor: string;
  textColor:      string;
  fontFamily:     string;     // CSS font-family string
  logoUrl?:       string;     // S3 presigned URL for logo (optional)
  borderRadius:   { sm: string; md: string; lg: string };
  shadow:         { sm: string; md: string };
  customRules?:   string;     // freeform CSS rules to inject (e.g. custom components)
}

const BRAND_BUCKET  = process.env.DESIGN_S3_BUCKET ?? 'fip-cowork-workspaces';
const BRAND_PREFIX  = 'design/brand';
const s3            = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });

// In-process cache: orgId → {brand, loadedAt}
const cache = new Map<string, { brand: BrandContext; loadedAt: number }>();
const CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

export async function getBrandContext(orgId: string): Promise<BrandContext> {
  const cached = cache.get(orgId);
  if (cached && Date.now() - cached.loadedAt < CACHE_TTL_MS) {
    return cached.brand;
  }

  try {
    const key = `${BRAND_PREFIX}/${orgId}/brand.json`;
    const resp = await s3.send(new GetObjectCommand({
      Bucket: BRAND_BUCKET,
      Key:    key,
    }));
    const raw  = await resp.Body!.transformToString();
    const brand = JSON.parse(raw) as BrandContext;
    cache.set(orgId, { brand, loadedAt: Date.now() });
    return brand;
  } catch {
    // Org has no brand file — return Fortress AM defaults
    return getFortressDefaults(orgId);
  }
}

export async function saveBrandContext(orgId: string, brand: BrandContext): Promise<void> {
  const key = `${BRAND_PREFIX}/${orgId}/brand.json`;
  await s3.send(new PutObjectCommand({
    Bucket:      BRAND_BUCKET,
    Key:         key,
    Body:        JSON.stringify(brand, null, 2),
    ContentType: 'application/json',
  }));
  cache.delete(orgId); // invalidate cache
}

export function formatBrandContextForPrompt(brand: BrandContext): string {
  return `
Primary color:    ${brand.primaryColor}
Secondary color:  ${brand.secondaryColor}
Accent color:     ${brand.accentColor}
Background:       ${brand.backgroundColor}
Text:             ${brand.textColor}
Font family:      ${brand.fontFamily}
Border radius SM: ${brand.borderRadius.sm}
Border radius MD: ${brand.borderRadius.md}
Border radius LG: ${brand.borderRadius.lg}
Shadow SM:        ${brand.shadow.sm}
Shadow MD:        ${brand.shadow.md}
${brand.customRules ? `Custom CSS rules:\n${brand.customRules}` : ''}
`.trim();
}

function getFortressDefaults(orgId: string): BrandContext {
  return {
    orgId,
    primaryColor:    '#1a2332',
    secondaryColor:  '#2c3e58',
    accentColor:     '#d4af37',
    backgroundColor: '#ffffff',
    textColor:       '#1a2332',
    fontFamily:      'Inter, system-ui, -apple-system, sans-serif',
    logoUrl:         undefined,
    borderRadius:    { sm: '4px', md: '8px', lg: '12px' },
    shadow:          {
      sm: '0 1px 3px rgba(0,0,0,0.08)',
      md: '0 4px 16px rgba(0,0,0,0.12)',
    },
  };
}
```

### 7.2 S3 Layout for Brand Files

```
s3://fip-cowork-workspaces/
└── design/
    ├── brand/
    │   └── {orgId}/
    │       ├── brand.json          ← brand tokens
    │       └── logo.svg            ← optional logo upload
    ├── projects/
    │   └── {orgId}/
    │       └── {projectId}/
    │           └── screens/
    │               ├── {screenId}_v1.html
    │               ├── {screenId}_v2.html
    │               ├── {screenId}_varA.html
    │               ├── {screenId}_varB.html
    │               └── {screenId}_varC.html
    └── refs/
        └── {taskId}/
            └── {filename}          ← uploaded reference images
```

---

## 8. Design Runner

**File: `agents/design/runner.ts`**

The design runner is a thin wrapper around the generic `runTask` from `runner.ts`, with pre-processing to inject brand context and post-processing to version-stamp generated HTML.

```typescript
import path from 'path';
import fs from 'fs/promises';
import crypto from 'crypto';
import { S3Client, PutObjectCommand, GetObjectCommand } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { getBrandContext, formatBrandContextForPrompt } from '../../services/brandService.js';
import type { SseChunk } from '../../agent/runner.js';  // P1 fix: correct path (singular 'agent', up two levels)

export interface DesignTaskParams {
  taskId:        string;
  userId:        string;
  userEmail:     string;
  orgId:         string;
  projectId:     string;
  screenId?:     string;      // set for edits; undefined for new screens
  priorHtml?:    string;      // for edit tasks
  prompt:        string;
  variantCount:  1 | 2 | 3;  // 1 = single generate, 3 = variants
  deviceTarget:  'mobile' | 'desktop' | 'responsive';
  convertToBlazor: boolean;
  referenceFiles?: string[];  // S3 keys of uploaded reference images
}

const S3_BUCKET  = process.env.DESIGN_S3_BUCKET ?? 'fip-cowork-workspaces';
const S3_PREFIX  = process.env.DESIGN_S3_PREFIX ?? 'design';
const s3         = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });

export async function runDesignTask(
  params: DesignTaskParams,
  emit: (chunk: SseChunk) => void
): Promise<void> {

  const { taskId, orgId, projectId, prompt, variantCount,
          deviceTarget, convertToBlazor, priorHtml, screenId } = params;

  // ── 1. Load brand context ─────────────────────────────────────────────
  emit({ type: 'step', text: 'Loading brand context...' });
  const brand = await getBrandContext(orgId);
  const brandBlock = formatBrandContextForPrompt(brand);

  // ── 2. Build system prompt ────────────────────────────────────────────
  const rawSystemPrompt = await fs.readFile(
    path.join(process.cwd(), 'agents/design/system-prompt.md'), 'utf8');
  const systemPrompt = rawSystemPrompt.replace('{{BRAND_CONTEXT}}', brandBlock);

  // ── 3. Build user prompt ──────────────────────────────────────────────
  let userPrompt: string;

  if (priorHtml) {
    // Edit mode: inject prior HTML
    userPrompt = [
      'Here is the current design. Apply the requested change — preserve all elements',
      'not mentioned in the change request:\n',
      '```html',
      priorHtml,
      '```\n',
      `Requested change: ${prompt}`,
      `Device target: ${deviceTarget}`,
    ].join('\n');
  } else if (variantCount > 1) {
    // Variant mode: generate 3 parallel designs
    await runVariantTask(params, systemPrompt, brand, emit);
    return;
  } else {
    // New screen
    userPrompt = [
      `Generate a UI screen: ${prompt}`,
      `Device target: ${deviceTarget}`,
      'Save the complete HTML to screen.html in the working directory.',
    ].join('\n');
  }

  // ── 4. Run Claude agent ───────────────────────────────────────────────
  emit({ type: 'step', text: 'Generating design...' });

  const workingDir = `/tmp/cowork-${taskId}`;
  await fs.mkdir(workingDir, { recursive: true });

  // Import and run generic task runner with design system prompt override
  const { runTask } = await import('../../agent/runner.js');
  await runTask(
    { taskId, userId: params.userId, userEmail: params.userEmail,
      prompt: userPrompt, workingDir, maxBudgetUsd: 0.50, maxTurns: 8,
      systemPromptOverride: systemPrompt },  // systemPromptOverride in TaskParams (P1 fix)
    emit
  );

  // ── 5. Find generated HTML ────────────────────────────────────────────
  const generated = await findGeneratedHtml(workingDir);
  if (!generated) {
    emit({ type: 'error', text: 'No HTML file was generated. Try a more specific prompt.' });
    return;
  }

  // ── 6. Version and upload to S3 ───────────────────────────────────────
  const sid      = screenId ?? crypto.randomUUID();
  const version  = await getNextVersion(orgId, projectId, sid);
  const s3Key    = `${S3_PREFIX}/projects/${orgId}/${projectId}/screens/${sid}_v${version}.html`;

  const htmlContent = await fs.readFile(generated, 'utf8');
  await s3.send(new PutObjectCommand({
    Bucket:      S3_BUCKET,
    Key:         s3Key,
    Body:        htmlContent,
    ContentType: 'text/html',
    Metadata: {
      'org-id':    orgId,
      'project-id': projectId,
      'screen-id': sid,
      'version':   String(version),
      'task-id':   taskId,
    },
  }));

  // ── 7. Generate presigned download URL ───────────────────────────────
  const downloadUrl = await getSignedUrl(
    s3,
    new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3Key }),
    { expiresIn: 3600 }
  );

  // ── 8. Register screen version in Redis ──────────────────────────────
  const { getRedis } = await import('../../services/taskStore.js');
  const redis = await getRedis();
  const versionKey = `design:screen:${orgId}:${projectId}:${sid}:versions`;
  await redis.rpush(versionKey, JSON.stringify({
    version, s3Key, taskId,
    createdAt: new Date().toISOString(),
    prompt,
  }));
  await redis.expire(versionKey, 60 * 60 * 24 * 30); // 30-day TTL

  // ── 9. Emit file_output with HTML source ─────────────────────────────
  emit({
    type:        'file_output',
    outputType:  'html',
    fileName:    `screen_v${version}.html`,
    downloadUrl,
    sizeBytes:   Buffer.byteLength(htmlContent, 'utf8'),
  });

  // Also emit the HTML source inline for "Copy Code" clipboard
  emit({
    type: 'result',
    text: JSON.stringify({
      screenId:    sid,
      version,
      projectId,
      htmlSource:  htmlContent,
      downloadUrl,
      s3Key,
    }),
  });

  // ── 10. Optional: Blazor conversion pass ─────────────────────────────
  if (convertToBlazor) {
    emit({ type: 'step', text: 'Converting to Blazor component...' });
    await runBlazorConversion(
      taskId, htmlContent, orgId, projectId, sid, version, emit);
  }
}

// ── Variant generation ────────────────────────────────────────────────────

async function runVariantTask(
  params: DesignTaskParams,
  systemPrompt: string,
  brand: BrandContext,
  emit: (chunk: SseChunk) => void
): Promise<void> {
  const { taskId, orgId, projectId, prompt, deviceTarget } = params;

  const variantInstructions = [
    { suffix: 'varA', style: 'REFINED — clean, minimal, maximum whitespace. Typography-led. Restrained color.' },
    { suffix: 'varB', style: 'CONTEMPORARY — card-based, soft shadows, brand accent highlights. Modern SaaS.' },
    { suffix: 'varC', style: 'BOLD — strong hero, prominent brand color, high visual impact.' },
  ];

  emit({ type: 'step', text: 'Generating 3 design variants in parallel...' });

  const { runTask } = await import('../runner.js');
  const screenId    = crypto.randomUUID();

  // P2 fix: stagger parallel Bedrock calls by 500ms to reduce throttling risk
  const results = await Promise.allSettled(
    variantInstructions.map(async (variant, i) => {
      if (i > 0) await new Promise(r => setTimeout(r, i * 500)); // 0ms, 500ms, 1000ms
      const varWorkingDir = `/tmp/cowork-${taskId}-${variant.suffix}`;
      await fs.mkdir(varWorkingDir, { recursive: true });

      const varPrompt = [
        `Generate a UI screen — variant ${i + 1} of 3.`,
        `Design direction: ${variant.style}`,
        `Screen description: ${prompt}`,
        `Device target: ${deviceTarget}`,
        `Save as screen.html in the working directory.`,
      ].join('\n');

      const chunks: SseChunk[] = [];
      await runTask(
        { taskId: `${taskId}-${variant.suffix}`, userId: params.userId,
          userEmail: params.userEmail, prompt: varPrompt,
          workingDir: varWorkingDir, maxBudgetUsd: 0.35, maxTurns: 6,
          systemPromptOverride: systemPrompt },  // P1 fix: systemPromptOverride in params
        (chunk) => chunks.push(chunk)
      );

      const generated = await findGeneratedHtml(varWorkingDir);
      if (!generated) return null;

      const htmlContent = await fs.readFile(generated, 'utf8');
      const s3Key = `${S3_PREFIX}/projects/${orgId}/${projectId}/screens/${screenId}_${variant.suffix}.html`;

      await s3.send(new PutObjectCommand({
        Bucket: S3_BUCKET, Key: s3Key, Body: htmlContent, ContentType: 'text/html',
        Metadata: { 'org-id': orgId, 'project-id': projectId, 'screen-id': screenId,
                    'variant': variant.suffix, 'task-id': taskId },
      }));

      const downloadUrl = await getSignedUrl(
        s3, new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3Key }), { expiresIn: 3600 });

      return { suffix: variant.suffix, downloadUrl, htmlContent, s3Key };
    })
  );

  const variants = results
    .filter(r => r.status === 'fulfilled' && r.value !== null)
    .map(r => (r as PromiseFulfilledResult<any>).value);

  // Emit all variants as file_output chunks
  for (const v of variants) {
    emit({
      type: 'file_output', outputType: 'html',
      fileName: `screen_${v.suffix}.html`,
      downloadUrl: v.downloadUrl,
      sizeBytes: Buffer.byteLength(v.htmlContent, 'utf8'),
    });
  }

  // Emit consolidated result for workspace to parse variant list
  emit({
    type: 'result',
    text: JSON.stringify({
      screenId,
      projectId,
      isVariants: true,
      variants: variants.map(v => ({
        label:       v.suffix === 'varA' ? 'Refined' : v.suffix === 'varB' ? 'Contemporary' : 'Bold',
        suffix:      v.suffix,
        downloadUrl: v.downloadUrl,
        s3Key:       v.s3Key,
      })),
    }),
  });

  emit({ type: 'step', text: `${variants.length} variants generated.` });
}

// ── Blazor conversion ─────────────────────────────────────────────────────

async function runBlazorConversion(
  taskId: string, htmlContent: string, orgId: string,
  projectId: string, screenId: string, version: number,
  emit: (chunk: SseChunk) => void
): Promise<void> {
  const { runTask } = await import('../runner.js');

  const conversionPrompt = [
    'Convert the following HTML/CSS design to a Blazor Razor component.',
    'Follow these rules:',
    '1. Use MudBlazor components where appropriate (MudButton, MudTextField, MudSelect, etc.)',
    '2. Keep custom CSS for layout, spacing, and brand-specific styling',
    '3. Extract hardcoded text as [Parameter] properties with defaults',
    '4. Output the .razor file as component.razor in the working directory',
    '5. Do not add any functionality beyond what is visible in the HTML',
    '\nHTML to convert:\n```html\n',
    htmlContent,
    '\n```',
  ].join('\n');

  const convWorkingDir = `/tmp/cowork-${taskId}-blazor`;
  await fs.mkdir(convWorkingDir, { recursive: true });

  await runTask(
    { taskId: `${taskId}-blazor`, userId: 'system', userEmail: 'system',
      prompt: conversionPrompt, workingDir: convWorkingDir,
      maxBudgetUsd: 0.25, maxTurns: 4 },
    // No systemPromptOverride — Blazor conversion uses generic runner default
    (chunk) => {
      if (chunk.type !== 'step' && chunk.type !== 'tool_call') return; // suppress verbose
    }
  );

  const razorFile = path.join(convWorkingDir, 'component.razor');
  try {
    const razorContent = await fs.readFile(razorFile, 'utf8');
    const s3Key = `${S3_PREFIX}/projects/${orgId}/${projectId}/screens/${screenId}_v${version}.razor`;

    await s3.send(new PutObjectCommand({
      Bucket: S3_BUCKET, Key: s3Key, Body: razorContent, ContentType: 'text/plain',
    }));

    const downloadUrl = await getSignedUrl(
      s3, new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3Key }), { expiresIn: 3600 });

    emit({
      type: 'file_output', outputType: 'other',
      fileName: `component_v${version}.razor`,
      downloadUrl,
      sizeBytes: Buffer.byteLength(razorContent, 'utf8'),
    });

    emit({ type: 'step', text: 'Blazor component ready for download.' });
  } catch {
    emit({ type: 'step', text: 'Blazor conversion complete — check output files.' });
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────

async function findGeneratedHtml(dir: string): Promise<string | null> {
  try {
    const files = await fs.readdir(dir);
    const html  = files.find(f => f.endsWith('.html'));
    return html ? path.join(dir, html) : null;
  } catch { return null; }
}

async function getNextVersion(
  orgId: string, projectId: string, screenId: string
): Promise<number> {
  const { getRedis } = await import('../../services/taskStore.js');
  const redis  = await getRedis();
  const key    = `design:screen:${orgId}:${projectId}:${screenId}:versions`;
  const len    = await redis.llen(key);
  return len + 1;
}
```

**Note to Tony — `runTask` signature change (P1 fix):**

`runTask` is an `async function*` (AsyncGenerator) — adding a positional parameter would break TypeScript. Instead, add `systemPromptOverride` to the existing `TaskParams` interface. **Do NOT change the function signature.**

In `agent/runner.ts`, update `TaskParams`:

```typescript
// In agent/runner.ts — update the existing interface:
interface TaskParams {
  taskId:                string;
  userId:                string;
  userEmail:             string;
  prompt:                string;
  workingDir:            string;
  maxBudgetUsd:          number;
  maxTurns:              number;
  systemPromptOverride?: string;  // ← add this optional field only
}
```

Inside `runTask`, replace the `SYSTEM_PROMPT` constant reference with:

```typescript
const effectiveSystemPrompt = params.systemPromptOverride?.trim()
  ? params.systemPromptOverride
  : SYSTEM_PROMPT;
```

The existing call site in `routes/tasks.ts` passes no `systemPromptOverride` — it is optional and defaults to the built-in prompt. No change needed there.

The design runner passes it explicitly:

```typescript
await runTask({
  taskId, userId: params.userId, userEmail: params.userEmail,
  prompt: userPrompt, workingDir, maxBudgetUsd: 0.50, maxTurns: 8,
  systemPromptOverride: systemPrompt,   // ← use design system prompt
});
```

---

## 9. Route Handlers

**File: `routes/design.ts`**

```typescript
import express from 'express';
import multer from 'multer';
import crypto from 'crypto';
import { runDesignTask } from '../agents/design/runner.js';
import { saveBrandContext } from '../services/brandService.js';
import { getRedis } from '../services/taskStore.js';
import { createTaskMeta, updateTaskComplete, updateTaskFailed,
         publishChunk, subscribeToTask } from '../services/taskStore.js';
import { uploadInputsToS3 } from '../services/fileService.js';
import type { AuthedRequest } from '../middleware/auth.js';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/', limits: { fileSize: 10 * 1024 * 1024 } });

// ── POST /agents/design/projects/:projectId/screens ───────────────────────
// Generate a new screen (or 3 variants)
router.post(
  '/projects/:projectId/screens',
  upload.array('refs', 3),
  async (req, res) => {
    const authed    = req as unknown as AuthedRequest;
    const { projectId } = req.params;
    const {
      prompt, deviceTarget = 'responsive',
      variantCount = '1', convertToBlazor = 'false', orgId,
    } = req.body as Record<string, string>;

    if (!prompt?.trim()) { res.status(400).json({ error: 'prompt required' }); return; }

    const taskId  = crypto.randomUUID();
    const screenId = crypto.randomUUID();

    // Upload reference images to S3 if attached
    const files = req.files as Express.Multer.File[] | undefined;
    if (files?.length) await uploadInputsToS3(files, taskId);

    await createTaskMeta(taskId, {
      userId:    authed.userId,
      userEmail: authed.userEmail,
      prompt,
      agentId:   'design',
    });

    res.json({ taskId, screenId });

    // Run async
    (async () => {
      try {
        await runDesignTask(
          { taskId, userId: authed.userId, userEmail: authed.userEmail,
            orgId: orgId ?? 'fortress-am', projectId, screenId,
            prompt, deviceTarget: deviceTarget as any,
            variantCount: Math.min(parseInt(variantCount), 3) as 1 | 2 | 3,
            convertToBlazor: convertToBlazor === 'true',
          },
          (chunk) => publishChunk(taskId, chunk)
        );
        await updateTaskComplete(taskId);
      } catch (err: any) {
        await updateTaskFailed(taskId, err.message);
        await publishChunk(taskId, { type: 'error', text: err.message });
      }
    })();
  }
);

// ── POST /agents/design/projects/:projectId/screens/:screenId/edit ────────
// Edit an existing screen (iterative refinement)
router.post(
  '/projects/:projectId/screens/:screenId/edit',
  async (req, res) => {
    const authed = req as unknown as AuthedRequest;
    const { projectId, screenId } = req.params;
    const { prompt, priorHtml, orgId, deviceTarget = 'responsive' } = req.body as Record<string, string>;

    if (!prompt?.trim())    { res.status(400).json({ error: 'prompt required' });    return; }
    if (!priorHtml?.trim()) { res.status(400).json({ error: 'priorHtml required' }); return; }

    const taskId = crypto.randomUUID();
    await createTaskMeta(taskId, {
      userId:    authed.userId,
      userEmail: authed.userEmail,
      prompt,
      agentId:   'design',
    });

    res.json({ taskId, screenId });

    (async () => {
      try {
        await runDesignTask(
          { taskId, userId: authed.userId, userEmail: authed.userEmail,
            orgId: orgId ?? 'fortress-am', projectId, screenId,
            priorHtml, prompt, deviceTarget: deviceTarget as any,
            variantCount: 1, convertToBlazor: false },
          (chunk) => publishChunk(taskId, chunk)
        );
        await updateTaskComplete(taskId);
      } catch (err: any) {
        await updateTaskFailed(taskId, err.message);
        await publishChunk(taskId, { type: 'error', text: err.message });
      }
    })();
  }
);

// ── GET /agents/design/projects/:projectId/screens/:screenId/versions ─────
// Get version history for a screen
router.get('/projects/:projectId/screens/:screenId/versions', async (req, res) => {
  const authed = req as unknown as AuthedRequest;
  const { projectId, screenId } = req.params;
  const orgId = (req.query.orgId as string) ?? 'fortress-am';

  const redis = await getRedis();
  const key   = `design:screen:${orgId}:${projectId}:${screenId}:versions`;
  const raw   = await redis.lrange(key, 0, -1);
  const versions = raw.map((v: string) => JSON.parse(v));
  res.json({ screenId, projectId, versions });
});

// ── GET /agents/design/tasks/:taskId/stream ───────────────────────────────
// SSE stream for design task progress (same pattern as generic tasks)
router.get('/tasks/:taskId/stream', async (req, res) => {
  const { taskId } = req.params;
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');

  const cleanup = await subscribeToTask(taskId, (chunk) => {
    res.write(`data: ${JSON.stringify(chunk)}\n\n`);
    if (chunk.type === 'result' || chunk.type === 'error') {
      cleanup();
      res.end();
    }
  });

  req.on('close', cleanup);
});

// ── PUT /agents/design/brand/:orgId ───────────────────────────────────────
// Save brand context for an org (admin only in production)
router.put('/brand/:orgId', async (req, res) => {
  const { orgId } = req.params;
  const brand = req.body;
  await saveBrandContext(orgId, brand);
  res.json({ ok: true });
});

export default router;
```

**In `server.ts`**, mount the router:

```typescript
import designRouter from './routes/design.js';
// ...
app.use('/agents/design', authenticate, designRouter);
```

---

## 10. CoworkWeb Workspace UI

**File: `Components/Pages/Agents/Workspaces/DesignWorkspace.razor`**

The Design Workspace is a three-panel layout:
- **Left panel:** project/screen history (past designs, version list)
- **Center panel:** live preview iframe + controls (device size, variant tabs if applicable)
- **Right panel:** prompt input + brand context indicator + action buttons

```razor
@namespace CoworkWeb.Components.Pages.Agents.Workspaces
@inject AgentApiClient AgentApi
@inject IJSRuntime JS
@inject ISnackbar Snackbar

<div class="design-workspace">

    @* ── Left: History ──────────────────────────────────────────────── *@
    <div class="design-panel design-panel--history">
        <div class="design-panel-header">
            <span>Project History</span>
            <MudButton Size="Size.Small" Variant="Variant.Text"
                       OnClick="NewProject">+ New Project</MudButton>
        </div>

        @if (!_screens.Any())
        {
            <div class="design-history-empty">
                No screens yet. Describe a UI to generate your first design.
            </div>
        }
        else
        {
            <div class="design-history-list">
                @foreach (var screen in _screens.OrderByDescending(s => s.CreatedAt))
                {
                    var isActive = screen.ScreenId == _activeScreenId;
                    <div class="@($"design-history-item{(isActive ? " design-history-item--active" : "")}")"
                         @onclick="() => LoadScreen(screen)">
                        <div class="design-history-prompt">@TruncatePrompt(screen.Prompt)</div>
                        <div class="design-history-meta">
                            v@(screen.Version) · @screen.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt")
                        </div>
                    </div>
                }
            </div>
        }
    </div>

    @* ── Center: Preview ────────────────────────────────────────────── *@
    <div class="design-panel design-panel--preview">

        <div class="design-preview-toolbar">
            @* Device size toggle *@
            <div class="design-device-toggle">
                <MudButtonGroup Size="Size.Small">
                    <MudButton Class="@DeviceClass("mobile")"
                               StartIcon="@Icons.Material.Outlined.PhoneAndroid"
                               OnClick="() => SetDevice(""mobile"")">Mobile</MudButton>
                    <MudButton Class="@DeviceClass("desktop")"
                               StartIcon="@Icons.Material.Outlined.DesktopWindows"
                               OnClick="() => SetDevice(""desktop"")">Desktop</MudButton>
                    <MudButton Class="@DeviceClass("responsive")"
                               StartIcon="@Icons.Material.Outlined.Devices"
                               OnClick="() => SetDevice(""responsive"")">Full</MudButton>
                </MudButtonGroup>
            </div>

            @* Variant tabs (shown only when 3 variants generated) *@
            @if (_variants.Count > 1)
            {
                <div class="design-variant-tabs">
                    @foreach (var v in _variants)
                    {
                        <span class="@($"design-variant-tab{(_activeVariant == v.Suffix ? " active" : "")}")"
                              @onclick="() => SelectVariant(v)">
                            @v.Label
                        </span>
                    }
                </div>
            }

            @* Export buttons *@
            <div style="display:flex; gap:6px; margin-left:auto;">
                @if (!string.IsNullOrEmpty(_activeDownloadUrl))
                {
                    <MudButton Size="Size.Small" Variant="Variant.Outlined"
                               StartIcon="@Icons.Material.Outlined.Download"
                               Href="@_activeDownloadUrl" Target="_blank">
                        HTML
                    </MudButton>
                    <MudButton Size="Size.Small" Variant="Variant.Outlined"
                               StartIcon="@Icons.Material.Outlined.ContentCopy"
                               OnClick="CopyCode">
                        Copy Code
                    </MudButton>
                }
            </div>
        </div>

        @* Preview iframe *@
        <div class="design-preview-frame-container @GetFrameClass()">
            @if (_generating)
            {
                <div class="design-generating-overlay">
                    <MudProgressCircular Indeterminate="true" Color="Color.Primary" />
                    <div class="design-generating-text">@_generatingStatus</div>
                </div>
            }
            else if (string.IsNullOrEmpty(_activeDownloadUrl))
            {
                <div class="design-empty-preview">
                    <MudIcon Icon="@Icons.Material.Outlined.Palette"
                             Style="font-size:48px; color:#d1d5db;" />
                    <div>Describe a screen to generate your first design</div>
                </div>
            }
            else
            {
                <iframe src="@_activeDownloadUrl"
                        class="design-preview-iframe"
                        sandbox="allow-scripts"
                        title="Design preview" />
            }
        </div>

        @* Version history strip (shown when editing existing screen) *@
        @if (_versions.Count > 1)
        {
            <div class="design-version-strip">
                @foreach (var ver in _versions)
                {
                    <span class="@($"design-version-dot{(ver.Version == _activeVersion ? " active" : "")}")"
                          title="v@(ver.Version) — @ver.CreatedAt.ToLocalTime().ToString("MMM d h:mm tt")"
                          @onclick="() => RestoreVersion(ver)">
                        v@(ver.Version)
                    </span>
                }
            </div>
        }

        @* Blazor component download (shown after conversion) *@
        @if (!string.IsNullOrEmpty(_blazorDownloadUrl))
        {
            <div class="design-blazor-badge">
                <MudIcon Icon="@Icons.Material.Outlined.Code" Style="font-size:14px;" />
                <span>Blazor component ready</span>
                <MudButton Size="Size.Small" Variant="Variant.Text"
                           Href="@_blazorDownloadUrl" Target="_blank">
                    Download .razor
                </MudButton>
            </div>
        }

    </div>

    @* ── Right: Prompt & Controls ────────────────────────────────────── *@
    <div class="design-panel design-panel--controls">

        @* Brand indicator *@
        <div class="design-brand-indicator">
            <div class="design-brand-swatch"
                 style="background:@_brandPrimary;"></div>
            <span class="design-brand-name">@_brandName brand</span>
        </div>

        @* Prompt input *@
        <div class="design-prompt-section">
            <MudTextField @bind-Value="_prompt"
                          Label="Describe your design"
                          Lines="4"
                          Placeholder="A dashboard page with a sidebar nav, KPI cards at the top, and a data table below. Use the Fortress Navy color scheme."
                          Variant="Variant.Outlined" />
        </div>

        @* Options row *@
        <div class="design-options-row">
            <MudSelect @bind-Value="_deviceTarget" Label="Device" Dense="true"
                       Variant="Variant.Outlined" Style="max-width:110px;">
                <MudSelectItem Value="@("responsive")">Responsive</MudSelectItem>
                <MudSelectItem Value="@("desktop")">Desktop</MudSelectItem>
                <MudSelectItem Value="@("mobile")">Mobile</MudSelectItem>
            </MudSelect>

            <MudCheckBox @bind-Value="_generateVariants" Label="3 variants" Dense="true" />
            <MudCheckBox @bind-Value="_convertToBlazor"  Label="+ Blazor"   Dense="true" />
        </div>

        @* Reference image upload *@
        <div class="design-ref-upload">
            <InputFile id="design-ref-input" OnChange="OnRefImageSelected"
                       accept="image/*" style="display:none;" multiple />
            <MudButton Size="Size.Small" Variant="Variant.Text"
                       StartIcon="@Icons.Material.Outlined.Image"
                       OnClick="OpenRefPicker">
                Add reference images
            </MudButton>
            @if (_refImages.Any())
            {
                <div class="design-ref-thumbnails">
                    @foreach (var img in _refImages)
                    {
                        <span class="design-ref-thumb">
                            @img.Name
                            <span @onclick="() => RemoveRef(img)">×</span>
                        </span>
                    }
                </div>
            }
        </div>

        @* Generate button *@
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   FullWidth="true"
                   StartIcon="@Icons.Material.Outlined.AutoAwesome"
                   OnClick="@(string.IsNullOrEmpty(_activeDownloadUrl) ? Generate : EditCurrent)"
                   Disabled="@(_generating || string.IsNullOrWhiteSpace(_prompt))">
            @if (_generating)
            {
                <span>Generating...</span>
            }
            else if (!string.IsNullOrEmpty(_activeDownloadUrl))
            {
                <span>Apply Changes</span>
            }
            else
            {
                <span>Generate Design</span>
            }
        </MudButton>

        @if (!string.IsNullOrEmpty(_activeDownloadUrl))
        {
            <MudButton Variant="Variant.Outlined" Color="Color.Default"
                       FullWidth="true"
                       OnClick="GenerateNew">
                New Screen
            </MudButton>
        }

        @* Step log (live generation feedback) *@
        @if (_stepLog.Any())
        {
            <div class="design-step-log">
                @foreach (var step in _stepLog.TakeLast(4))
                {
                    <div class="design-step-entry">@step</div>
                }
            </div>
        }

    </div>

</div>

@code {
    [Parameter] public string OrgId { get; set; } = "fortress-am";

    private string _prompt          = "";
    private string _deviceTarget    = "responsive";
    private bool   _generateVariants = false;
    private bool   _convertToBlazor  = false;
    private bool   _generating       = false;
    private string _generatingStatus = "Generating design...";

    private string _activeDownloadUrl = "";
    private string _blazorDownloadUrl = "";
    private string _activeScreenId    = "";
    private string _activeProjectId   = "";
    private int    _activeVersion     = 1;
    private string _activeVariant     = "";
    private string _activeHtmlSource  = "";

    private List<string>          _stepLog     = new();
    private List<ScreenHistoryItem> _screens   = new();
    private List<VersionInfo>      _versions   = new();
    private List<VariantInfo>      _variants   = new();
    private List<IBrowserFile>     _refImages  = new();

    private string _brandPrimary = "#1a2332";
    private string _brandName    = "Fortress AM";

    protected override void OnInitialized()
    {
        _activeProjectId = Guid.NewGuid().ToString();
    }

    private async Task Generate()
    {
        if (string.IsNullOrWhiteSpace(_prompt)) return;
        _generating       = true;
        _generatingStatus = "Starting...";
        _variants.Clear();
        _stepLog.Clear();
        _blazorDownloadUrl = "";

        try
        {
            // P1 fix: Use AgentApiClient (injects internal JWT) instead of raw HttpClient
            var files = new List<(string Name, Stream Data, string ContentType)>();
            foreach (var img in _refImages)
                files.Add((img.Name, img.OpenReadStream(10 * 1024 * 1024), img.ContentType ?? "image/png"));

            var (taskId, screenId) = await AgentApi.StartDesignScreenAsync(
                _activeProjectId, _prompt, _deviceTarget,
                _generateVariants ? 3 : 1, _convertToBlazor, OrgId, files);

            _activeScreenId = screenId;
            await StreamTask(taskId);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Generation failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _generating = false;
            StateHasChanged();
        }
    }

    private async Task EditCurrent()
    {
        if (string.IsNullOrWhiteSpace(_prompt) || string.IsNullOrEmpty(_activeHtmlSource)) return;
        _generating = true;
        _generatingStatus = "Applying changes...";
        _stepLog.Clear();
        _blazorDownloadUrl = "";

        try
        {
            // P1 fix: Use AgentApiClient for auth-carrying HTTP
            var taskId = await AgentApi.EditDesignScreenAsync(
                _activeProjectId, _activeScreenId, _prompt,
                _activeHtmlSource, OrgId, _deviceTarget);
            await StreamTask(taskId);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Edit failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _generating = false;
            StateHasChanged();
        }
    }

    private async Task StreamTask(string taskId)
    {
        using var cts    = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        // P1 fix: AgentApiClient.OpenStreamAsync carries internal JWT
        using var stream = await AgentApi.OpenDesignStreamAsync(taskId, cts.Token);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

            var json  = line[6..];
            var chunk = System.Text.Json.JsonSerializer.Deserialize<SseChunkDto>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            if (chunk == null) continue;

            switch (chunk.Type)
            {
                case "step":
                    _generatingStatus = chunk.Text ?? "";
                    _stepLog.Add(chunk.Text ?? "");
                    StateHasChanged();
                    break;

                case "file_output" when chunk.OutputType == "html":
                    _activeDownloadUrl = chunk.DownloadUrl ?? "";
                    _activeVersion++;
                    // P2 fix: positional record requires constructor syntax, not object initializer
                    _screens.Insert(0, new ScreenHistoryItem(
                        _activeScreenId, _prompt, _activeVersion,
                        DateTime.UtcNow, _activeDownloadUrl));
                    StateHasChanged();
                    break;

                case "file_output" when chunk.FileName?.EndsWith(".razor") == true:
                    _blazorDownloadUrl = chunk.DownloadUrl ?? "";
                    StateHasChanged();
                    break;

                case "result":
                    HandleResultChunk(chunk.Text ?? "");
                    break;

                case "error":
                    Snackbar.Add(chunk.Text ?? "Generation error", Severity.Error);
                    break;
            }
        }
    }

    private void HandleResultChunk(string json)
    {
        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            var result = System.Text.Json.JsonSerializer.Deserialize<ScreenResult>(json, opts);
            if (result == null) return;

            _activeHtmlSource = result.HtmlSource ?? "";

            if (result.IsVariants && result.Variants != null)
            {
                _variants = result.Variants.Select(v =>
                    new VariantInfo(v.Label, v.Suffix, v.DownloadUrl)).ToList();
                if (_variants.Any())
                {
                    _activeVariant     = _variants[0].Suffix;
                    _activeDownloadUrl = _variants[0].DownloadUrl;
                }
            }
        }
        catch { /* ignore malformed result */ }
    }

    private void SelectVariant(VariantInfo v)
    {
        _activeVariant     = v.Suffix;
        _activeDownloadUrl = v.DownloadUrl;
    }

    private async Task CopyCode()
    {
        if (!string.IsNullOrEmpty(_activeHtmlSource))
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", _activeHtmlSource);
            Snackbar.Add("HTML copied to clipboard.", Severity.Success);
        }
    }

    private void GenerateNew()
    {
        _activeScreenId    = Guid.NewGuid().ToString();
        _activeDownloadUrl = "";
        _activeHtmlSource  = "";
        _blazorDownloadUrl = "";
        _activeVersion     = 0;
        _variants.Clear();
        _stepLog.Clear();
        _prompt = "";
    }

    private void NewProject()
    {
        _activeProjectId = Guid.NewGuid().ToString();
        _screens.Clear();
        GenerateNew();
    }

    private void LoadScreen(ScreenHistoryItem screen)
    {
        _activeScreenId    = screen.ScreenId;
        _activeDownloadUrl = screen.DownloadUrl;
        _activeVersion     = screen.Version;
    }

    private async Task RestoreVersion(VersionInfo ver)
    {
        // Navigate iframe to prior version presigned URL
        _activeDownloadUrl = ver.DownloadUrl;
        _activeVersion     = ver.Version;
        StateHasChanged();
    }

    private void SetDevice(string device)   => _deviceTarget = device;
    private string DeviceClass(string d)    => d == _deviceTarget ? "mud-button-filled" : "";
    private string GetFrameClass()          => _deviceTarget switch
    {
        "mobile"  => "design-frame--mobile",
        "desktop" => "design-frame--desktop",
        _         => "design-frame--responsive",
    };

    private void OnRefImageSelected(InputFileChangeEventArgs e)
        => _refImages.AddRange(e.GetMultipleFiles(3));
    private void RemoveRef(IBrowserFile f)  => _refImages.Remove(f);
    // P2 fix: getElementById is not void — use a window helper instead.
    // In CoworkWeb/wwwroot/js/cowork.js, add:
    //   window.triggerElementClick = (id) => document.getElementById(id)?.click();
    private void OpenRefPicker()
        => JS.InvokeVoidAsync("triggerElementClick", "design-ref-input");

    private static string TruncatePrompt(string p)
        => p.Length > 50 ? p[..47] + "..." : p;

    // ── DTOs ─────────────────────────────────────────────────────────────
    record CreateScreenResponse(string TaskId, string ScreenId);
    record ScreenHistoryItem(string ScreenId, string Prompt, int Version,
        DateTime CreatedAt, string DownloadUrl);
    record VersionInfo(int Version, string S3Key, string DownloadUrl, DateTime CreatedAt, string Prompt);
    record VariantInfo(string Label, string Suffix, string DownloadUrl);
    record ScreenResult(string? ScreenId, string? ProjectId, string? HtmlSource,
        string? DownloadUrl, bool IsVariants, List<VariantResultItem>? Variants);
    record VariantResultItem(string Label, string Suffix, string DownloadUrl, string S3Key);

    class SseChunkDto
    {
        public string? Type        { get; set; }
        public string? Text        { get; set; }
        public string? OutputType  { get; set; }
        public string? FileName    { get; set; }
        public string? DownloadUrl { get; set; }
    }
}
```

### CSS for Design Workspace

Add to `cowork.css` (or a new `design-workspace.css` imported from `App.razor`):

```css
.design-workspace {
    display: grid;
    grid-template-columns: 220px 1fr 280px;
    height: calc(100vh - 56px); /* full height minus nav */
    overflow: hidden;
}

/* History panel */
.design-panel--history {
    border-right: 1px solid var(--mud-palette-lines-default);
    overflow-y: auto;
    display: flex; flex-direction: column;
}
.design-panel-header {
    padding: 12px 14px;
    font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px;
    display: flex; justify-content: space-between; align-items: center;
    border-bottom: 1px solid var(--mud-palette-lines-default);
    background: var(--mud-palette-background-grey);
}
.design-history-list { padding: 8px; }
.design-history-item {
    padding: 8px 10px; border-radius: 6px; cursor: pointer;
    margin-bottom: 4px;
}
.design-history-item:hover { background: var(--mud-palette-action-hover); }
.design-history-item--active { background: rgba(124,58,237,0.1); }
.design-history-prompt { font-size: 12px; font-weight: 600; color: var(--mud-palette-text-primary); }
.design-history-meta   { font-size: 10px; color: var(--mud-palette-text-secondary); margin-top: 2px; }
.design-history-empty  { padding: 24px 14px; text-align: center; font-size: 12px; color: var(--mud-palette-text-secondary); }

/* Preview panel */
.design-panel--preview {
    display: flex; flex-direction: column;
    background: #f3f4f6;
    overflow: hidden;
}
.design-preview-toolbar {
    display: flex; align-items: center; gap: 8px;
    padding: 8px 12px;
    background: white;
    border-bottom: 1px solid var(--mud-palette-lines-default);
    flex-shrink: 0;
}
.design-preview-frame-container {
    flex: 1; overflow: hidden; position: relative;
    display: flex; align-items: center; justify-content: center;
}
.design-frame--responsive .design-preview-iframe { width: 100%; height: 100%; border: none; }
.design-frame--mobile     .design-preview-iframe { width: 375px; height: 100%; border: none; box-shadow: 0 0 0 1px #d1d5db; border-radius: 8px; }
.design-frame--desktop    .design-preview-iframe { width: 1280px; height: 100%; border: none; transform-origin: top left; }

.design-generating-overlay {
    position: absolute; inset: 0;
    display: flex; flex-direction: column; align-items: center; justify-content: center;
    background: rgba(255,255,255,0.85); gap: 12px;
}
.design-generating-text { font-size: 13px; color: #6b7280; }
.design-empty-preview {
    display: flex; flex-direction: column; align-items: center; gap: 12px;
    color: #9ca3af; font-size: 13px;
}

.design-version-strip {
    display: flex; gap: 6px; padding: 6px 12px; background: white;
    border-top: 1px solid var(--mud-palette-lines-default); flex-shrink: 0;
}
.design-version-dot {
    padding: 2px 8px; border-radius: 4px; font-size: 10px; font-weight: 600;
    cursor: pointer; background: #f3f4f6; color: #6b7280;
}
.design-version-dot.active { background: rgba(124,58,237,0.15); color: #7c3aed; }

.design-variant-tabs { display: flex; gap: 4px; }
.design-variant-tab {
    padding: 4px 10px; border-radius: 4px; font-size: 11px; font-weight: 600;
    cursor: pointer; color: #6b7280; background: #f3f4f6;
}
.design-variant-tab.active { background: rgba(124,58,237,0.15); color: #7c3aed; }

.design-blazor-badge {
    display: flex; align-items: center; gap: 6px; padding: 6px 12px;
    background: rgba(124,58,237,0.06); border-top: 1px solid rgba(124,58,237,0.2);
    font-size: 12px; color: #7c3aed; flex-shrink: 0;
}

/* Controls panel */
.design-panel--controls {
    border-left: 1px solid var(--mud-palette-lines-default);
    padding: 14px;
    overflow-y: auto;
    display: flex; flex-direction: column; gap: 12px;
}
.design-brand-indicator {
    display: flex; align-items: center; gap: 8px;
    padding: 8px 10px; border-radius: 6px; background: #f9fafb;
    font-size: 12px; color: #6b7280;
}
.design-brand-swatch {
    width: 16px; height: 16px; border-radius: 3px; flex-shrink: 0;
}
.design-options-row {
    display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
}
.design-ref-upload { }
.design-ref-thumbnails { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px; }
.design-ref-thumb {
    padding: 2px 8px; background: #f3f4f6; border-radius: 4px;
    font-size: 10px; color: #6b7280; display: flex; align-items: center; gap: 4px;
}
.design-step-log {
    background: #f9fafb; border-radius: 6px; padding: 8px;
    font-size: 10px; font-family: monospace; color: #6b7280;
    display: flex; flex-direction: column; gap: 2px;
}
.design-step-entry { }
```

---

## 11. API Contract Summary

| Endpoint | Method | Purpose |
|---------|--------|---------|
| `/agents/design/projects/:projectId/screens` | POST | Generate new screen (or 3 variants) |
| `/agents/design/projects/:projectId/screens/:screenId/edit` | POST | Edit existing screen |
| `/agents/design/projects/:projectId/screens/:screenId/versions` | GET | Get version history |
| `/agents/design/tasks/:taskId/stream` | GET | SSE stream for task progress |
| `/agents/design/brand/:orgId` | PUT | Update org brand context |
| `/agents/design/brand/:orgId` | GET | Get org brand context |

### Request: New Screen

```json
POST /agents/design/projects/{projectId}/screens
Content-Type: multipart/form-data

prompt:          "A login page with email/password and a Fortress logo at top"
deviceTarget:    "responsive"   // "mobile" | "desktop" | "responsive"
variantCount:    "1"            // "1" | "3"
convertToBlazor: "false"        // "true" | "false"
orgId:           "fortress-am"
refs:            (optional binary file attachments)
```

### Response: New Screen

```json
{ "taskId": "uuid", "screenId": "uuid" }
```

Then stream `/agents/design/tasks/:taskId/stream` for SSE chunks:

```json
// Progress
{ "type": "step", "text": "Generating design..." }

// HTML file output
{ "type": "file_output", "outputType": "html", "fileName": "screen_v1.html",
  "downloadUrl": "https://s3.../presigned-url", "sizeBytes": 12400 }

// Consolidated result (parse for htmlSource and variant info)
{ "type": "result", "text": "{\"screenId\":\"...\",\"htmlSource\":\"<!DOCTYPE html>...\",\"downloadUrl\":\"...\"}" }

// Variant mode result
{ "type": "result", "text": "{\"screenId\":\"...\",\"isVariants\":true,\"variants\":[
    {\"label\":\"Refined\",\"suffix\":\"varA\",\"downloadUrl\":\"...\",\"s3Key\":\"...\"},
    {\"label\":\"Contemporary\",\"suffix\":\"varB\",\"downloadUrl\":\"...\",\"s3Key\":\"...\"},
    {\"label\":\"Bold\",\"suffix\":\"varC\",\"downloadUrl\":\"...\",\"s3Key\":\"...\"}
  ]}" }

// Blazor file output (only if convertToBlazor=true)
{ "type": "file_output", "outputType": "other", "fileName": "component_v1.razor",
  "downloadUrl": "https://s3.../presigned-url", "sizeBytes": 3200 }
```

---

## 12. File Summary

### New Files (5)
```
fip/cowork/src/CoworkAgent/src/agents/design/runner.ts
fip/cowork/src/CoworkAgent/src/agents/design/system-prompt.md
fip/cowork/src/CoworkAgent/src/agents/design/tools.ts          ← (stubs only for now; MCP tools future)
fip/cowork/src/CoworkAgent/src/routes/design.ts
fip/cowork/src/CoworkAgent/src/services/brandService.ts
fip/cowork/src/CoworkWeb/Components/Pages/Agents/Workspaces/DesignWorkspace.razor
```

### Modified Files (4)
```
fip/cowork/src/CoworkAgent/src/agents/registry.ts              (add design entry)
fip/cowork/src/CoworkAgent/src/agent/runner.ts                 (add systemPromptOverride param)
fip/cowork/src/CoworkAgent/src/server.ts                       (mount design router)
fip/cowork/src/CoworkWeb/Components/Pages/Agents/AgentPage.razor  (add design workspace case)
```

---

## 13. Acceptance Criteria

### Generation
1. `POST /agents/design/projects/{id}/screens` with a text prompt returns `{taskId, screenId}` within 500ms; task runs async
2. Streaming `/agents/design/tasks/:taskId/stream` emits at least one `step` chunk, one `file_output` chunk, and one `result` chunk before closing
3. The generated HTML file at the `downloadUrl` renders in a browser without external dependencies (no CDN, no 404 assets)
4. Generated HTML uses CSS custom properties for all colors and includes `:root { --color-primary: #1a2332; ... }` with Fortress AM defaults when no org brand is configured

### Iterative Refinement
5. `POST .../screens/:screenId/edit` with `priorHtml` produces a new HTML version that preserves the structure from `priorHtml` and applies the requested change
6. The new version is stored in S3 at `{screenId}_v2.html`; `GET .../versions` returns both v1 and v2

### Variants
7. `POST .../screens` with `variantCount=3` returns 3 separate `file_output` chunks and one `result` chunk with `isVariants: true`
8. The 3 variants are visually distinct — they do NOT differ only in color (verify manually); Refined variant has more whitespace than Bold variant

### Brand Context
9. `PUT /agents/design/brand/fortress-am` with a custom `primaryColor: "#FF0000"` stores the brand; subsequent generation using `orgId=fortress-am` produces HTML with `--color-primary: #FF0000` in `:root`
10. An org with no brand file (`PUT` never called) receives the Fortress AM defaults

### Blazor Conversion
11. `POST .../screens` with `convertToBlazor=true` produces a second `file_output` with `fileName` ending in `.razor`
12. The `.razor` file contains at least one MudBlazor component (`<MudButton`, `<MudTextField`, etc.) where the HTML had equivalent button/input elements
13. The `.razor` file does NOT contain `Variant="Variant.Outlined"` inline on MudButton (uses CSS class or FIP conventions)

### Workspace UI
14. `DesignWorkspace.razor` renders on `/agents/design` page with three panels visible (history, preview, controls)
15. Clicking "Generate Design" fires the API call and shows the progress overlay in the center panel
16. After generation, the iframe renders the HTML preview
17. The version strip appears after a second edit (v1 → v2)
18. "3 variants" checkbox causes variant tabs to appear in the preview toolbar after generation
19. "Copy Code" button copies the HTML source to clipboard (verify `navigator.clipboard.writeText` called)
20. "Download .razor" link appears only when `convertToBlazor` was checked and the task completed

### Error Handling
21. If Claude generates no HTML file (unexpected), the SSE stream emits `{ "type": "error" }` and the workspace shows an error snackbar
22. A generation timeout (3 minutes) in the Blazor SSE reader triggers the CancellationTokenSource and closes the stream gracefully

---

## 14. Variant UX Decision

**Resolved: Single overlay.**

Two options were evaluated:

| | Single overlay | Per-variant spinners |
|-|---------------|---------------------|
| **UX** | "Generating 3 variants..." then all 3 tabs appear | Each tab slot shows its own spinner as it arrives |
| **State model** | One bool `_generating` | `Dictionary<string, bool> _variantGenerating` |
| **SSE changes** | None — variants emit to main stream | Requires per-variant sub-streams or intermediate SSE events |
| **Error handling** | `Promise.allSettled` — failed variant just doesn't appear | Per-slot failure state needed |

**Decision: Single overlay.** Rationale:

1. Variant generation takes 15–30 seconds total. The UX difference between "one spinner" and "three spinners arriving at different times" is minimal for that duration.
2. The per-variant model requires either separate SSE streams (3× the connection overhead) or a new SSE event type (`variant_partial`) — that is a non-trivial infrastructure addition.
3. The staggered calls (P2 #7 fix: 0/500ms/1000ms) mean the three variants finish within a few seconds of each other — a single spinner that clears all three together is the correct representation.
4. Phase 2 can add per-variant progress if user research shows it matters.

**What Tony builds:**
- Single `_generating` bool
- Single "Generating 3 variants..." overlay in the preview pane
- All 3 variant tabs appear simultaneously when all 3 `file_output` chunks arrive
- No per-variant progress indicators

---

## 15. Clint Review Priorities

```
⚠️  HIGH: runner.ts signature change — runTask gains a second parameter
          systemPromptOverride before emit. All existing callers in routes/tasks.ts,
          routes/agents.ts, and any other files that call runTask must be updated
          to pass null as the second argument. If Tony misses a call site,
          the TypeScript compiler will catch it (type mismatch), but verify
          all callers are updated.

⚠️  HIGH: DesignWorkspace.razor streams SSE via HttpClient.GetStreamAsync with
          a 3-minute CancellationToken. In Blazor Server, this holds the SignalR
          circuit open for the duration. For a generation that takes 30-60 seconds
          this is acceptable. Verify the CancellationTokenSource is disposed (the
          using declaration handles this). Do NOT remove the timeout — an orphaned
          stream can hold the circuit open indefinitely.

⚠️  HIGH: runDesignTask calls runTask for variants with independent taskIds
          ({taskId}-varA, etc.). These sub-task IDs are NOT registered in Redis
          via createTaskMeta. The main taskId IS registered. Variant progress
          is absorbed into the main task stream — variants run silently.

          VARIANT UX DECISION (resolved — see §16):
          Single overlay is CORRECT for this spec. Build the single-overlay model.
          Do NOT implement per-variant progress spinners in Sprint 3.

⚠️  MEDIUM: brandService.ts uses an in-process Map cache. In ECS with multiple
            task replicas, each replica has its own cache. A brand update via
            PUT /brand/:orgId will only invalidate the cache on the replica that
            received the request. Other replicas continue serving stale brand
            until their 5-minute TTL expires. For Phase 1 (single replica, Fortress
            internal only), this is acceptable. Note for Phase 2 multi-replica.

⚠️  MEDIUM: The iframe sandbox attribute is set to "allow-scripts" only (same as
            other Cowork output iframes per the Cowork architecture spec constraint).
            Generated HTML with <script> tags will execute; HTML with
            form submissions, navigation, or window.open will be blocked.
            The design system prompt instructs Claude not to generate JS unless
            requested — verify generated HTML doesn't include unexpected scripts
            that could cause CSP violations.

⚠️  LOW: DesignWorkspace.razor injects HttpClient directly. In Blazor Server,
         HttpClient is registered as scoped. Verify it's registered in the Cowork
         DI container and that the CoworkAgent:BaseUrl config key is present.
         The existing Cowork services (TaskPage.razor, etc.) likely already
         have this pattern — copy the HttpClient registration from those files.
```

---

## 16. Phase 2 Extensions (Not in MVP)

| Feature | Notes |
|---------|-------|
| Interactive prototyping | Generate `<a href="#screen2">` links between screens; render as clickable prototype sequence |
| Voice input | Web Speech API → text input → same generation flow |
| Design system extraction | Point Claude at a URL + screenshot; extract CSS tokens into `brand.json` |
| Figma export | If demand exists: use open-source `html-to-figma` library (no Google API) |
| Infinite canvas | Embed Excalidraw or Tldraw iframe as the canvas layer; screens rendered as embedded iframes on the canvas |
| Per-user design projects | Currently project IDs are ephemeral (new GUID on component init); persist to DB with project name/description |
| Component library output | Generate a full set of components (button, card, input, nav) as a matched set instead of a single screen |

---

_Spec by Reed Richards | Design Agent = 6 new files, 4 modified. Pure Claude (Bedrock) generation engine. No external SDK dependencies. Brand context per-org from S3. Three-panel workspace UI. Iterative refinement, variant generation, Blazor component conversion. Full CC-executable._
