# BUILD BRIEF — ADO#2866 — Google Stitch MCP Integration
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §6.3 Design Agent**
**Agent:** Tony Stark | **Cycle:** 1 | **Date:** 2026-05-07

---

## Context

You are Tony Stark (software-engineer). You are implementing FAIT v2 Sprint 3, WI #2866.
FAIT v2 repo: `~/projects/fip/fait-v2/` | branch: `main`
Spec: `memory/projects/fait-v2-spec-2026-04-27.md` (§6.3)
Current HEAD: `bda1964` (Design Agent Blazor layer from WI#2865 — already on main)

---

## What Was Built Already (on main)

- `IUserAgentRuntime.DispatchToolCallAsync` — added by WI#2865, stubs POST to harness `/tools/{toolName}`
- `DesignAgentService` — calls `DispatchToolCallAsync` for Stitch tools (generate, extract, refine)
- `agent-harness/Dockerfile` — Node.js 20 + Claude Code CLI + AWS CLI
- `agent-harness/harness-server.js` — HTTP shim server (Express, port 3000)

---

## Objective

Wire Google Stitch MCP into the FAIT v2 agent harness so that Stitch tools are callable from within the Fargate container. The Blazor app (DesignAgentService) dispatches tool calls to the harness via HTTP; the harness runs the Stitch MCP server and proxies the call.

---

## What to Build

### 1. Install `@google/stitch-mcp` in the agent harness

In `agent-harness/package.json`, add:
```json
"@google/stitch-mcp": "latest"
```

In `agent-harness/Dockerfile`, after the existing npm install:
```dockerfile
# Install Stitch MCP globally so it's available as npx stitch-mcp
RUN npm install -g @google/stitch-mcp
```

If `@google/stitch-mcp` is not the correct package name, check npm for the correct Stitch MCP package. The Stitch MCP server is invoked as `npx stitch-mcp` or similar.

### 2. GCP Service Account Credential Injection

The GCP service account JSON is stored in AWS Secrets Manager as:
`fait-v2/gcp-stitch-service-account`
(Secret: `arn:aws:secretsmanager:us-east-1:742932328420:secret:fait-v2/gcp-stitch-service-account`)

The harness needs `GOOGLE_APPLICATION_CREDENTIALS` pointing to a file containing the JSON key.

In `harness-server.js`, at startup (before starting Stitch MCP), add a credential bootstrap step:
```javascript
const { SecretsManagerClient, GetSecretValueCommand } = require('@aws-sdk/client-secrets-manager');
const fs = require('fs');
const path = require('path');

async function bootstrapGcpCredentials() {
    const secretName = process.env.GCP_STITCH_SECRET_NAME || 'fait-v2/gcp-stitch-service-account';
    if (!secretName) return;
    
    try {
        const client = new SecretsManagerClient({ region: process.env.AWS_REGION || 'us-east-1' });
        const response = await client.send(new GetSecretValueCommand({ SecretId: secretName }));
        const credPath = '/tmp/gcp-service-account.json';
        fs.writeFileSync(credPath, response.SecretString, { mode: 0o600 });
        process.env.GOOGLE_APPLICATION_CREDENTIALS = credPath;
        console.log('[harness] GCP credentials bootstrapped');
    } catch (err) {
        console.warn('[harness] GCP credentials not available — Stitch will be unavailable:', err.message);
    }
}
```

Add `await bootstrapGcpCredentials()` at harness startup (before any route handlers are registered).

Add `@aws-sdk/client-secrets-manager` to `agent-harness/package.json` dependencies.

### 3. Stitch MCP Tool Routing in harness-server.js

The harness already has (or needs) a `/tools/:toolName` endpoint. Wire Stitch tools through it:

```javascript
const stitchTools = [
    'generate_screen_from_text',
    'extract_design_context', 
    'fetch_screen_code',
    'fetch_screen_image',
    'list_projects',
    'list_screens',
    'refine_screen'
];

// In the /tools/:toolName handler, add Stitch tool dispatch:
if (stitchTools.includes(toolName)) {
    // Invoke via Stitch MCP using child_process or MCP client
    const result = await invokeStitchTool(toolName, args);
    return res.json({ result });
}
```

For invoking Stitch MCP, use the MCP stdio transport pattern (spawn `stitch-mcp` as a subprocess and communicate via stdin/stdout JSON-RPC). If the package exposes a Node.js API, prefer that.

### 4. Add `/tools/stitch/health` endpoint

```javascript
app.get('/tools/stitch/health', async (req, res) => {
    const available = !!process.env.GOOGLE_APPLICATION_CREDENTIALS && 
                      fs.existsSync(process.env.GOOGLE_APPLICATION_CREDENTIALS);
    res.json({ available, reason: available ? 'ok' : 'GCP credentials not configured' });
});
```

This endpoint is called by `IsStitchAvailableAsync` in the Blazor app — it should return `{ available: true }` when GCP credentials are loaded.

Update `DesignAgentService.IsStitchAvailableAsync` in the Blazor app (if needed) to call `GET {harnessUrl}/tools/stitch/health` and read `available` from the response.

### 5. CLAUDE.md update

Add Stitch MCP to the system CLAUDE.md MCP server directory at `~/projects/fip/fait-v2/CLAUDE.md` (or wherever the harness CLAUDE.md lives):

```markdown
## Stitch MCP (Google Labs)
- Purpose: HTML/CSS visual screen generation, design DNA extraction
- Tools: generate_screen_from_text, extract_design_context, fetch_screen_code, fetch_screen_image, list_projects, list_screens
- Auth: GCP service account (GOOGLE_APPLICATION_CREDENTIALS env var, bootstrapped from Secrets Manager at startup)
- Availability: Only when GCP credentials are configured. Check /tools/stitch/health before use.
```

### 6. Acceptance Criteria
- [ ] `@google/stitch-mcp` (or correct package) installed in agent harness image
- [ ] GCP credentials bootstrapped from `fait-v2/gcp-stitch-service-account` secret at harness startup
- [ ] `GOOGLE_APPLICATION_CREDENTIALS` set to `/tmp/gcp-service-account.json` at runtime
- [ ] `generate_screen_from_text` callable via `POST /tools/generate_screen_from_text` on harness
- [ ] `extract_design_context` callable via `POST /tools/extract_design_context` on harness
- [ ] `/tools/stitch/health` returns `{ available: true }` when credentials loaded
- [ ] `IsStitchAvailableAsync` in Blazor hits the harness health endpoint (not just config check)
- [ ] Stitch MCP listed in CLAUDE.md
- [ ] Graceful degradation: if Stitch secret missing, harness logs warning and health endpoint returns `available: false` — no crash
- [ ] Build: `dotnet build` succeeds (if any Blazor changes), harness Node server starts clean
- [ ] CC CLI used (mandatory)

---

## Mandatory Rules
- **CC CLI MANDATORY:**
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/fait-v2/`
- Commit: `feat(fait-v2#2866): wire Stitch MCP into Fargate harness`
- CSS variable rule applies to ANY Blazor UI changes
- No hardcoded AWS account IDs or region strings — use env vars
- varchar(36) for GUID columns, GuidFormat=None on ALL MySQL connections

---

## Key Info
- **Stitch secret ARN:** `arn:aws:secretsmanager:us-east-1:742932328420:secret:fait-v2/gcp-stitch-service-account`
- **GCP project:** `fortress-stitch` (#228012980634)
- **GCP service account:** `stitch-service-account@fortress-stitch.iam.gserviceaccount.com`
- **Harness Docker image ECR repo:** `fait-v2-harness` (if separate) or same ECR as main app — check existing CodeBuild config
- **Harness server:** `agent-harness/harness-server.js`

---

## ADO Work Item Updates (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2866,"text":"**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED."}'
```

---

## Deliverables
1. Build Report at `~/projects/fip/fait-v2/pipeline/ADO2866-BUILD-REPORT.md`
2. All changes committed and pushed to `origin/main`
3. ADO WI #2866 comment with commit hash
4. Report back to Maria
