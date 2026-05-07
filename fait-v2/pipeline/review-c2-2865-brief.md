# REVIEW BRIEF — ADO#2865 — Google Stitch Design Agent (Cycle 2)
**Sprint 3, Lane 2 | FAIT v2 Epic #2835 | §6.3 Design Agent**
**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 2 of 2
**Date:** 2026-05-07

---

## Context

You are Clint Barton (Hawkeye), code reviewer for the FIP pipeline.

This is **Cycle 2** review of ADO#2865 — Google Stitch Design Agent. Tony fixed all 4 issues from your Cycle 1 NEEDS-CHANGES verdict.

**Repo:** `~/projects/fip/fait-v2/` | **Branch:** `main`
**Cycle 1 commit:** `aa91a57` | **Cycle 2 fixes commit:** `3ca547d`
**Spec:** `memory/projects/fait-v2-spec-2026-04-27.md` (§6.0, §6.3)

---

## Cycle 1 Issues — Verify All Fixed

### 1 (CRITICAL) — `downloadBase64` JS function missing
**Clint C1 finding:** `IJSRuntime.InvokeVoidAsync("downloadBase64", ...)` called in `DesignArtifactCard.razor` but no JS function defined anywhere. Runtime crash on download.
**Tony's fix:** Created `wwwroot/js/app.js` with `window.downloadBase64(fileName, mimeType, base64String)`; added `<script src="/js/app.js"></script>` to `Components/App.razor` before `</body>`.
**Verify:** File exists, function signature matches JS invocation in C#, script tag present in App.razor.

### 2 (CRITICAL) — `DesignAgentService` never wrote to DB
**Clint C1 finding:** `design_agent_sessions` and `design_agent_artifacts` tables created via migration but `DesignAgentService` never persisted rows. `IDbContextFactory` not injected. Tables would remain empty forever.
**Tony's fix:** Injected `IDbContextFactory<FaitV2DbContext>` into `DesignAgentService`; `GenerateScreenAsync` now persists a `DesignAgentSession` before generation; `SaveArtifactAsync` now persists a `DesignAgentArtifact` after S3 upload; `SessionId` threaded through `DesignAgentResult` record.
**Verify:** Constructor injection present, session write before generation, artifact write after S3 upload, `SessionId` field on `DesignAgentResult`, propagation to `DesignAgentView.razor`.

### 3 (CRITICAL) — `IsStitchAvailableAsync` always returned `true`
**Clint C1 finding:** Dead code — a health endpoint HTTP call that always returned `Task.FromResult(true)` regardless. No actual check.
**Tony's fix:** Replaced with config-based check: `_config["Stitch:GcpCredentialsConfigured"] == "true"`.
**Verify:** Method body performs the config check, no fake HTTP call, config key is correct.

### 4 (IMPORTANT) — Silent catch in `SendPrompt`
**Clint C1 finding:** `catch (Exception ex)` in `SendPrompt` swallowed exceptions silently — no logging. Impossible to debug failures.
**Tony's fix:** Added `Logger.LogError(ex, "SendPrompt failed for userId={UserId}", _userId)`.
**Verify:** `ILogger` injected, `LogError` call present with userId context in catch block.

---

## Tony's Flagged Items for Clint to Check

Tony raised two items:

1. **`RefineScreenAsync` does not persist a new session** — it reuses caller's `_currentSessionId` via fallback. Intentional (refine = same session). Confirm this is acceptable per spec §6.3 semantics.

2. **`SessionId` propagation through `DesignAgentResult`** — `GenerateScreenAsync` sets `SessionId` on the result; `RefineScreenAsync` passes `null` which falls back to `_currentSessionId`. Verify the fallback path is correct and there's no session loss scenario.

---

## Mandatory: Use Claude Code CLI

Write your review brief to a file and run:
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat review-c2-2865-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC must read the actual files in `~/projects/fip/fait-v2/`. Do NOT review by reasoning alone.

---

## CSS Variable Rule (Mandatory Check)

All styling in changed files must use CSS variables from `fortress.css`. Specifically:
- `wwwroot/js/app.js` — no CSS relevance (pure JS)
- `Components/App.razor` — only added `<script>` tag, no CSS
- Changed Razor/CSS files must not introduce hardcoded colors, font sizes, or spacing values

---

## Files Changed in Cycle 2

| File | Change |
|------|--------|
| `wwwroot/js/app.js` | Created — `window.downloadBase64` browser download helper |
| `Components/App.razor` | Modified — added `<script src="/js/app.js"></script>` |
| `Services/IDesignAgentService.cs` | Modified — `SaveArtifactAsync` signature + `SessionId` on `DesignAgentResult` |
| `Services/DesignAgentService.cs` | Modified — DB factory injection, session/artifact persistence, `IsStitchAvailableAsync` |
| `Components/Agent/DesignAgentView.razor` | Modified — `ILogger` inject, error logging, `result.SessionId` wiring |

---

## ADO Work Item Update (MANDATORY)

After completing review, post a comment to ADO #2865:

```
mcporter call devops.add_comment --args '{"project":"Fortress","id":2865,"text":"**[Hawkeye — REVIEW cycle 2]**\nCode review PASS/NEEDS-CHANGES. Cycles: 2. [Issues if any]."}'
```

---

## Deliverables

1. **Review Report** at `~/projects/fip/fait-v2/pipeline/ADO2865-REVIEW-REPORT-C2.md`
   - Verdict: PASS / NEEDS-CHANGES
   - Verification of all 4 cycle 1 fixes
   - Assessment of Tony's 2 flagged items
   - Any new issues found
2. **ADO comment** posted to #2865
3. Report back to Maria with verdict
