# FfE Sprint 9 Spec — Reactive Workbook Watching

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** Sprint 6 (`writeRangeData()`) landed. Sprint 8 (named ranges) recommended but not required.  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What the Source Shows

### No existing event handlers

The codebase has **zero worksheet event registrations.** `useExcelContext.ts` deliberately documents *why* it polls rather than using `onSelectionChanged`:

```
// onSelectionChanged registration inside Excel.run() produces a proxy that cannot
// be safely removed from a different Excel.run() context on cleanup, causing
// memory leaks. Polling avoids this entirely.
```

This is the core challenge for Sprint 9: `worksheet.onChanged` has the same lifecycle problem. The event handler is registered inside an `Excel.run()` context, but removing it requires a `context.remove()` call — and that must happen inside a *new* `Excel.run()` where the handler proxy can be re-obtained. The pattern requires storing the `handlerResult` reference and re-fetching it for removal.

### All write paths go through `Excel.run()` in service modules

Every FAIT write calls `Excel.run()` in one of these services:
- `excelWriter.ts` — `applySuggestions()`, `writeRangeData()` (S6), `writeToTable()`, `createNamedRange()` (S7/S8)
- `chartBuilder.ts` — `insertChart()`
- `pivotBuilder.ts` — `insertPivotTable()`
- `cfBuilder.ts` — `applyConditionalFormat()`
- `sortFilterBuilder.ts` — `applySortFilter()`

`context.runtime.enableEvents = false` must be set inside each `Excel.run()` call that writes to the workbook when watch mode is active. This is the loop prevention mechanism. It needs a global flag that the service functions can check.

### Current header buttons

The header bar (right side) has these buttons in order:
`⚠` (check issues) → `🔍` (FORGE) → `📊` (chart) → `🔄` (pivot) → `🎨` (CF) → `🔀` (sort/filter) → `🗑` (clear history) → Model indicator → `⚙` (settings)

Watch mode toggle `👁` goes between sort/filter `🔀` and clear history `🗑`.

### `send()` in `useChat.ts`

The `send()` function is the single entry point for all AI queries. It handles streaming, error recovery, and `parseSuggestions()`. Watch mode will call `send()` via `handleSend()` in `ChatPanel` — same path as user-initiated sends. The only difference: the message is auto-generated from the watch prompt, and watch-triggered sends should be visually distinguished.

---

## What Sprint 9 Delivers

1. **Watch mode toggle** (`👁` button in header) — turns on/off reactive watching
2. **Watch configuration panel** (inline, below header) — select watched range (defaults to current selection), enter a watch prompt (what FAIT should do when the range changes)
3. **`worksheet.onChanged` event handler** — fires when any cell in the watched range changes
4. **Debounce (500ms)** — batches rapid edits; only triggers FAIT after the user stops typing
5. **Loop prevention** — `context.runtime.enableEvents = false` set during all FAIT writes when watch mode is active
6. **`triggerSource` guard (1.13 debounce path)** — at 1.13 baseline, use a write-in-progress flag instead of `triggerSource`; optionally bump to 1.14 for the cleaner path
7. **Watch status indicator** — pulsing dot in header when active; last-triggered timestamp
8. **Stop watching** — toggle off, or automatically stops if the watched range is deleted

---

## Design Decisions

### Decision 1: Watch prompt configuration — minimal, not modal

The watch configuration is a collapsible inline panel (same pattern as CF prompt, sort/filter prompt). It doesn't need a modal. Two inputs:

```
👁 Watch mode — ON
Watching: [Sheet1!A1:D10     ]  [Use selection]
Prompt:   [Re-check for errors in this range          ]
```

The "Use selection" button fills the range input with the current selection address. The prompt defaults to a useful starting point ("Analyze this range and flag any issues or changes"). User can change both before clicking "Start Watching."

### Decision 2: Loop prevention — two-tier approach

**Tier 1 — `isFaitWriting` flag (always active, 1.13-safe):**

A module-level boolean `let isFaitWriting = false` in a new `watchMode.ts` service. All FAIT write functions check this flag; if true, the `onChanged` handler suppresses the trigger.

Setting pattern:
```typescript
isFaitWriting = true;
try {
  await writeRangeData(target, data);
} finally {
  isFaitWriting = false;
}
```

This is cleaner than threading `enableEvents` through every service function's `Excel.run()`. `enableEvents = false` is a session-level Excel setting — if FAIT crashes mid-write with `enableEvents = false`, events stay disabled globally. A flag in JS is safer: it's local to the add-in's memory, not a persistent Excel state.

**Tier 2 — `context.runtime.enableEvents = false` (defense-in-depth, set alongside the flag):**

Inside each write's `Excel.run()`, additionally set `ctx.runtime.enableEvents = false` when `isFaitWriting` is true. Belt-and-suspenders: if the flag check races (unlikely in single-threaded JS), Excel's own event suppression catches it.

**The `triggerSource` question (ExcelApi 1.14):**

`WorksheetChangedEventArgs.triggerSource` distinguishes user edits (`TriggerSource.User`) from program edits (`TriggerSource.Program`). At 1.13, we don't have it — hence the flag approach.

**Decision: Build at 1.13 with the flag approach. Manifest stays at 1.13.**

Rationale:
- The flag approach works correctly for FAIT's architecture — all writes are synchronous `Excel.run()` calls with clear start/end boundaries
- ExcelApi 1.14 bump requires Build 14326+ on Windows (August 2021) and Mac 16.52+ — safe for M365, but LTSC 2021 caps at 1.13
- The spec documents exactly where to add `triggerSource` check if a future bump to 1.14 happens (one-line addition in the event handler)
- No user-visible difference between the 1.13 and 1.14 implementations for this use case

### Decision 3: Event handler lifecycle — `eventResult` ref stored in component

Excel JS API event handlers require storing the event result object to remove the handler later. The correct pattern:

```typescript
// Register
const result = await Excel.run(async (ctx) => {
  const sheet = ctx.workbook.worksheets.getActiveWorksheet();
  const handler = sheet.onChanged.add(handleChange);
  await ctx.sync();
  return handler;  // store this proxy
});
eventHandlerRef.current = result;

// Remove
if (eventHandlerRef.current) {
  await eventHandlerRef.current.context.remove(eventHandlerRef.current);
  eventHandlerRef.current = null;
}
```

The `eventHandlerRef` lives in `ChatPanel` as a `useRef`. **It must NOT be inside `useEffect` cleanup** because the handler proxy requires its own `ctx.sync()` for removal — this can't happen synchronously in a React cleanup. Instead, `stopWatching()` is an explicit async function called when the user toggles off.

### Decision 4: Watched range filtering — check intersection in handler

`worksheet.onChanged` fires for ALL changes on the worksheet, not just the watched range. The event args include `address` (the changed cell/range). Filter: check if the changed address intersects the watched range. Use `getIntersectionOrNullObject()` inside the handler's `Excel.run()` — same pattern as Sprint 7 table detection.

### Decision 5: Watch prompt is free-form text, prepended with context

The user configures a watch prompt like "Re-check for errors in this range." When the handler fires:
1. Read the watched range values via `getSelectedRange()` equivalent for the watched address
2. Call `formatContext()` on those values
3. Prepend the context to the watch prompt
4. Call `send(watchPrompt, context)` via `handleSend()` — same exact path as user-typed messages

This means watch-triggered responses appear in the chat thread like any other message, clearly labeled with a `👁 Watch trigger` prefix injected into the user message content.

### Decision 6: Debounce — 500ms, reset on each change

Excel fires `onChanged` per-cell even during paste operations (multiple events in rapid succession). A 500ms debounce collapses a paste of 50 cells into a single FAIT trigger. Implementation: `debounceTimerRef` in the component, reset on each handler fire.

**Maximum one in-flight watch request at a time.** If the debounce fires while a previous watch-triggered send is still loading, skip the new trigger (don't queue). Checked via the existing `loading` state from `useChat`.

---

## Data Model

### New watch mode state in `ChatPanel`

```typescript
// ── Sprint 9: Watch mode state ────────────────────────────────────────────
const [watchModeOn, setWatchModeOn] = useState(false);
const [showWatchConfig, setShowWatchConfig] = useState(false);
const [watchRange, setWatchRange] = useState('');           // e.g. "Sheet1!A1:D10"
const [watchPrompt, setWatchPrompt] = useState('Analyze changes in this range and flag any issues');
const [watchTriggerCount, setWatchTriggerCount] = useState(0);
const [lastWatchTrigger, setLastWatchTrigger] = useState<Date | null>(null);
const eventHandlerRef = useRef<any>(null);   // stores the onChanged event result proxy
const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
```

### New `watchMode.ts` service (loop prevention flag)

```typescript
// watchMode.ts — module-level write-in-progress flag for loop prevention
let _isFaitWriting = false;

export function setFaitWriting(val: boolean): void {
  _isFaitWriting = val;
}

export function isFaitWriting(): boolean {
  return _isFaitWriting;
}
```

This is a module singleton. Imported by `ChatPanel` for write wrapping, and by `excelWriter.ts` service functions to conditionally set `ctx.runtime.enableEvents = false`.

---

## Parallelization Map

```
Single sequential CC session. 4 files + 1 new file. 5 total.

  Task 1: watchMode.ts           NEW FILE — isFaitWriting flag; setFaitWriting(); isFaitWriting()

  Task 2: excelWriter.ts         Import watchMode; set ctx.runtime.enableEvents = false during
                                   writes when isFaitWriting() is true; add registerWatchHandler()
                                   and unregisterWatchHandler() functions

  Task 3: ChatPanel.tsx          Watch mode state; toggle button; config panel; 
                                   start/stop handlers; debounced onChanged dispatch;
                                   wrap all FAIT write calls with setFaitWriting(true/false)

  Task 4: ChatPanel.tsx          (same file — grouped separately for spec clarity)
                                   Watch status indicator in header; "👁 Watch trigger:" 
                                   prefix on auto-triggered messages
```

Since Tasks 3 and 4 are both in `ChatPanel.tsx`, CC can do them in one pass. They're separated in this spec to make the review surface explicit.

**Files: 1 new + 2 modified. No new npm packages.**

---

## File-Level Spec

### Task 1 (NEW): `src/taskpane/services/watchMode.ts`

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

That's the entire file. Simple. The module-level flag works because all FAIT writes are async functions in a single-threaded JS environment — there's no race condition between setting and checking the flag within one event loop tick.

---

### Task 2: `src/taskpane/services/excelWriter.ts`

Two additions:

**Addition A: Import `isFaitWriting` at the top:**

```typescript
import { isFaitWriting } from './watchMode';
```

**Addition B: Set `ctx.runtime.enableEvents = false` in `writeRangeData()` and `applySuggestions()` when a FAIT write is in progress:**

In `writeRangeData()`, inside `Excel.run()`, after `ctx.sync()` is confirmed safe to call (i.e., at the start of the `Excel.run()` callback):

```typescript
return Excel.run(async (ctx: any) => {
  // Defense-in-depth loop prevention when watch mode is active
  if (isFaitWriting()) {
    ctx.runtime.enableEvents = false;
  }

  const sheet = ctx.workbook.worksheets.getActiveWorksheet();
  // ... rest of existing code unchanged ...
```

Same addition in `applySuggestions()`:

```typescript
await Excel.run(async (ctx: any) => {
  if (isFaitWriting()) {
    ctx.runtime.enableEvents = false;
  }

  const sheet = ctx.workbook.worksheets.getActiveWorksheet();
  // ... rest unchanged ...
```

**Addition C: Add `registerWatchHandler()` and `unregisterWatchHandler()` functions:**

```typescript
/**
 * Register a worksheet.onChanged event handler.
 * Returns the event result object — caller must store it to unregister later.
 *
 * @param onChange  Callback invoked when the worksheet changes.
 *                  Receives the Excel.WorksheetChangedEventArgs.
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
 * Safe to call if the handler is null — does nothing.
 *
 * @param handlerResult  The event result object returned by registerWatchHandler().
 */
export async function unregisterWatchHandler(handlerResult: any): Promise<void> {
  if (!handlerResult) return;
  try {
    await handlerResult.context.remove(handlerResult);
  } catch {
    // Handler may already be invalid (sheet deleted, etc.) — silent failure is safe
  }
}
```

**Do NOT add `isFaitWriting()` guards to `chartBuilder.ts`, `pivotBuilder.ts`, `cfBuilder.ts`, or `sortFilterBuilder.ts`.** Those operations are user-triggered (confirmation dialogs), not reactive. They don't need the guard because they're never initiated by watch mode. The flag + `enableEvents` guard in `excelWriter.ts` is sufficient since that's where all data writes happen.

**Do NOT change** `writeToTable()`, `createNamedRange()`, `deleteNamedRange()`, `WriteRangeError`, `WriteTableError`, or `NamedRangeError`.

---

### Tasks 3 + 4: `src/taskpane/components/ChatPanel.tsx`

Eight targeted changes. No restructuring.

**Change 1: Add imports**

```typescript
import { registerWatchHandler, unregisterWatchHandler } from '../services/excelWriter';
import { setFaitWriting } from '../services/watchMode';
```

**Change 2: Add Sprint 9 state (after Sprint 8 state block)**

```typescript
// ── Sprint 9: Watch mode state ────────────────────────────────────────────
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

**Change 3: Add watch mode handlers**

After `handleNameRangeKeyDown`, add:

```typescript
// ── Sprint 9: Watch Mode ──────────────────────────────────────────────────

const handleWatchToggle = () => {
  if (watchModeOn) {
    stopWatching();
  } else {
    // Pre-fill range with current selection if available
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
    // Failed to register handler — watch mode stays off
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

const handleWatchChange = async (args: any) => {
  // Loop prevention: ignore events triggered by FAIT's own writes
  if (isFaitWriting()) return;

  // ExcelApi 1.14+ path (future): check triggerSource === 'User' here
  // if (args.triggerSource && args.triggerSource !== Excel.TriggerSource.User) return;

  // Check if the change intersects the watched range
  // args.address is the address of the changed cells
  const changedAddress: string = args.address ?? '';
  if (!changedAddress || !watchRange) return;

  // Quick prefix check — if the changed cell is definitely not in the watched sheet, skip
  // Full intersection check would require Excel.run() which is too expensive to do synchronously here
  // Use a lightweight sheet name prefix check for fast rejection
  const watchSheet = watchRange.split('!')[0] ?? '';
  const changedSheet = changedAddress.split('!')[0] ?? '';
  if (watchSheet && changedSheet && watchSheet.toLowerCase() !== changedSheet.toLowerCase()) return;

  // Debounce: reset timer on each change event
  if (debounceTimerRef.current) {
    clearTimeout(debounceTimerRef.current);
  }

  debounceTimerRef.current = setTimeout(async () => {
    debounceTimerRef.current = null;
    await triggerWatchAnalysis();
  }, 500);
};

const triggerWatchAnalysis = async () => {
  // Don't trigger if a FAIT operation is already in progress
  if (loading) return;
  if (isFaitWriting()) return;

  try {
    // Read the watched range
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
    // Prefix the message to distinguish watch-triggered sends in the chat thread
    const triggerMessage = `👁 Watch trigger: ${watchPrompt}`;

    setWatchTriggerCount((n) => n + 1);
    setLastWatchTrigger(new Date());

    await send(triggerMessage, context);
  } catch (e) {
    // Non-fatal — watch continues even if one trigger fails
    console.warn('FAIT watch: trigger analysis failed:', e);
  }
};
```

**Note on `isFaitWriting` import:** `isFaitWriting` is used in `handleWatchChange` and `triggerWatchAnalysis`. Add to imports:

```typescript
import { setFaitWriting, isFaitWriting } from '../services/watchMode';
```

**Change 4: Wrap all FAIT write calls with `setFaitWriting(true/false)` in `handleWriteTableConfirm()`**

The `writeRangeData()` call is already inside `handleWriteTableConfirm()`. Wrap:

```typescript
// BEFORE
const result = await writeRangeData(target, data);

// AFTER
setFaitWriting(true);
let result;
try {
  result = await writeRangeData(target, data);
} finally {
  setFaitWriting(false);
}
```

Same pattern for `writeToTable()` in the `isTableTarget` branch (Sprint 7):

```typescript
// BEFORE (Sprint 7)
const result = await writeToTable(target, pendingTableData.rows);

// AFTER
setFaitWriting(true);
let result;
try {
  result = await writeToTable(target, pendingTableData.rows);
} finally {
  setFaitWriting(false);
}
```

**Change 5: Wrap FAIT write calls in the confirmation dialog handlers**

The `applySuggestions()` / `applySingleSuggestion()` calls happen inside `WriteSuggestionsDialog.tsx`, not `ChatPanel.tsx`. Those need the guard too.

Add the `setFaitWriting` wrapper inside `WriteSuggestionsDialog.tsx`:

```typescript
// Add import to WriteSuggestionsDialog.tsx
import { setFaitWriting } from '../services/watchMode';

// In handleAcceptAll():
setFaitWriting(true);
try {
  await applySuggestions(suggestions);
} finally {
  setFaitWriting(false);
}

// In handleAcceptCurrent():
setFaitWriting(true);
try {
  await applySingleSuggestion(s);
} finally {
  setFaitWriting(false);
}
```

This means `WriteSuggestionsDialog.tsx` is also a changed file. Update the file count.

**Change 6: Clean up watch handler on component unmount**

Add a `useEffect` cleanup:

```typescript
// Sprint 9: Cleanup watch handler on unmount
useEffect(() => {
  return () => {
    // Synchronous cleanup — clear debounce timer
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    // Note: async handler cleanup (unregisterWatchHandler) cannot run in synchronous
    // cleanup. The handler will become stale when the component unmounts. This is
    // acceptable — the add-in taskpane rarely unmounts during a session.
    // If it does unmount (navigation), Excel will GC the handler proxy.
  };
}, []);
```

**Change 7: Add watch button to header and watch config panel to JSX**

In the header, after the sort/filter `🔀` button and before the clear history `🗑` button, add:

```typescript
{/* Watch mode toggle — Sprint 9 */}
<button
  onClick={handleWatchToggle}
  title={watchModeOn ? `Watch mode ON — ${watchTriggerCount} trigger${watchTriggerCount !== 1 ? 's' : ''}` : 'Enable watch mode — FAIT reacts to cell changes'}
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
      {/* Pulsing active dot */}
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

Add the `watchPulse` animation to the `<style>` block in `MessageBubble.tsx` — wait, that's in `MessageBubble`. Better: add a `<style>` tag in `ChatPanel`'s JSX return, alongside where other inline styles live.

Add at the end of the JSX return (before the closing `</div>`):

```typescript
<style>{`
  @keyframes watchPulse {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.4; transform: scale(0.7); }
  }
`}</style>
```

**Watch config panel** — add after the header div and before the CF inline prompt:

```typescript
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
        onClick={() => {
          if (selectionInfo?.address) setWatchRange(selectionInfo.address);
        }}
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
      <span style={{ fontSize: '11px', color: '#8899aa', flexShrink: 0, paddingTop: '5px' }}>Prompt:</span>
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
        onClick={startWatching}
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
      onClick={stopWatching}
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

**Change 8: `Excel` global declaration**

`ChatPanel.tsx` doesn't currently declare `Excel` — it accesses it via service functions. But `triggerWatchAnalysis()` calls `Excel.run()` directly. Add at the top of the file:

```typescript
/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Excel: any;
/* eslint-enable @typescript-eslint/no-explicit-any */
```

---

### File changes summary (updated with WriteSuggestionsDialog)

| File | Change type | Description |
|------|-------------|-------------|
| `src/taskpane/services/watchMode.ts` | **NEW** | `isFaitWriting` flag module |
| `src/taskpane/services/excelWriter.ts` | Modify | Import `isFaitWriting`; set `enableEvents=false` in write functions; add `registerWatchHandler()` + `unregisterWatchHandler()` |
| `src/taskpane/components/ChatPanel.tsx` | Modify | Watch state; handlers; config panel; status bar; `setFaitWriting` wrapping; `Excel` declare |
| `src/taskpane/components/WriteSuggestionsDialog.tsx` | Modify | Import `setFaitWriting`; wrap `applySuggestions` + `applySingleSuggestion` calls |

**1 new file + 3 modified. No new npm packages.**

---

## The `triggerSource` Path (Future 1.14 Upgrade)

This is fully documented so a future bump is a one-line change.

At ExcelApi 1.14, `WorksheetChangedEventArgs.triggerSource` is available with values:
- `Excel.TriggerSource.User` — human edit
- `Excel.TriggerSource.Program` — programmatic write

The 1.14 guard in `handleWatchChange`:

```typescript
// Uncomment this block when manifest.xml bumps to MinVersion="1.14":
// if (args.triggerSource && args.triggerSource !== Excel.TriggerSource.User) {
//   return; // FAIT's own writes — skip
// }
```

This replaces the `isFaitWriting()` check as the primary loop guard (the `isFaitWriting` flag becomes belt-and-suspenders). The `enableEvents = false` calls in `excelWriter.ts` also become unnecessary — but leave them in place for defense-in-depth.

**When to bump to 1.14:**
- When LTSC 2021 support is no longer a concern, OR
- When an Office 2021 LTSC user reports watch mode triggering on FAIT's own writes (the flag approach handles it correctly but `triggerSource` is the canonical solution)

---

## UX Flow — Exact Sequences

### Flow A: User enables watch mode

```
1. User clicks 👁 button in header
2. Watch config panel expands below the header
3. Range input pre-filled with current selection (e.g. "Sheet1!A1:D10")
4. Prompt input: default text visible, user edits to "Flag any cell that drops below 0"
5. User clicks "Start Watching"
6. registerWatchHandler(handleWatchChange) called — onChanged registered
7. watchModeOn = true
8. Config panel collapses; green status bar appears: "● Watching: Sheet1!A1:D10"
9. 👁 button turns green with pulsing dot
```

### Flow B: Cell change triggers FAIT

```
1. Watch mode is ON for "Sheet1!B2:B10"
2. User types "123" in cell B5 and presses Enter
3. onChanged fires: args.address = "Sheet1!B5"
4. handleWatchChange checks: isFaitWriting()? → false
5. Sheet match check: "Sheet1" === "Sheet1" → proceed
6. Debounce timer set for 500ms
7. User immediately types in B6 → onChanged fires again → timer reset
8. User stops editing — 500ms passes
9. triggerWatchAnalysis() fires:
   a. loading? → false → proceed
   b. Excel.run() reads Sheet1!B2:B10
   c. formatContext() produces context block
   d. triggerMessage = "👁 Watch trigger: Flag any cell that drops below 0"
   e. send(triggerMessage, context) called
   f. User message appears in chat: "👁 Watch trigger: Flag any cell..."
   g. FAIT responds: "Cell B6 dropped to -45 — below the 0 threshold..."
10. watchTriggerCount += 1, lastWatchTrigger = now
11. Status bar updates: "● Watching: Sheet1!B2:B10 · last triggered 3:15:22 PM"
```

### Flow C: FAIT writes back — loop prevention

```
1. Watch mode is ON for "Sheet1!A1:D10"
2. FAIT sends a suggestion JSON block back (cell write suggestions)
3. User opens WriteSuggestionsDialog, clicks "Accept All"
4. handleAcceptAll():
   setFaitWriting(true)       ← flag set
   await applySuggestions()   ← writes values to cells
     ↳ inside Excel.run():
       if (isFaitWriting()) ctx.runtime.enableEvents = false  ← events suppressed
       range.values = [[...]]  ← this would normally fire onChanged
       await ctx.sync()
   setFaitWriting(false)      ← flag cleared (in finally)
5. Excel.run() context exits → events re-enabled for next run()
6. onChanged event either didn't fire (events were disabled) or fired but
   handleWatchChange() returned immediately (isFaitWriting() = true)
7. No infinite loop. Watch mode continues watching for USER edits.
```

### Flow D: User stops watch mode

```
1. User clicks Stop in the green status bar (or clicks 👁 button again)
2. stopWatching() called:
   - clearTimeout(debounceTimerRef) — clears any pending debounce
   - unregisterWatchHandler(eventHandlerRef.current) — removes onChanged handler
   - eventHandlerRef.current = null
3. watchModeOn = false
4. Green status bar disappears
5. 👁 button returns to grey
```

---

## Acceptance Criteria

1. **Watch config panel** opens when `👁` is clicked; closes when "Start Watching" or "Cancel" is clicked
2. **"Use selection" button** fills the range input with `selectionInfo.address`
3. **`startWatching()`** registers `worksheet.onChanged` — returns without error when a valid range is entered
4. **`watchModeOn = true`** after successful registration; green status bar visible; `👁` button green with pulsing dot
5. **Cell edit in watched range** triggers FAIT analysis after 500ms debounce
6. **Watch-triggered message** appears in chat thread with `👁 Watch trigger:` prefix
7. **Rapid edits** (e.g. paste 20 cells) produce exactly ONE FAIT trigger (debounce working)
8. **`loading = true` guard**: if a FAIT response is still streaming when a change fires, the trigger is skipped (no queueing)
9. **Loop prevention**: FAIT writing back to the watched range does NOT trigger a new watch analysis (verified via `isFaitWriting()` flag + `enableEvents = false`)
10. **"Stop" button** and `👁` toggle both call `stopWatching()` — handler deregistered, status bar hidden
11. **WriteSuggestionsDialog** `applySuggestions()` calls wrapped with `setFaitWriting(true/false)`
12. **No manifest change:** `MinVersion="1.13"` unchanged
13. **No regression:** All Sprint 1–8 features work identically with watch mode off

---

## Constraints for CC

- Touch only the 4 files listed (1 new, 3 modified)
- `watchMode.ts` must be a module-level singleton — NO class, NO React state, NO closure. It must be importable by both `excelWriter.ts` (service layer) and `ChatPanel.tsx` (component layer) without circular dependencies
- Do NOT add `setFaitWriting` wraps inside `chartBuilder.ts`, `pivotBuilder.ts`, `cfBuilder.ts`, or `sortFilterBuilder.ts` — those services are user-confirmed actions, not reactive writes
- `handleWatchChange` is a raw event callback — it is NOT `async/await` safe for direct Excel API calls. The sheet-name prefix check inside it is intentionally synchronous. `Excel.run()` calls go in `triggerWatchAnalysis()` which is called via `setTimeout`
- The debounce timer MUST be cleared in `stopWatching()` — otherwise a pending timer could fire after the handler is unregistered, causing an orphaned `triggerWatchAnalysis()` call
- `eventHandlerRef.current` cleanup on unmount: synchronous `clearTimeout` only — async `unregisterWatchHandler` CANNOT run in React's `useEffect` cleanup (synchronous). Document this in a comment; it's an acceptable tradeoff for a taskpane that rarely unmounts
- Do NOT suppress events globally across the app — `ctx.runtime.enableEvents = false` is scoped to a single `Excel.run()` context. It resets to `true` when the context exits. This is the correct pattern.
- The `Excel` global declaration in `ChatPanel.tsx` must use `declare const Excel: any` — same pattern as the service files

---

## Clint Review Priorities

```
⚠️  HIGH: Verify setFaitWriting(false) is called in a `finally` block — NOT in the
          try block. If writeRangeData() throws, the flag must still be cleared.
          If setFaitWriting(false) is in the try block, a failed write leaves watch
          mode permanently suppressed.

⚠️  HIGH: Verify handleWatchChange is NOT declared async. The onChanged callback
          receives a proxy object from Excel that becomes invalid after the sync
          tick. Do not await inside handleWatchChange directly — the setTimeout
          wrapper in triggerWatchAnalysis() is the correct async boundary.

⚠️  HIGH: Confirm registerWatchHandler() returns the handler proxy AND that
          eventHandlerRef.current stores it. If eventHandlerRef.current is null or
          undefined when stopWatching() calls unregisterWatchHandler(), the handler
          is never deregistered and continues firing silently.

⚠️  HIGH: Verify clearTimeout(debounceTimerRef.current) runs in stopWatching() BEFORE
          unregisterWatchHandler(). If the order is reversed and the timer fires between
          the two calls, triggerWatchAnalysis() runs against an already-unregistered context.

⚠️  MEDIUM: Confirm WriteSuggestionsDialog.tsx has BOTH applySuggestions() (handleAcceptAll)
            AND applySingleSuggestion() (handleAcceptCurrent) wrapped with setFaitWriting.
            Both write to the workbook — both need the loop guard.

⚠️  MEDIUM: Confirm ctx.runtime.enableEvents = false is set BEFORE any range.values = 
            assignment inside the Excel.run() callback. If set after, the event may fire
            before it's suppressed.

⚠️  MEDIUM: Confirm the debounce timer resets correctly: clearTimeout + setTimeout on
            every call to handleWatchChange while watching. A single missed clearTimeout
            would allow two FAIT triggers from one burst of edits.

⚠️  LOW: The `<style>` block with `@keyframes watchPulse` — confirm it doesn't conflict
         with the existing `@keyframes blink` in MessageBubble.tsx. They're in different
         components but global CSS injection can produce name collisions. Rename if needed.

⚠️  LOW: The pulsing dot on the 👁 button uses position:absolute — confirm the button
         container has position:relative, or the dot will position relative to the
         nearest positioned ancestor (possibly the header bar).
```

---

## Architectural Note: Why Module-Level Flag, Not React State

The `isFaitWriting` flag could theoretically live in React state (`useState`). It doesn't, for two reasons:

1. **Timing.** React state updates are batched and asynchronous. The `onChanged` handler fires synchronously from Excel's event loop. If `isFaitWriting` were React state, a write could complete and set the state to `false` before a queued state batch processes — creating a window where the flag reads stale. A module-level variable is synchronously read and written.

2. **Cross-boundary access.** `excelWriter.ts` is a pure service module — it has no access to React hooks or component state. The only clean way to share state between `excelWriter.ts` and `ChatPanel` without coupling the service layer to React is a module-level singleton. This is the same pattern as `sessionStorage.ts` (which uses module-level storage accessor functions).

The tradeoff: module state is harder to inspect in React DevTools. For a binary flag with a clear lifetime (set before write, cleared in finally), this is acceptable.

---

_Spec by Reed Richards | Sprint 9 is 1 new file + 3 edits. The architecture is straightforward once you know the event handler lifecycle gotcha: the proxy object from `Excel.run()` must be stored and re-presented to `.context.remove()` for cleanup — you can't re-fetch it in a new run context._
