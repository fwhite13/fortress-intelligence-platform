# Review Report: WI832
## Verdict: NEEDS-CHANGES
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fip
cat ~/projects/fait-for-excel/review-brief-wi832.md | claude --model sonnet -p
```

First 20 lines of output:
```
## WI832 Code Review — FAIT Cowork Sprint 1

---

### CRITICAL: DataProtection — `Program.cs` ✅ PASS

Lines 67–70:
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();
```
Both required calls are present with the exact string `"FortressAI"`. No issues.

---

### CRITICAL: iframe sandbox — `TaskPage.razor` ✅ PASS

Line 57:
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| SetApplicationName("FortressAI") present | ✅ | `Program.cs` lines 67–70: `.SetApplicationName("FortressAI")` — exact string confirmed |
| DisableAutomaticKeyGeneration() present | ✅ | `Program.cs` line 70: `.DisableAutomaticKeyGeneration()` — present in same chain |
| iframe sandbox="allow-scripts" only | ✅ | `TaskPage.razor` line 57: `sandbox="allow-scripts"` — no additional tokens |
| JWT secret from env var, no fallback default | ✅ | `InternalTokenService.cs`: throws on null via `?? throw`. `auth.ts`: `if (!SECRET) throw` — no `?? "dev-secret"` in either |
| COWORK_INTERNAL_SECRET throws at module load | ✅ | `auth.ts` lines 4–5: top-level module scope, outside any function — fires on process start |
| FipModule change is additive only | ✅ | FAIT/FIRM/FORMS values and all 3 switch cases unchanged; Cowork appended only |
| .NET 8 correct (matches FIP monorepo) | ✅ | `CoworkWeb.csproj`: `net8.0`. All other FIP apps confirmed net8.0. Spec said net9 — net8 is correct. |
| allowedTools safe for Sprint 1 | ⚠️ | `runner.ts` line 57: `allowedTools: ['Read', 'Write', 'Edit', 'Bash']` — Bash present. Sprint 1 spec restricts to file read/write. Requires explicit sign-off. See Issues. |
| SSE req.on('close') cleanup | ❌ | `tasks.ts` GET `/tasks/:id/stream` has no `req.on('close', ...)` handler — generator runs indefinitely on client disconnect |
| Dockerfiles use monorepo-root paths | ⚠️ | `Dockerfile.agent`: ✅ paths are monorepo-root relative. `Dockerfile.web`: ✅ paths correct BUT uses `.NET 9` Docker images (`sdk:9.0`, `aspnet:9.0`) despite `net8.0` target — see Issues |
| CloudWatch graceful on missing log group | ✅ | `audit.ts`: blanket `catch` swallows all exceptions including ResourceNotFoundException, logs to stderr only — non-fatal |

---

## Issues Found

### ❌ IMPORTANT — Dockerfile.web uses .NET 9 images despite net8.0 target

**File:** `cowork/Dockerfile.web`, lines 1 and 12  
**Severity:** Important (not critical — .NET 9 runtime can run net8.0, but it diverges from every other FIP Dockerfile and must be corrected)

**Current:**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:9.0
```

**Required fix:**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
...
FROM mcr.microsoft.com/dotnet/aspnet:8.0
```

All other FIP Dockerfiles use `8.0` images. This mismatch diverges from the monorepo standard. The `.csproj` correctly targets `net8.0`; the Dockerfile must match.

---

### ❌ IMPORTANT — No SSE close handler in tasks.ts

**File:** `cowork/src/CoworkAgent/src/routes/tasks.ts`, `GET /tasks/:id/stream` handler  
**Severity:** Important

When the browser tab closes mid-task, the `for await` loop over the Claude SDK generator continues running — consuming API credits, holding the working directory open, and orphaning entries in `taskStreams` — until `result` or `error` is eventually yielded or `maxTurns` is exhausted.

**Required fix:**
```typescript
router.get('/:id/stream', async (req, res) => {
  const { id } = req.params;
  const gen = taskStreams.get(id);
  if (!gen) { res.status(404).json({ error: 'Task not found' }); return; }

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  let closed = false;
  req.on('close', () => {
    closed = true;
    taskStreams.delete(id);
  });

  try {
    for await (const chunk of gen) {
      if (closed) break;
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);
      if (chunk.type === 'result' || chunk.type === 'error') {
        taskStreams.delete(id);
        break;
      }
    }
  } catch (err: any) {
    if (!closed) res.write(`data: ${JSON.stringify({ type: 'error', text: err.message })}\n\n`);
  } finally {
    res.end();
  }
});
```

Also check: does `query()` from `@anthropic-ai/claude-agent-sdk` accept an `AbortController` signal? If so, wire it to the `close` event for full generator cancellation.

---

### ⚠️ FLAG — Bash in allowedTools requires explicit sign-off

**File:** `cowork/src/CoworkAgent/src/agent/runner.ts`, line 57  
**Severity:** Flag — not a blocker if intentional and signed off

```typescript
allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
```

The Sprint 1 spec restricts to file read/write tools. `Bash` is the highest-risk tool in this context because the env block injects user identity into the subprocess environment and the container has network egress. The model could use Bash to:
- Exfiltrate uploaded files via `curl` to an external endpoint
- Pivot to other services on the internal Docker network (e.g., FORGE)
- Execute arbitrary shell commands in the task working directory

The developer comment (`// SAFE non-secret identifiers only`) shows awareness that secrets must not appear in env, which is correct. But that does not mitigate Bash risk.

**Required action:** If Bash is needed for Sprint 1 (e.g., `pandoc`, `pip install`), obtain explicit sign-off from Fred and document it in the sprint notes. If it is not required, remove it from `allowedTools` and re-add in a later sprint with proper network-egress controls.

---

## Verdict

**NEEDS-CHANGES** — 2 fixes required before merge, 1 requiring sign-off.

The 3 critical security checks all pass: DataProtection is correctly configured, the iframe sandbox is locked down, and JWT secrets have no fallback values in either language. These were the highest-risk items and they are correct.

Two issues must be fixed:
1. **Dockerfile.web SDK/runtime mismatch** — targets `net8.0` but pulls `.NET 9` images. Straightforward fix: change `sdk:9.0` and `aspnet:9.0` to `sdk:8.0` and `aspnet:8.0`.
2. **Missing SSE close handler** — the generator runs indefinitely on client disconnect. Fix with `req.on('close', ...)` guard as detailed above.

One item requires human decision: **Bash in `allowedTools`**. Remove it or get explicit sign-off. The code otherwise looks production-ready for Sprint 1 scope.

Tony goes back for these two fixes + Bash sign-off/removal. This is a cycle 1 of 2.

---

*Reviewed by Hawkeye (Clint Barton) — code-reviewer — 2026-03-17*

---
## Review Cycle 2

### CC Invocation
```
cd /home/fredw/projects/fip && cat ~/projects/fait-for-excel/review-brief-wi832-c2.md | claude --model sonnet -p
```

First 10 lines of CC output:
```
---

## WI832 C2 Re-Check — Review Verdict

### Fix 1 — .NET 9 Alignment ✅ CONFIRMED

| File | Finding |
|------|---------|
| `Dockerfile.web` L1 | `FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build` ✅ |
| `Dockerfile.web` L12 | `FROM mcr.microsoft.com/dotnet/aspnet:9.0` ✅ |
```

### C2 Fix Verification

| Fix | Result | Evidence |
|-----|--------|----------|
| Dockerfile sdk:9.0 / aspnet:9.0 | ✅ | `FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build` (L1), `FROM mcr.microsoft.com/dotnet/aspnet:9.0` (L12) |
| csproj net9.0 | ✅ | `<TargetFramework>net9.0</TargetFramework>` (CoworkWeb.csproj L3) |
| SSE cancelled flag + close handler | ✅ | `let cancelled = false` (L76); `req.on('close', () => { cancelled = true; })` (L77-79), registered before try block |
| SSE if (cancelled) break in loop | ✅ | `if (cancelled) break` at L83 — first statement in `for await` loop body, checks every iteration |
| C1 passing items unchanged | ✅ | SetApplicationName/DisableAutomaticKeyGeneration (Program.cs L69-70), sandbox (TaskPage.razor L57), COWORK_INTERNAL_SECRET (auth.ts L4-5) — all confirmed present |

### Advisory (non-blocking)
`CoworkWeb.csproj` pins `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` to `Version="8.0.*"` in a net9.0 project. NuGet backward-compat means it resolves and runs correctly; this is an incomplete upgrade artifact. Does not meet bar for NEEDS-CHANGES. Fred may want to bump to `9.0.*` in a follow-up.

### C2 Verdict: PASS

Both C1 required fixes are correctly and substantively implemented. SSE close handler is properly structured — handler registered before the stream loop, `if (cancelled) break` is the first statement in each iteration. No regressions. Approved for SECURITY stage.

---

*Reviewed by Hawkeye (Clint Barton) — code-reviewer — 2026-03-17*

---
## Post-Deploy Diff Review (a2b3089 → 9804313)

### CC verdict
All security-critical C2 items hold. SDK API fixes in runner.ts are correct for SDK 0.2.77. .NET 9 is properly restored. One non-blocking follow-up: `multer` 1.x is deprecated (CVEs patched in 2.x) — tracked for future upgrade, not a blocker.

### Checks
| Item | Result |
|------|--------|
| .NET 9 (Dockerfile + csproj) | ✅ |
| DataProtection both lines | ✅ |
| iframe sandbox unchanged | ✅ |
| JWT no-fallback unchanged | ✅ |
| runner.ts SDK fix correct | ✅ |
| SSE close handler intact | ✅ |

### Notes
- `preToolCall` hook removal is correct — audit logging preserved in `tool_use` block
- `assistantMsg.message.content` access matches SDK `SDKAssistantMessage` shape (`.message` nested property)
- `multer@1.4.5-lts.2` deprecated in lock file — follow-up upgrade to 2.x recommended but non-blocking
- `DataProtection.EntityFrameworkCore 8.0.*` in net9.0 project remains (flagged at C2 advisory, still non-blocking)

### Verdict: CLEAR

*Post-deploy diff reviewed by Hawkeye (Clint Barton) — code-reviewer — 2026-03-17*
