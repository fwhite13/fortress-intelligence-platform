# Review Report: FAIT for Excel — Sprint 2
**Reviewer:** Hawkeye (Clint Barton) — code-reviewer  
**Review Cycle:** 1 of 2  
**Add-in commit:** `bc339836` (`~/projects/fait-for-excel/`)  
**Backend commit:** `95e47667` (`~/projects/fip/`)  
**Date:** 2026-03-14  

---

## Verdict: NEEDS-CHANGES

**2 Critical issues. 1 Important issue. 2 Nitpicks.**  
All must be fixed before advancing to SECURITY.

---

## Checklist Results (30 items)

### Write-back Safety (Items 1–7)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | `range.formulas = [[s.formula]]` uses 2D array | ✅ PASS | Correct: `[[s.formula]]` |
| 2 | `range.values = [[s.value]]` uses 2D array | ✅ PASS | Correct: `[[s.value]]` |
| 3 | `.comments.add()` inside try/catch | ✅ PASS | Wrapped — **see Critical #1 below** |
| 4 | All writes in single `Excel.run()` with one `ctx.sync()` at end | ✅ PASS | Single `Excel.run`, single `await ctx.sync()` at end of loop |
| 5 | "Review Each" correctly advances index, stops at last item | ✅ PASS | `handleAcceptCurrent` and `handleSkipCurrent` both check `currentIndex < suggestions.length - 1`, call `onAcceptAll()` when done |
| 6 | "Accept All" calls `applySuggestions()` from `excelWriter.ts` | ✅ PASS | `WriteSuggestionsDialog.handleAcceptAll()` → `applySuggestions(suggestions)` |
| 7 | `suggestionParser.ts` JSON parse wrapped in try/catch, bad JSON → `suggestions: null` | ✅ PASS | try/catch present; catch returns `{ displayText: rawText, suggestions: null }` |

---

### SSE Backend (Items 8–14)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 8 | `Chat` signature is `Task` (not `Task<IActionResult>`) | ✅ PASS | `public async Task Chat(...)` — correct |
| 9 | SSE detected via `Request.Headers.Accept.ToString().Contains("text/event-stream")` | ✅ PASS | Exact match |
| 10 | SSE sets `Content-Type: text/event-stream` AND `Cache-Control: no-cache` | ✅ PASS | Both headers set; also sets `X-Accel-Buffering: no` (bonus) |
| 11 | SSE writes `data: {JsonSerializer.Serialize(chunk.Text)}\n\n` per chunk | ✅ PASS | Exact format |
| 12 | SSE sends `data: [DONE]\n\n` after foreach loop | ✅ PASS | Present after the `await foreach` block |
| 13 | KB retrieval + system prompt logic shared before SSE/buffered fork | ✅ PASS | KB retrieval and `systemPromptBuilder` construction happen before the `if (wantsStream)` branch |
| 14 | `OperationCanceledException` caught in SSE path | ✅ PASS | `catch (OperationCanceledException) { /* client disconnected */ }` |

---

### KB-Search Endpoint (Items 15–17)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 15 | `POST /api/haven/kb-search` exists under `[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]` | ✅ PASS | Controller-level attribute covers both endpoints |
| 16 | Returns `{ results: [{ content, source, score }] }` — top 5 by score descending | ✅ PASS | `.OrderByDescending(c => c.Score).Take(5).Select(c => new { content, source, score })` → `Ok(new { results })` |
| 17 | Both corp and project KB retrieval wrapped in separate try/catch | ✅ PASS | Corp in own try/catch, project in own try/catch — independent failures |

---

### SSE Client (Items 18–21)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 18 | `sendChatStreaming()` sends `Accept: text/event-stream` header | ✅ PASS | `Accept: 'text/event-stream'` present in headers |
| 19 | Parses SSE lines: splits on `\n`, processes `data: ` prefix, skips `data: [DONE]` | ✅ PASS | `buffer.split('\n')`, `line.startsWith('data: ')`, `line.trim() !== 'data: [DONE]'` |
| 20 | Buffer accumulation handles partial chunks (carries remainder) | ✅ PASS | `buffer = lines.pop() ?? ''` correctly retains incomplete last line |
| 21 | `useChat.ts` streaming appends via functional `setMessages(prev => ...)` update | ✅ PASS | `setMessages((prev) => { const next = [...prev]; next[assistantIndex] = ...; return next; })` — no stale closure |

---

### Error Scanner (Items 22–24)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 22 | `scanRangeForIssues()` uses `Excel.run()` + `ctx.sync()` correctly | ✅ PASS | `return Excel.run(async (ctx) => { ...; await ctx.sync(); ... })` |
| 23 | Column formula-presence: checks if any cell has formula starting with `=` | ✅ PASS | `(range.formulas[r][c] as string).startsWith('=')` sets `colHasFormulas[c] = true` |
| 24 | Hardcoded-in-formula-column detection skips row 0 | ✅ PASS | Guard: `r > 0 && colHasFormulas[c] && ...` |

---

### ChatPanel Integration (Items 25–28)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 25 | Calls `parseSuggestions(rawText)` and displays `displayText` | ✅ PASS | `const { displayText, suggestions } = parseSuggestions(rawText)` → `content: displayText` in final message |
| 26 | "Check for issues" only triggers on click | ✅ PASS | `onClick={handleCheckIssues}` — not in any `useEffect` or selection handler |
| 27 | "Ask FORGE" calls `POST /api/haven/kb-search` via `searchKb()` in `faitApi.ts` | ✅ PASS | `handleForgeSearch` → `searchKb(forgeQuery.trim(), apiKey)` |
| 28 | `KbResultPanel` and `ErrorSummaryCard` shown/hidden via state | ✅ PASS | Both gated: `{scanIssues !== null && <ErrorSummaryCard ...>}` and `{(forgeLoading || forgeResults !== null) && <KbResultPanel ...>}` |

---

### Correctness (Items 29–30)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 29 | No `.ts`/`.tsx` source files in any wwwroot commit | ✅ PASS | FIP commit `95e47667` only modified `HavenChatController.cs`; wwwroot contains only built assets (`.js`, `.css`, `.png`, `.html`) |
| 30 | `wwwroot/excel-addin/` NOT updated in this sprint's commits | ✅ PASS | The `wwwroot/excel-addin/` directory was not touched in commit `95e47667`; still contains Sprint 1 build artifacts |

---

## Issues — Required Fixes

### 🔴 CRITICAL #1 — `excelWriter.ts`: Wrong API for comments.add() (always fails silently)

**File:** `src/taskpane/services/excelWriter.ts`, line 22  
**Severity:** Critical — comments are never written; the bug is hidden by the try/catch

**What's wrong:**
```typescript
// WRONG — Range has no .comments property in Excel JS API
range.comments.add(ctx.workbook, `AI suggestion: ${s.explanation}`);
```

`Range` does not expose a `.comments` property with an `.add()` method in the Office JS API. The `CommentCollection.add()` method lives on `Workbook` or `Worksheet`, and its signature is `add(cellAddress: Range | string, content: string)` — not `add(workbook, content)`.

This call will throw a runtime error every time. The try/catch swallows it silently. No comments are ever added to cells.

**Fix:**
```typescript
// CORRECT — use sheet.comments.add(address, content)
try {
  sheet.comments.add(s.address, `AI suggestion: ${s.explanation}`);
} catch {
  /* ignore comment failures */
}
```

Note: `sheet` is already available in scope (`ctx.workbook.worksheets.getActiveWorksheet()`).

---

### 🔴 CRITICAL #2 — `WriteSuggestionsDialog.tsx`: `onAcceptAll` prop wired to `useWriteBack.acceptAll()` which re-runs write logic

**File:** `src/taskpane/components/ChatPanel.tsx`, line 332; `src/taskpane/hooks/useWriteBack.ts`, lines 15–22  
**Severity:** Critical — double-write on "Accept All" path; data integrity risk

**What's wrong:**

In `ChatPanel.tsx`, `WriteSuggestionsDialog` is rendered with:
```tsx
onAcceptAll={acceptAll}   // ← acceptAll from useWriteBack
```

Inside `useWriteBack.acceptAll()`:
```typescript
const acceptAll = async () => {
  if (!suggestions) return;
  setApplying(true);
  try {
    await applySuggestions(suggestions);  // ← writes to Excel
  } ...
};
```

But `WriteSuggestionsDialog.handleAcceptAll()` **also** calls `applySuggestions(suggestions)` internally, and then calls `onAcceptAll()` (which is `useWriteBack.acceptAll()`) as a completion callback:

```typescript
const handleAcceptAll = async () => {
  ...
  await applySuggestions(suggestions);   // write #1 — in the dialog
  onAcceptAll();                          // calls useWriteBack.acceptAll() → write #2
};
```

This results in `applySuggestions()` being called **twice** for the "Accept All" path: once inside `WriteSuggestionsDialog.handleAcceptAll()`, and again inside `useWriteBack.acceptAll()`.

**Fix:** `onAcceptAll` should be a pure "dismiss" callback — it must not re-run the write logic. Change `ChatPanel.tsx` to pass a dismiss handler instead:

```tsx
// In ChatPanel.tsx — pass a dismiss callback, not acceptAll()
onAcceptAll={() => { acceptAll_dismiss(); }}  
```

Or restructure so `useWriteBack.acceptAll()` does NOT call `applySuggestions()` directly, and the dialog owns the write. The simplest fix: rename the prop's behavior to match. The dialog's `handleAcceptAll` should do the write; `onAcceptAll` should just close the dialog:

```typescript
// useWriteBack — remove applySuggestions() call from acceptAll, make it just dismiss
const acceptAll = () => {
  setShowDialog(false);
  setSuggestions(null);
};
```

---

### 🟡 IMPORTANT #1 — `excelWriter.ts`: `ctx.sync()` called after loop with potential partial-write on error

**File:** `src/taskpane/services/excelWriter.ts`, lines 7–26  
**Severity:** Important — all-or-nothing behaviour is correct, but comment failures mid-loop cause early `throw` from `ctx.sync()` on the **non-comment** operations if Office JS batches them

**Detail:** The `try/catch` around `range.comments.add()` is inside the `Excel.run` callback, which is correct. However, with the fix in Critical #1, if `sheet.comments.add()` fails on one suggestion and is caught, the loop continues and the remaining writes still batch correctly. This is fine — just confirming the pattern is sound after the fix.

**No code change needed beyond fixing Critical #1.** Flagging for awareness.

---

### 🔵 NITPICK #1 — `useChat.ts`: `assistantIndex` resolved via Promise + `setMessages` callback is fragile

**File:** `src/taskpane/hooks/useChat.ts`, lines 38–43  
**Detail:** Using `await new Promise<number>((resolve) => { setMessages((prev) => { resolve(prev.length); ... }) })` to capture the index is clever but unconventional and relies on React calling the state updater synchronously within the `Promise` callback (which is true in React 18 concurrent mode for synchronous updates, but not guaranteed across React versions). A simpler approach is to compute `index = messages.length + 1` (user message + placeholder) before calling `setMessages`.  
**Action:** Non-blocking for this review cycle but should be cleaned up.

---

### 🔵 NITPICK #2 — `ChatPanel.tsx`: selection polling interval creates a mild perf concern

**File:** `src/taskpane/components/ChatPanel.tsx`, lines 58–67  
**Detail:** `setInterval(refresh, 2000)` polls Excel every 2 seconds to update selection info. In Office JS, polling is an acceptable pattern, but this call fires even when the task pane is not visible. Consider gating it on `document.visibilityState` or using `context.workbook.onSelectionChanged` instead.  
**Action:** Non-blocking, but worth a follow-up ticket.

---

## Summary

| Category | Count |
|---|---|
| ✅ Passing items | 27/30 |
| 🔴 Critical | 2 |
| 🟡 Important | 1 (no code change needed — informational) |
| 🔵 Nitpick | 2 |

**All focus items passed:**
- ✅ #8 — Method signature is `Task` not `Task<IActionResult>` 
- ✅ #13 — KB retrieval shared before SSE/buffered fork, not duplicated
- ✅ #21 — Functional `setMessages(prev => ...)` used throughout — no stale closure

**Must fix before advancing:**
1. `excelWriter.ts` — Wrong `range.comments.add()` API call (Critical #1)
2. `WriteSuggestionsDialog` / `useWriteBack` — Double-write on Accept All (Critical #2)

---

*Report generated: 2026-03-14 | Review cycle 1 of 2 | Returning to BUILD*
