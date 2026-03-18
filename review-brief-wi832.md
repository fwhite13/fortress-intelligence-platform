# WI832 Review Brief — FAIT Cowork Sprint 1

You are reviewing code in `/home/fredw/projects/fip/cowork/` (commit 668c18b).
This is a two-container new service: CoworkWeb (.NET 8 Blazor Server) and CoworkAgent (Node.js TypeScript Express).

## Files to Review

### Critical Files
1. `cowork/src/CoworkWeb/Program.cs` — DataProtection config, cookie auth
2. `cowork/src/CoworkWeb/Components/Pages/TaskPage.razor` — iframe sandbox attribute
3. `cowork/src/CoworkWeb/Services/InternalTokenService.cs` — JWT secret from env
4. `cowork/src/CoworkAgent/src/middleware/auth.ts` — JWT validation, secret at module load
5. `cowork/src/CoworkAgent/src/agent/runner.ts` — Agent SDK allowedTools
6. `cowork/src/CoworkAgent/src/routes/tasks.ts` — SSE stream, req.on('close') cleanup
7. `cowork/src/CoworkAgent/src/agent/audit.ts` — CloudWatch error handling
8. `cowork/Dockerfile.web` and `cowork/Dockerfile.agent` — build context paths
9. `shared/FipShared/Models/FipModule.cs` — additive-only change

### Additional Files
10. `cowork/src/CoworkWeb/CoworkWeb.csproj` — target framework
11. `cowork/src/CoworkAgent/src/server.ts` — overall structure

## Priority Checks to Verify

### CRITICAL: DataProtection in Program.cs
Verify `AddDataProtection()` has BOTH:
- `.SetApplicationName("FortressAI")` — exact string, not "Cowork" or "FortressAI-Cowork"
- `.DisableAutomaticKeyGeneration()`
Both must be present. Missing either breaks shared .FortressAI.Session cookie for ALL FIP apps.

### CRITICAL: iframe sandbox in TaskPage.razor
Find the iframe element. Verify `sandbox` attribute is exactly `"allow-scripts"` with NO additional tokens.
`allow-same-origin` would be a security hole — the rendered HTML could access parent cookies and DOM.

### CRITICAL: JWT secret — no hardcoded fallback
- `InternalTokenService.cs`: secret must come from `config["CoworkAgent:InternalSecret"]` and throw if missing. No `?? "dev-secret"` fallback.
- `auth.ts`: secret must come from `process.env.COWORK_INTERNAL_SECRET` and throw if missing. No fallback default.

### HIGH: auth.ts secret check — module load time
The `throw new Error('COWORK_INTERNAL_SECRET env var required')` in auth.ts must be at MODULE LOAD TIME (top-level, outside any function). If it's inside the middleware function, an unset secret won't be caught until the first authenticated request.

### HIGH: FipModule.cs — additive only
Verify:
1. No existing enum values (FAIT, FIRM, FORMS) were modified or removed
2. No existing switch cases were modified
3. Cowork was appended to enum and switch cases only
4. The `Url()` switch for Cowork uses same domain format as other modules

### HIGH: .NET version
CoworkWeb.csproj uses `net8.0`. Confirm this is correct (ALL other FIP apps use net8.0 — spec said net9 but net8 is correct to match the monorepo).

### MEDIUM: allowedTools in runner.ts
`allowedTools: ['Read', 'Write', 'Edit', 'Bash']` — Bash is included. The spec restricts Sprint 1 to file read/write tools. Flag whether Bash is intentional and whether it poses a risk given the env vars passed (COWORK_TASK_ID, COWORK_USER_ID, COWORK_USER_EMAIL).

### MEDIUM: SSE cleanup in tasks.ts
In `GET /tasks/:id/stream` — is there a `req.on('close', ...)` handler to cancel the generator and prevent memory leaks when the browser tab is closed?

### MEDIUM: Dockerfiles — build context paths
`Dockerfile.web` uses `COPY shared/FipShared/...` and `COPY cowork/src/CoworkWeb/...` — these are relative to monorepo root. Good.
`Dockerfile.agent` uses `COPY cowork/src/CoworkAgent/...` — verify this is also relative to monorepo root.
Flag any `./` paths that would only work from inside the cowork/ directory.

### LOW: CloudWatch error handling in audit.ts
Verify the try/catch in `auditLog()` catches `ResourceNotFoundException` gracefully and does NOT crash the process. The log group `/cowork/tasks` may not exist at startup.

### LOW: FipModule.Cowork URL
The `Url()` extension returns `"https://cowork.fortressintelligence.com"`. Does this match the pattern of other modules? Flag if the domain is different from what other modules use.

## What to Report

For each check above, report:
- PASS ✅ or FAIL ❌
- Exact evidence (file path, line, quoted code)
- For failures: exact fix required

Pay special attention to the Bash tool in allowedTools — this is the most nuanced finding. The spec says Sprint 1 should restrict to file read/write tools. Bash being present alongside the system env var injection needs to be flagged even if it appears intentional.

Also check: does the SSE route have NO `req.on('close', ...)` handler? If absent, flag it as a medium issue — the generator keeps running even if the client disconnects.
