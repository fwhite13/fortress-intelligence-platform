# Review Report — ADO#3241

## Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

No formal developer brief with §2 / §7 structure was provided for this architectural story. Review is based on the build report acceptance criteria and the task description.

**Acceptance Criteria (from build report):**

- [x] `node --check harness-server.js` — PASS (verified, no syntax issues)
- [x] `dotnet build` — PASS (0 errors per build report)
- [x] Harness emits `event: kb_sources\ndata: {...}` when KB flags set — ✅ Verified in diff
- [x] Harness emits `event: tool_call\ndata: {...}` before/after graph_, ado_, web_search tool calls — ✅ Verified in diff
- [x] FargateUserAgentRuntime.cs SSE loop handles typed `event:` lines — ✅ Verified
- [x] ChatView.razor handles `kb_sources` → `_lastKbResult` populated — ✅ Verified
- [x] ChatView.razor handles `tool_call` → `_activeToolCalls` updated + rendered — ✅ Verified
- [x] `KbFlags` added to `TurnRequest` and passed from ChatView — ✅ Verified

**Spec compliance verdict:** ✅ COMPLIANT (core feature deliverables met)

---

## CC Review Summary

Ran CC Sonnet adversarial review against the full diff and source files. CC investigated all 16 review focus points from the task brief.

**CC findings confirmed as real issues:** 4 (Q3, Q6, Q11, Q16)
**CC findings dismissed as false positives / acceptable:** 12
**Issues I'm adding from direct code inspection:** 0

---

## Consistency Audit

**Cross-file contracts verified:**

| Contract | Result |
|----------|--------|
| Harness emits `event: tool_call` + `data:` JSON → FargateUserAgentRuntime reads `event:` line then `data:` line | ✅ Correct |
| Harness `{ server, toolName, status, summary }` → Blazor `ToolCallPayload` record fields | ✅ Field names match (camelCase JSON ↔ JsonPropertyName attributes) |
| Harness `{ sources: [...] }` → Blazor `KbSourcesPayload` record fields | ✅ Field names match |
| `KbFlags` C# record field names (CorpKbEnabled/PersonalKbEnabled/TeamKbEnabled) → Harness dual-case reads (e.g., `kbFlags.CorpKbEnabled \|\| kbFlags.corpKbEnabled`) | ✅ Harness handles both PascalCase and camelCase — robust |
| `KbFlags: null` when `anyKbActive = false` → Harness `rawBody.KbFlags ?? rawBody.kbFlags ?? null` → `if (kbFlags)` guard | ✅ No retrieval fires when no KB active |
| `HarnessEvent.Payload` field carries raw JSON string for `kb_sources` and `tool_call` | ✅ FargateUserAgentRuntime sets `Payload: json` (raw string); ChatView deserializes via `evt.Payload ?? "{}"` |

**Undocumented dependencies checked:**

- `ForgeQueryService`, `KnowledgeBaseService`, `KbQueryService` — still `@inject`'d in ChatView.razor but have zero call sites in the component body (Issue I2 below).
- `ToolCallAccumulator` (McpDtos.cs) — removed from ChatView.razor field declaration; McpDtos.cs type still exists in the service layer and is used by other components. No orphan in the service layer, only the removed field reference was cleaned up. ✅

---

## Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| Important | `harness-server.js` | ~line 1830 | No size cap on KB context injection | Add per-chunk truncation + total cap |
| Important | `ChatView.razor` | Lines 8-9, 13 | 3 orphaned `@inject` directives | Remove all three |
| Important | `fortress.css` | Lines 1587-1629 | 5 CSS custom properties referenced but not defined in `:root` | Add to `:root` or use existing tokens |
| Important | `harness-server.js` | ~line 2239 | `search_knowledge_base` emits no `tool_call` SSE event | Intentional or gap — needs clarification |
| Nitpick | `ChatView.razor` | Line 863 | Project KB collapsed to `TeamKbEnabled` — no per-project KB ID | Documented simplification, acceptable |
| Nitpick | `harness-server.js` | ~line 2201 | `ado_list_projects` missing from `adoSummaries` map | Add entry or accept generic fallback |

---

## Critical Issues [0]

None.

---

## Important Issues [4]

### I1 — No size cap on KB context injection
- **File:** `fait-v2/agent-harness/harness-server.js` (~line 1830)
- **Category:** Correctness / Reliability
- **Issue:** `r.content?.text` is never truncated before being concatenated into the system prompt. With `maxResults=5` per KB and up to 3 KBs active, that's up to 15 uncapped chunks. Bedrock KB chunks can each be 2-8KB of text. The rebuilt `fullSystemPrompt` could grow to 50-100KB+, which approaches Bedrock's system prompt limits and inflates `inputTokens` per turn significantly.
- **Evidence:**
  ```js
  const contextText = results.map((r, i) => `[${i+1}] ${r.content?.text || ''}`).join('\n\n');
  ```
- **Impact:** If a KB contains large chunks, the system prompt may overflow Bedrock's context window and cause the turn to fail with an API error.
- **Fix:**
  ```diff
  - const contextText = results.map((r, i) => `[${i+1}] ${r.content?.text || ''}`).join('\n\n');
  + const contextText = results.map((r, i) => `[${i+1}] ${(r.content?.text || '').substring(0, 2000)}`).join('\n\n');
  ```
  Additionally, consider adding a total-injection cap after `fullSystemPrompt = systemParts.join(...)`:
  ```js
  if (fullSystemPrompt.length > 50000) {
      console.warn(`[harness] KB: system prompt ${fullSystemPrompt.length} chars — may be large`);
  }
  ```

---

### I2 — Three orphaned `@inject` directives in ChatView.razor
- **File:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` (lines 8-9, 13)
- **Category:** Quality / Dead Code
- **Issue:** The KB retrieval removal eliminated ALL call sites for `KbSvc`, `KbQuerySvc`, and `ForgeQuery` in this component. All three `@inject` lines remain. Blazor resolves every injected service on component initialization — these three services are being constructed and wired into the component for no reason.
  ```razor
  @inject KnowledgeBaseService KbSvc       // zero call sites
  @inject KbQueryService KbQuerySvc        // zero call sites
  @inject ForgeQueryService ForgeQuery     // zero call sites
  ```
  Verified: `grep -n "KbSvc\|KbQuerySvc\|ForgeQuery" ChatView.razor` returns exactly 3 results — the inject declarations themselves.
- **Impact:** Wasted DI resolution on every chat view render. More importantly, if any of these services hold Bedrock clients or HTTP resources, they'll be initialized unnecessarily.
- **Fix:** Remove all three `@inject` lines:
  ```diff
  - @inject KnowledgeBaseService KbSvc
  - @inject KbQueryService KbQuerySvc
  ...
  - @inject ForgeQueryService ForgeQuery
  ```

---

### I3 — CSS custom properties referenced but not defined in `:root`
- **File:** `fait/src/FortressAI.Web/wwwroot/css/fortress.css` (lines 1587-1629)
- **Category:** Quality / Design System
- **Issue:** The new tool-call indicator CSS uses 6 custom properties. Checking `:root` (lines 10-107):

  | Variable | Defined? | Effective value |
  |---|---|---|
  | `--color-text-secondary` | ✅ Yes (line 34) | `#6b7280` |
  | `--color-surface-subtle` | ❌ Not defined | Falls back to `rgba(0,0,0,0.03)` |
  | `--color-text-accent` | ❌ Not defined | Falls back to `var(--color-primary, #3b82f6)` = `#1a2332` (dark navy) |
  | `--color-accent-muted` | ❌ Not defined | Falls back to `rgba(59,130,246,0.08)` (blue tint) |
  | `--color-text-success` | ❌ Not defined | Falls back to `var(--color-text-secondary)` = `#6b7280` |
  | `--color-text-danger` | ❌ Not defined | Falls back to `#ef4444` |
  | `--color-danger-muted` | ❌ Not defined | Falls back to `rgba(239,68,68,0.08)` |

  In particular, `--color-text-accent` falls through to `--color-primary` = `#1a2332` (dark navy), which may not read well on the muted blue background of `.tool-call-active`. The fallbacks happen to be functional but don't align with the design system's semantic token pattern.
- **Impact:** Dark mode, theme customization, or future design system updates won't apply to tool-call indicators.
- **Fix option A (preferred):** Add to `:root`:
  ```css
  --color-surface-subtle:   rgba(0, 0, 0, 0.03);
  --color-text-accent:      var(--color-info);          /* #2563eb / #3B82F6 */
  --color-accent-muted:     var(--color-info-bg);       /* rgba(59,130,246,0.10) — already defined */
  --color-text-success:     var(--color-success);       /* #059669 / #10B981 */
  --color-text-danger:      var(--color-error);         /* #dc2626 / #EF4444 */
  --color-danger-muted:     var(--color-error-bg);      /* rgba(239,68,68,0.10) — already defined */
  ```
- **Fix option B (quicker):** Rewrite the tool-call CSS rules to use existing tokens directly:
  ```css
  .tool-call-active {
      color: var(--color-info, #3b82f6);
      background: var(--color-info-bg, rgba(59,130,246,0.10));
  }
  .tool-call-done {
      color: var(--color-text-secondary);
      opacity: 0.7;
  }
  .tool-call-error {
      color: var(--color-error, #ef4444);
      background: var(--color-error-bg, rgba(239,68,68,0.10));
  }
  ```

---

### I4 — `search_knowledge_base` tool emits no `tool_call` SSE event (spec gap, needs clarification)
- **File:** `fait-v2/agent-harness/harness-server.js` (~line 2239)
- **Category:** Spec Gap
- **Issue:** The `graph_*`, `ado_*`, and `web_search` branches all wrap their dispatch in `emitToolCall(calling)` / `emitToolCall(done|error)`. The final `else` branch (which handles `search_knowledge_base`, `read_memory`, `write_memory`, `create_document`, `list_files`, `read_file`) emits nothing. From the user's perspective, in-turn KB searches (when Claude decides to call `search_knowledge_base` mid-conversation) are invisible — no spinner, no summary.
- **Impact:** User sees no feedback during in-turn KB tool calls. Less critical than the harness-side KB retrieval (which gets `kb_sources`), but inconsistent with the transparency goal of this story.
- **Decision needed:** Is this intentional (internal plumbing hidden from UI) or an oversight? If oversight, add `emitToolCall` wraps to the `else` branch and any other tool branches that should be visible.

---

## Nitpicks [2]

### N1 — Project KB collapsed to `TeamKbEnabled` with no separate project KB path
- **File:** `ChatView.razor` (line 863)
- `hasProjectKb` maps to `TeamKbEnabled` → harness uses `TEAM_KB_ID`
- Build report explicitly notes this as a known simplification. Acceptable.

### N2 — `ado_list_projects` missing from `adoSummaries` map
- **File:** `harness-server.js` (~line 2201)
- Falls to generic fallback: `"Calling ado_list_projects..."` — not harmful, just inconsistent with the other ado entries.
- **Fix:** Add `ado_list_projects: 'Listing ADO projects...'` to the map.

---

## What to fix

Tony, three required changes before this ships:

**1. KB chunk truncation (harness-server.js ~line 1830)**
Add `.substring(0, 2000)` on `r.content?.text` before concatenating into `contextText`. Five uncapped chunks per KB × 3 KBs = potential system prompt blowout.

**2. Remove orphaned @inject (ChatView.razor lines 8-9, 13)**
Delete the three `@inject` lines for `KbSvc`, `KbQuerySvc`, and `ForgeQuery` — all call sites are gone, these are dead DI.

**3. Fix tool-call CSS to use defined tokens (fortress.css)**
Either add the 5 missing variables to `:root` mapping to existing tokens, or rewrite the tool-call rules to use `--color-info`, `--color-info-bg`, `--color-error`, `--color-error-bg`, `--color-text-secondary` directly.

**I4 (search_knowledge_base transparency):** Please clarify intent — if it should emit `tool_call` events, add `emitToolCall` wraps in the `else` branch. If intentionally silent, document it in a comment.

---

## Positive Observations

- The per-KB error isolation with `Promise.all` is solid — `doKbRetrieval` never rejects, so one bad KB doesn't kill the others. ✅
- The `fullSystemPrompt` rebuild timing is correct — the agentic loop at line 2056 uses the post-KB-injection value. ✅
- SSE parsing in `FargateUserAgentRuntime.cs` correctly handles the event-before-data SSE pattern, including the blank-line boundary reset and the fallback for unrecognized event types. ✅
- The `HandleToolCallEvent` `FindLastIndex` logic correctly handles duplicate in-flight tool names (second `done` matches the second `calling`). ✅
- `_activeToolCalls` clear on `HandleSend` and chat switch covers all stale-indicator scenarios. ✅
- The `toolInput` availability before the `adoSummaries` dict is correct — parsed at line ~2080 before any branch. ✅

---

## Cycle 2 Review — 2026-05-11

### Verdict: NEEDS-CHANGES

---

### Cycle 2 Focus: Verify 4 NEEDS-CHANGES fixes from Cycle 1

Commits reviewed: `29f2d89b` + `376f126a`

---

### Fix 1: KB Chunk Cap — ✅ PASS

`.substring(0, 2000)` correctly applied to `r.content?.text` in `doKbRetrieval`:

```js
// harness-server.js ~line 1847
const contextText = results.map((r, i) => `[${i+1}] ${(r.content?.text || '').substring(0, 2000)}`).join('\n\n');
```

Chunk count is implicitly capped by the `5` argument in `retrieveFromKbFull(kbId, query, 5)`. Worst case: 3 KBs × 5 chunks × 2000 chars = 30,000 chars — bounded and acceptable.

---

### Fix 2: Dead Inject Removal — ✅ PASS

All three `@inject` lines removed from `ChatView.razor`:
```diff
-@inject KnowledgeBaseService KbSvc
-@inject KbQueryService KbQuerySvc
-@inject ForgeQueryService ForgeQuery
```

No residual references to `KbSvc`, `KbQuerySvc`, or `ForgeQuery` anywhere in the file.

---

### Fix 3: CSS Tokens — ✅ PASS

All 6 required variables now defined in `:root`:

| Variable | Value | Semantic |
|---|---|---|
| `--color-surface-sunken` | `#f3f4f6` | Sunken surface ✓ |
| `--color-success` | `#10B981` | Green ✓ |
| `--color-error` | `#EF4444` | Red ✓ |
| `--color-info` | `#3B82F6` | Blue ✓ |
| `--color-error-bg` | `rgba(239,68,68,0.10)` | Light red ✓ |
| `--color-info-bg` | `rgba(59,130,246,0.10)` | Light blue ✓ |

Tool-call CSS classes consume the tokens correctly.

---

### Fix 4: Builtin tool_call Events — ❌ PARTIAL (2 gaps)

`getBuiltinSummary()` helper added ✅. `emitToolCall` wrapper added ✅. Coverage of 6 branches correct ✅. Two gaps:

---

### Issues Found

| Severity | File | Issue |
|---|---|---|
| **Important** | `harness-server.js` | `search_memory` has no `else if` dispatch arm — falls to `else` → executes as `search_knowledge_base`. Calls to `search_memory` are misdirected, emit wrong event name, and call the wrong endpoint. `search_memory` is BUILTIN_TOOLS member #2 in the set definition; cycle 2 explicitly promised to cover all 7 BUILTIN_TOOLS with `emitToolCall`. |
| **Low-Medium** | `harness-server.js` | `create_document` `if (cdData.error)` branch emits no `emitToolCall` — UI stays in "calling" spinner forever when the document API returns a logical error (HTTP 200 + `{error: "..."}`). The network `catch` path is correctly handled. |

---

### Required Cycle 3 Fixes

**Fix A — `search_memory` dispatch arm** (`harness-server.js`, before the `else` clause ~line 2275):

```js
} else if (toolUseAccumulator.name === 'search_memory') {
    emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
    try {
        const smRes = await fetch(`http://localhost:${PORT}/tools/search_memory`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, ...toolInput })
        });
        const smData = await smRes.json();
        toolResultText = JSON.stringify(smData, null, 2);
        emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', 'Memory search complete');
    } catch (smErr) {
        toolResultText = `Memory search error: ${smErr.message}`;
        isError = true;
        emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${smErr.message.substring(0, 100)}`);
    }
```

**Fix B — `create_document` API error path** (`harness-server.js`, inside the `if (cdData.error)` block ~line 2161):

```diff
  if (cdData.error) {
      toolResultText = `\n\n[Document Error]\n${cdData.error}\n\n`;
+     emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', cdData.error.substring(0, 100));
  }
```

---

### Positive Observations

- KB chunk cap implementation is clean and correct.
- Dead inject removal is complete — no residual references found.
- CSS token values are semantically correct and the `:root` definition is authoritative.
- `getBuiltinSummary()` is a clean helper with good defaults.
- 6 of 7 BUILTIN_TOOLS branches have full `calling`/`done`/`error` coverage.

---

_Reviewed by Hawkeye (Clint Barton) — Cycle 2 — 2026-05-11_

---

## Cycle 3 Review — 2026-05-11

### Verdict: PASS ✅

---

### Cycle 3 Focus: Verify 2 NEEDS-CHANGES gaps from Cycle 2

Commit reviewed: `c984fdb0` (`harness-server.js` only, +16 lines)

---

### Fix A: `search_memory` dispatch arm — ✅ PASS

New `else if` block inserted at line 2131, precisely between `read_memory` (ends ~2130) and `write_memory` (~2146):

```
2116: } else if (toolUseAccumulator.name === 'read_memory') {
  ...
2131: } else if (toolUseAccumulator.name === 'search_memory') {
  ...
2146: } else if (toolUseAccumulator.name === 'write_memory') {
```

- Correctly positioned — does NOT fall through to `search_knowledge_base` ✅
- `calling` emitted before `fetch` (line 2132) ✅
- `done` emitted on success (line 2141) ✅
- `error` emitted in catch (line 2144) ✅
- POST URL `http://localhost:${PORT}/tools/search_memory` matches registered route at line 798 ✅
- `await` present on fetch (line 2134) ✅
- Entire fetch block wrapped in `try/catch` — no unhandled rejections ✅
- Tool name `'search_memory'` used consistently — no camelCase or hyphen variants ✅

Structurally identical to the `read_memory` arm — pattern match confirmed.

---

### Fix B: `create_document` error path — ✅ PASS

```js
2175:     const cdData = await cdRes.json();
2176:     if (cdData.error) {
2177:         emitToolCall(res, 'builtin', 'create_document', 'error', `Document creation failed: ${cdData.error}`);
2178:         toolResultText = `\n\n[Document Error]\n${cdData.error}\n\n`;
2179:     } else {
```

`emitToolCall(..., 'error', ...)` fires at line 2177 before the block exits. UI will correctly exit the "calling" spinner on document API logical errors. Network-level catch path (line ~2194) also correctly emits error. ✅

---

### General Scan — ✅ PASS

No logic errors, typos, missing awaits, or unhandled rejections in the 16 new lines.

---

### Summary

All cycle 2 required fixes are correctly implemented. Commit `c984fdb0` resolves both reported gaps cleanly and introduces no new issues.

---

_Reviewed by Hawkeye (Clint Barton) — Cycle 3 — 2026-05-11_
