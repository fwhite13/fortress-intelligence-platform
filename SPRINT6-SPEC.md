# FfE Sprint 6 Spec — Write Table to Range

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** Sprint 2 gaps (WI#814) must be landed — `writeRangeData()` and `WriteRangeError` exist in `excelWriter.ts` ✅  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What the Source Actually Shows

I read the full source before writing this spec. Key findings:

### What already exists

- `writeRangeData(targetCell, data[][])` — ✅ implemented and exported from `excelWriter.ts`
- `WriteRangeError` — ✅ exported from `excelWriter.ts`
- `writeRangeData` is **already imported** in `ChatPanel.tsx` (line 5: `import { writeRangeData, WriteRangeError } from '../services/excelWriter';`) — but never called
- `WriteSuggestionsDialog.tsx` — `handleAcceptAll` at line 56 already has the `|| msg.includes('does not fit')` guard ✅
- `WriteSuggestionsDialog.tsx` — `handleAcceptCurrent` at line 86 is **missing** `|| msg.includes('does not fit')` ← the Clint nitpick

### What doesn't exist

- No markdown table parser anywhere in the codebase — `parseSuggestions()` only handles `\`\`\`json ... \`\`\`` blocks with named keys (`suggestions`, `chart_spec`, `pivot_spec`, `cf_spec`, `sort_filter_spec`)
- No `table_data` key in `ParseResult` — the parser has no concept of a 2D data table in the response
- No "Write to sheet" button in the message bubble or anywhere in the UI
- No target-cell prompt / dialog for write-table operations
- `Message` interface has no `tableData` field — messages have `role` and `content` only
- `MessageBubble.tsx` uses `simpleMarkdown()` — a minimal renderer that handles `**bold**`, `` `code` ``, and newlines. It does **not** render `|`-delimited markdown tables as HTML tables

### The Clint nitpick (fold into Sprint 6)

`handleAcceptCurrent` (line 86, `WriteSuggestionsDialog.tsx`):
```typescript
// CURRENT — missing 'does not fit'
if (msg.includes('dimension') || msg.includes('mismatch')) {
```
Should match `handleAcceptAll` at line 56:
```typescript
// CORRECT — already in handleAcceptAll
if (msg.includes('dimension') || msg.includes('mismatch') || msg.includes('does not fit')) {
```
This is a 1-word fix. Fold it into Sprint 6 — it touches `WriteSuggestionsDialog.tsx` which is already in scope.

---

## What Sprint 6 Delivers

The user sends a message like "Give me a table of Q1 through Q4 revenue by region" and FAIT responds with a markdown table. Currently that table renders as plain text in the bubble — no action available. After Sprint 6:

1. FAIT returns a response containing a markdown table
2. The parser extracts the table into a `ParsedTable` (headers + 2D rows)
3. The message bubble renders the table as a styled HTML table
4. Below the table: a "Write to Sheet ↓" button
5. User clicks → a small inline prompt asks for a target cell (default: active selection or "A1")
6. User confirms → `writeRangeData(targetCell, data)` writes the table to the sheet
7. Success toast: "Written to [address]"

---

## Parsing Strategy: How FAIT Returns Tabular Data

`parseSuggestions()` currently only parses fenced JSON blocks. There are two ways FAIT can return a table:

**Option A — Markdown table (prose response)**  
FAIT naturally renders tables in markdown:
```
| Region | Q1 | Q2 | Q3 | Q4 |
|--------|----|----|----|----|
| North  | 12 | 15 | 18 | 21 |
| South  | 8  | 10 | 11 | 13 |
```

**Option B — JSON `table_data` block (structured response)**  
FAIT returns a fenced JSON block when prompted for write-ready data:
```json
{
  "table_data": {
    "headers": ["Region", "Q1", "Q2", "Q3", "Q4"],
    "rows": [
      ["North", 12, 15, 18, 21],
      ["South", 8, 10, 11, 13]
    ]
  }
}
```

**Decision: Support both. Detect markdown tables in `parseSuggestions()` AND add a `table_data` JSON block parser.**

Rationale: FAIT's prose responses will naturally produce markdown tables. The JSON path gives deterministic write-ready data when FAIT is explicitly asked for structured output. Both paths should produce the same `ParsedTable` interface so downstream code is identical.

---

## Data Model

```typescript
// New interface — add to suggestionParser.ts
export interface ParsedTable {
  headers: string[];
  rows: (string | number | boolean | null)[][];
}
```

`ParseResult` in `suggestionParser.ts` gains one new field:
```typescript
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;   // ← NEW
}
```

`Message` in `useChat.ts` gains one optional field:
```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;  // ← NEW — non-null only on assistant messages
}
```

---

## Parallelization Map

```
Single sequential CC session — all changes are in fait-for-excel/src/ only.
No shared files between tasks. 5 files total.

  Task 1: suggestionParser.ts   — add ParsedTable interface + markdown table parser
                                   + table_data JSON block parser
  Task 2: useChat.ts            — propagate tableData from parseSuggestions into Message
  Task 3: MessageBubble.tsx     — render table as HTML + "Write to Sheet ↓" button
  Task 4: ChatPanel.tsx         — handle writeTable action: target-cell prompt → writeRangeData call
  Task 5: WriteSuggestionsDialog.tsx — 1-line fix: add '|| msg.includes("does not fit")' to handleAcceptCurrent
```

---

## File-Level Spec

### Task 1: `src/taskpane/services/suggestionParser.ts`

**Add `ParsedTable` interface** (before `ParseResult`):

```typescript
export interface ParsedTable {
  headers: string[];
  rows: (string | number | boolean | null)[][];
}
```

**Add `tableData: ParsedTable | null` to `ParseResult`:**

```typescript
export interface ParseResult {
  displayText: string;
  suggestions: CellSuggestion[] | null;
  chartSpec: ChartSpec | null;
  pivotSpec: PivotSpec | null;
  cfSpec: CfSpec | null;
  sortFilterSpec: SortFilterSpec | null;
  tableData: ParsedTable | null;   // ← add this field
}
```

**Add two parsers inside `parseSuggestions()` — both set `tableData` if detected. Add them after the `sort_filter_spec` block and before the final cleanup.**

**Parser A — `table_data` JSON block:**

```typescript
// ── table_data block ──────────────────────────────────────────────────────
const tableDataRegex = /```json\s*(\{[\s\S]*?"table_data"[\s\S]*?\})\s*```/;
const tableDataMatch = displayText.match(tableDataRegex);
if (tableDataMatch && !tableData) {
  try {
    const parsed = JSON.parse(tableDataMatch[1]);
    const td = parsed.table_data;
    if (
      td &&
      Array.isArray(td.headers) &&
      td.headers.length > 0 &&
      Array.isArray(td.rows) &&
      td.rows.length > 0
    ) {
      tableData = {
        headers: td.headers as string[],
        rows: td.rows as (string | number | boolean | null)[][],
      };
      displayText = displayText.replace(tableDataMatch[0], '');
    }
  } catch {
    // Bad JSON — leave displayText unchanged
  }
}
```

**Parser B — Markdown table detection:**

```typescript
// ── markdown table detection ──────────────────────────────────────────────
// Only run if no table_data JSON block was found
if (!tableData) {
  // Match a markdown table: header row | separator row | 1+ data rows
  // Each row: starts and optionally ends with |, cells separated by |
  const mdTableRegex = /(\|.+\|\s*\n\|[-| :]+\|\s*\n(?:\|.+\|\s*\n?)+)/g;
  const mdTableMatch = mdTableRegex.exec(displayText);
  if (mdTableMatch) {
    try {
      const lines = mdTableMatch[1]
        .trim()
        .split('\n')
        .map((l) => l.trim());

      // lines[0] = header, lines[1] = separator, lines[2..] = data rows
      if (lines.length >= 3) {
        const parseRow = (line: string): string[] =>
          line
            .replace(/^\|/, '')
            .replace(/\|$/, '')
            .split('|')
            .map((c) => c.trim());

        const headers = parseRow(lines[0]);
        // Skip separator (lines[1])
        const rows = lines.slice(2).map((line) => {
          return parseRow(line).map((cell) => {
            // Coerce numeric strings to numbers
            const n = Number(cell.replace(/,/g, ''));
            return cell !== '' && !isNaN(n) && isFinite(n) ? n : cell;
          });
        }) as (string | number | boolean | null)[][];

        if (headers.length > 0 && rows.length > 0) {
          tableData = { headers, rows };
          // Leave the markdown table in displayText — MessageBubble renders it as HTML
          // (do NOT strip it from displayText — the table IS the display content)
        }
      }
    } catch {
      // Malformed table — ignore
    }
  }
}
```

**Important note on markdown table stripping:** Unlike JSON blocks (which are stripped from `displayText` because they'd render as ugly JSON), markdown table text is left in `displayText`. `MessageBubble` will detect `tableData` on the message and render it as a styled HTML table instead of the raw `| | |` pipe text. The rendered HTML table replaces the raw `displayText` rendering when `tableData` is present — see Task 3.

**Initialize `tableData` at the top of `parseSuggestions()`:**

```typescript
let tableData: ParsedTable | null = null;
```

**Update the return statement:**

```typescript
return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData };
```

**Do NOT modify** any existing parser blocks (suggestions, chart_spec, pivot_spec, cf_spec, sort_filter_spec).

---

### Task 2: `src/taskpane/hooks/useChat.ts`

**Update `Message` interface:**

```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;  // ← add this field
}
```

**Add import** for `ParsedTable`:

```typescript
import type { ParsedTable } from '../services/suggestionParser';
// (ParseResult is already imported via parseSuggestions)
```

Wait — `ParsedTable` is not currently imported. Add it to the existing import:

```typescript
// BEFORE
import { parseSuggestions } from '../services/suggestionParser';

// AFTER
import { parseSuggestions } from '../services/suggestionParser';
import type { ParsedTable } from '../services/suggestionParser';
```

Or as a single combined import:

```typescript
import { parseSuggestions, type ParsedTable } from '../services/suggestionParser';
```

**In the `send()` function**, propagate `tableData` when finalizing the assistant message. Find this block:

```typescript
// BEFORE
setMessages((prev) => {
  const next = [...prev];
  next[assistantIndex] = { role: 'assistant', content: displayText, streaming: false };
  return next;
});
```

Replace with:

```typescript
// AFTER
const { displayText, suggestions, tableData } = parseSuggestions(rawText);

setMessages((prev) => {
  const next = [...prev];
  next[assistantIndex] = {
    role: 'assistant',
    content: displayText,
    streaming: false,
    tableData: tableData ?? null,
  };
  return next;
});
```

**Note:** `parseSuggestions` is already called once to get `{ displayText, suggestions }`. Replace that call so it also destructures `tableData` in one pass:

```typescript
// BEFORE (single destructure call — already in useChat.ts)
const { displayText, suggestions } = parseSuggestions(rawText);

// AFTER
const { displayText, suggestions, tableData } = parseSuggestions(rawText);
```

Do NOT call `parseSuggestions` twice. One call, destructure all needed fields.

**Do NOT change** anything else in `useChat.ts`. The `UseChatReturn` interface, `send()` signature, error handling, streaming logic — all untouched.

---

### Task 3: `src/taskpane/components/MessageBubble.tsx`

This is the most visible change. When an assistant message has `tableData`, render a styled HTML table instead of the raw pipe text, plus a "Write to Sheet ↓" button.

**Update `MessageBubbleProps`:**

```typescript
import type { ParsedTable } from '../services/suggestionParser';

interface MessageBubbleProps {
  message: Message;
  streaming?: boolean;
  onWriteTable?: (tableData: ParsedTable) => void;  // ← add this callback
}
```

**Add a `TableRenderer` sub-component** (inside the same file, above `MessageBubble`):

```typescript
const TableRenderer: React.FC<{
  tableData: ParsedTable;
  onWrite: () => void;
}> = ({ tableData, onWrite }) => {
  return (
    <div style={{ marginTop: '6px', overflowX: 'auto', maxWidth: '100%' }}>
      <table
        style={{
          borderCollapse: 'collapse',
          fontSize: '11px',
          width: '100%',
          color: '#e8edf3',
        }}
      >
        <thead>
          <tr>
            {tableData.headers.map((h, i) => (
              <th
                key={i}
                style={{
                  padding: '4px 8px',
                  background: '#1a3a5f',
                  borderBottom: '1px solid #2e5080',
                  textAlign: 'left',
                  fontWeight: '600',
                  whiteSpace: 'nowrap',
                  color: '#d4af37',
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {tableData.rows.map((row, ri) => (
            <tr
              key={ri}
              style={{
                background: ri % 2 === 0 ? '#131f2e' : '#0f1720',
              }}
            >
              {row.map((cell, ci) => (
                <td
                  key={ci}
                  style={{
                    padding: '3px 8px',
                    borderBottom: '1px solid #1a2840',
                    whiteSpace: 'nowrap',
                    textAlign: typeof cell === 'number' ? 'right' : 'left',
                  }}
                >
                  {cell === null || cell === undefined ? '' : String(cell)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>

      {/* Write to Sheet button */}
      <button
        onClick={onWrite}
        title="Write this table to the active worksheet"
        style={{
          marginTop: '6px',
          padding: '4px 10px',
          background: '#1e3a5f',
          border: '1px solid #2e5080',
          borderRadius: '4px',
          color: '#d4af37',
          fontSize: '11px',
          fontWeight: '600',
          cursor: 'pointer',
          display: 'flex',
          alignItems: 'center',
          gap: '4px',
        }}
      >
        <span>↓</span>
        <span>Write to Sheet</span>
      </button>
    </div>
  );
};
```

**Update `MessageBubble` to render `TableRenderer` for assistant messages with `tableData`:**

When the message has `tableData`, render the styled table instead of the raw markdown pipe text. The `content` field will still contain the markdown table text (we don't strip it), but we suppress the `simpleMarkdown(content)` rendering and show the HTML table instead — unless still streaming (show raw text while streaming).

```typescript
const MessageBubble: React.FC<MessageBubbleProps> = ({ message, streaming, onWriteTable }) => {
  const isUser = message.role === 'user';
  const isStreaming = streaming ?? message.streaming ?? false;
  const hasTable = !isUser && !isStreaming && message.tableData != null;

  // For assistant messages with a parsed table, suppress raw pipe text rendering:
  // strip the markdown table out of content for text display, show HTML table below
  let displayContent = message.content;
  if (hasTable && message.tableData) {
    // Remove the raw markdown table from the text so it's not doubled
    displayContent = message.content
      .replace(/\|.+\|\s*\n\|[-| :]+\|\s*\n(?:\|.+\|\s*\n?)+/g, '')
      .trim();
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: isUser ? 'flex-end' : 'flex-start',
        padding: '4px 8px',
        animation: 'fadeIn 0.2s ease-out',
      }}
    >
      {/* Role label */}
      <span
        style={{
          fontSize: '10px',
          fontWeight: '600',
          color: isUser ? '#8899aa' : '#d4af37',
          marginBottom: '2px',
          textTransform: 'uppercase',
          letterSpacing: '0.5px',
        }}
      >
        {isUser ? 'You' : 'FAIT'}
      </span>

      {/* Bubble */}
      <div
        style={{
          maxWidth: '90%',
          padding: '8px 12px',
          borderRadius: isUser ? '12px 12px 4px 12px' : '12px 12px 12px 4px',
          background: isUser ? '#243447' : '#1e3a5f',
          border: `1px solid ${isUser ? '#2e3f54' : '#2e5080'}`,
          color: '#e8edf3',
          fontSize: '13px',
          lineHeight: 1.6,
          wordBreak: 'break-word',
          position: 'relative',
        }}
      >
        {/* Text content — suppress when a table is present and text is empty after stripping */}
        {(displayContent.length > 0 || isStreaming) && (
          <span dangerouslySetInnerHTML={{ __html: simpleMarkdown(displayContent) }} />
        )}

        {/* Streaming cursor */}
        {isStreaming && (
          <span
            aria-hidden="true"
            style={{
              display: 'inline-block',
              width: '2px',
              height: '13px',
              background: '#d4af37',
              marginLeft: '2px',
              verticalAlign: 'text-bottom',
              animation: 'blink 1s step-end infinite',
            }}
          />
        )}

        {/* Rendered table + Write button */}
        {hasTable && message.tableData && onWriteTable && (
          <TableRenderer
            tableData={message.tableData}
            onWrite={() => onWriteTable(message.tableData!)}
          />
        )}
      </div>

      <style>{`
        @keyframes blink {
          0%, 100% { opacity: 1; }
          50% { opacity: 0; }
        }
        @keyframes fadeIn {
          from { opacity: 0; transform: translateY(4px); }
          to   { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};
```

**Note on `onWriteTable` being optional:** `MessageBubble` is rendered from `MessageList`. `MessageList` currently doesn't pass callbacks into `MessageBubble`. See Task 4 for how this threads through.

**Do NOT change** `simpleMarkdown()`. It stays exactly as-is.

---

### Task 4: `src/taskpane/components/ChatPanel.tsx`

Three focused additions. Do not restructure anything else.

**Add state for the write-table flow:**

After the existing `sortFilterLoading` state (around line 90), add:

```typescript
// ── Sprint 6: Write Table state ───────────────────────────────────────────
const [pendingTableData, setPendingTableData] = useState<ParsedTable | null>(null);
const [writeTableTarget, setWriteTableTarget] = useState('');
const [writeTableLoading, setWriteTableLoading] = useState(false);
const [writeTableError, setWriteTableError] = useState<string | null>(null);
const [writeTableSuccess, setWriteTableSuccess] = useState<string | null>(null);
const writeTableInputRef = useRef<HTMLInputElement>(null);
```

Add import for `ParsedTable`:

```typescript
import type { ParsedTable } from '../services/suggestionParser';
```

**Add `handleWriteTable` handler** (after `handleClearHistory`, before the `modelLabel` line):

```typescript
// ── Sprint 6: Write Table ─────────────────────────────────────────────────
const handleWriteTableRequest = (tableData: ParsedTable) => {
  // User clicked "Write to Sheet" on a message bubble
  setPendingTableData(tableData);
  setWriteTableTarget(selectionInfo?.address?.split(':')[0] ?? 'A1');
  setWriteTableError(null);
  setWriteTableSuccess(null);
  // Focus the target input after render
  setTimeout(() => writeTableInputRef.current?.focus(), 50);
};

const handleWriteTableConfirm = async () => {
  if (!pendingTableData) return;
  const target = writeTableTarget.trim() || 'A1';

  setWriteTableLoading(true);
  setWriteTableError(null);
  setWriteTableSuccess(null);

  // Build 2D array: headers row first, then data rows
  const data: (string | number | boolean | null)[][] = [
    pendingTableData.headers,
    ...pendingTableData.rows,
  ];

  try {
    const result = await writeRangeData(target, data);
    setWriteTableSuccess(`Written to ${result.address} (${result.rows} rows × ${result.cols} cols)`);
    setPendingTableData(null);
  } catch (e) {
    if (e instanceof WriteRangeError) {
      if (e.code === 'EMPTY_DATA') {
        setWriteTableError('No data to write.');
      } else if (e.code === 'DIMENSION_MISMATCH') {
        setWriteTableError('Rows have inconsistent column counts — cannot write.');
      } else {
        setWriteTableError('Write failed — check the target cell address and try again.');
      }
    } else {
      setWriteTableError('Write failed — check the target cell address and try again.');
    }
  } finally {
    setWriteTableLoading(false);
  }
};

const handleWriteTableCancel = () => {
  setPendingTableData(null);
  setWriteTableError(null);
  setWriteTableSuccess(null);
};

const handleWriteTableKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
  if (e.key === 'Enter') handleWriteTableConfirm();
  if (e.key === 'Escape') handleWriteTableCancel();
};
```

**Add the write-table inline prompt panel** — insert this block in the JSX after the sort/filter input panel and before the FORGE search bar. It only renders when `pendingTableData` is non-null:

```typescript
{/* ── Sprint 6: Write Table target cell prompt ── */}
{pendingTableData && (
  <div
    style={{
      padding: '8px 10px',
      borderBottom: '1px solid #2e3f54',
      background: '#111d2b',
      flexShrink: 0,
    }}
  >
    <div style={{ fontSize: '11px', color: '#8899aa', marginBottom: '4px' }}>
      Writing {pendingTableData.rows.length + 1} rows ×{' '}
      {pendingTableData.headers.length} cols — top-left cell:
    </div>
    <div style={{ display: 'flex', gap: '6px' }}>
      <input
        ref={writeTableInputRef}
        value={writeTableTarget}
        onChange={(e) => setWriteTableTarget(e.target.value)}
        onKeyDown={handleWriteTableKeyDown}
        placeholder="e.g. A1 or Sheet1!B3"
        style={{
          flex: 1,
          background: '#1a2332',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          color: '#e8edf3',
          padding: '5px 8px',
          fontSize: '12px',
          outline: 'none',
        }}
      />
      <button
        onClick={handleWriteTableConfirm}
        disabled={writeTableLoading}
        style={{
          background: '#d4af37',
          color: '#0f1720',
          border: 'none',
          borderRadius: '4px',
          padding: '5px 10px',
          fontSize: '12px',
          fontWeight: '600',
          cursor: 'pointer',
        }}
      >
        {writeTableLoading ? '…' : 'Write'}
      </button>
      <button
        onClick={handleWriteTableCancel}
        style={{
          background: '#2e3f54',
          color: '#e8edf3',
          border: 'none',
          borderRadius: '4px',
          padding: '5px 8px',
          fontSize: '12px',
          cursor: 'pointer',
        }}
      >
        ✕
      </button>
    </div>
    {writeTableError && (
      <div style={{ marginTop: '4px', fontSize: '11px', color: '#e07070' }}>
        {writeTableError}
      </div>
    )}
  </div>
)}

{/* ── Sprint 6: Write Table success toast ── */}
{writeTableSuccess && !pendingTableData && (
  <div
    style={{
      padding: '6px 10px',
      borderBottom: '1px solid #2e3f54',
      background: '#0f2a1a',
      color: '#6fcf97',
      fontSize: '11px',
      display: 'flex',
      justifyContent: 'space-between',
      alignItems: 'center',
      flexShrink: 0,
    }}
  >
    <span>✓ {writeTableSuccess}</span>
    <button
      onClick={() => setWriteTableSuccess(null)}
      style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
    >
      ✕
    </button>
  </div>
)}
```

**Thread `onWriteTable` through `MessageList` → `MessageBubble`.**

Find the `<MessageList>` render in the JSX:

```typescript
// BEFORE
<MessageList messages={messages} loading={loading} />

// AFTER
<MessageList
  messages={messages}
  loading={loading}
  onWriteTable={handleWriteTableRequest}
/>
```

This requires updating `MessageList` props — see the note below.

**`MessageList.tsx` — prop threading (minimal change):**

```typescript
// Add onWriteTable to MessageListProps
import type { ParsedTable } from '../services/suggestionParser';

interface MessageListProps {
  messages: Message[];
  loading: boolean;
  onWriteTable?: (tableData: ParsedTable) => void;  // ← add this
}

// Pass it through to each MessageBubble
{messages.map((msg, idx) => (
  <MessageBubble
    key={idx}
    message={msg}
    onWriteTable={msg.role === 'assistant' ? onWriteTable : undefined}
  />
))}
```

This is a small threading change to `MessageList.tsx`. It's the only change to that file.

**Do NOT change** `handleSend()`, `handleChart()`, `handlePivot()`, `handleFormat()`, `handleSortFilter()`, `handleClearHistory()`, or any existing state/logic.

---

### Task 5: `src/taskpane/components/WriteSuggestionsDialog.tsx`

One-line fix only. No other changes.

**Find `handleAcceptCurrent` (around line 86):**

```typescript
// BEFORE
if (msg.includes('dimension') || msg.includes('mismatch')) {

// AFTER
if (msg.includes('dimension') || msg.includes('mismatch') || msg.includes('does not fit')) {
```

That is the entire change to this file.

---

## Files Changed Summary

| File | Change type | Description |
|------|-------------|-------------|
| `src/taskpane/services/suggestionParser.ts` | Modify | Add `ParsedTable` interface; add `tableData` to `ParseResult`; add markdown + JSON parsers |
| `src/taskpane/hooks/useChat.ts` | Modify | Add `tableData` to `Message`; propagate from `parseSuggestions` result |
| `src/taskpane/components/MessageBubble.tsx` | Modify | Add `TableRenderer`; render table + "Write to Sheet ↓" button; strip raw pipe text when table present |
| `src/taskpane/components/MessageList.tsx` | Modify | Thread `onWriteTable` prop through to `MessageBubble` |
| `src/taskpane/components/ChatPanel.tsx` | Modify | Add write-table state; add handlers; add target-cell prompt panel + success toast |
| `src/taskpane/components/WriteSuggestionsDialog.tsx` | 1-line fix | Add `|| msg.includes('does not fit')` to `handleAcceptCurrent` |

**No new files. No new npm packages. 6 files total.**

---

## UX Flow — Exact Sequence

```
1. User: "Give me a Q1–Q4 revenue table by region"

2. FAIT responds with markdown table in response:
   | Region | Q1 | Q2 | Q3 | Q4 |
   |--------|----|----|----|----|
   | North  | 12 | 15 | 18 | 21 |

3. parseSuggestions() detects markdown table → tableData = { headers: [...], rows: [[...]] }

4. useChat sets message.tableData on the assistant Message

5. MessageBubble renders:
   - Any pre-table text (e.g. "Here is the data you requested:")
   - Styled HTML table (dark theme, gold headers, zebra rows)
   - "↓ Write to Sheet" button below the table

6. User clicks "↓ Write to Sheet"
   → onWriteTable callback fires up to ChatPanel
   → pendingTableData set
   → writeTableTarget pre-filled with active selection top-left cell (or "A1")
   → Target cell input panel appears above context indicator

7. User sees: "Writing 3 rows × 5 cols — top-left cell: [A1     ] [Write] [✕]"
   User changes "A1" to "C3" if desired → presses Enter or clicks Write

8. handleWriteTableConfirm():
   - Builds 2D array: [headers, ...rows] (headers row included)
   - Calls writeRangeData("C3", data)
   - On success: green toast "✓ Written to Sheet1!C3:G6 (3 rows × 5 cols)"
   - pendingTableData cleared, prompt panel hidden

9. On error: red message below input field
   - "Write failed — check the target cell address and try again."
   - User can retry with different target or ✕ to cancel
```

---

## Error Handling Matrix

| Scenario | Behavior |
|----------|----------|
| `writeRangeData` succeeds | Green toast with written address and dimensions |
| `writeRangeData` throws `EMPTY_DATA` | "No data to write." in red below input |
| `writeRangeData` throws `DIMENSION_MISMATCH` | "Rows have inconsistent column counts — cannot write." |
| `writeRangeData` throws `EXCEL_ERROR` | "Write failed — check the target cell address and try again." |
| User presses Escape in target input | Cancel — `pendingTableData` cleared, prompt hidden |
| Malformed markdown table (no separator row) | Parser skips silently — no table rendered, raw text shown |
| FAIT returns `table_data` JSON AND markdown table | JSON block wins (`tableData` set first, markdown parser skips) |
| `onWriteTable` clicked while another write is in progress | Button is not disabled — concurrent writes possible but unlikely (user sees only one pending table at a time) |

---

## Default Target Cell Logic

The target cell input is pre-populated with the **top-left cell of the currently selected range** (extracted from `selectionInfo.address`):

```typescript
setWriteTableTarget(selectionInfo?.address?.split(':')[0] ?? 'A1');
```

`selectionInfo.address` is already tracked in `ChatPanel` as part of the existing selection polling. `"Sheet1!A1:D5".split(':')[0]` → `"Sheet1!A1"` — a valid address for `writeRangeData`. If no selection, falls back to `"A1"` on the active worksheet.

---

## Acceptance Criteria

1. **Markdown table detection:** FAIT response containing a `|col|col|` markdown table causes `parseSuggestions()` to populate `tableData` with correct `headers` and `rows`
2. **JSON table detection:** FAIT response containing a `` ```json { "table_data": { ... } } ``` `` block populates `tableData` identically
3. **HTML table renders** in the message bubble: gold headers, zebra rows, right-aligned numbers
4. **"↓ Write to Sheet" button** appears below the rendered table (not for user messages, not during streaming)
5. **Clicking the button** opens the target-cell input panel pre-filled with the current selection top-left cell (or "A1")
6. **Enter/Write button** calls `writeRangeData` with the specified target cell
7. **Success:** green toast with address and dimensions appears; prompt panel closes
8. **Error:** red message below input; prompt stays open for retry
9. **Escape** cancels the prompt without writing
10. **`WriteSuggestionsDialog.handleAcceptCurrent`** error detection includes `'does not fit'`
11. **All Sprint 1–5 features unchanged:** chat, context injection, suggestions write-back, chart, pivot, CF, sort/filter, FORGE search, error scanner, session persistence, slash commands

---

## Constraints for CC

- Touch only the 6 files listed above (5 primary + `MessageList.tsx` for prop threading)
- Do NOT rewrite `suggestionParser.ts` — only ADD the new interfaces and parsers
- Do NOT change `simpleMarkdown()` in `MessageBubble.tsx`
- Do NOT change `writeRangeData()` or `applySuggestions()` in `excelWriter.ts`
- Do NOT change `useChat.ts` send/streaming logic — only add the `tableData` field propagation
- Do NOT add any new npm packages
- `ParsedTable` must be exported from `suggestionParser.ts` (other files import it)
- Headers row is included when writing to sheet: data array = `[headers, ...rows]`

---

## Clint Review Priorities

```
⚠️  HIGH: Verify markdown table regex in parser handles both:
          (a) tables with no leading/trailing pipe   Col1  |  Col2
          (b) tables with leading/trailing pipe      | Col1 | Col2 |
          The parseRow() function strips leading/trailing | — confirm this works for both forms.

⚠️  HIGH: Confirm writeRangeData receives [headers, ...rows] (not just rows).
          The table spec is headers + data. Missing the headers row means FAIT's column
          names are lost. This is in handleWriteTableConfirm — verify the data array build.

⚠️  HIGH: Verify getResizedRange(rows-1, cols-1) in writeRangeData is correct for the
          full array including headers (rows = data.length, not tableData.rows.length).
          data.length = tableData.rows.length + 1 (the +1 is the headers row).

⚠️  MEDIUM: Confirm ParsedTable is exported from suggestionParser.ts, not just defined.
            Both useChat.ts and ChatPanel.tsx import it by type.

⚠️  MEDIUM: Confirm WriteSuggestionsDialog handleAcceptCurrent fix is present:
            || msg.includes('does not fit')  — check it's in handleAcceptCurrent (line ~86),
            not just handleAcceptAll (line ~56). Both should have the same three conditions.

⚠️  LOW: Confirm success toast disappears when user sends a new message (or at least
         can be manually dismissed). The toast has a ✕ button — verify it works.

⚠️  LOW: Verify numeric coercion in markdown table parser — "1,234" should coerce to 1234.
         The regex strips commas before Number() conversion.
```

---

_Spec by Reed Richards | Sprint 6 is 6 files, all small changes. writeRangeData() already exists — this is purely the wire-up._
