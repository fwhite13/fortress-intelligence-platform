# REVIEW BRIEF — ADO#2866 — Stitch MCP Integration (Cycle 1)
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §6.3**
**Reviewer:** Hawkeye (Clint Barton) | **Cycle:** 1 | **Date:** 2026-05-07

---

## Context

You are Clint Barton (Hawkeye), code reviewer for the FIP pipeline.

Reviewing ADO#2866 — Google Stitch MCP integration into the FAIT v2 Fargate agent harness.

**Repo:** `~/projects/fip/fait-v2/` | **Branch:** `main` | **Commit:** `0f90656`
**Spec:** `memory/projects/fait-v2-spec-2026-04-27.md` (§6.3)

---

## What Tony Built

**Harness (`agent-harness/`):**
- `package.json` — added `stitch-mcp: latest` + `@aws-sdk/client-secrets-manager: ^3.0.0`
- `Dockerfile` — added `RUN npm install -g stitch-mcp`
- `harness-server.js`:
  - `bootstrapGcpCredentials()` — pulls JSON key from Secrets Manager (`fait-v2/gcp-stitch-service-account`), writes to `/tmp/gcp-service-account.json`, sets `GOOGLE_APPLICATION_CREDENTIALS`
  - `invokeStitchTool()` — spawns `stitch-mcp` as subprocess, MCP JSON-RPC over stdio
  - `GET /tools/stitch/health` — returns `{ available: bool, reason }`
  - `POST /tools/:toolName` — routes 7 Stitch tools to `invokeStitchTool`
  - Async IIFE startup (bootstrap before listen)

**Blazor (`src/FortressAI.V2.Web/`):**
- `IDesignAgentService.cs` — `IsStitchAvailableAsync(string userId, CancellationToken)` signature updated
- `DesignAgentService.cs` — `IsStitchAvailableAsync` now calls `GET /tools/stitch/health` via `IHttpClientFactory`; all 3 callers updated
- `DesignAgentView.razor` — passes `_userId` to `IsStitchAvailableAsync`

**Repo:**
- `CLAUDE.md` — created with architecture + Stitch MCP docs

---

## Review Focus Areas

### 1. Credential bootstrap safety
- Does `bootstrapGcpCredentials()` handle the case where Secrets Manager is unavailable (network error, missing secret) without crashing the harness?
- Is the `/tmp/gcp-service-account.json` file written with `mode: 0o600`?
- Is `GOOGLE_APPLICATION_CREDENTIALS` set BEFORE the Stitch MCP subprocess is spawned?
- Does the health endpoint accurately reflect credential state?

### 2. MCP stdio invocation — `invokeStitchTool()`
- Is the MCP JSON-RPC protocol correct (jsonrpc 2.0, `tools/call` method, `params.name` + `params.arguments`)?
- Are errors from the subprocess caught and returned gracefully (no unhandled promise rejections)?
- Is there a timeout on the subprocess call? (Stitch can be slow — missing timeout = hung request)
- Are the 7 tool names correct and does the routing cover all of them?

### 3. Blazor `IsStitchAvailableAsync` — HTTP call to harness
- Does it use `IHttpClientFactory` (named client) rather than raw `HttpClient`?
- Does it handle `HttpRequestException` (harness not yet started) gracefully — returning `false`, not throwing?
- Does it correctly parse `{ available: bool }` from the response?

### 4. CLAUDE.md
- Is it at repo root and does it accurately describe the Stitch MCP integration?

### 5. CSS variable rule
- Any Razor changes must use CSS variables only — no hardcoded colors/fonts/sizes

### 6. Build
- `dotnet build` — 0 errors, 0 warnings

---

## Mandatory: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat review-2866-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC must read the actual files. Do NOT review by reasoning alone.

---

## ADO Comment (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2866,"text":"**[Hawkeye — REVIEW cycle 1]**\nCode review PASS/NEEDS-CHANGES. Cycles: 1. [details]."}'
```

---

## Deliverables
1. Review Report at `~/projects/fip/fait-v2/pipeline/ADO2866-REVIEW-REPORT.md`
2. ADO comment on #2866
3. Announce verdict to Maria
