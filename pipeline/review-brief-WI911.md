# Code Review Brief: WI911 — Cowork Design Agent
# Reviewer: Hawkeye (Clint Barton), Cycle 1

You are reviewing commit 3716baf in the fip/cowork project.
I have already read all the key files and gathered the data below.
Your job is to analyze it and produce a structured review verdict.

---

## P1 CHECK 1: runner.ts TaskParams + systemPromptOverride usage

FILE READ: cowork/src/CoworkAgent/src/agent/runner.ts

FINDINGS:
- `TaskParams` interface at line ~30 contains: `systemPromptOverride?: string;  // Optional: use instead of default SYSTEM_PROMPT when provided`
- `runTask` is declared as: `export async function* runTask(params: TaskParams): AsyncGenerator<SseChunk>`
- The body contains:
  ```
  const effectiveSystemPrompt = params.systemPromptOverride?.trim()
    ? params.systemPromptOverride
    : SYSTEM_PROMPT;
  const systemPrompt = [
    effectiveSystemPrompt,
    ...
  ].filter(Boolean).join('\n\n');
  ```
  Then `systemPrompt` is passed to `query({ ... options: { systemPrompt, ... } })`

VERDICT: ✅ PASS — interface has the field, runTask is still async function*, and the override IS used in the body.

---

## P1 CHECK 2: AgentApiClient in DesignWorkspace.razor + 4 extension methods

FILE READ: cowork/src/CoworkWeb/Components/Pages/Agents/Workspaces/DesignWorkspace.razor

FINDINGS:
- Top of file: `@inject AgentApiClient AgentApi` (NOT @inject HttpClient) ✅
- Also injects: `@inject IJSRuntime JS` and `@inject ISnackbar Snackbar` — both valid

FILE READ: cowork/src/CoworkWeb/Services/AgentApiClient.cs

4 NEW EXTENSION METHODS:
1. `GetAgentMetaAsync(string agentId)` — exists ✅
2. `StartDesignScreenAsync(...)` — exists, full multipart form implementation ✅
3. `EditDesignScreenAsync(...)` — exists ✅
4. `OpenDesignStreamAsync(string taskId)` — exists ✅

USAGE IN DesignWorkspace:
- `AgentApi.StartDesignScreenAsync(...)` called in `Generate()` ✅
- `AgentApi.EditDesignScreenAsync(...)` called in `EditCurrent()` ✅
- `AgentApi.OpenDesignStreamAsync(...)` called in `StreamTask()` ✅
- Note: `GetAgentMetaAsync` is called in `AgentPage.razor`, NOT in DesignWorkspace (correct — it's the router's responsibility)

VERDICT: ✅ PASS — AgentApiClient used throughout, 4 methods exist and are used appropriately.

---

## P1 CHECK 3: All 3 "CREATE" files exist

FILES VERIFIED FROM GIT DIFF:
- `cowork/src/CoworkAgent/src/agents/registry.ts` — 107 lines ADDED (new file) ✅
- `cowork/src/CoworkAgent/src/routes/agents.ts` — 34 lines ADDED (new file) ✅
- `cowork/src/CoworkWeb/Components/Pages/Agents/AgentPage.razor` — 61 lines ADDED (new file) ✅

VERDICT: ✅ PASS — all 3 required CREATE files exist.

---

## P1 CHECK 4: Import path in design runner

FILE READ: cowork/src/CoworkAgent/src/agents/design/runner.ts

IMPORT AT TOP:
```typescript
import { runTask } from '../../agent/runner.js';
import type { SseChunk } from '../../agent/runner.js';
```

Path analysis:
- File is at: `src/agents/design/runner.ts`
- `../../agent/runner.js` resolves to: `src/agent/runner.js` ✅
- This is correct — two levels up (design/ → agents/ → src/agent/)

DYNAMIC IMPORTS IN FILE:
```typescript
const { getRedis } = await import('../../services/taskStore.js');
```
This resolves to `src/services/taskStore.js` — NOT importing from runner.js, this is fine.
No dynamic imports of runner.js found in file.

VERDICT: ✅ PASS — static import uses `'../../agent/runner.js'`, no dynamic imports of runner.js.

---

## P1 CHECK 5: 500ms stagger on variants

FILE READ: cowork/src/CoworkAgent/src/agents/design/runner.ts — `runVariantTask` function

STAGGER IMPLEMENTATION:
```typescript
const results = await Promise.allSettled(
  variantInstructions.map(async (variant, i) => {
    if (i > 0) await new Promise(r => setTimeout(r, i * 500)); // 0ms, 500ms, 1000ms
    ...
    await runTaskWithEmit(...);
    ...
  })
);
```

Analysis:
- i=0: no delay (immediate start) ✅
- i=1: waits 500ms before starting ✅
- i=2: waits 1000ms before starting ✅
- All 3 ARE wrapped in `Promise.allSettled` — they run concurrently BUT each call starts 500ms offset.
- The stagger is achieved via the `setTimeout` inside each async callback, not by sequential await.
- This is the correct pattern: concurrent with stagger, not serial.

VERDICT: ✅ PASS — 500ms stagger correctly implemented (i * 500ms delay per variant before Bedrock call).

---

## P1 CHECK 6: sandbox="allow-scripts" on iframe

FILE READ: cowork/src/CoworkWeb/Components/Pages/Agents/Workspaces/DesignWorkspace.razor

IFRAME MARKUP:
```html
<iframe src="@_activeDownloadUrl"
        class="design-preview-iframe"
        sandbox="allow-scripts"
        title="Design preview" />
```

VERDICT: ✅ PASS — sandbox="allow-scripts" present and ONLY that attribute. No `allow-same-origin`.

---

## P2 CHECK 7: window.triggerElementClick in cowork.js

FILE READ: cowork/src/CoworkWeb/wwwroot/js/cowork.js

CONTENT:
```javascript
window.triggerElementClick = function (id) {
    var el = document.getElementById(id);
    if (el) el.click();
};
```

DesignWorkspace.razor usage:
```csharp
private void OpenRefPicker()
    => JS.InvokeVoidAsync("triggerElementClick", "design-ref-input");
```

The hidden file input has: `id="design-ref-input"` ✅

VERDICT: ✅ PASS — helper exists, calls .click(), Blazor calls it with "design-ref-input" which matches the input's id.

---

## P2 CHECK 8: ScreenHistoryItem record syntax

FILE READ: DesignWorkspace.razor @code block

RECORD DEFINITIONS:
```csharp
record ScreenHistoryItem(string ScreenId, string Prompt, int Version,
    DateTime CreatedAt, string DownloadUrl);
```

USAGE:
```csharp
_screens.Insert(0, new ScreenHistoryItem(
    _activeScreenId, _prompt, _activeVersion,
    DateTime.UtcNow, _activeDownloadUrl));
```

Analysis:
- Positional record (constructor-syntax parameters) ✅
- Created with `new ScreenHistoryItem(...)` positional constructor call ✅
- NOT using object initializer `{ Prop = value }` syntax ✅

All other records in file also use positional constructor syntax:
- `record VersionInfo(int Version, string S3Key, string DownloadUrl, DateTime CreatedAt, string Prompt)`
- `record VariantInfo(string Label, string Suffix, string DownloadUrl)`
- `record ScreenResult(...)`
- `record VariantResultItem(...)`

VERDICT: ✅ PASS — all records use correct positional constructor syntax.

---

## P2 CHECK 9: brandService.ts S3 error handling

FILE READ: cowork/src/CoworkAgent/src/services/brandService.ts

getBrandContext IMPLEMENTATION:
```typescript
export async function getBrandContext(orgId: string): Promise<BrandContext> {
  const cached = cache.get(orgId);
  if (cached && Date.now() - cached.loadedAt < CACHE_TTL_MS) {
    return cached.brand;
  }

  try {
    const key = `${BRAND_PREFIX}/${orgId}/brand.json`;
    const resp = await s3.send(new GetObjectCommand({ Bucket: BRAND_BUCKET, Key: key }));
    const raw   = await resp.Body!.transformToString();
    const brand = JSON.parse(raw) as BrandContext;
    cache.set(orgId, { brand, loadedAt: Date.now() });
    return brand;
  } catch {
    // Org has no brand file — return Fortress AM defaults
    return getFortressDefaults(orgId);
  }
}
```

VERDICT: ✅ PASS — catch clause returns `getFortressDefaults(orgId)` on ANY S3 error (no-such-key, permission error, parse error, etc.). First-run safe.

---

## P3 CHECK 10: No files outside fip/cowork/

GIT DIFF STAT shows these paths:
- `cowork/COWORK-DESIGN-AGENT-SPEC.md` — inside cowork/ ✅ (spec file, not source)
- `cowork/src/CoworkAgent/dist/...` — compiled output, inside cowork/ ✅
- `cowork/src/...` — all source files inside cowork/ ✅
- `pipeline/WI911-BUILD-REPORT.md` — outside cowork/, EXPECTED per pipeline process ✅

ISSUE NOTED: `dist/` compiled output is committed to the repo. This is non-blocking (appears to be the repo's convention — TypeScript project commits dist/), but worth flagging.

VERDICT: ✅ PASS — all code changes inside cowork/. pipeline/WI911-BUILD-REPORT.md is expected. dist/ commit is the existing project convention.

---

## P3 CHECK 11: No new npm packages

GIT DIFF on package.json shows NO changes (empty diff output). This means package.json was not touched in this commit.

The design runner uses:
- `@aws-sdk/client-s3` — already in package.json as pre-existing dependency
- `@aws-sdk/s3-request-presigner` — need to verify this was pre-existing

FILE: package.json — checking current state for these packages.
Given that `npm run build` produced 0 TypeScript errors and the diff shows no package.json changes, these packages were already present.

VERDICT: ✅ PASS — no new npm packages added.

---

## ADDITIONAL OBSERVATIONS (not in review priorities)

1. **`runTask` call in Blazor conversion passes no `systemPromptOverride`** — the Blazor conversion pass at lines ~280-300 of design runner.ts explicitly passes no systemPromptOverride. The comment says "No systemPromptOverride — Blazor conversion uses generic runner default". This is intentional and correct.

2. **`cowork/COWORK-DESIGN-AGENT-SPEC.md` committed** — the spec itself is committed in the cowork/ directory alongside the code. This is appropriate (spec as documentation).

3. **`dist/` files committed** — TypeScript compiled output in dist/ is included in the commit. This appears to be the project's existing convention (not introduced by WI911). Non-blocking.

4. **`_activeVersion` counter logic** — In DesignWorkspace.razor, `_activeVersion` is initialized to 1 in the fields and reset to 0 in GenerateNew(). When a `file_output` HTML chunk arrives, `_activeVersion++` is called. This means a fresh screen starts at 1 (0 → incremented to 1), which is correct. This is a minor implementation nuance but not a bug.

5. **No error recovery for failed variants** — `runVariantTask` uses `Promise.allSettled` and filters out `null` results, so partial variant generation (1 or 2 of 3 succeed) is handled gracefully. This is good defensive coding.

---

## SUMMARY

| Priority | Check | Verdict |
|----------|-------|---------|
| P1-1 | TaskParams systemPromptOverride + usage in runTask body | ✅ PASS |
| P1-2 | AgentApiClient injection + 4 extension methods | ✅ PASS |
| P1-3 | 3 CREATE files exist | ✅ PASS |
| P1-4 | Import path ../../agent/runner.js | ✅ PASS |
| P1-5 | 500ms stagger on variants | ✅ PASS |
| P1-6 | sandbox="allow-scripts" only on iframe | ✅ PASS |
| P2-7 | triggerElementClick helper + Blazor invocation | ✅ PASS |
| P2-8 | ScreenHistoryItem positional record syntax | ✅ PASS |
| P2-9 | brandService catch returns getFortressDefaults | ✅ PASS |
| P3-10 | No files outside fip/cowork/ | ✅ PASS |
| P3-11 | No new npm packages | ✅ PASS |

**ALL 11 CHECKS PASS.**

OVERALL VERDICT: PASS

Please confirm this analysis and produce the final review report in the format specified.
