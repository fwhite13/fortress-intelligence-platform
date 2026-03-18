# FAIT Cowork — MVP Architecture Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation Planning  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What Was Read

- `RESEARCH-COWORK.md` — full research from Bruce: Claude Cowork features, competitive landscape, Agent SDK API, sandboxing patterns, recommended technical approach
- `fip/shared/FipShared/Models/FipModule.cs` — current FIP suite modules (FAIT, FIRM, FORMS) + URL/naming pattern
- `fip/shared/FipShared/wwwroot/css/fip-tokens.css` — full design token system (navy/gold, Inter font, spacing scale)
- `fip/fait/src/FortressAI.Web/Program.cs` — FIP auth pattern (cookie consumer, FIP portal owns Entra OIDC, `.FortressAI.Session` shared cookie)
- Session context: AWS account `742932328420`, region `us-east-1`, ECS cluster `fortress-tools-cluster`

**Nothing guessed.** All decisions derived from live code and research.

---

## 1. Product Definition

### What Is FAIT Cowork?

FAIT Cowork is an **agentic task assistant** for non-technical FIP business users. It is the sovereign, FORGE-grounded equivalent of Claude Cowork — same "leave a task for a coworker" mental model, but:
- All data stays within FIP's AWS infrastructure
- Every action is auditable
- Outputs are grounded in FORGE knowledge
- Authentication is FIP Entra SSO — no separate Anthropic account

### The "Task, Not Chat" Mental Model

**NOT this:** "Ask a question → get an answer → ask follow-up."  
**THIS:** "Describe what you need → Claude plans and works → you review → Claude delivers a file or output."

The primary interaction unit is a **Task**, not a message. A task has:
- A description (what the user wants)
- Uploaded inputs (files, images, text)
- An agentic execution thread (Claude working, streamed to the user)
- One or more output artifacts (HTML file, Word doc, analysis summary)
- An audit trail

This framing is critical for UX. When building the UI, every design decision should be tested against the question: "Does this feel like handing a task to a competent colleague, or does it feel like typing into a chat box?"

### What FAIT Cowork Is NOT

| NOT | Because |
|-----|---------|
| A chatbot | There are no "messages" — there are tasks. Tasks have outputs. |
| A code IDE for developers | Tony, Natasha, and the engineering team use Claude Code. Cowork is for Elise and Lauren. |
| An Excel/PPT add-in | That's FfE and FfP. Cowork is standalone browser-based. |
| A document storage system | Cowork produces outputs. FORGE stores knowledge. Cowork doesn't replace either. |
| A real-time search tool | FORGE search is already in FAIT. Cowork uses FORGE as grounding context, not as a query interface. |
| A general internet browser agent | Phase 1 has no web browsing. FORGE is the context source. Phase 2+ can add connectors. |

### Primary Users — Phase 1

**Elise Lippe** and **Lauren Williams** — non-technical business users. Neither writes code. Neither manages files via CLI. They work in browser-based tools. They are the Anthropic Cowork target persona.

Phase 1 tasks they'll actually use:

| Task | Input | Output |
|------|-------|--------|
| HTML prototype from description | Text description + optional wireframe image | Rendered HTML file (viewable inline) |
| Document from scattered notes | Pasted text / uploaded .txt or .docx | Structured Word-style document (HTML or .docx) |
| File summarization | Uploaded PDF, .txt, .docx, .xlsx | Summary document with key findings |
| Data analysis from spreadsheet | Uploaded .xlsx or .csv | Analysis summary with insights and table extracts |

### One-Sentence Pitch

> FAIT Cowork is Claude Cowork — same magic, sovereign by default — where every output is grounded in FORGE knowledge, every action is auditable, and no file ever touches Anthropic's cloud.

---

## 2. FIP Integration

### Where Does Cowork Live in the FIP Suite?

**URL:** `https://cowork.dev.fortressam.ai` (dev) / `https://cowork.fortressam.ai` (prod)

Cowork is a first-class FIP module alongside FAIT, FIRM, and FORMS. It appears in the FIP waffle menu. When the Cowork nav item is clicked from any FIP app, the user is taken to `cowork.fortressam.ai`.

### FipModule Enum Addition

**File:** `fip/shared/FipShared/Models/FipModule.cs`

Add `Cowork` to the enum and extend the switch expressions:

```csharp
public enum FipModule
{
    FAIT,
    FIRM,
    FORMS,
    Cowork   // ← new
}

public static class FipModuleExtensions
{
    public static string FullName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "Fortress AI Tools",
        FipModule.FIRM   => "Fortress Intelligence & Risk Management",
        FipModule.FORMS  => "Fortress Form Tools",
        FipModule.Cowork => "FAIT Cowork",       // ← new
        _                => module.ToString()
    };

    public static string ShortName(this FipModule module) => module switch
    {
        FipModule.FAIT   => "FAIT",
        FipModule.FIRM   => "FIRM",
        FipModule.FORMS  => "FORMS",
        FipModule.Cowork => "Cowork",            // ← new
        _                => module.ToString()
    };

    public static string Url(this FipModule module) => module switch
    {
        FipModule.FAIT   => "https://fait.fortressintelligence.com",
        FipModule.FIRM   => "https://firm.fortressintelligence.com",
        FipModule.FORMS  => "https://forms.fortressintelligence.com",
        FipModule.Cowork => "https://cowork.fortressintelligence.com", // ← new
        _                => "#"
    };
}
```

**This is the only FipShared change.** The existing `FipNavBar.razor` already iterates `Enum.GetValues<FipModule>()` — Cowork will appear automatically in the waffle menu once the enum value exists. No `FipNavBar.razor` changes needed.

### FIP Auth: Cookie Consumer Pattern (Same as FAIT)

Cowork follows the exact same auth pattern as FAIT:
- **FIP portal owns Entra OIDC.** Cowork does not register its own Entra OIDC handler.
- **Shared `.FortressAI.Session` cookie.** Issued by FIP portal after Entra SSO; all FIP apps read it.
- **Data protection key ring shared** via the FIP PostgreSQL key ring database.
- **Unauthenticated redirect:** `GET /auth/redirect-to-login` → FIP portal → Entra → back to Cowork.

The Cowork backend is an ASP.NET Core app (see Section 4). Its `Program.cs` auth setup is a copy of FAIT's `Program.cs` auth section (lines 152–160, 316–340), with `fait` replaced by `cowork` in the callback URLs.

**Env vars Cowork needs (same pattern as FAIT):**
```
FIP__LoginUrl=https://fip.dev.fortressam.ai
FIP__CoworkCallbackUrl=https://cowork.dev.fortressam.ai/auth/cowork-session
FIP_KEYRING_DB_NAME=cowork_dev (or shared with fred_dev — TBD with DevOps)
```

### Design Tokens and Visual Identity

Cowork uses `fip-tokens.css` verbatim — the same design token file already in `FipShared/wwwroot/css/`. No new colors, no dark theme. All design decisions use existing CSS custom properties:

- Background: `--color-bg-page` (`#F8FAFC`)
- Surface/card: `--color-bg-card` (`#FFFFFF`)
- Header: `--color-header-bg` (`#1E293B`)
- Accent: `--color-gold` (`#C9A84C`)
- Primary text: `--color-text-primary` (`#0F172A`)
- Border: `--color-border` (`#E2E8F0`)
- Font: `--font-primary` (Inter)

Cowork is a **light-mode** application. No dark theme. No `PaletteDark`. Consistent with the FIP suite-wide light mode decision.

### FIP Nav in Cowork

Since Cowork is a React/Next.js app (not Blazor), it **cannot use `FipNavBar.razor`** directly. It needs a React equivalent of the FIP nav bar — a `FipNavBar` React component.

For Phase 1 MVP, this is a simplified header-only nav (no waffle menu — that's a future sprint). It shows:
- FIP "F" wordmark (SVG)
- "FAIT Cowork" label
- User avatar with email (from session)
- Sign out link

The full waffle menu integration (letting Cowork users navigate to FAIT/FIRM/FORMS via the nav) is a Phase 2 task — it requires the waffle URLs and is non-blocking for MVP.

---

## 3. Technical Architecture

### System Architecture

```
[Browser — Elise/Lauren]
   │ HTTPS (fip-issued cookie auth)
   ▼
[Cowork Web — Next.js, ECS Fargate]
   │ REST API
   ▼
[Cowork API — ASP.NET Core or Node.js, ECS Fargate]
   │ Per-task spawns ephemeral container
   ▼
[Task Runner Container — Docker, ECS Fargate]
   │ Claude Agent SDK (TypeScript)
   │ AWS Bedrock — Claude Sonnet 4.6
   │ Reads: FIP workspace storage (S3 or EFS)
   │ Writes: output artifacts to S3
   └─────────────────────────────────────────
      [FORGE KB — read-only query via FAIT API]
      [CloudWatch — audit log every tool call]
      [AWS Bedrock — us-east-1]
```

**Data boundary:** Everything stays within FIP's AWS account `742932328420`. No data exits to Anthropic's infrastructure. Bedrock calls are intra-AWS (same account, same region).

### Architecture Decision: Why These Tech Choices

**Frontend: Next.js (React)**

Not Blazor. Reasons:
1. FAIT Cowork is not a Blazor app — it's an interactive web product with real-time streaming, file upload preview, inline HTML rendering, and a task-based UX that benefits from React's composition model
2. Blazor SSR's hydration model (which caused the dead-click crisis in FfP/FfE) is wrong for real-time streaming interfaces
3. The FfE/FfP add-in codebase is React — Tony already has React fluency in this codebase
4. Next.js gives server-side rendering (good for auth cookie reading at page load) + API routes (good for BFF pattern) in one package

Drawback: Can't reuse `FipNavBar.razor`. Mitigation: Phase 1 nav is a simple React header (30 lines). Full waffle nav is Phase 2.

**Backend: Node.js (TypeScript) — single service**

The Claude Agent SDK has a TypeScript package (`@anthropic-ai/claude-agent-sdk`). Running the SDK in a Node.js backend means:
- No language-boundary serialization between the API and the agent loop
- Streaming agent output → browser via SSE is natural in Node.js
- Shared TypeScript type system with the frontend
- One deployable instead of two (avoid a .NET API + Node SDK split)

The `ASP.NET Core` option is viable (could proxy to a Node.js agent subprocess) but adds unnecessary complexity. For Phase 1, a Node.js/TypeScript API that serves both the REST endpoints and the agent loop is cleaner.

**Exception:** If FIP has strong `.NET` operational expertise and wants to stay in the ASP.NET Core stack for consistency, the backend can be ASP.NET Core with a `dotnet-exec` process spawning the Agent SDK CLI per task. This is messy. Recommend Node.js.

**Agent Execution: Server-side container (per task), not per-session container**

Bruce's research recommended "per-session Docker containers." After analysis, the correct granularity is **per-task**, not per-session. Reasons:
- A task has a defined start and end. A session does not.
- Per-task containers are ephemeral — they start on task creation, run to completion, and are discarded. Clean state every time.
- Per-session containers require session management, keepalive, and cleanup that adds operational complexity.
- ECS Fargate supports ephemeral task launch — containers start in ~10 seconds, billed per use. No idle cost.

For Phase 1, the "container" is simply the ECS task itself (the Node.js backend runs in a single ECS task per deployment; the agent loop runs within the same Node.js process, sandboxed by the Node.js file system restrictions). Per-task VM isolation is a Phase 2 security hardening step.

**File Storage: S3 (user workspace bucket)**

Each user has a workspace prefix in a shared S3 bucket: `fip-cowork-workspaces/<userId>/`. The agent's file system access is limited to a temp directory that maps to this prefix (synced via S3 at task start and task completion). In Phase 1, this is simplified: files upload directly to S3, agent reads from S3 download, agent writes to local temp directory, output artifacts are uploaded to S3 on task completion.

Phase 2 adds real bidirectional S3 sync (EFS mount or S3 FUSE for seamless file access during task execution).

**FORGE Integration: Read-only via FAIT API**

Cowork reads FORGE context the same way FfE and FfP do — via `GET/POST /api/haven/kb-search` with an API key. Phase 1: the Cowork backend calls FORGE on the user's behalf before launching the agent, injects the top-3 relevant FORGE results as system context. Phase 2: the agent can call FORGE as a tool mid-task.

**Model: Claude Sonnet 4.6 via AWS Bedrock**

Pinned via env var:
```
CLAUDE_CODE_USE_BEDROCK=1
ANTHROPIC_DEFAULT_SONNET_MODEL=us.anthropic.claude-sonnet-4-6
AWS_REGION=us-east-1
```

Cross-region inference (`us.` prefix) is enabled — routes to whichever US Bedrock endpoint has capacity. This is important because Sonnet 4.6 can have throughput spikes during business hours.

Haiku as the fallback/fast model for simple tasks (file renaming, quick reformatting):
```
ANTHROPIC_DEFAULT_HAIKU_MODEL=us.anthropic.claude-haiku-4-5-20251001-v1:0
```

**Prompt Caching:** Enable on Bedrock (us-east-1 supports it). The FORGE context block injected into every task will be large and repeated per user. Caching saves ~90% of the token cost on the context prefix with Bedrock's 5-minute TTL. Mark the system prompt + FORGE context block as cacheable.

### Agent SDK Integration

The Agent SDK (`@anthropic-ai/claude-agent-sdk`) is imported as a library. Tony does NOT run the CLI and parse its stdout. He uses the programmatic API:

```typescript
import { query } from '@anthropic-ai/claude-agent-sdk';

// Task execution (simplified — real impl adds approval gates, streaming, error handling)
export async function runTask(params: {
  prompt: string;
  workingDir: string;    // temp dir for this task
  forgeContext: string;  // injected FORGE KB results
  maxBudgetUsd: number;
  onChunk: (chunk: AgentChunk) => void;
}): Promise<TaskResult> {

  const systemPrompt = buildSystemPrompt(params.forgeContext);

  for await (const message of query({
    prompt: params.prompt,
    options: {
      cwd: params.workingDir,        // agent's file access is scoped to this dir
      allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
      maxBudgetUsd: params.maxBudgetUsd,
      maxTurns: 30,
      systemPrompt,
      hooks: {
        preToolCall: approvalGateHook(params),
      },
    },
  })) {
    params.onChunk(normalizeChunk(message));
    if ('result' in message) {
      return { success: true, result: message.result };
    }
  }

  return { success: false, result: 'Task ended without result' };
}
```

**Tool whitelist for Phase 1:**
- `Read` — read files in the working directory
- `Write` — write files in the working directory
- `Edit` — edit files in the working directory
- `Bash` — restricted to safe commands (see Security Model)

**Tool blacklist for Phase 1:** No `WebSearch`, no `WebFetch`, no network tools. The agent's internet access is zero in Phase 1.

### Approval Gate Hook

The preToolCall hook intercepts every tool call before execution and can block or approve:

```typescript
function approvalGateHook(params: TaskParams) {
  return async (toolName: string, toolInput: any) => {
    // Audit log every tool call
    await auditLog.record({ toolName, toolInput, taskId: params.taskId, userId: params.userId });

    // Destructive Bash commands require user approval
    if (toolName === 'Bash' && isDestructiveCommand(toolInput.command)) {
      const approved = await waitForApproval(params.taskId, toolName, toolInput);
      if (!approved) return { action: 'block', reason: 'User declined' };
    }

    // Allow all file reads/writes in working dir (already sandboxed by cwd)
    return { action: 'allow' };
  };
}

function isDestructiveCommand(cmd: string): boolean {
  const destructivePatterns = ['rm ', 'rmdir', 'del ', 'format', 'mkfs', 'dd ', '> /', 'sudo'];
  return destructivePatterns.some(p => cmd.toLowerCase().includes(p));
}
```

The "wait for approval" flow is async: the gate emits an `approval_required` SSE event to the browser, pauses execution, and waits for a `POST /tasks/:id/approve` or `POST /tasks/:id/reject` API call from the user.

---

## 4. Repo + Deployment

### Repo Location

**`fip/cowork/`** — inside the `fip/` monorepo, parallel to `fip/fait/`, `fip/firm/`, `fip/forms/`.

Rationale: Same Docker build context expansion (already handled for FfP/WI813), same CI/CD pipeline, same ECR registry pattern, same ECS cluster.

```
fip/
├── fait/          ← existing
├── firm/          ← existing
├── forms/         ← existing
├── shared/        ← existing (FipNavBar, FipModule)
└── cowork/        ← NEW
    ├── src/
    │   ├── web/           ← Next.js frontend (React)
    │   │   ├── app/       ← Next.js app router pages
    │   │   ├── components/
    │   │   └── lib/       ← API client, types
    │   └── api/           ← Node.js/TypeScript API
    │       ├── routes/    ← Express/Fastify routes
    │       ├── agent/     ← Agent SDK integration
    │       ├── services/  ← file storage, FORGE client, audit log
    │       └── middleware/ ← auth, rate limiting
    ├── Dockerfile.web
    ├── Dockerfile.api
    ├── buildspec.yml
    └── docker-compose.dev.yml
```

**Decision: Two Dockerfiles (web + api) or one?**

For Phase 1: **One Dockerfile, one ECS service.** Run both Next.js and the API in the same container on different ports (or let Next.js API routes serve as the BFF layer). This is simpler to deploy and operate, at the cost of not being able to scale web and API independently. Phase 2 can split them when load requires it.

**Revised structure with single service:**
```
fip/cowork/
├── Dockerfile        ← builds both web and api, runs as single service
├── package.json      ← workspace root (npm workspaces)
├── packages/
│   ├── web/          ← Next.js app (uses Next.js API routes for BFF)
│   └── agent/        ← Agent SDK wrapper (imported by Next.js API routes)
└── buildspec.yml
```

### ECR Repository

New ECR repository: `cowork-web` in account `742932328420`, region `us-east-1`.

Follow the same pattern as `fred-chat` (FAIT) and `firm-web` (FIRM).

### ECS Service

New ECS service on the existing `fortress-tools-cluster`:
- Service name: `cowork-web`
- Task definition: `cowork-web`
- Launch type: Fargate
- Container image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web`
- CPU: 512 (0.5 vCPU) — scale up when agent tasks need it
- Memory: 1024 MB — agent SDK + Next.js comfortably fit
- Environment variables: (see below)

### Task Definition Environment Variables

```
# Auth (same pattern as FAIT)
FIP__LoginUrl=https://fip.dev.fortressam.ai
FIP__CoworkCallbackUrl=https://cowork.dev.fortressam.ai/auth/cowork-session
FIP_KEYRING_DB_NAME=cowork_dev        # or shared DB — TBD with DevOps

# Agent / Bedrock
CLAUDE_CODE_USE_BEDROCK=1
AWS_REGION=us-east-1
ANTHROPIC_DEFAULT_SONNET_MODEL=us.anthropic.claude-sonnet-4-6
ANTHROPIC_DEFAULT_HAIKU_MODEL=us.anthropic.claude-haiku-4-5-20251001-v1:0

# Storage
COWORK_S3_BUCKET=fip-cowork-workspaces
COWORK_S3_REGION=us-east-1

# FORGE API (same endpoint as FAIT/FfE)
FORGE_API_URL=https://fait.dev.fortressam.ai
FORGE_API_KEY=<service account API key — different from user API keys>

# Cost limits
COWORK_MAX_BUDGET_USD_PER_TASK=0.50
COWORK_MAX_TURNS_PER_TASK=30
```

**IAM Role:** The ECS task execution role needs permissions for:
- `bedrock:InvokeModel` / `bedrock:InvokeModelWithResponseStream` on `us.anthropic.claude-*` model ARNs
- `s3:GetObject`, `s3:PutObject`, `s3:DeleteObject` on `fip-cowork-workspaces` bucket
- Standard ECS task execution permissions (ECR, CloudWatch Logs)

No `bedrock:CreateModelInvocationJob` — Phase 1 uses synchronous inference only.

### Dockerfile

```dockerfile
FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
COPY package.json package-lock.json ./
COPY packages/web/package.json ./packages/web/
COPY packages/agent/package.json ./packages/agent/
RUN npm ci --workspaces

# Build agent package
COPY packages/agent/ ./packages/agent/
RUN npm run build --workspace=packages/agent

# Build Next.js app
COPY packages/web/ ./packages/web/
RUN npm run build --workspace=packages/web

FROM node:22-alpine AS production
WORKDIR /app
ENV NODE_ENV=production

COPY --from=base /app/packages/web/.next ./packages/web/.next
COPY --from=base /app/packages/web/public ./packages/web/public
COPY --from=base /app/packages/agent/dist ./packages/agent/dist
COPY --from=base /app/node_modules ./node_modules
COPY package.json ./

EXPOSE 3000
CMD ["npm", "run", "start", "--workspace=packages/web"]
```

### BuildSpec

`fip/cowork/buildspec.yml`:

```yaml
version: 0.2
phases:
  pre_build:
    commands:
      - aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin 742932328420.dkr.ecr.us-east-1.amazonaws.com
      - COMMIT_HASH=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c 1-7)
      - IMAGE_TAG=${COMMIT_HASH:=latest}
  build:
    commands:
      # Build context is the fip/ monorepo root (same as FAIT post-WI813 change)
      - docker build -f cowork/Dockerfile -t cowork-web .
      - docker tag cowork-web:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG
      - docker tag cowork-web:latest 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:latest
  post_build:
    commands:
      - docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG
      - docker push 742932328440.dkr.ecr.us-east-1.amazonaws.com/cowork-web:latest
      - printf '[{"name":"cowork-web","imageUri":"%s"}]' 742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:$IMAGE_TAG > imagedefinitions.json
artifacts:
  files: imagedefinitions.json
```

---

## 5. Security Model

### What the Agent Can Touch

**In Phase 1 (permitted):**
- Read files uploaded by the current user to their workspace `/tmp/cowork-<taskId>/`
- Write/create files in the same working directory
- Execute `Bash` commands from a whitelist: `node`, `python3`, `cat`, `echo`, `mkdir`, `cp`, `mv`, `ls`, `wc`, `grep`, `sed`, `awk`, `sort`, `uniq`, `head`, `tail`, `curl` (localhost only), `jq`, `date`

**Blocked:**
- Any path outside `/tmp/cowork-<taskId>/` — the agent's `cwd` is set to this directory and the Agent SDK enforces it
- `rm -rf`, `sudo`, `chmod 777`, `mkfs`, `dd`, `wget` (external), any command writing to `/etc/`, `/usr/`, `/root/`
- Network access to any external URL (no `WebSearch`, no `WebFetch` tools)
- Bedrock model invocation on behalf of the agent (nested model calls) — the agent IS the model; it does not call other models
- Reading environment variables containing secrets (`process.env.FORGE_API_KEY`, etc.) — the working directory is isolated; env vars are not passed to Bash subprocess

### Approval Gates

**Auto-approved (no user prompt):**
- `Read` tool — reading any file in working directory
- `Write` / `Edit` tool — writing any file in working directory
- Safe `Bash` commands (cat, echo, ls, grep, python3, node)

**Requires user approval (pops approval dialog in browser, task pauses):**
- Any Bash command matching `isDestructiveCommand()` pattern
- Future: network access if added in Phase 2

**Always blocked (no approval possible):**
- Commands that modify system paths
- Commands that exfiltrate data to external services
- Any tool not in the Phase 1 whitelist

### Audit Log

Every event is logged to CloudWatch Logs in the `cowork-tasks` log group:

```json
{
  "timestamp": "2026-03-16T14:32:00Z",
  "taskId": "task-abc123",
  "userId": "elise.lippe@fortressam.ai",
  "event": "tool_call",
  "tool": "Write",
  "input": { "file_path": "prototype.html", "content": "..." },
  "approved": true,
  "durationMs": 1240
}
```

Logged events: `task_created`, `task_started`, `tool_call`, `approval_requested`, `approval_granted`, `approval_denied`, `task_completed`, `task_failed`, `task_cancelled`.

The audit log is append-only. Cowork has no `DeleteLogStream` IAM permission.

### Data Classification

Files uploaded to Cowork are treated as **internal/sensitive** by default:
- Stored in S3 with server-side encryption (SSE-S3)
- Bucket policy: no public access, no cross-account access
- Pre-signed URLs for download (15-minute expiry)
- Files retained for 30 days, then auto-deleted via S3 lifecycle policy

Phase 2 adds a formal data classification UI (user marks files as `public`, `internal`, `confidential`).

---

## 6. MVP Scope (Phase 1)

### What Tony Builds in Sprint 1

Sprint 1 is the foundation. The goal is end-to-end working: user can log in, describe a task, upload a file, watch Claude work, and download an output. Not pretty. Not full-featured. Working.

**Sprint 1 deliverables:**

| # | Deliverable | Notes |
|---|-------------|-------|
| 1 | FIP auth integration | Cookie consumer pattern; redirect to FIP for SSO |
| 2 | Task creation UI | Text input for task description + file upload (up to 5 files, 10MB each) |
| 3 | Agent execution | Claude Agent SDK on Bedrock, streaming output to browser via SSE |
| 4 | Task stream UI | Numbered steps ("Claude is reading your file…"), real-time update |
| 5 | Output display | Rendered HTML preview (iframe) + plain-text output + download link |
| 6 | Basic audit log | CloudWatch Logs: task_created, tool_call, task_completed |
| 7 | FipModule.Cowork | Add to FipShared enum (one file change in the shared RCL) |

**Sprint 1 does NOT include:**
- Approval gate UI (auto-approve all for Sprint 1 — approval gate hook is in code but auto-approves)
- Task history (no persistence beyond the current browser session)
- FORGE KB injection (hardcode a placeholder system prompt for S1)
- Multiple concurrent tasks
- A polished UI (functional > beautiful in S1)

**What S1 proves:** The Bedrock-backed Agent SDK works inside FIP's infrastructure, authenticated via FIP Entra SSO, streaming output to a browser in real time.

### Phase 1 Complete (Sprints 1–3 Estimate)

| Sprint | Focus |
|--------|-------|
| S1 | Foundation — auth, task UI, agent execution, streaming, basic output |
| S2 | Output types — HTML preview (iframe render), document view, file download; approval gates UI |
| S3 | FORGE integration — inject FORGE KB context into agent system prompt; task history (S3-persisted) |

### Phase 2 (Future)

- Persistent user instructions ("always use Fortress brand tone")
- Multiple concurrent tasks with task queue UI
- FORGE write-back (save outputs to FORGE KB)
- FIP waffle nav integration in the React nav bar
- Admin panel: usage dashboard, cost per user, audit log browser
- Advanced file types: PDF extraction, XLSX parsing pre-processing before agent sees data
- Connector framework (Phase 2 analog of Cowork's plugins)

---

## 7. Sprint 1 Spec

### Single CC Session — Sequential Tasks

```
Task 1:  Repo scaffold: fip/cowork/ with package.json workspace structure
Task 2:  packages/agent/ — Agent SDK wrapper (runTask, auditLog stub)
Task 3:  FIP auth middleware — cookie reading + redirect-to-login
Task 4:  Task API — POST /tasks, GET /tasks/:id/stream (SSE)
Task 5:  S3 service — upload, download, pre-signed URLs
Task 6:  packages/web/ — Next.js scaffold with fip-tokens.css
Task 7:  FipNavBar (React) — simplified header (FIP logo, "Cowork", user avatar)
Task 8:  TaskCreate page — task description input + file upload dropzone
Task 9:  TaskStream component — SSE consumer, step display, progress indicator
Task 10: OutputPanel — HTML iframe preview + text view + download button
Task 11: FipModule.Cowork — add to FipShared/Models/FipModule.cs
Task 12: Dockerfile + buildspec.yml
```

---

### Task 1: Repo Scaffold

Create `fip/cowork/` with the following structure:

```
fip/cowork/
├── package.json          ← npm workspaces root
├── packages/
│   ├── agent/
│   │   ├── package.json
│   │   ├── tsconfig.json
│   │   └── src/
│   │       ├── index.ts
│   │       ├── runner.ts     ← runTask()
│   │       ├── audit.ts      ← auditLog()
│   │       └── types.ts
│   └── web/
│       ├── package.json
│       ├── tsconfig.json
│       ├── next.config.ts
│       └── app/
│           ├── layout.tsx
│           ├── page.tsx            ← redirect to /tasks/new
│           ├── tasks/
│           │   ├── new/page.tsx    ← task creation
│           │   └── [id]/page.tsx   ← task execution + output
│           └── api/
│               ├── tasks/
│               │   ├── route.ts            ← POST /api/tasks
│               │   └── [id]/
│               │       └── stream/route.ts  ← GET /api/tasks/:id/stream (SSE)
│               └── auth/
│                   ├── cowork-session/route.ts
│                   └── redirect-to-login/route.ts
├── Dockerfile
└── buildspec.yml
```

**`package.json` (root):**
```json
{
  "name": "fait-cowork",
  "private": true,
  "workspaces": ["packages/*"],
  "scripts": {
    "dev": "npm run dev --workspace=packages/web",
    "build": "npm run build --workspace=packages/agent && npm run build --workspace=packages/web",
    "start": "npm run start --workspace=packages/web"
  }
}
```

---

### Task 2: `packages/agent/src/runner.ts`

```typescript
import { query } from '@anthropic-ai/claude-agent-sdk';
import { auditLog } from './audit';
import type { TaskParams, AgentChunk, TaskResult } from './types';

const SYSTEM_PROMPT = `You are FAIT Cowork — an AI assistant helping professionals at Fortress Asset Management complete business tasks.

You have access to files uploaded by the user in your working directory. You can read, analyze, and create files. You focus on producing clear, useful outputs: documents, analyses, HTML prototypes, and summaries.

Guidelines:
- Always explain what you're doing as you work (users can see your steps in real time)
- Create output files with clear, descriptive names
- When creating HTML, produce clean, well-structured markup with inline CSS — no external dependencies
- When creating documents, structure them clearly with headers
- Be concise in your explanations but thorough in your outputs
- When finished, explicitly name the output file(s) you created

Data sovereignty: You are running on Fortress AM's private infrastructure backed by AWS Bedrock. No data leaves Fortress AM's environment.`;

export async function runTask(params: TaskParams): Promise<TaskResult> {
  const startedAt = new Date().toISOString();

  await auditLog({
    taskId: params.taskId,
    userId: params.userId,
    event: 'task_started',
    data: { prompt: params.prompt.slice(0, 200) }, // truncate for log
  });

  try {
    for await (const message of query({
      prompt: params.prompt,
      options: {
        cwd: params.workingDir,
        allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
        maxBudgetUsd: params.maxBudgetUsd ?? 0.50,
        maxTurns: params.maxTurns ?? 30,
        systemPrompt: SYSTEM_PROMPT + (params.forgeContext ? `\n\n## FORGE Knowledge Context\n${params.forgeContext}` : ''),
        hooks: {
          preToolCall: async (toolName: string, toolInput: any) => {
            await auditLog({
              taskId: params.taskId,
              userId: params.userId,
              event: 'tool_call',
              data: { tool: toolName, input: toolInput },
            });

            // Phase 1: auto-approve all (approval gate UI in Sprint 2)
            // In Sprint 2: check if destructive, emit approval_required event, await
            return { action: 'allow' as const };
          },
        },
      },
    })) {
      const chunk: AgentChunk = normalizeMessage(message);
      params.onChunk(chunk);

      if ('result' in message) {
        await auditLog({
          taskId: params.taskId,
          userId: params.userId,
          event: 'task_completed',
          data: { durationMs: Date.now() - new Date(startedAt).getTime() },
        });
        return { success: true, result: message.result };
      }
    }
  } catch (error: any) {
    await auditLog({
      taskId: params.taskId,
      userId: params.userId,
      event: 'task_failed',
      data: { error: error.message },
    });
    throw error;
  }

  return { success: false, result: 'Task ended without result' };
}

function normalizeMessage(message: any): AgentChunk {
  if ('result' in message) return { type: 'result', content: message.result };
  if (message.type === 'assistant') return { type: 'assistant', content: message.content };
  if (message.type === 'user') return { type: 'user', content: message.content };
  return { type: 'system', content: JSON.stringify(message) };
}
```

---

### Task 3: `packages/agent/src/types.ts`

```typescript
export interface TaskParams {
  taskId: string;
  userId: string;
  prompt: string;
  workingDir: string;
  forgeContext?: string;
  maxBudgetUsd?: number;
  maxTurns?: number;
  onChunk: (chunk: AgentChunk) => void;
}

export interface AgentChunk {
  type: 'assistant' | 'user' | 'result' | 'system' | 'error';
  content: any;
}

export interface TaskResult {
  success: boolean;
  result: string;
}
```

---

### Task 4: `packages/web/app/api/tasks/route.ts`

```typescript
import { NextRequest, NextResponse } from 'next/server';
import { requireAuth } from '@/lib/auth';
import { createTask, startTaskAsync } from '@/lib/taskRunner';
import { uploadInputFiles } from '@/lib/s3';

export async function POST(req: NextRequest) {
  const user = await requireAuth(req);
  if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

  const formData = await req.formData();
  const prompt = formData.get('prompt') as string;
  const files = formData.getAll('files') as File[];

  if (!prompt?.trim()) {
    return NextResponse.json({ error: 'Prompt required' }, { status: 400 });
  }

  const taskId = crypto.randomUUID();
  const workingDir = `/tmp/cowork-${taskId}`;

  // Upload files to working dir (S3 in prod; local temp in dev)
  await uploadInputFiles(files, workingDir, taskId);

  // Create task record in memory (Phase 1: in-process Map; Phase 2: database)
  createTask(taskId, { userId: user.email, prompt, workingDir });

  // Start agent async — don't await; client polls SSE stream
  startTaskAsync(taskId).catch(console.error);

  return NextResponse.json({ taskId });
}
```

---

### Task 5: `packages/web/app/api/tasks/[id]/stream/route.ts`

```typescript
import { NextRequest } from 'next/server';
import { requireAuth } from '@/lib/auth';
import { getTaskStream } from '@/lib/taskRunner';

export async function GET(req: NextRequest, { params }: { params: { id: string } }) {
  const user = await requireAuth(req);
  if (!user) return new Response('Unauthorized', { status: 401 });

  const encoder = new TextEncoder();
  const stream = new ReadableStream({
    async start(controller) {
      const taskStream = getTaskStream(params.id);
      if (!taskStream) {
        controller.enqueue(encoder.encode(`data: ${JSON.stringify({ type: 'error', content: 'Task not found' })}\n\n`));
        controller.close();
        return;
      }

      for await (const chunk of taskStream) {
        controller.enqueue(encoder.encode(`data: ${JSON.stringify(chunk)}\n\n`));
        if (chunk.type === 'result' || chunk.type === 'error') {
          controller.close();
          return;
        }
      }
      controller.close();
    },
  });

  return new Response(stream, {
    headers: {
      'Content-Type': 'text/event-stream',
      'Cache-Control': 'no-cache',
      'Connection': 'keep-alive',
    },
  });
}
```

---

### Task 6: `packages/web/app/tasks/new/page.tsx` (TaskCreate)

Key UX elements:
1. Task description textarea (large, prominent)
2. File upload dropzone (drag-and-drop; shows file chips when files added)
3. "Start Task" button (gold, disabled when textarea is empty)
4. Task type hint cards (non-interactive, just visual prompts): "HTML Prototype", "Document from Notes", "Summarize Files", "Analyze Data"

```typescript
'use client';
import React, { useState, useRef } from 'react';

export default function NewTask() {
  const [prompt, setPrompt] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [loading, setLoading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!prompt.trim() || loading) return;
    setLoading(true);

    const formData = new FormData();
    formData.append('prompt', prompt);
    files.forEach(f => formData.append('files', f));

    const res = await fetch('/api/tasks', { method: 'POST', body: formData });
    const { taskId } = await res.json();
    window.location.href = `/tasks/${taskId}`;
  };

  return (
    <main style={{ maxWidth: '720px', margin: '0 auto', padding: '32px 16px' }}>
      <h1 style={{ fontSize: 'var(--text-2xl)', fontWeight: 'var(--font-semibold)', color: 'var(--color-text-primary)', marginBottom: '8px' }}>
        New Task
      </h1>
      <p style={{ color: 'var(--color-text-secondary)', marginBottom: '24px', fontSize: 'var(--text-base)' }}>
        Describe what you need. Claude will plan and complete the task step by step.
      </p>

      <form onSubmit={handleSubmit}>
        {/* Prompt textarea */}
        <textarea
          value={prompt}
          onChange={e => setPrompt(e.target.value)}
          placeholder="e.g. Create an HTML prototype of a dashboard showing Q1 fund performance metrics. Include a summary table and a simple chart placeholder."
          rows={6}
          style={{
            width: '100%', padding: '12px 14px', fontSize: 'var(--text-base)',
            border: '1px solid var(--color-border)', borderRadius: 'var(--radius-md)',
            fontFamily: 'var(--font-primary)', resize: 'vertical', boxSizing: 'border-box',
            color: 'var(--color-text-primary)', background: 'var(--color-bg-input)',
          }}
        />

        {/* File upload */}
        <div
          onClick={() => fileInputRef.current?.click()}
          style={{
            marginTop: '12px', border: '2px dashed var(--color-border)', borderRadius: 'var(--radius-md)',
            padding: '16px', textAlign: 'center', cursor: 'pointer', color: 'var(--color-text-secondary)',
            fontSize: 'var(--text-sm)', background: 'var(--color-surface-sunken)',
          }}
        >
          {files.length === 0
            ? 'Drop files here or click to upload (PDF, .docx, .xlsx, .txt, .png, .jpg — max 10MB each)'
            : files.map(f => f.name).join(', ')
          }
          <input ref={fileInputRef} type="file" multiple style={{ display: 'none' }}
            onChange={e => setFiles(Array.from(e.target.files ?? []))} />
        </div>

        <button
          type="submit"
          disabled={!prompt.trim() || loading}
          style={{
            marginTop: '16px', padding: '10px 24px', background: 'var(--color-btn-gold-bg)',
            color: 'var(--color-btn-gold-text)', border: 'none', borderRadius: 'var(--radius-md)',
            fontSize: 'var(--text-base)', fontWeight: 'var(--font-semibold)', cursor: loading || !prompt.trim() ? 'not-allowed' : 'pointer',
            opacity: loading || !prompt.trim() ? 0.6 : 1,
          }}
        >
          {loading ? 'Starting…' : 'Start Task →'}
        </button>
      </form>
    </main>
  );
}
```

---

### Task 7: `packages/web/app/tasks/[id]/page.tsx` (TaskStream + OutputPanel)

Key elements:
1. Task description (top, read-only)
2. Live step feed — each SSE chunk renders as a numbered step (e.g., "1. Reading uploaded file…", "2. Analyzing data structure…")
3. Thinking indicator (animated dots) while agent is working
4. `OutputPanel` — once `result` event arrives:
   - If any `.html` file in working dir → iframe preview
   - All output files → download links
5. "New Task" button after completion

The step feed renders each `assistant` chunk from the SSE stream. For Claude's thinking blocks and tool calls, extract the human-readable description:
- `tool_use` blocks → "Using [toolName]: [brief description]"
- Text blocks → render the text verbatim (Claude's narration of what it's doing)

---

### Task 8: FipNavBar (React)

Simple header for Phase 1. No waffle menu (Phase 2):

```typescript
// packages/web/components/FipNavBar.tsx
import React from 'react';

interface FipNavBarProps {
  userEmail: string;
  userInitial: string;
  onSignOut: () => void;
}

export function FipNavBar({ userEmail, userInitial, onSignOut }: FipNavBarProps) {
  return (
    <header style={{
      height: '48px', background: 'var(--color-header-bg)', display: 'flex',
      alignItems: 'center', justifyContent: 'space-between', padding: '0 16px',
      borderBottom: '1px solid rgba(255,255,255,0.08)',
    }}>
      {/* Left: FIP wordmark + app name */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
        <span style={{ fontWeight: 'var(--font-bold)', color: 'var(--color-gold)', fontSize: 'var(--text-lg)', letterSpacing: 'var(--tracking-wide)' }}>
          F
        </span>
        <span style={{ color: 'var(--color-text-inverse)', fontSize: 'var(--text-base)', fontWeight: 'var(--font-medium)' }}>
          FAIT Cowork
        </span>
      </div>

      {/* Right: user avatar + sign out */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
        <span style={{ color: 'var(--color-text-tertiary)', fontSize: 'var(--text-sm)' }}>{userEmail}</span>
        <div style={{
          width: '28px', height: '28px', borderRadius: '50%', background: 'var(--color-gold)',
          color: 'var(--color-header-bg)', display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: '12px', fontWeight: 'var(--font-bold)',
        }}>
          {userInitial}
        </div>
        <button onClick={onSignOut} style={{ background: 'none', border: 'none', color: 'var(--color-text-tertiary)', cursor: 'pointer', fontSize: 'var(--text-sm)' }}>
          Sign out
        </button>
      </div>
    </header>
  );
}
```

---

## Clint Review Priorities

```
⚠️  HIGH: Verify FIP auth cookie pattern is correct. The Cowork Next.js app reads
          the .FortressAI.Session cookie set by the FIP portal. Confirm the
          data protection key ring connection string is set via FIP_KEYRING_DB_NAME
          env var. Without this, cookie validation will fail silently.

⚠️  HIGH: Verify CLAUDE_CODE_USE_BEDROCK=1 is set in the ECS task definition.
          Without this env var, the Agent SDK will attempt to use the Anthropic API
          directly, fail authentication, and not fall back. Confirm in prod task def.

⚠️  HIGH: Verify the Agent SDK's cwd is set to the task working directory
          (/tmp/cowork-<taskId>/). If cwd is not set or is set to /app,
          the agent can read the application source code, Dockerfile, and secrets.
          This is the primary sandboxing control for Phase 1 — it must be correct.

⚠️  HIGH: Verify the S3 bucket (fip-cowork-workspaces) has:
          - Block all public access: ON
          - SSE-S3 encryption: ON (at minimum)
          - No public bucket policy
          - Pre-signed URL expiry: 15 minutes (900 seconds)
          File exfiltration via a misconfigured S3 bucket is a high-severity risk.

⚠️  MEDIUM: Verify FipModule.Cowork is added to FipShared/Models/FipModule.cs
            and ALL switch expressions are updated (FullName, ShortName, Url).
            C# exhaustive switch warns but does not error on missing cases —
            a missing Cowork case will silently return the default ("_") value.

⚠️  MEDIUM: Verify the Bedrock IAM role only has InvokeModel and
            InvokeModelWithResponseStream permissions on the specific
            us.anthropic.claude-* model ARN patterns. Do not use
            bedrock:* — least privilege is critical for a multi-user product.

⚠️  MEDIUM: Verify the agent SDK version is pinned in packages/agent/package.json.
            Do not use "latest" — the Agent SDK API surface can break between
            minor versions. Pin to a specific semver (e.g., "1.2.3").

⚠️  LOW: Verify the HTML iframe in OutputPanel has the sandbox attribute set:
         <iframe sandbox="allow-scripts allow-same-origin" ...>
         Without sandbox, user-uploaded HTML content could access the parent
         page's DOM. This is a Phase 1 XSS vector.
```

---

## Open Questions for Fred

1. **Node.js vs .NET API backend:** Spec recommends Node.js for Agent SDK alignment. If FIP operations team strongly prefers .NET for consistency with FAIT/FIRM/FORMS, this is negotiable — the ASP.NET Core approach adds complexity but is feasible. Needs a decision before Tony starts Task 2.

2. **Data protection key ring:** Should Cowork share the `fred_dev` DB for key ring, or use its own `cowork_dev` DB? Sharing is simpler but couples the deployments. Decision needed for the auth middleware.

3. **Phase 1 task persistence:** Current spec uses an in-process `Map` (task data lost on container restart). For a production Phase 1, a Redis or DynamoDB backing for task state is needed. Simpler to add in Sprint 2 — confirm this is acceptable for initial testing.

4. **FORGE API key for Cowork:** Cowork calls FORGE on behalf of users. Does each user's API key get passed through, or does Cowork use a service account API key? Service account is simpler and avoids key management per-user, but means all Cowork FORGE access is attributed to one identity. Recommendation: service account for Phase 1; per-user key pass-through in Phase 2.

---

_Spec by Reed Richards | FAIT Cowork MVP: sovereign Claude Cowork equivalent on Bedrock. 12 Sprint 1 tasks. No data leaves FIP's AWS account._
