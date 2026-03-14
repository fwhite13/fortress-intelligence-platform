# Review Report: FAIT for Excel — Sprint 1
**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1 of 2
**Date:** 2026-03-13
**Add-in commit:** `5c7b188` (`~/projects/fait-for-excel/`)
**Backend commit:** `022da21` (`~/projects/fip/fait/`)

---

## Verdict: NEEDS-CHANGES

Two **Critical** issues require fixes before this can pass. Both are in `storage.ts`. Everything else is solid — Tony built this well.

---

## Issue Summary

| # | Severity | File | Issue |
|---|----------|------|-------|
| A | **Critical** | `storage.ts` | `setApiKey()` not wrapped in try/catch — crashes silently in some Excel environments |
| B | **Critical** | `useExcelContext.ts` | Selection change handler registered inside `Excel.run()` context — handler reference captured from wrong scope; cleanup will silently fail in most environments |
| C | Important | `contextFormatter.ts` | `getCellAddr()` only handles cols A–Z (26 cols) — silently produces wrong addresses for col 27+ even though `getFullWorksheet` allows 50 cols |
| D | Nitpick | `ChatPanel.tsx` | `setInterval(refresh, 2000)` polls `getSelectedRange()` every 2s — redundant given `useExcelContext` registers a proper `onSelectionChanged` event; double-refresh overhead |

---

## Checklist Results (30 items)

### Security (items 1–6)

**1. ✅ PASS — `OfficeRuntime.storage` used, not `localStorage`**
`storage.ts` uses `OfficeRuntime.storage.getItem/setItem/removeItem` throughout. Zero `localStorage` calls found anywhere in the codebase.

**2. ✅ PASS — API key never logged**
Grepped all services and components for `console.log`, `console.error`, `console.warn` — no console calls found anywhere in the add-in source. No logging of the `apiKey` parameter.

**3. ✅ PASS — Cell sanitization present and correct**
`contextFormatter.ts` line 4:
```typescript
const sanitize = (v: unknown): string =>
  String(v)
    .replace(/[\n\r]/g, ' ')   // prevent prompt injection via newlines
    .replace(/\|/g, '\\|');    // escape pipe chars
```
`\n` and `\r` stripped. Regex uses character class `[\n\r]` which correctly catches both CR and LF. Applied to all cell values via `row.map(sanitize)`. Headers also run through `sanitize`. ✅

**4. ✅ PASS — No unguarded write-back calls**
Grepped for `range.values =` and `range.formulas =` — none found. `excelReader.ts` only reads (`range.load(...)`, `ctx.sync()`). No write paths exist in Sprint 1.

**5. ✅ PASS — API key stripped from error messages**
`faitApi.ts` uses opaque error codes (`INVALID_KEY`, `TIMEOUT`, `SERVICE_UNAVAILABLE`, `HTTP_${status}`) — the raw `apiKey` string is never concatenated into any thrown message. `useChat.ts` maps these codes to user-facing strings; the key never appears in UI output.

**6. ⚠️ FAIL (Critical) — `setApiKey()` missing try/catch**
`getApiKey()` and `clearApiKey()` are both wrapped in try/catch. `setApiKey()` is NOT:
```typescript
// storage.ts — current implementation
export async function setApiKey(key: string): Promise<void> {
  await OfficeRuntime.storage.setItem(KEY, key);  // ← no try/catch
}
```
`OfficeRuntime.storage` can throw in some Excel environments (private mode, restrictive group policies, certain Excel Online configurations). An unhandled rejection here will bubble up to `SettingsPanel.handleSaveAndTest()` as an uncaught error with a raw exception message, potentially exposing the key value in the exception chain.

**Required fix:**
```typescript
export async function setApiKey(key: string): Promise<void> {
  try {
    await OfficeRuntime.storage.setItem(KEY, key);
  } catch (e) {
    throw new Error('STORAGE_UNAVAILABLE');
  }
}
```
`SettingsPanel` should handle `STORAGE_UNAVAILABLE` with a user-friendly message.

---

### FAIT Backend (items 7–11)

**7. ✅ PASS — `AppKeyAuthOptions.ApiKeys: List<string>` field present**
```csharp
public List<string> ApiKeys { get; set; } = new();
```
Confirmed in `AppKeyAuthHandler.cs`.

**8. ✅ PASS — `AllKeys` computed property correct**
```csharp
public IEnumerable<string> AllKeys =>
    ApiKeys
        .Concat(string.IsNullOrEmpty(ApiKey) ? Array.Empty<string>() : new[] { ApiKey })
        .Where(k => !string.IsNullOrEmpty(k));
```
Returns both `ApiKeys` entries and the legacy `ApiKey`, filtering empty/null. Logic is correct.

**9. ✅ PASS — Ordinal comparison used**
```csharp
if (allKeys.Count == 0 || !allKeys.Any(k => string.Equals(apiKey, k, StringComparison.Ordinal)))
    return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
```
`StringComparison.Ordinal` confirmed — case-sensitive, no culture folding. This is correct.

**10. ✅ PASS — `Program.cs` registers `AppKeys:ExcelAddin`**
```csharp
authBuilder.AddScheme<AppKeyAuthOptions, AppKeyAuthHandler>("AppKeyAuth", options =>
{
    options.ApiKey = builder.Configuration["AppKeys:Haven"];
    var excelKey = builder.Configuration["AppKeys:ExcelAddin"];
    if (!string.IsNullOrEmpty(excelKey))
        options.ApiKeys.Add(excelKey);
});
```
Correctly reads `AppKeys:ExcelAddin` from configuration and adds it to `opts.ApiKeys`. Guard for empty string prevents accidentally adding a blank key.

**11. ✅ PASS — `appsettings.json` has correct structure**
```json
"AppKeys": {
  "Haven": "",
  "ExcelAddin": ""
}
```
Both placeholders present. Values are empty strings as expected for a dev config (production values injected via ECS environment variables).

---

### Office JS Correctness (items 12–16)

**12. ✅ PASS — All Office JS calls gated on `Office.onReady()`**
`index.tsx`:
```typescript
Office.onReady(() => {
  const root = createRoot(container);
  root.render(<App />);
});
```
React tree doesn't mount until `onReady` fires. All `Excel.run()` and `OfficeRuntime.storage` calls are inside components/hooks that only run after mount. ✅

**13. ✅ PASS — `Excel.run()` pattern correct**
Both `getSelectedRange()` and `getFullWorksheet()` in `excelReader.ts` use `async (ctx) =>` and call `await ctx.sync()` after all `.load()` calls. Pattern is correct.

**14. ✅ PASS — `getSelectedRange()` loads all required properties**
```typescript
range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);
await ctx.sync();
```
All five required properties loaded before sync. ✅

**15. ✅ PASS — `getFullWorksheet()` hard caps at 500 rows × 50 cols**
```typescript
// Hard cap: 500 rows × 50 cols
const vals = (range.values as unknown[][]).slice(0, 500).map((r) => r.slice(0, 50));
const fmls = (range.formulas as string[][]).slice(0, 500).map((r) => r.slice(0, 50));
```
Hard caps applied via `.slice()` before return. Row/col counts on the returned struct also clamped with `Math.min`. Comment confirms intent. ✅

**16. ⚠️ FAIL (Critical) — `useExcelContext.ts` selection change handler has memory leak / broken cleanup**

The handler is registered inside `Excel.run()`, which creates a **transient context object** (`ctx`). The `handler` variable captures the event registration result from inside that context. When the cleanup function tries to call `handler.remove()` in a *new* `Excel.run()` call, the original `ctx` proxy is no longer valid — the remove call will silently fail or throw.

Additionally, the `Office.onReady()` callback inside `useEffect` is an async fire-and-forget: if the component unmounts before `Excel.run()` completes registration, the cleanup function runs with `handler === null` and the event listener is never removed — a genuine memory/listener leak.

```typescript
// Current (broken cleanup)
return () => {
  if (handler) {
    try {
      Excel.run(async (ctx: any) => {
        handler.remove();   // ← handler is from a DIFFERENT ctx — wrong
        await ctx.sync();
      });
    } catch { }
  }
};
```

**Required fix — use `ctx.workbook.onSelectionChanged` with a stable event registration:**
The standard pattern for removable Excel event handlers is to use the `Excel.EventRegistration` API or to keep the handler registered for the lifetime of the add-in (acceptable for a task pane). For Sprint 1, the simplest correct approach is to not attempt removal in cleanup (task pane event handlers are automatically cleaned up when the task pane closes) or to use the `detach()` method on the result returned from `add()` within the same `Excel.run` context.

Note: `ChatPanel.tsx` already has a `setInterval(refresh, 2000)` for selection info — the hook's `onSelectionChanged` handler is effectively redundant for the current UI. If the intent is just to keep the indicator fresh, the interval in `ChatPanel` is simpler and already working. The hook's handler registration should either be fixed or removed.

---

### FAIT API Integration (items 17–20)

**17. ✅ PASS — Correct endpoint**
```typescript
const FAIT_BASE = 'https://fait.dev.fortressam.ai';
// ...
fetch(`${FAIT_BASE}/api/haven/chat`, ...)
```
Confirmed.

**18. ✅ PASS — `x-api-key` header used**
```typescript
headers: {
  'Content-Type': 'application/json',
  'x-api-key': apiKey,
},
```
No `Authorization: Bearer` pattern. ✅

**19. ✅ PASS — 30-second AbortController timeout**
```typescript
const controller = new AbortController();
const timeout = setTimeout(() => controller.abort(), 30_000);
```
Timeout set to exactly 30,000ms. `clearTimeout(timeout)` called in `finally`. AbortError caught and mapped to `TIMEOUT`. ✅

**20. ✅ PASS — Error mapping correct**
`useChat.ts`:
- `INVALID_KEY` → `"Invalid API key — check Settings"` ✅
- `TIMEOUT` → user-friendly message ✅
- `SERVICE_UNAVAILABLE` → user-friendly message ✅
- Catch-all → `"FAIT unavailable — try again"` ✅

---

### Context Formatter (items 21–23)

**21. ✅ PASS — Header row detection correct**
```typescript
const isHeader =
  row0.length > 0 &&
  row0.every((v) => typeof v === 'string' && v.trim() !== '' && isNaN(Number(v)));
```
Detects row 0 as header if: non-empty, all entries are strings, none parse as numbers. Logic matches spec. Headers output on separate `Headers:` line; data rows start at `Row 1`. ✅

**22. ✅ PASS — Context truncated at ~6,000 chars**
```typescript
if (out.length > 6000) {
  out = out.slice(0, 5900) + '\n[... truncated for brevity]\n';
}
```
Hard cut at 5,900 chars with exactly the required message appended. `[END SPREADSHEET CONTEXT]` appended after. ✅

**23. ✅ PASS — Non-trivial formulas included**
```typescript
const fmlStr = fmlRow
  .map((f: string, ci: number) =>
    f.startsWith('=') ? `${getCellAddr(...)}=${f}` : ''
  )
  .filter(Boolean)
  .join(', ');
if (fmlStr) out += `Formulas: ${fmlStr}\n`;
```
Only formulas starting with `=` are included. Blank/literal values skipped. ✅

**⚠️ Important: `getCellAddr()` column address overflow (Issue C)**
```typescript
function getCellAddr(row: number, col: number): string {
  const colLetter = String.fromCharCode(65 + col);   // A–Z only
  return `${colLetter}${row + 1}`;
}
```
`getFullWorksheet` caps at 50 columns (col indices 0–49). `String.fromCharCode(65 + 26)` = `[`, not `AA`. For columns 27–49 (AA–AX in Excel), the formula cell address will be wrong (non-alpha chars), making the formula context confusing/misleading. This doesn't cause a runtime error but produces incorrect cell references in the AI context.

**Required fix:**
```typescript
function getCellAddr(row: number, col: number): string {
  let colStr = '';
  let c = col;
  do {
    colStr = String.fromCharCode(65 + (c % 26)) + colStr;
    c = Math.floor(c / 26) - 1;
  } while (c >= 0);
  return `${colStr}${row + 1}`;
}
```

---

### UX Correctness (items 24–27)

**24. ✅ PASS — `App.tsx` routing logic correct**
```typescript
if (!apiKey || showSettings) {
  return <SettingsPanel onKeySet={handleKeySet} />;
}
return <ChatPanel apiKey={apiKey} onOpenSettings={handleOpenSettings} />;
```
`<SettingsPanel>` shown when `apiKey` is null/empty. `<ChatPanel>` shown when key exists. Loading state handled with spinner. ✅

**25. ✅ PASS — Context read only on send**
`ChatPanel.handleSend()` calls `getSelectedRange()` only when the user sends a message and `includeSelection` is true. The textarea `onChange` handler (`handleInput`) only updates local state — no spreadsheet reads. ✅

Note: `ChatPanel` has a `setInterval(refresh, 2000)` for the context indicator label — this calls `getSelectedRange()` every 2 seconds, but **only to update the UI indicator** (address/row/col counts), not to build context. The context for the AI is always read fresh at send time. Item 25 passes.

**26. ✅ PASS — "Include selection" defaults to ON**
```typescript
const [includeSelection, setIncludeSelection] = useState(true);
```
Default state is `true`. Checkbox is `checked={includeSelection}`. ✅

**27. ✅ PASS — `ModelPicker` has Haiku and Sonnet; default Sonnet**
```tsx
// ModelPicker.tsx
<option value="haiku">Haiku (fast)</option>
<option value="sonnet">Sonnet (best)</option>
```
```typescript
// ChatPanel.tsx
const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');
```
Default is `sonnet`. Both options present. ✅

---

### Manifest (items 28–30)

**28. ✅ PASS — Valid GUID in `<Id>`**
```xml
<Id>a1b2c3d4-e5f6-7890-abcd-ef1234567890</Id>
```
Valid GUID format (8-4-4-4-12). Not placeholder text. ✅

**29. ✅ PASS — `<SourceLocation>` correct**
```xml
<SourceLocation DefaultValue="https://fait.dev.fortressam.ai/excel-addin/"/>
```
Also confirmed in `bt:Url id="Taskpane.Url"`. Vite config sets `base: '/excel-addin/'` to match. ✅

**30. ✅ PASS — `<Set Name="ExcelApi" MinVersion="1.13"/>`**
```xml
<Requirements>
  <Sets>
    <Set Name="ExcelApi" MinVersion="1.13"/>
  </Sets>
</Requirements>
```
Exact match. ✅

---

## Required Changes Before PASS

### Critical — Must Fix

**Issue A: `storage.ts` — `setApiKey()` missing try/catch**

File: `src/taskpane/services/storage.ts`

```typescript
// CURRENT (missing try/catch)
export async function setApiKey(key: string): Promise<void> {
  await OfficeRuntime.storage.setItem(KEY, key);
}

// REQUIRED
export async function setApiKey(key: string): Promise<void> {
  try {
    await OfficeRuntime.storage.setItem(KEY, key);
  } catch {
    throw new Error('STORAGE_UNAVAILABLE');
  }
}
```

And in `SettingsPanel.tsx`, add handling for `STORAGE_UNAVAILABLE`:
```typescript
} else if (msg === 'STORAGE_UNAVAILABLE') {
  setError('Unable to save API key — Office storage unavailable in this environment');
}
```

---

**Issue B: `useExcelContext.ts` — broken handler cleanup / memory leak**

File: `src/taskpane/hooks/useExcelContext.ts`

The simplest correct fix: remove the broken event handler registration entirely and rely on the `setInterval` in `ChatPanel.tsx` (already working and sufficient for the indicator). The hook's `readSelection` function remains useful; only the `onSelectionChanged` registration needs to go.

```typescript
// REMOVE the useEffect block that registers onSelectionChanged
// KEEP readSelection and selectionInfo state
// ChatPanel's setInterval already keeps the indicator fresh

export function useExcelContext() {
  const readSelection = async (): Promise<SpreadsheetContext | null> => {
    try {
      return await getSelectedRange();
    } catch {
      return null;
    }
  };

  return { readSelection };
}
```

Alternatively, if the event handler is desired for future Sprint 2 use, implement it correctly using the detach pattern within a persistent `Excel.run` context that stays open for the add-in lifetime. That's more complex — for Sprint 1, removal is the right call.

---

### Important — Must Fix

**Issue C: `contextFormatter.ts` — `getCellAddr()` only handles A–Z (26 cols)**

File: `src/taskpane/services/contextFormatter.ts`

```typescript
// CURRENT (wrong for col > 25)
function getCellAddr(row: number, col: number): string {
  const colLetter = String.fromCharCode(65 + col);
  return `${colLetter}${row + 1}`;
}

// REQUIRED (correct multi-letter column support)
function getCellAddr(row: number, col: number): string {
  let colStr = '';
  let c = col;
  do {
    colStr = String.fromCharCode(65 + (c % 26)) + colStr;
    c = Math.floor(c / 26) - 1;
  } while (c >= 0);
  return `${colStr}${row + 1}`;
}
```

---

### Nitpick — Clean Up

**Issue D: `ChatPanel.tsx` — redundant 2-second polling**

`useExcelContext` already exists to surface selection info via event. The `setInterval(refresh, 2000)` in `ChatPanel` makes 30 `Excel.run()` calls per minute just to update the indicator label. With Issue B fixed (either by removing the broken handler or implementing it correctly), `ChatPanel` should use the hook's `selectionInfo` state rather than its own polling interval.

This doesn't block Sprint 1 functionality but adds unnecessary Office JS churn.

---

## What Tony Built Well

- **Focus Items all clear:** `localStorage` not used anywhere (Item 1); cell sanitization correct and covers both CR and LF (Item 3); Ordinal comparison on backend (Item 9)
- Clean service layer separation — `storage.ts`, `faitApi.ts`, `contextFormatter.ts`, `excelReader.ts` each have a single responsibility
- `Office.onReady()` gate is correct and all Office JS calls are properly deferred
- The 30-second AbortController timeout is implemented correctly with proper `clearTimeout` cleanup
- Backend `AllKeys` computed property handles the legacy/multi-key scenario cleanly
- `appsettings.json` structure is correct; `Program.cs` registration is clean
- Manifest GUID is valid; ExcelApi 1.13 requirement correctly declared

---

## Re-entry Instructions for BUILD

Fix all three issues (A, B, C) before re-submission. Issue D (nitpick) should also be addressed. No scope creep — do not change anything not listed above.

**Priority order:**
1. `storage.ts` — `setApiKey` try/catch + `SettingsPanel` error handling
2. `useExcelContext.ts` — remove broken onSelectionChanged registration (simplest fix)
3. `contextFormatter.ts` — fix `getCellAddr` for cols 26+
4. `ChatPanel.tsx` — remove 2s polling interval, use hook's selectionInfo instead
