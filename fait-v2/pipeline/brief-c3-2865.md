# BUILD BRIEF — ADO#2865 — Design Agent (Cycle 3 — Single Fix)
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §6.3 Design Agent**
**Agent:** Tony Stark | **Cycle:** 3 | **Date:** 2026-05-07

---

## Context

Cycle 3 is a single one-line fix. Clint caught it in his C2 review.

**Repo:** `~/projects/fip/fait-v2/` | **Branch:** `main`
**Current HEAD:** `3ca547d`

---

## The Fix

**File:** `Components/Agent/DesignAgentView.razor`

**Problem:** `_currentSessionId` is initialized once at component creation (`Guid.NewGuid().ToString()`) and never updated after `GenerateScreenAsync` returns. The fallback logic `result.SessionId ?? _currentSessionId` is the right shape — but the fallback value is always the initial GUID, which was never persisted to `design_agent_sessions`. Every Refine artifact gets saved with a phantom session ID.

**Fix — one line, after `GenerateScreenAsync` returns and before `SaveArtifactAsync`:**
```csharp
_currentSessionId = result.SessionId ?? _currentSessionId;
```

That's it. No other changes needed.

---

## Mandatory Rules

- **CC CLI MANDATORY:** Write a brief file, then:
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief-c3-2865.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/fait-v2/`
- Commit: `fix(fait-v2#2865): update _currentSessionId after GenerateScreenAsync`
- Run `dotnet build` to confirm 0 errors

---

## ADO Comment (MANDATORY)

```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2865,"text":"**[Tony Stark — BUILD cycle 3]**\nCommit {hash}: update _currentSessionId after GenerateScreenAsync. Build: SUCCEEDED."}'
```

---

## Deliverables

1. The fix applied and committed
2. Build report appended to `~/projects/fip/fait-v2/pipeline/ADO2865-BUILD-REPORT.md` (Cycle 3 section)
3. ADO comment on #2865
4. Report back to Maria
