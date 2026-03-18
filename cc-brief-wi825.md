# CC Brief: WI825 — FfE S9: Reactive Workbook Watching

You are implementing Sprint 9 of FAIT for Excel: Reactive Workbook Watching.

Working directory: `/home/fredw/projects/fait-for-excel/`

Touch ONLY these files:
1. `src/taskpane/services/watchMode.ts` — NEW FILE
2. `src/taskpane/services/excelWriter.ts` — MODIFY
3. `src/taskpane/components/ChatPanel.tsx` — MODIFY
4. `src/taskpane/components/WriteSuggestionsDialog.tsx` — MODIFY

Also modify `public/manifest.xml` and `manifest.local.xml` to bump ExcelApi to 1.13.

Do NOT touch any other files. No new npm packages.

---

## CRITICAL RULES — READ FIRST

**Rule 1: `setFaitWriting(false)` MUST be in `finally`, never just in `try`**

```typescript
// CORRECT:
setFaitWriting(true);
try {
  await writeOperation();
} finally {
  setFaitWriting(false);
}

// WRONG — flag stays true if write throws:
try {
  setFaitWriting(true);
  await writeOperation();
  setFaitWriting(false);  // NEVER do this
}
```

**Rule 2: `handleWatchChange` MUST NOT be async**

The `onChanged` event proxy is only valid synchronously. After any `await`, the proxy is invalidated. Capture data synchronously, queue async work via setTimeout:

```typescript
// CORRECT:
const handleWatchChange = (event: any) => {
  const address = event.address ?? '';  // capture synchronously
  setTimeout(() => triggerWatchAnalysis(address), 0);
};

// WRONG:
const handleWatchChange = async (event: any) => { ... };
```

**Rule 3: Store `registerWatchHandler()` return value**

```typescript
eventHandlerRef.current = await registerWatchHandler(handleWatchChange);
```

**Rule 4: `clearTimeout` BEFORE `unregisterWatchHandler` in `stopWatching()`**

```typescript
const stopWatching = async () => {
  if (debounceTimerRef.current) {
    clearTimeout(debounceTimerRef.current);  // FIRST
    debounceTimerRef.current = null;
  }
  if (eventHandlerRef.current) {
    await unregisterWatchHandler(eventHandlerRef.current);
    eventHandlerRef.current = null;
  }
  setWatchModeOn(false);
  setShowWatchConfig(false);
};
```

**Rule 5: `ctx.runtime.enableEvents = false` BEFORE any write in `excelWriter.ts`**

```typescript
return Excel.run(async (ctx: any) => {
  if (isFaitWriting()) {
    ctx.runtime.enableEvents = false;  // before any write
  }
  // ... writes ...
});
```

**Rule 6: Both `applySuggestions()` and `applySingleSuggestion()` calls in `WriteSuggestionsDialog.tsx` need the wrap**

**Rule 7: Animation name `watchPulse` is safe** — `global.css` only has `@keyframes bounce` and `@keyframes fadeIn`. `MessageBubble.tsx` has `@keyframes blink` and `@keyframes fadeIn`. No collision with `watchPulse`.

**Rule 8: ExcelApi 1.13 in both manifest files**

---

## FILE 1 (NEW): `src/taskpane/services/watchMode.ts`

Create this file exactly:

```typescript
/**
 * Watch mode write-in-progress flag.
 *
 * All FAIT write operations set this flag to true before writing and false after.
 * The worksheet.onChanged handler checks this flag and suppresses FAIT triggers
 * when a FAIT write is in progress (loop prevention).
 *
 * Module-level singleton — safe in single-threaded JS/React environment.
 */

let _isFaitWriting = false;

/** Set to true before any FAIT write; false immediately after (in a finally block). */
export function setFaitWriting(val: boolean): void {
  _isFaitWriting = val;
}

/** Returns true if FAIT is currently in the middle of a write operation. */
export function isFaitWriting(): boolean {
  return _isFaitWriting;
}
```

---

## FILE 2 (MODIFY): `src/taskpane/services/excelWriter.ts`

### Change A: Add import at the top (after the existing import line)

Add this import after `import type { CellSuggestion } from '../components/WriteSuggestionsDialog';`:

```typescript
import { isFaitWriting } from './watchMode';
```

### Change B: In `writeRangeData()`, add `enableEvents` guard

Inside the `return Excel.run(async (ctx: any) => {` block, add at the very beginning (before any operations):

```typescript
  // Sprint 9: Defense-in-depth loop prevention when watch mode is active
  if (isFaitWriting()) {
    ctx.runtime.enableEvents = false;
  }
```

### Change C: In `writeToTable()`, add `enableEvents` guard

Inside the `return Excel.run(async (ctx: any) => {` block, add at the very beginning:

```typescript
  // Sprint 9: Defense-in-depth loop prevention when watch mode is active
  if (isFaitWriting()) {
    ctx.runtime.enableEvents = false;
  }
```

### Change D: Add `registerWatchHandler()` and `unregisterWatchHandler()` at the end of the file

Append these two functions at the end of `excelWriter.ts` (before any final blank lines):

```typescript
// ── Sprint 9: Watch mode event handler registration ───────────────────────

/**
 * Register a worksheet.onChanged event handler on the active worksheet.
 * Returns the event result object — caller MUST store it to unregister later.
 *
 * @param onChange  Callback invoked when the worksheet changes.
 *                  MUST NOT be async — the event proxy is only valid synchronously.
 */
export async function registerWatchHandler(
  onChange: (args: any) => void
): Promise<any> {
  return Excel.run(async (ctx: any) => {
    const sheet = ctx.workbook.worksheets.getActiveWorksheet();
    const handler = sheet.onChanged.add(onChange);
    await ctx.sync();
    return handler;
  });
}

/**
 * Unregister a previously registered worksheet.onChanged handler.
 * Safe to call with null — does nothing.
 *
 * @param handlerResult  The event result object returned by registerWatchHandler().
 */
export async function unregisterWatchHandler(handlerResult: any): Promise<void> {
  if (!handlerResult) return;
  try {
    await handlerResult.context.remove(handlerResult);
  } catch {
    // Handler may already be invalid (sheet deleted, workbook closed) — silent failure is safe
  }
}
```

---

## FILE 3 (MODIFY): `src/taskpane/components/ChatPanel.tsx`

### Change 1: Add imports

In the imports section, add these two new imports (after the existing service imports):

```typescript
import { registerWatchHandler, unregisterWatchHandler } from '../services/excelWriter';
import { setFaitWriting, isFaitWriting } from '../services/watchMode';
```

### Change 2: Add `declare const Excel` for direct Excel.run() usage in this component

After the existing `/* global Excel */` comment (line ~240 area — ChatPanel already uses Excel.run() in handleSend), add if not already present:

Actually, ChatPanel already uses `Excel.run` via the `/* global Excel */` pattern in services — but ChatPanel itself calls `Excel.run()` in `handleSend`. Check if there's already a `/* global Excel */` or `declare const Excel` at the top. If not, add:

```typescript
/* global Excel */
```

at the top of the file (after imports).

### Change 3: Add Sprint 9 state (after the Sprint 8 state block)

After the Sprint 8 named range state declarations (around the `handleNameRangeKeyDown` handler area), add:

```typescript
  // ── Sprint 9: Watch mode state ─────────────────────────────────────────
  const [watchModeOn, setWatchModeOn] = useState(false);
  const [showWatchConfig, setShowWatchConfig] = useState(false);
  const [watchRange, setWatchRange] = useState('');
  const [watchPrompt, setWatchPrompt] = useState(
    'Analyze changes in this range and flag any issues or anomalies'
  );
  const [watchTriggerCount, setWatchTriggerCount] = useState(0);
  const [lastWatchTrigger, setLastWatchTrigger] = useState<Date | null>(null);
  const eventHandlerRef = useRef<any>(null);
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
```

### Change 4: Add watch mode handlers (after `handleNameRangeKeyDown`)

After `handleNameRangeKeyDown`, add these handlers. NOTE: `handleWatchChange` is NOT async:

```typescript
  // ── Sprint 9: Watch Mode ────────────────────────────────────────────────

  const handleWatchToggle = () => {
    if (watchModeOn) {
      void stopWatching();
    } else {
      if (selectionInfo?.address) {
        setWatchRange(selectionInfo.address);
      }
      setShowWatchConfig((v) => !v);
    }
  };

  const startWatching = async () => {
    if (!watchRange.trim()) return;
    if (eventHandlerRef.current) {
      await unregisterWatchHandler(eventHandlerRef.current);
      eventHandlerRef.current = null;
    }
    try {
      const handler = await registerWatchHandler(handleWatchChange);
      eventHandlerRef.current = handler;
      setWatchModeOn(true);
      setShowWatchConfig(false);
    } catch (e) {
      console.warn('FAIT watch: failed to register handler:', e);
    }
  };

  const stopWatching = async () => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }
    if (eventHandlerRef.current) {
      await unregisterWatchHandler(eventHandlerRef.current);
      eventHandlerRef.current = null;
    }
    setWatchModeOn(false);
    setShowWatchConfig(false);
  };

  // NOTE: handleWatchChange is intentionally NOT async.
  // The onChanged event proxy is only valid synchronously — do not await inside this function.
  const handleWatchChange = (event: any) => {
    // Loop prevention: ignore events triggered by FAIT's own writes
    if (isFaitWriting()) return;

    const changedAddress: string = event.address ?? '';
    if (!changedAddress || !watchRange) return;

    // Lightweight sheet-name prefix check for fast rejection (no Excel.run needed)
    const watchSheet = watchRange.split('!')[0] ?? '';
    const changedSheet = changedAddress.split('!')[0] ?? '';
    if (watchSheet && changedSheet && watchSheet.toLowerCase() !== changedSheet.toLowerCase()) return;

    // Debounce: reset timer on each change event
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    debounceTimerRef.current = setTimeout(() => {
      debounceTimerRef.current = null;
      void triggerWatchAnalysis();
    }, 500);
  };

  const triggerWatchAnalysis = async () => {
    if (loading) return;
    if (isFaitWriting()) return;

    try {
      const ctx = await Excel.run(async (excelCtx: any) => {
        const sheet = excelCtx.workbook.worksheets.getActiveWorksheet();
        const range = sheet.getRange(watchRange);
        range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);
        await excelCtx.sync();
        return {
          address: range.address as string,
          rows: range.rowCount as number,
          cols: range.columnCount as number,
          values: range.values as unknown[][],
          formulas: range.formulas as string[][],
        };
      });

      const context = formatContext(ctx);
      const triggerMessage = `👁 Watch trigger: ${watchPrompt}`;

      setWatchTriggerCount((n) => n + 1);
      setLastWatchTrigger(new Date());

      await send(triggerMessage, context);
    } catch (e) {
      console.warn('FAIT watch: trigger analysis failed:', e);
    }
  };
```

### Change 5: Add cleanup useEffect (after existing useEffect blocks)

Add this useEffect (for synchronous cleanup of the debounce timer on unmount):

```typescript
  // Sprint 9: Cleanup debounce timer on unmount
  // Note: async unregisterWatchHandler() cannot run in React's synchronous cleanup.
  // The handler proxy becomes stale on unmount — acceptable for a taskpane that rarely unmounts.
  useEffect(() => {
    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current);
      }
    };
  }, []);
```

### Change 6: Wrap write calls in `handleWriteTableConfirm()` with `setFaitWriting`

In the `handleWriteTableConfirm` function, find the `isTableTarget` branch (writeToTable call) and the cell address branch (writeRangeData call). Wrap each with setFaitWriting:

**For `writeToTable` call** — change from:
```typescript
        const result = await writeToTable(target, pendingTableData.rows);
```
to:
```typescript
        setFaitWriting(true);
        let result: Awaited<ReturnType<typeof writeToTable>>;
        try {
          result = await writeToTable(target, pendingTableData.rows);
        } finally {
          setFaitWriting(false);
        }
```

**For `writeRangeData` call** — change from:
```typescript
        const result = await writeRangeData(target, data);
```
to:
```typescript
        setFaitWriting(true);
        let result: Awaited<ReturnType<typeof writeRangeData>>;
        try {
          result = await writeRangeData(target, data);
        } finally {
          setFaitWriting(false);
        }
```

### Change 7: Add watch button to header JSX

In the header's right-side button group, insert the watch button AFTER the sort/filter `🔀` button and BEFORE the clear history `🗑` button:

```tsx
          {/* Watch mode toggle — Sprint 9 */}
          <button
            onClick={handleWatchToggle}
            title={watchModeOn
              ? `Watch mode ON — ${watchTriggerCount} trigger${watchTriggerCount !== 1 ? 's' : ''}`
              : 'Enable watch mode — FAIT reacts to cell changes'}
            aria-label={watchModeOn ? 'Disable watch mode' : 'Enable watch mode'}
            style={{
              ...headerBtnStyle,
              color: watchModeOn ? '#6fcf97' : (showWatchConfig ? '#d4af37' : '#8899aa'),
              position: 'relative',
            }}
          >
            {watchModeOn ? (
              <>
                👁
                <span
                  aria-hidden="true"
                  style={{
                    position: 'absolute',
                    top: '2px',
                    right: '2px',
                    width: '5px',
                    height: '5px',
                    borderRadius: '50%',
                    background: '#6fcf97',
                    animation: 'watchPulse 2s ease-in-out infinite',
                  }}
                />
              </>
            ) : '👁'}
          </button>
```

### Change 8: Add watch config panel and status bar to JSX

AFTER the header `</div>` closing tag and BEFORE the CF inline prompt `{showCfInput && (...)`:

```tsx
      {/* ── Sprint 9: Watch mode config panel ── */}
      {showWatchConfig && !watchModeOn && (
        <div
          style={{
            padding: '10px 12px',
            borderBottom: '1px solid #2e3f54',
            background: '#0f1720',
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: '8px',
          }}
        >
          <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
            👁 Watch Mode
          </div>

          {/* Range input */}
          <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
            <span style={{ fontSize: '11px', color: '#8899aa', flexShrink: 0 }}>Range:</span>
            <input
              value={watchRange}
              onChange={(e) => setWatchRange(e.target.value)}
              placeholder="e.g. Sheet1!A1:D20"
              style={{
                flex: 1,
                background: '#1a2332',
                border: '1px solid #2e3f54',
                borderRadius: '4px',
                color: '#e8edf3',
                padding: '4px 8px',
                fontSize: '12px',
                outline: 'none',
              }}
            />
            <button
              onClick={() => { if (selectionInfo?.address) setWatchRange(selectionInfo.address); }}
              title="Use current selection"
              style={{
                background: '#1e2d3e',
                border: '1px solid #2e3f54',
                borderRadius: '4px',
                color: '#8899aa',
                fontSize: '11px',
                padding: '4px 8px',
                cursor: 'pointer',
                flexShrink: 0,
              }}
            >
              Use selection
            </button>
          </div>

          {/* Prompt input */}
          <div style={{ display: 'flex', gap: '6px', alignItems: 'flex-start' }}>
            <span style={{ fontSize: '11px', color: '#8899aa', flexShrink: 0, paddingTop: '5px' }}>
              Prompt:
            </span>
            <input
              value={watchPrompt}
              onChange={(e) => setWatchPrompt(e.target.value)}
              placeholder="What should FAIT do when this range changes?"
              style={{
                flex: 1,
                background: '#1a2332',
                border: '1px solid #2e3f54',
                borderRadius: '4px',
                color: '#e8edf3',
                padding: '4px 8px',
                fontSize: '12px',
                outline: 'none',
              }}
            />
          </div>

          {/* Start / Cancel buttons */}
          <div style={{ display: 'flex', gap: '6px' }}>
            <button
              onClick={() => void startWatching()}
              disabled={!watchRange.trim()}
              style={{
                background: watchRange.trim() ? '#1a3020' : '#1e2d3e',
                border: `1px solid ${watchRange.trim() ? '#2e5040' : '#2e3f54'}`,
                borderRadius: '4px',
                color: watchRange.trim() ? '#6fcf97' : '#445566',
                fontSize: '11px',
                fontWeight: '600',
                padding: '5px 12px',
                cursor: watchRange.trim() ? 'pointer' : 'not-allowed',
              }}
            >
              Start Watching
            </button>
            <button
              onClick={() => setShowWatchConfig(false)}
              style={{
                background: 'none',
                border: '1px solid #2e3f54',
                borderRadius: '4px',
                color: '#556677',
                fontSize: '11px',
                padding: '5px 8px',
                cursor: 'pointer',
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* ── Sprint 9: Watch mode active status bar ── */}
      {watchModeOn && (
        <div
          style={{
            padding: '4px 12px',
            borderBottom: '1px solid #1a3020',
            background: '#0d1a10',
            flexShrink: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span
              style={{
                display: 'inline-block',
                width: '6px',
                height: '6px',
                borderRadius: '50%',
                background: '#6fcf97',
                animation: 'watchPulse 2s ease-in-out infinite',
              }}
            />
            <span style={{ fontSize: '11px', color: '#6fcf97', fontWeight: '600' }}>
              Watching: {watchRange}
            </span>
            {lastWatchTrigger && (
              <span style={{ fontSize: '10px', color: '#445566' }}>
                · last triggered {lastWatchTrigger.toLocaleTimeString()}
              </span>
            )}
          </div>
          <button
            onClick={() => void stopWatching()}
            title="Stop watching"
            style={{
              background: 'none',
              border: '1px solid #2e4030',
              borderRadius: '4px',
              color: '#6fcf97',
              fontSize: '10px',
              padding: '2px 6px',
              cursor: 'pointer',
            }}
          >
            Stop
          </button>
        </div>
      )}
```

### Change 9: Add `watchPulse` CSS animation

In the JSX return, find where other inline `<style>` blocks are (or just before the closing `</div>` of the root element). Add:

```tsx
      <style>{`
        @keyframes watchPulse {
          0%, 100% { opacity: 1; transform: scale(1); }
          50% { opacity: 0.4; transform: scale(0.7); }
        }
      `}</style>
```

---

## FILE 4 (MODIFY): `src/taskpane/components/WriteSuggestionsDialog.tsx`

### Change 1: Add import

After `import { applySuggestions, applySingleSuggestion } from '../services/excelWriter';`, add:

```typescript
import { setFaitWriting } from '../services/watchMode';
```

### Change 2: Wrap `applySuggestions()` in `handleAcceptAll`

Change from:
```typescript
      await applySuggestions(suggestions);
```
to:
```typescript
      setFaitWriting(true);
      try {
        await applySuggestions(suggestions);
      } finally {
        setFaitWriting(false);
      }
```

### Change 3: Wrap `applySingleSuggestion()` in `handleAcceptCurrent`

Change from:
```typescript
      await applySingleSuggestion(s);
```
to:
```typescript
      setFaitWriting(true);
      try {
        await applySingleSuggestion(s);
      } finally {
        setFaitWriting(false);
      }
```

---

## Manifest Files

In `public/manifest.xml`, find:
```xml
<Set Name="ExcelApi" MinVersion="1.4"/>
```
Change to:
```xml
<Set Name="ExcelApi" MinVersion="1.13"/>
```

In `manifest.local.xml`, make the same change.

---

## After all changes, run:

```bash
npm run build
```

Fix any TypeScript errors. The build must succeed with no errors.

## Important TypeScript notes

- In `handleWriteTableConfirm`, when using `let result` before the try/finally block, you may need to handle the case where the variable could be uninitialized. Use `!` assertion or restructure if needed. The existing code uses the result variable directly after — make sure it still works.
- The `loading` variable in `triggerWatchAnalysis` comes from `useChat` destructuring — it's already in scope.
- `send` in `triggerWatchAnalysis` is the `send` from `useChat` — already in scope.
- `formatContext` is already imported — use it directly.
- `selectionInfo` is already in component state — use it directly.

## Git commit

After a successful build:
```bash
git add -A
git commit -m "feat(S9): WI825 reactive workbook watching — onChanged event, loop prevention, watch config UI"
```
