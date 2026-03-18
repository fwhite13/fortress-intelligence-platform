# FAIT Cowork — Research Report
**Compiled:** 2026-03-16  
**Researcher:** Bruce Banner  
**Purpose:** Deep research pass to inform FAIT Cowork product design — a sovereign, FORGE-grounded equivalent of Claude Cowork for FIP business users.

---

## Table of Contents

1. [Track 1: Claude Cowork — Feature Overview, UX & Pricing](#track-1-claude-cowork)
2. [Track 2: Competitive Landscape](#track-2-competitive-landscape)
3. [Track 3: Technical Foundations](#track-3-technical-foundations)
4. [FAIT Cowork Opportunity — Analysis & Recommendations](#fait-cowork-opportunity)

---

## Track 1: Claude Cowork

### What Is It?

Claude Cowork launched January 13, 2026 as a research preview, positioned as "Claude Code for the rest of your work." The premise: developers were using Claude Code for non-coding tasks (vacation research, building slide decks, cleaning email, recovering files), so Anthropic stripped the command-line complexity and built a consumer-friendly agent. It shares the exact same underlying architecture as Claude Code — the Claude Agent SDK's agentic loop — but wrapped in an approachable desktop UI.

The core metaphor: **it feels like leaving tasks for a coworker**, not a back-and-forth chat. You assign a task, Claude plans, executes in parallel, checks its own work, and loops you in when it needs direction.

It was reportedly built in approximately a week and a half — largely by Claude Code itself — which is its own recursive story.

### Full Feature Overview

**Core Capability: Folder-Based Agency**
- User designates a specific local folder Claude can read/write/create files in
- Claude can NOT access anything outside the designated folder
- Runs via VM under the hood, enforcing the folder boundary at the OS level
- Tasks include: reorganizing downloads, generating expense spreadsheets from receipt screenshots, drafting reports from scattered notes, converting file types, compressing PDFs, batch image processing

**Agentic Loop**
- Multi-step planning: Claude formulates a plan, executes it, checks work, asks for clarification if blocked
- Parallel task queuing: users can queue multiple tasks simultaneously
- Not a back-and-forth; tasks run while you're doing other things
- Based on the Claude Agent SDK (same engine as Claude Code)

**Connectors / Skills / Plugins**
- Integrates with existing Claude connectors: Asana, Notion, PayPal, Google Drive, Gmail, Google Calendar, DocuSign, Apollo, Clay, Outreach, SimilarWeb, MSCI, FactSet, WordPress, Harvey, LegalZoom
- Skills layer: improved ability to create documents, presentations, spreadsheets
- Plugin marketplace (enterprise): admins can build private plugin marketplaces with custom branding per department — HR, design, engineering, operations, financial analysis, investment banking, equity research, private equity, wealth management
- MCP connector support

**Browser Integration**
- Pairs with "Claude in Chrome" browser extension
- Claude can browse the web, search, and interact with web-based workflows (e.g., clean up Gmail inbox, cancel subscriptions)
- Browser access is opt-in; risks are explicitly disclosed

**Office Integration (Feb 2026 Enterprise launch)**
- Claude for Excel and Claude for PowerPoint add-ins
- Context passes seamlessly between Excel, PowerPoint, and Cowork — cross-app data flow without restarting
- Works across multiple files simultaneously

**Global/Folder Instructions**
- Users can set persistent instructions: preferred tone, format, role context
- Per-folder instructions that activate automatically when working in that folder
- Eliminates need to re-explain context every session

**Cross-Device (Feb 2026)**
- Windows support added Feb 10, 2026 (full feature parity with macOS)
- Cross-device sync noted as a planned future improvement

**Microsoft Copilot Cowork (March 2026)**
- Microsoft launched "Copilot Cowork" powered by Claude at $30/user/month
- Cloud-based, runs inside M365 infrastructure (not desktop-based)
- Accesses full enterprise data graph: Outlook, Teams, Calendar, SharePoint, Excel
- Currently in Research Preview, broader rollout expected late March 2026
- Part of Microsoft 365 Wave 3; requires M365 E7 at $99/user/month full bundle

### UX — How a Non-Technical User Experiences It

- Three tabs in Claude macOS/Windows desktop app: **Chat**, **Code**, **Cowork**
- Sessions are called "tasks" not "chats"
- User designates a folder, then describes what they want in plain language
- Claude surfaces what it's doing as it works — user can monitor for unexpected patterns
- Approval gates for significant/risky actions before execution
- Example use cases WIRED tested successfully: organize files into folders, convert file types, generate reports, take over browser to search web, tidy up Gmail inbox
- WIRED reviewer called it "a nice surprise" that "worked fairly well" — notably positive compared to prior agent disappointments

### Availability & Pricing

| Plan | Availability | Price |
|------|-------------|-------|
| Max | Research preview (Jan 13, 2026 launch) | $100-200/month |
| Pro | Added Jan 16, 2026 | ~$20/month |
| Team | Added Jan 23, 2026 | ~$30/user/month |
| Enterprise | Added Jan 23, 2026 | Custom |
| Windows | Full parity Feb 10, 2026 | Same as above |
| Microsoft Copilot Cowork | March 2026 preview | $30/user/month add-on |

**Research preview status**: Anthropic is iterating rapidly — Windows parity arrived in < 4 weeks from Mac launch.

### Limitations — What It Can't Do

1. **No audit logs**: "Cowork activity is not captured in audit logs, Compliance API, or data exports." — Explicitly not for regulated workloads (yet)
2. **Prompt injection vulnerability**: Malicious content in web pages, emails, or documents can hijack Claude's actions. Anthropic has defenses but acknowledges this is still an active research area
3. **Destructive actions possible**: Claude CAN delete local files if instructed — requires careful guidance
4. **No cross-device sync yet**: Noted as planned future feature
5. **macOS-first**: Windows came later; mobile not available
6. **Internet required**: Despite running locally, internet connection required
7. **Prompt injection via MCP/plugins**: Third-party extensions expand attack surface
8. **No compliance for regulated data**: HIPAA, financial regulatory workloads not supported in research preview
9. **Browser extension dependency**: Browser tasks require Claude in Chrome; can't work headlessly

### How It Differs from Claude Desktop / Claude.ai Chat

| Feature | Claude Chat | Claude Code | Claude Cowork |
|---------|-------------|-------------|---------------|
| Interface | Web/Desktop chat | Terminal CLI | Desktop app tab |
| Target user | Everyone | Developers | Knowledge workers |
| File access | No | Yes (full FS) | Yes (sandboxed folder) |
| Agentic loop | Limited | Full | Full |
| Multi-step tasks | No | Yes | Yes |
| Parallel tasks | No | No | Yes |
| Browser control | No | Limited | Yes (Chrome ext) |
| Connectors | Yes | Limited | Yes, enhanced |
| Code execution | No | Yes | Limited |
| Approval gates | No | Optional | Yes, default |

### Data Privacy & Sovereignty Story

**Anthropic direct (claude.ai/Enterprise):**
- Anthropic does not use Enterprise/Team/Pro input data to train models: "we do not train our models on your Claude for Work data"
- Data transits Anthropic's infrastructure (US-based)
- No HIPAA compliance for Cowork in research preview
- Cowork NOT in audit logs/Compliance API — explicit limitation for regulated use

**Critical gap**: All Cowork data goes through Anthropic's cloud. File contents, browser actions, and task context leave the local machine. This is a fundamental sovereignty gap that a Bedrock-based FAIT Cowork would close.

---

## Track 2: Competitive Landscape

### Overview Table

| Product | Target User | Pricing | Core Differentiator | Data Sovereignty |
|---------|------------|---------|--------------------|--------------------|
| **Claude Cowork** | Knowledge workers (non-dev) | $20-200/mo (Pro-Max) | Folder-based agency, Claude-native, office integration | None — Anthropic cloud, no audit logs for regulated |
| **Lovable** | Non-technical builders, startup founders | Free / $25/mo Pro / $50/mo Teams+SSO | Fastest MVP in 12 min, Supabase native, visual editor | Opt-out at $50/mo; no enterprise VPC option |
| **v0 (Vercel)** | Frontend devs, designers | Free / $20/mo Pro / $30/user Team | Best-in-class React/UI component generation | SOC 2 Type II; no self-host; Vercel cloud only |
| **Bolt (StackBlitz)** | Devs, hackers | Free / $20/mo Pro / $30/mo Plus | Zero-setup WebContainer browser-native full Node.js | Browser sandbox only; cloud inference |
| **Replit Agent** | Non-devs to mid-level devs | Free / $20/mo Core / $35/user Team | Zero-setup, built-in deploy+database, Agent 3 autonomous | SOC 2 (Enterprise only); shared infra; no VPC |
| **GitHub Copilot Workspace** | Developers (issue → PR flow) | $10/mo Pro / $19/user Business / $39/user Enterprise | Deep GitHub integration, issue-to-PR automation | Enterprise: GHEC data residency options |
| **GitHub Spark** | Non-technical builders | Copilot Pro+ ($10-20/mo) | Natural language → mini-apps, no code required | GitHub/Microsoft cloud |
| **Cursor** | Professional developers | Free / $20/mo Pro / $40/user Business | Best-in-class IDE, codebase understanding, multi-file edits | Cloud inference, no self-host |
| **Windsurf (Codeium)** | Developers | Free / $15/mo Pro / $30/user Team | Cheaper than Cursor, proprietary SWE models | Cloud only |
| **Microsoft Copilot (M365)** | Enterprise knowledge workers | $30/user Cowork add-on / $99/user E7 | M365 native, full enterprise data graph, compliance | M365 trust boundary, GDPR/HIPAA on higher plans |

### Detailed Analysis

#### Lovable
**What it does:** Full-stack web app builder from natural language. Describe an app, Lovable generates React + Supabase (auth, database, API). Visual editor lets non-technical users click-to-modify. GitHub sync gives code ownership.  
**Pricing:** Free (5 messages/day) / $25/mo Pro (100 credits) / $50/mo Teams (SSO + data opt-out)  
**Target user:** Non-technical founders, product managers, startup teams  
**Strengths:** Fastest full-stack MVP (12 minutes), best non-dev UX, native Supabase, 2.3M MAU, $75M ARR, $1.8B valuation  
**Weaknesses:** Credit-limited, complex business logic requires dev intervention, outputs need hardening for production, no VPC/self-host  
**Data sovereignty:** Basic — $50/mo tier gets data opt-out; no self-hosted option  
**Key differentiator for FIP context:** Most approachable for pure non-devs. But outputs are web apps, not documents/analysis — different from Cowork's knowledge-worker tasks.

#### v0 (Vercel)
**What it does:** Generates production-grade React/Next.js UI components from natural language or image uploads. Frontend-only — no backend, no database. The output is copy-pasteable code, not a deployed app.  
**Pricing:** Free ($5 credits) / $20/mo Pro / $30/user Team / Enterprise custom. **Note:** May 2025 shift from unlimited to metered caused community backlash.  
**Target user:** Frontend developers, designers with some React knowledge  
**Strengths:** Best code quality of any AI UI builder, SOC 2 Type II, Vercel ecosystem, image-to-code  
**Weaknesses:** Frontend only, no backend/auth/database generation, requires dev to wire up logic, pricing unpredictable  
**Data sovereignty:** SOC 2 compliant; Vercel cloud only; no self-host  
**Key differentiator for FIP context:** Relevant for HTML prototype generation — but outputs require a developer to do anything meaningful with them. Not self-serve for Elise/Lauren.

#### Bolt (StackBlitz)
**What it does:** Full Node.js development environment running entirely in the browser via WebContainer technology. Zero install — open a URL, describe an app, code runs in browser. Netlify one-click deploy.  
**Pricing:** Free (1M tokens/mo with 300k daily limit) / $20/mo Pro (10M tokens) / $30/mo Plus (20M tokens)  
**Target user:** Developers who want zero-setup; hackathon use cases  
**Strengths:** Zero setup, mobile works, any npm package, framework-agnostic (React/Vue/Svelte)  
**Weaknesses:** Assumes coding knowledge ("less for dummies" — Reddit user quote), token-based pricing adds up, harder to migrate projects, limited integrations  
**Sandboxing:** WebContainer runs inside browser security sandbox — code can't access local machine. Node.js virtualized TCP stack. Security-by-default from browser origin model.  
**Data sovereignty:** WebContainer is browser-local; AI inference goes to cloud  
**Key differentiator for FIP context:** Technical-leaning; not suitable as-is for Elise/Lauren. The WebContainer tech is architecturally interesting for sandboxed sovereign execution.

#### Replit Agent (Agent 3)
**What it does:** Cloud IDE with autonomous AI agent (Agent 3) that builds, debugs, and deploys full apps from natural language. Includes built-in database (PostgreSQL), deployment, and collaboration. Zero local setup — fully browser-based.  
**Pricing:** Free (limited Agent credits) / $20/mo Core (full Agent 3) / $35/user/mo Teams / Enterprise custom  
**Target user:** Non-technical to mid-level developers; education; prototyping  
**Strengths:** Zero setup, truly autonomous (tested: built web scraper in 20 minutes with zero-coding-experience user), built-in DB+deploy, extended thinking mode, web search  
**Weaknesses:** Gets stuck on edge cases, shared infrastructure (performance varies), limited DevOps/CI-CD, $35/user Teams gets expensive fast  
**Sandboxing:** Container + VM-based isolated environments, separate per project  
**Data sovereignty:** SOC 2 Type II on Enterprise plans only; shared infra for Core; no VPC/self-host  
**Key differentiator for FIP context:** Closest to "non-dev builds apps" workflow. But the outputs are apps — not knowledge-worker documents/analysis. Data residency not available below Enterprise.

#### GitHub Copilot Workspace
**What it does:** Issue-to-code workflow automation. Takes a GitHub issue, explores codebase context, proposes implementation plan, generates code changes, opens PR. Deeply integrated into GitHub.  
**Pricing:** $10/mo Pro / $19/user Business / $39/user Enterprise  
**Target user:** Developers; requires GitHub workflow knowledge  
**Strengths:** Native GitHub integration, enterprise data residency options (GHEC), strong compliance story  
**Weaknesses:** Developer-only; requires understanding of PRs and code review; not for knowledge workers  
**Data sovereignty:** GitHub Enterprise Cloud offers data residency; solid compliance story  
**Key differentiator for FIP context:** Not relevant for non-dev use cases. Relevant as a comparison point for FIP developers, not for Elise/Lauren.

#### GitHub Spark
**What it does:** Natural language to "micro-apps" (small single-purpose tools). Describe a calculator, task tracker, or form — Spark builds and hosts it instantly. No coding required. Public preview for Copilot Pro+ subscribers (July 2025).  
**Pricing:** Bundled with Copilot Pro+ ($10-20/mo)  
**Target user:** "Built for people with all levels of technical fluency" — explicit non-dev positioning  
**Strengths:** Very low friction, no deployment needed, natural language + visual editing  
**Weaknesses:** Small scope ("micro-apps"), GitHub/Microsoft cloud only, not full-featured app builder  
**Key differentiator for FIP context:** Most directly comparable to what FAIT Cowork might offer for quick HTML prototype generation — but limited to GitHub ecosystem.

#### Cursor
**What it does:** AI-first IDE (fork of VS Code) with deep codebase understanding, multi-file editing, agent mode for autonomous coding tasks  
**Pricing:** Free Hobby / $20/mo Pro / $200/mo Ultra / $40/user Business  
**Target user:** Professional developers  
**Strengths:** Best codebase understanding, most powerful for complex refactoring, familiar IDE feel  
**Weaknesses:** Developer only — requires coding knowledge; no browser-based access; no self-host  
**Key differentiator for FIP context:** Not for Elise/Lauren. Relevant competitor for FIP developers using Claude Code.

#### Windsurf (Codeium)
**What it does:** AI IDE similar to Cursor, with proprietary SWE-1.5 model optimized for code  
**Pricing:** Free / $15/mo Pro / $30/user Team  
**Target user:** Professional developers  
**Strengths:** Cheaper than Cursor, good agent usage, proprietary speed-optimized models  
**Weaknesses:** Developer only  
**Key differentiator for FIP context:** Same as Cursor — developer tool, not relevant for non-dev users.

#### Microsoft Copilot Cowork (M365)
**What it does:** Cloud AI agent powered by Anthropic Claude running inside M365 infrastructure. Manages multi-step tasks across Outlook, Teams, Calendar, SharePoint, Excel. Persists across sessions, coordinates workflows spanning multiple data sources.  
**Pricing:** $30/user/month add-on / $99/user/month E7 bundle  
**Target user:** Enterprise knowledge workers in M365 shops  
**Strengths:** Full M365 integration, enterprise compliance, GDPR/HIPAA on higher tiers, "Work IQ" contextual grounding  
**Weaknesses:** Expensive, requires M365 E7 commitment, currently research preview  
**Data sovereignty:** M365 trust boundary; enterprise data residency on appropriate plans  
**Key differentiator for FIP context:** The most direct competitor to FAIT Cowork for enterprise knowledge workers. FIP's advantage: Bedrock-native, FORGE KB integration, FIP auth.

---

## Track 3: Technical Foundations

### 3.1 Claude Code CLI + Bedrock Configuration

**How it works:** Claude Code CLI supports Bedrock via environment variables. No code changes required — it's a first-class supported path.

**Minimal configuration:**
```bash
# Enable Bedrock mode
export CLAUDE_CODE_USE_BEDROCK=1
export AWS_REGION=us-east-1

# IAM credentials (multiple options)
export AWS_ACCESS_KEY_ID=...
export AWS_SECRET_ACCESS_KEY=...
export AWS_SESSION_TOKEN=...
# OR
aws configure
# OR SSO
aws sso login --profile=myprofile
export AWS_PROFILE=myprofile
# OR Bedrock API keys (simplest for multi-user deployment)
export AWS_BEARER_TOKEN_BEDROCK=your-bedrock-api-key
```

**Pin model versions (critical for multi-user deployments):**
```bash
export ANTHROPIC_DEFAULT_SONNET_MODEL='us.anthropic.claude-sonnet-4-6'
export ANTHROPIC_DEFAULT_HAIKU_MODEL='us.anthropic.claude-haiku-4-5-20251001-v1:0'
export ANTHROPIC_DEFAULT_OPUS_MODEL='us.anthropic.claude-opus-4-6-v1'
```
⚠️ Without pinning, alias-based model references break when Anthropic releases new versions.

**Auto credential refresh** (for SSO / corporate IdP):
```json
{
  "awsAuthRefresh": "aws sso login --profile myprofile",
  "env": { "AWS_PROFILE": "myprofile" }
}
```

**Bedrock-specific limitations vs. Anthropic direct:**

| Feature | Anthropic Direct | AWS Bedrock |
|---------|-----------------|-------------|
| Prompt caching TTL | Optional 1-hour | Fixed 5-minute TTL |
| Prompt caching availability | All regions | Selected regions (us-east-1, us-west-2) |
| Login/logout commands | Enabled | Disabled (uses AWS auth) |
| Extended thinking | Full support | Available via `interleaved-thinking-2025-05-14` beta header |
| Rate limits | Lower (per API key) | Higher (AWS account-level; often better) |
| Data residency | Anthropic US cloud | AWS region of your choice |
| Model versions | Latest via aliases | Must pin; new versions need manual update |
| Cross-region inference | N/A | Supported via `us.` prefix profiles |
| Application inference profiles | N/A | Supported — route to custom ARNs |

**Data sovereignty on Bedrock:** AWS guarantees data does not leave the selected region. Bedrock does not use customer data for model training. This is the core sovereignty argument.

### 3.2 Anthropic API Agentic Loops — Building Cowork Directly

**The agentic loop pattern:**
1. Send user prompt + tool definitions + conversation history to Claude
2. Claude responds with text and/or tool_use blocks
3. Execute requested tools, collect results
4. Send tool_result blocks back to Claude as `user` turn
5. Repeat until Claude produces text with no tool calls
6. Return final result to user

**Tool use patterns:**

```python
# Minimal agentic loop
tools = [
    {"name": "read_file", "description": "...", "input_schema": {...}},
    {"name": "write_file", "description": "...", "input_schema": {...}},
    {"name": "web_search", "description": "...", "input_schema": {...}},
]

messages = [{"role": "user", "content": user_prompt}]

while True:
    response = client.messages.create(
        model="us.anthropic.claude-sonnet-4-6",
        tools=tools,
        messages=messages,
        max_tokens=4096
    )
    
    if response.stop_reason == "end_turn":
        break
    
    # Execute tool calls
    tool_results = []
    for block in response.content:
        if block.type == "tool_use":
            result = execute_tool(block.name, block.input)
            tool_results.append({"type": "tool_result", "tool_use_id": block.id, "content": result})
    
    messages.append({"role": "assistant", "content": response.content})
    messages.append({"role": "user", "content": tool_results})
```

**Extended thinking / adaptive thinking:**
- Sonnet 4.6: Interleaved thinking via `interleaved-thinking-2025-05-14` beta header — thinking blocks appear between tool calls
- Opus 4.6: Interleaved thinking NOT available
- On Bedrock: Supported via `additional_request_fields` (see Strands Agents example)
- Independent of `effort` setting

**Streaming:** Use `client.messages.stream()` for real-time token streaming. In a UI context, stream Claude's thinking/planning steps to show users what's happening.

**Approval gates:** Before executing potentially destructive tools (file delete, send email, make API calls), pause the loop and emit an approval event to the UI. The loop resumes only after user confirmation.

**Cost/turn limits:**
- `max_turns`: cap tool-use iterations (good for scoped tasks)
- `max_budget_usd`: cap by spend threshold (good for production)

### 3.3 Claude Agent SDK — Programmatic Embedding

**Key finding:** Yes, there is a full programmatic SDK. It was renamed from "Claude Code SDK" to "Claude Agent SDK" and is available in both Python and TypeScript.

```bash
npm install @anthropic-ai/claude-agent-sdk    # TypeScript
pip install claude-agent-sdk                   # Python
```

**What it gives you:**
- The exact same agent loop that powers Claude Code — file read/write, bash, web search, web fetch, glob, grep
- Hooks for intercepting/modifying/blocking tool calls before execution
- `max_turns` and `max_budget_usd` controls
- Session management and context compaction (automatic when context fills)
- Streaming message types: `SystemMessage`, `AssistantMessage`, `UserMessage`, `ResultMessage`
- **Bedrock + Vertex + Azure all supported** via the same env vars as Claude Code CLI

```typescript
import { query } from "@anthropic-ai/claude-agent-sdk";

// FAIT Cowork example — Bedrock-backed
// (CLAUDE_CODE_USE_BEDROCK=1 set in env)
for await (const message of query({
  prompt: "Create an HTML prototype from these wireframe notes",
  options: {
    allowedTools: ["Read", "Write", "WebSearch"],
    maxBudgetUsd: 0.50,    // cost cap
    maxTurns: 20,          // turn cap
  }
})) {
  if ("result" in message) {
    // Final result — display to user
    displayResult(message.result);
  } else if (message.type === "assistant") {
    // Stream intermediate steps to UI
    streamToUI(message);
  }
}
```

**Hooks example (approval gate):**
```typescript
import { query, ClaudeAgentOptions } from "@anthropic-ai/claude-agent-sdk";

const options: ClaudeAgentOptions = {
  hooks: {
    preToolCall: async (toolName, toolInput) => {
      if (isDestructive(toolName, toolInput)) {
        const approved = await requestUserApproval(toolName, toolInput);
        return approved ? { action: "allow" } : { action: "block", reason: "User declined" };
      }
      return { action: "allow" };
    }
  }
};
```

**CRITICAL NOTE:** Anthropic does not allow third-party developers to offer `claude.ai` login or Anthropic rate limits in Agent SDK-based products. Authentication must be via API keys — which aligns perfectly with the Bedrock approach.

### 3.4 Sandboxing — How Competitors Do It

**Bolt (StackBlitz) — WebContainer:**
- WebAssembly-based virtualization of Node.js runtime running entirely in the browser
- Code has ZERO access to the local machine — browser origin model enforces this
- Virtualized TCP network stack via ServiceWorker API
- Security is browser-native: same sandbox that protects arbitrary web page JS
- Limitation: WebAssembly-only; can't run arbitrary native code (no Python, no system tools)
- Speed: Instant boot (seconds vs Docker's minutes)

**Replit — Container + VM isolation:**
- Container-based isolation per project (similar to Docker)
- VM-level isolation for sensitive operations
- Shared infrastructure (multi-tenant) — performance varies with load
- Full Linux environment available; much more capable than WebContainer
- Security boundary: container walls; not browser-native

**Lovable — Cloud execution + Supabase:**
- Code generation runs in Lovable's cloud
- Execution via Vercel edge deployment or Supabase functions
- Not isolated per-user at execution time — outputs are deployed apps
- Security: SOC 2 Type II at higher tiers; data opt-out at $50/mo

**Claude Cowork — Local VM:**
- Boris Cherny (Anthropic, head of Claude Code): "We use a virtual machine under the hood"
- VM enforces folder boundary — Claude literally cannot see folders not granted access
- File execution/code happens inside the VM
- Browser extension is separate; operates in browser's sandbox

**For a sovereign FAIT Cowork equivalent:**

Option A — **Local execution (Cowork-style)**:
- Electron/Tauri app with embedded VM or process sandbox
- User designates work folder; all file ops sandboxed to it
- Claude Agent SDK runs locally, talks to Bedrock
- Pros: No server infrastructure; data never leaves device except to Bedrock
- Cons: Desktop app distribution; platform-specific; VM overhead

Option B — **Server-side containers (Replit-style)**:
- Per-session Docker containers on FIP infrastructure
- Agent SDK runs in container, mounts only permitted paths
- Web-based UI; no local install required
- Pros: Web UI; easier to manage; no local install
- Cons: FIP must run container infrastructure; data leaves device to FIP servers (but stays within FIP boundary)

Option C — **Browser WebContainer (Bolt-style)**:
- WebAssembly execution in browser; no server execution
- Limited to Node.js/WASM-compatible operations
- Pros: Zero server infra for execution; fully client-side
- Cons: Can't run Python, system tools, or arbitrary processes

**Recommended for FAIT Cowork:** Option B (server-side containers) gives the best balance of capability, UX (web-based, no install), and sovereignty (data stays in FIP/AWS infrastructure). Option A is viable if desktop is acceptable and FIP wants zero server execution.

---

## FAIT Cowork Opportunity

### The Gap Claude Cowork Leaves

Claude Cowork is genuinely impressive and moving fast. But it has four critical weaknesses that FAIT Cowork can exploit:

1. **Data exits Anthropic's cloud** — file contents, browser history, task context all go to Anthropic's US infrastructure. For FIP's regulated or sensitive clients, this is a non-starter.
2. **No audit logs in research preview** — explicitly stated: "Cowork activity is not captured in audit logs, Compliance API, or data exports. Do not use Cowork for regulated workloads."
3. **No FORGE KB integration** — Claude Cowork has generic connectors but no understanding of FIP's domain knowledge, proprietary data, or customer context
4. **No FIP auth/nav integration** — users must manage a separate Anthropic account; no SSO, no FIP workspace continuity

FAIT Cowork can solve all four on day one.

### Features to Take From Claude Cowork

| Feature | Take | Adapt | Skip |
|---------|------|-------|------|
| Folder-based file access | ✅ Core concept | Adapt to FIP workspace model | — |
| Agentic loop (plan → execute → check) | ✅ Use Agent SDK | Bedrock-backed | — |
| Parallel task queuing | ✅ UX pattern | — | — |
| Approval gates for destructive actions | ✅ Critical safety | Expand for regulated data | — |
| Skills layer (doc/presentation creation) | ✅ High-value | Add FORGE KB-grounded skills | — |
| Global/folder instructions | ✅ Solves re-context problem | Add FIP user profile persistence | — |
| "Tasks not chats" framing | ✅ UX metaphor | — | — |
| Plugin/connector marketplace | Later | FIP connectors first | Generic connectors initially |
| Browser extension | Later | FIP internal web tools | External web browsing initially |
| Excel/PowerPoint integration | ✅ High demand | Add FIP data sources | — |

### Features to Take From Competitors

| Source | Feature | Why It Matters for FAIT Cowork |
|--------|---------|-------------------------------|
| Lovable | Visual output preview (render HTML inline) | Elise/Lauren need to SEE the prototype immediately |
| Lovable | Non-dev UX ("for dummies" quality) | Must not assume any technical knowledge |
| Replit Agent | Extended thinking mode visible to user | Shows users Claude is "working hard" — builds trust |
| Replit Agent | Zero-setup web UI | No desktop install; reduces adoption friction |
| v0 | Image-to-code (upload wireframe → generate) | High-value for prototype generation use case |
| Bolt | Live preview alongside generation | See output as it builds |
| GitHub Spark | Micro-app framing | Low-intimidation first use case |
| M365 Copilot Cowork | Cross-app context passing | FORGE KB → document → analysis → output |

### What to Build First — Phased Roadmap

#### Phase 1: MVP (8-12 weeks) — "FORGE Cowork"

**Core loop:**
- Web UI (no install) with FIP auth/SSO
- User describes task in plain language
- Claude Agent SDK (Bedrock-backed) executes in server-side container
- Outputs rendered in-browser (HTML preview, document viewer)
- Approval gates before any file write or external action
- FIP workspace folder as the sandboxed work area

**Priority tasks to support (based on Elise/Lauren use cases):**
1. Generate HTML prototype from text description or wireframe image upload
2. Generate document (report, brief, analysis) from scattered notes/inputs
3. Summarize and restructure uploaded files
4. Build simple data analysis from uploaded spreadsheet data

**FORGE integration:** Read from designated FORGE KBs as context for task grounding. No write to FORGE in Phase 1 — read-only.

**Data flow (all sovereign):**
```
User → FIP auth → FAIT Cowork UI → FIP container (Agent SDK) → AWS Bedrock → Container → FIP storage
```
Nothing touches Anthropic's cloud. Full audit trail to FIP's log infrastructure.

#### Phase 2: Skills + Connectors (12-20 weeks)

- Skill templates: HTML prototyping, document drafting, data analysis, email drafting
- FORGE KB write-back (save outputs back to knowledge base)
- FIP connector integrations (whatever FIP's internal systems are)
- Excel/PowerPoint rendering in-browser
- Persistent user instructions (tone, format, role context)
- Task history and session replay

#### Phase 3: Collaboration + Admin (20-30 weeks)

- Multi-user shared task spaces
- Admin panel: audit logs, usage dashboards, cost visibility per user
- Private skill marketplace (department-specific skill packs)
- Approval workflow for sensitive output types
- Compliance mode: flag regulated data, require human review before output

### The Sovereign Advantage

FAIT Cowork's differentiator isn't just "Claude Cowork but private." It's:

1. **FORGE-grounded outputs** — every generated document, prototype, or analysis is grounded in FIP's proprietary knowledge, terminology, and domain context. Claude Cowork generates generic outputs. FAIT Cowork generates outputs that sound like they came from FIP experts.

2. **Full audit trail** — every action logged, every output tracked. This is a compliance requirement that Claude Cowork explicitly can't meet today. FAIT Cowork can meet it on day one.

3. **FIP identity continuity** — users don't manage a separate Anthropic account. They log in with FIP credentials, their work history lives in FIP's infrastructure, and their outputs integrate with FIP's existing workflows.

4. **Cost control and transparency** — Bedrock costs are visible in AWS Cost Explorer. Admins can see and cap per-user spend. Claude Cowork's costs are opaque at the user level.

5. **No vendor data risk** — Anthropic's privacy policy, however good, is still a third party. Bedrock data stays in FIP's AWS account under FIP's control.

### Recommended Technical Approach

**Recommended: Hybrid — Agent SDK + Bedrock, server-side containers, web UI**

```
[FIP Web UI (React/Next.js)]
  ↓ FIP auth (SSO)
[FAIT Cowork API (Node.js/Python)]
  ↓ spawns per-session container
[Docker container (ephemeral, per session)]
  ↓ runs Claude Agent SDK
  ↓ mounts FIP workspace storage
[AWS Bedrock — Claude Sonnet 4.6]
  ← FORGE KB context injection (read-only initially)
  → outputs written to FIP storage
  → audit log to FIP observability stack
```

**Why not CLI-only?** The CLI is a terminal tool. It can be scripted, but serving multiple concurrent non-technical users requires a proper web service layer. The Agent SDK is the right primitive — it's the CLI's engine extracted as a library.

**Why not API-direct (raw Anthropic API)?** Building the full agentic loop from scratch is 4-8 weeks of engineering that Anthropic has already done. The Agent SDK gives us the loop, tool execution, context management, and hooks for free. Use it.

**Why not Bedrock API keys (simple bearer tokens)?** Bedrock API keys are actually fine for a multi-user deployment — they're simpler than IAM for service accounts. BUT for a production FIP product, IAM roles with least-privilege policies attached to the container runtime is more secure and easier to rotate. Use IAM roles for the container execution role; optional Bedrock API keys for dev/staging.

**Model selection for Bedrock:**
- Primary: `us.anthropic.claude-sonnet-4-6` — best capability/cost balance for agentic tasks
- Fast/small: `us.anthropic.claude-haiku-4-5-20251001-v1:0` — for simple reformatting tasks, quick iterations
- Pin both via env vars; never use aliases in production

**Prompt caching on Bedrock:** Enable it. FORGE KB context injected into every session will be large and repeated — prompt caching gives 5-minute TTL cost savings. AWS Bedrock caching is available in us-east-1 and us-west-2. Budget for this in architecture.

**Extended thinking:** Enable `interleaved-thinking-2025-05-14` for complex analysis tasks. Show the "thinking" steps in the UI to build user trust that something real is happening (Replit does this well).

### The One-Sentence Pitch for FAIT Cowork

> **FAIT Cowork is Claude Cowork — same magic, sovereign by default — where every output is grounded in FORGE knowledge, every action is auditable, and no file ever touches Anthropic's cloud.**

---

## Appendix: Sources

1. VentureBeat — "Anthropic launches Cowork, a Claude Desktop agent" (Jan 13, 2026)
2. VentureBeat — "Anthropic says Claude Code transformed programming, now Claude Cowork is coming for the rest of the enterprise" (Feb 25, 2026)
3. WIRED — "Anthropic's Claude Cowork Is an AI Agent That Actually Works" (Jan 15, 2026)
4. Anthropic Blog — "Cowork: Claude Code for the rest of your work" (Jan 2026)
5. Anthropic Help Center — "Use Cowork safely"
6. WinBuzzer — "Microsoft Launches Copilot Cowork, Powered by Anthropic's Claude" (Mar 10, 2026)
7. Anthropic Docs — "Claude Code on Amazon Bedrock" (code.claude.com)
8. Anthropic Docs — "Agent SDK overview" (platform.claude.com)
9. Anthropic Docs — "How the agent loop works" (platform.claude.com)
10. DigitalApplied — "v0 vs Lovable vs Bolt: AI App Builder Comparison 2025"
11. Tailkits — "Lovable AI Code Builder – Features, Pricing & 2025 Updates"
12. Hackceleration — "Replit Review 2026: We Tested Agent 3 AI"
13. GitHub — "awesome-sandbox" (sandboxing comparison)
14. StackBlitz Blog — "Introducing WebContainers"
15. Spring.io — "AWS Bedrock Prompt Caching" (Bedrock vs Anthropic direct caching comparison)
16. CloudEagle — "GitHub Copilot Pricing Guide" (2026)
17. GitHub — "GitHub Spark" (github.com/features/spark)
18. IntuitionLabs — "Claude Enterprise Deployment & Training Guide 2026"

*Report compiled 2026-03-16 by Bruce Banner (FIP Research Agent)*
