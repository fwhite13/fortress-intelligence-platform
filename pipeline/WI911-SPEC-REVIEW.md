# Spec Review Report — WI#911: Cowork Design Agent

**Reviewer:** Hawkeye (Clint Barton) — Code Reviewer  
**Date:** 2026-03-20  
**Spec:** `fip/cowork/COWORK-DESIGN-AGENT-SPEC.md` (1,935 lines)  
**CC Invocation:** `cat review-brief.md | claude --model sonnet -p` (session: young-bison)  

---

## ⛔ VERDICT: SPEC NEEDS CLARIFICATION

**4 build-blocking P1 issues. Do NOT dispatch to Tony until all P1 blockers are resolved and P2-6 is answered by Fred.**

---

## runTask Call Sites — Complete List

The spec's §14 warns Tony to update all callers of `runTask`. Here is the complete list from the current codebase:

| File | Line | Usage |
|------|------|-------|
| `src/CoworkAgent/src/routes/tasks.ts` | 5 | `import { runTask } from '../agent/runner.js'` |
| `src/CoworkAgent/src/routes/tasks.ts` | 92 | `const gen = runTask({ taskId, userId, userEmail, prompt, workingDir, maxBudgetUsd, maxTurns })` — via `for await` |

**Only one call site exists.** The spec's §14 mentions `routes/agents.ts` as a caller — **that file does not exist** in the current codebase. See P1-3 below.

---

## P1 — Build Blockers (Must Fix Before Build Starts)

### P1-1: runner.ts Signature Mismatch — CRITICAL (Two Defects)

**Defect A: Execution model incompatibility**

The spec (§8, §9) proposes this signature for `runner.ts`:
```typescript
export async function* runTask(
  params: TaskParams,
  systemPromptOverride: string | null,
  emit: (chunk: SseChunk) => void
): Promise<void>
```

**The actual current runner is an AsyncGenerator:**
```typescript
export async function* runTask(params: TaskParams): AsyncGenerator<SseChunk>
```

The design runner calls it as:
```typescript
await runTask({ ... }, systemPrompt, (chunk) => chunks.push(chunk))
```
This is completely wrong — the existing runner uses `yield`, not `emit`. All callers use `for await`. The spec is trying to retrofit a callback interface onto a generator function. Tony cannot implement the spec as written; it will compile-fail and runtime-fail.

**Defect B: TypeScript type annotation is itself invalid**

`async function*` cannot have return type `Promise<void>`. An async generator returns `AsyncGenerator<T>`. The compiler will reject this outright.

**Required Resolution (Option A — recommended):**

Add `systemPromptOverride?: string` to the `TaskParams` interface. The function signature does not change at all — it remains an AsyncGenerator. The design runner accesses the override via `params.systemPromptOverride`.

```typescript
// No signature change needed:
export async function* runTask(params: TaskParams): AsyncGenerator<SseChunk>

// Inside runner.ts body — one conditional:
const systemPrompt = params.systemPromptOverride
  ? params.systemPromptOverride
  : buildDefaultSystemPrompt(params);  // existing logic

// Design runner calls it correctly:
import { runTask } from '../../agent/runner.js';  // corrected path — see P3-10

for await (const chunk of runTask({ ...params, systemPromptOverride: designSystemPrompt })) {
  emit(chunk);
}
```

`routes/tasks.ts` requires **zero changes** — it doesn't pass `systemPromptOverride`, and runner falls back to the default. Backward compatible.

---

### P1-2: HttpClient Injection — WRONG PATTERN + SECURITY ISSUE

**The spec's `DesignWorkspace.razor` opens with:**
```razor
@inject HttpClient Http
```

**This is wrong for two reasons:**

1. **Pattern mismatch:** No existing Cowork Blazor component injects `HttpClient` directly. Every component uses `@inject AgentApiClient AgentApi`. The project registers a **named HTTP client** (`"cowork-agent"`) accessed only through `AgentApiClient` via `IHttpClientFactory`.

2. **Security hole:** `AgentApiClient.CreateClient()` injects the internal JWT (`Authorization: Bearer <internal-token>`) on every request. Raw `HttpClient Http` has no token. The CoworkAgent `authMiddleware` will reject every request from `DesignWorkspace` with a 401. All API calls — generate, edit, stream — will fail at runtime.

**Required Resolution:** Replace all `Http.PostAsync`, `Http.PostAsJsonAsync`, `Http.GetStreamAsync` calls in `DesignWorkspace.razor` with calls through `AgentApiClient`. Tony needs to either:
- Add design-specific methods to `AgentApiClient` (`PostScreenAsync`, `OpenDesignStreamAsync`, etc.), OR  
- Add a general-purpose `PostAsync` / `GetStreamAsync` passthrough on `AgentApiClient` that applies the auth token

Either way: `@inject HttpClient Http` must become `@inject AgentApiClient AgentApi`.

Also remove `@inject IConfiguration Config` — `DesignWorkspace` should not know the agent base URL directly. `AgentApiClient` already encapsulates this.

---

### P1-3: AgentPage.razor and routes/agents.ts Do Not Exist

**The spec says to "modify" two files that don't exist:**

```
Modified Files:
  fip/cowork/src/CoworkAgent/src/agents/registry.ts   ← also doesn't exist (see below)
  fip/cowork/src/CoworkWeb/Components/Pages/Agents/AgentPage.razor  ← does not exist
```

**Filesystem reality:**
- `CoworkWeb/Components/Pages/Agents/` — directory does not exist
- `AgentPage.razor` — does not exist  
- `CoworkAgent/src/agents/registry.ts` — does not exist (no `agents/` directory at all; only `agent/` singular)
- `routes/agents.ts` — does not exist (spec's §14 lists it as a runTask caller)

**Required Resolution:** Tony must CREATE (not modify) all of these. The task brief must explicitly state:
- Create `AgentPage.razor` from scratch; reference `TaskPage.razor` as the pattern
- Create `agents/registry.ts` as a new file (no existing content to modify)
- Create `routes/agents.ts` as a new file; reference `routes/tasks.ts` as the pattern
- Create `Components/Pages/Agents/` directory

**Additional gap:** Even when `routes/agents.ts` is created, it must be mounted in `server.ts`. The spec's §9 shows `app.use('/agents/design', authenticate, designRouter)` — but the current `server.ts` only mounts `/tasks` and `/users`. Tony must add the mount line.

---

### P1-1c (Bonus): registry.ts — File Does Not Exist

The spec's §5 says to add the design entry to `agents/registry.ts`. This file does not exist. The `agents/` directory does not exist. Tony creates it fresh. No existing AGENT_REGISTRY to append to — he builds the registry file from the spec's entry as the first entry, or investigates whether registry logic lives elsewhere.

---

## P2 — Design Questions / Implementation Hazards

### P2-6: ⭐ QUESTION FOR FRED — Variant UX (Decide Before Build)

**Spec currently:** Single "Generating 3 variants..." overlay in the center panel. All 3 complete, then all 3 appear at once.

**Alternative:** Three separate progress indicators (one per variant) showing which completed first, with partial results rendering as they arrive.

**Why this matters before Tony starts:** It affects the component state model for `DesignWorkspace.razor`. Single-overlay = one `_generating` bool. Three indicators = three progress states + partial result handling. The data structures and SSE consumption logic differ meaningfully.

**🎯 Fred, please confirm: single overlay or per-variant progress?**

---

### P2-7: JS Interop Bug — Wrong API Call

```csharp
private void OpenRefPicker()
    => JS.InvokeVoidAsync("document.getElementById", "design-ref-input");
```

**This is wrong.** `document.getElementById` is not a `void` function — it returns an `HTMLElement`. Passing it to `InvokeVoidAsync` will throw a JS interop exception at runtime.

**Required Resolution:** Tony needs a registered JS helper:

```javascript
// In wwwroot/js/design-interop.js (or inline in index.html)
window.triggerElementClick = (elementId) => {
    const el = document.getElementById(elementId);
    if (el) el.click();
};
```

Called from Blazor as:
```csharp
await JS.InvokeVoidAsync("triggerElementClick", "design-ref-input");
```

Tony needs to add this JS snippet and update the interop call.

---

### P2-8: Dynamic Imports — Deviation from Codebase Pattern

The design runner (§8) uses dynamic imports:
```typescript
const { runTask } = await import('../runner.js');
const { getRedis } = await import('../../services/taskStore.js');
```

**The existing codebase uses zero dynamic imports** — all imports are static at the top of each file. Dynamic imports work in Node.js ESM, but deviate from the established pattern and can cause issues with TypeScript strict path checking and test frameworks.

**Required Resolution:** Convert to static imports at the top of `agents/design/runner.ts`. No runtime difference; keeps the pattern consistent. This is a `SHOULD fix`.

---

### P2-9 (New): Parallel Variant Calls — Claude API Concurrency Risk

The design runner fires 3 parallel `runTask` calls via `Promise.allSettled`. Three simultaneous Claude (Bedrock) API calls under the same account credentials could hit per-account concurrency limits, especially if other tasks are also running.

**Required Resolution:** Tony should add a stagger (e.g., 500ms delay between variant launches) or ensure the error handling in the variant loop handles `ThrottlingException` gracefully and falls back to sequential generation. At minimum, `Promise.allSettled` (not `Promise.all`) is already used — good. But Claude Bedrock throttling returns a specific error code; Tony should catch and retry with backoff.

---

## P3 — Cowork Constraints Check

### P3-9: Files Outside fip/cowork/ — ✅ CLEAN

All new and modified files in the spec are within `fip/cowork/src/`. No out-of-scope files. Pipeline constraint satisfied.

---

### P3-10: agents/design/runner.ts Import Path — BUILD-BREAKING

The design runner at `src/CoworkAgent/src/agents/design/runner.ts` contains:
```typescript
const { runTask } = await import('../runner.js');
```

`'../runner.js'` from `agents/design/` resolves to `agents/runner.ts` — **which does not exist**. The generic runner lives at `agent/runner.ts` (singular). The correct import is:

```typescript
import { runTask } from '../../agent/runner.js';
```

This is a build-breaking error. TypeScript will catch it if path checking is enabled; Node.js will throw `ERR_MODULE_NOT_FOUND` at runtime if not. Tony must use the corrected path.

Same issue exists for:
```typescript
const { getRedis } = await import('../../services/taskStore.js');
```
From `agents/design/runner.ts`, this resolves correctly to `src/services/taskStore.ts` ✓ — this one is fine.

---

## Additional Findings (from CC Analysis)

### NEW-1: ScreenHistoryItem Record — Initializer Syntax Bug

`DesignWorkspace.razor` creates `ScreenHistoryItem` using object initializer syntax:
```csharp
_screens.Insert(0, new ScreenHistoryItem
{
    ScreenId    = _activeScreenId,
    Prompt      = _prompt,
    Version     = _activeVersion,
    CreatedAt   = DateTime.UtcNow,
    DownloadUrl = _activeDownloadUrl,
});
```

But the spec declares it as a positional record:
```csharp
record ScreenHistoryItem(string ScreenId, string Prompt, int Version, DateTime CreatedAt, string DownloadUrl);
```

Positional record properties are `init`-only — object initializer syntax won't compile. Tony must use constructor syntax:
```csharp
new ScreenHistoryItem(_activeScreenId, _prompt, _activeVersion, DateTime.UtcNow, _activeDownloadUrl)
```

Or change to a `class` with `{ get; set; }` properties. Constructor syntax is consistent with `TaskPage.razor`'s existing record usage.

---

## Full Issue Summary

| ID | Priority | Severity | Issue | Required Resolution |
|----|----------|----------|-------|---------------------|
| P1-1a | P1 | **CRITICAL** | `runTask` spec proposes callback model on an AsyncGenerator | Add `systemPromptOverride?` to `TaskParams`; no signature change |
| P1-1b | P1 | **CRITICAL** | `async function*` + `Promise<void>` return type is a TS compile error | Resolved by Option A above |
| P1-2 | P1 | **CRITICAL** | `@inject HttpClient Http` bypasses JWT auth — all requests fail 401 | Use `@inject AgentApiClient AgentApi` throughout |
| P1-3 | P1 | **CRITICAL** | `AgentPage.razor`, `agents/registry.ts`, `routes/agents.ts` don't exist | Tony creates all three from scratch; task brief must say CREATE not modify |
| P3-10 | P1 | **CRITICAL** | `'../runner.js'` in design runner resolves to non-existent path | Change to `'../../agent/runner.js'` |
| P2-6 | P2 | **Design Q** | Variant UX: single overlay vs per-variant progress indicators | **⭐ Fred decides before Tony starts Blazor component** |
| P2-7 | P2 | **HIGH** | `JS.InvokeVoidAsync("document.getElementById", ...)` is wrong API | Add `window.triggerElementClick` JS helper; update interop call |
| NEW-1 | P2 | **HIGH** | Positional record used with initializer syntax — won't compile | Use constructor syntax for `ScreenHistoryItem` |
| P2-9 | P2 | **MEDIUM** | 3 parallel Claude calls risk API concurrency throttling | Add stagger delay or ThrottlingException retry logic |
| P2-8 | P2 | **LOW** | Dynamic imports deviate from static-import pattern | Convert to static imports (SHOULD fix) |
| P1-1c | P1 | **INFO** | `agents/registry.ts` is a new file, not a modification | Clarify in task brief |
| P1-4 | P1 | ✅ Clean | `uploadInputsToS3` export exists in fileService.ts | No action needed |
| P1-5 | P1 | ✅ Clean | Inline records in @code block — pattern is established (TaskPage.razor) | No action needed |
| P3-9 | P3 | ✅ Clean | All files within fip/cowork/src/ | No action needed |

---

## Pre-Build Checklist

These items must be resolved before dispatching to Tony:

- [ ] **P1-1:** Confirm Option A resolution — `systemPromptOverride?` added to `TaskParams`; no function signature change
- [ ] **P1-2:** Spec update: replace `@inject HttpClient Http` with `@inject AgentApiClient AgentApi`; define required new `AgentApiClient` methods
- [ ] **P1-3:** Task brief must say CREATE for `AgentPage.razor`, `routes/agents.ts`, `agents/registry.ts` — not modify
- [ ] **P3-10:** Correct import path in design runner: `'../../agent/runner.js'`
- [ ] **P2-6:** ⭐ **Fred confirms variant UX preference** (single overlay OR per-variant indicators)
- [ ] **P2-7:** Tony adds JS interop helper before using file picker
- [ ] **NEW-1:** Tony uses constructor syntax for `ScreenHistoryItem` record

---

*Hawkeye out. Ball's in Reed's court to update the spec, then Maria dispatches to Tony.*
