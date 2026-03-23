# Review Report: WI911 — Cowork Design Agent

**Date:** 2026-03-20
**Reviewer:** Hawkeye (Clint Barton) — code-reviewer
**Cycle:** 1 of 2
**Commit:** `3716baf`
**Build Report:** `pipeline/WI911-BUILD-REPORT.md`

---

## Verdict: ✅ PASS

All 11 review checks pass. No required fixes. Clear to advance to SECURITY stage.

---

## CC Invocation

```bash
cat pipeline/review-brief-WI911.md | claude --model sonnet -p
```

Review brief prepared from direct file reads of all 17 changed files. CC confirmed analysis and verdict.

---

## P1 Checks — Build-Blocking

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 1 | `TaskParams.systemPromptOverride?` in interface AND used in `runTask` body | ✅ PASS | Interface has field; `effectiveSystemPrompt` logic gates on it before building `systemPrompt`; passed to `query()` |
| 2 | `@inject AgentApiClient AgentApi` + 4 extension methods exist and are used | ✅ PASS | `StartDesignScreenAsync`, `EditDesignScreenAsync`, `OpenDesignStreamAsync` all called in DesignWorkspace; `GetAgentMetaAsync` called in AgentPage.razor (correct ownership) |
| 3 | All 3 CREATE files exist (`registry.ts`, `routes/agents.ts`, `AgentPage.razor`) | ✅ PASS | All 3 confirmed new adds in git diff, not modifications |
| 4 | Design runner imports from `'../../agent/runner.js'` (singular, two levels up) | ✅ PASS | Static import at top of file resolves correctly: `src/agents/design/` → `src/agent/runner.js`. No dynamic imports of runner.js. |
| 5 | 500ms stagger on 3 variant Bedrock calls | ✅ PASS | `if (i > 0) await new Promise(r => setTimeout(r, i * 500))` inside each `Promise.allSettled` callback — i=0: 0ms, i=1: 500ms, i=2: 1000ms |
| 6 | iframe `sandbox="allow-scripts"` ONLY (no `allow-same-origin`) | ✅ PASS | `sandbox="allow-scripts"` is the only sandbox attribute. |

All P1 checks clear.

---

## P2 Checks — Should Fix

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 7 | `window.triggerElementClick` exists + calls `.click()` + DesignWorkspace calls it with `"design-ref-input"` | ✅ PASS | cowork.js: `var el = document.getElementById(id); if (el) el.click();` — file input has `id="design-ref-input"` — Blazor: `JS.InvokeVoidAsync("triggerElementClick", "design-ref-input")` |
| 8 | `ScreenHistoryItem` uses positional record constructor (not object initializer) | ✅ PASS | `record ScreenHistoryItem(string ScreenId, ...)` — instantiated with `new ScreenHistoryItem(_activeScreenId, _prompt, ...)`. All 5 records in file use positional syntax. |
| 9 | `brandService.ts` catch returns `getFortressDefaults(orgId)` on any S3 error | ✅ PASS | Bare `catch { return getFortressDefaults(orgId); }` — catches everything: NoSuchKey, access denied, parse errors. First-run safe. |

All P2 checks clear.

---

## P3 Checks — Consistency

| # | Check | Result | Notes |
|---|-------|--------|-------|
| 10 | No files outside `fip/cowork/` | ✅ PASS | All 43 changed files are in `cowork/` or `pipeline/WI911-BUILD-REPORT.md` (expected artifact). `cowork/COWORK-DESIGN-AGENT-SPEC.md` is inside cowork/. |
| 11 | No new npm packages in `package.json` | ✅ PASS | Zero diff on `package.json`. AWS SDK packages used (`@aws-sdk/client-s3`, `@aws-sdk/s3-request-presigner`) were pre-existing dependencies. |

All P3 checks clear.

---

## Non-Blocking Observations

These do not require fixes but are logged for awareness:

1. **`dist/` committed** — TypeScript compiled output is part of the commit. This appears to be the existing project convention (not introduced by WI911). Not a defect.

2. **Blazor conversion: intentional no-override** — `runBlazorConversion()` passes no `systemPromptOverride`, falling through to the generic `SYSTEM_PROMPT`. This is correct and intentional (Blazor conversion uses the general agent, not the design system prompt).

3. **`_activeVersion` counter** — initialized to 1, reset to 0 in `GenerateNew()`, then incremented on first `file_output`. Results in correct v1 display for a fresh screen. Slightly opaque but not a bug.

4. **Partial variant resilience** — `Promise.allSettled` + null filtering means 1-of-3 or 2-of-3 variant success is handled gracefully. Good defensive pattern.

5. **Stub workspace components** — 4 stub components (`MarketingWorkspace`, `AnalystWorkspace`, `TechWriterWorkspace`, `OpsWorkspace`) created solely to allow `AgentPage.razor` to compile. Noted as intentional placeholder work for future WIs.

---

## Architectural Notes

The core design decisions hold up well:

- **`systemPromptOverride` wired end-to-end** — interface → body check → `effectiveSystemPrompt` → `systemPrompt` build → `query()`. No leakage of the generic SYSTEM_PROMPT into design tasks.
- **AgentApiClient as the single HTTP boundary** — all auth (internal JWT) flows through the client's `CreateClient()`. No raw HttpClient escapes.
- **Brand service degrades gracefully** — S3 miss falls back to Fortress AM defaults, never throws. Design tasks can always run regardless of brand setup state.
- **Variant stagger pattern** — concurrent with controlled throttle. Correct approach for Bedrock rate limiting.
- **iframe sandbox discipline** — `allow-scripts` only, no `allow-same-origin`. External HTML cannot access cookies or localStorage.

---

## Gate Decision

**PASS → Advance to SECURITY stage.**

No code changes required. Review cycle 1 complete.
