# CC Brief: WI821 — Wire writeRangeData to UI (Write Table to Sheet)

You are implementing Sprint 6 of FAIT for Excel. This wires `writeRangeData()` (already exists in `excelWriter.ts`) to the UI via a table detection + render + write flow.

**Working directory:** `/home/fredw/projects/fait-for-excel/`
**Touch ONLY these 6 files. No other files. No new npm packages.**

---

## CRITICAL RULES

1. Do NOT edit `excelWriter.ts` — `writeRangeData` and `WriteRangeError` are already correct
2. Do NOT call `parseSuggestions` twice — destructure `{ displayText, suggestions, tableData }` in ONE call
3. `ParsedTable` MUST be exported (other files import it by type)
4. `data = [headers, ...rows]` — headers row is FIRST in the write array
5. Do NOT add any new npm packages
6. Do NOT change `simpleMarkdown()` in MessageBubble.tsx

---

## FILE 1: `src/taskpane/services/suggestionParser.ts`

### Current state
The file has `ParseResult` interface and `parseSuggestions()` function that parses json blocks for suggestions, chart_spec, pivot_spec, cf_spec, sort_filter_spec. It has NO table parsing at all.

### Changes needed

**A) Add `ParsedTable` interface BEFORE `ParseResult`:**

```typescript
export interface ParsedTable {
  headers: string[];
  rows: (string | number | boolean | null)[][];
}
```

**B) Add `tableData: ParsedTable | null` to `ParseResult` interface:**

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

**C) In `parseSuggestions()` function:**

1. Initialize at the TOP of the function (after `let sortFilterSpec: SortFilterSpec | null = null;`):
```typescript
let tableData: ParsedTable | null = null;
```

2. After the `sort_filter_spec` block and BEFORE the cleanup line (`displayText = displayText.replace(/\n{3,}/g, '\n\n').trim();`), add these two parsers:

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

  // ── markdown table detection ──────────────────────────────────────────────
  // Only run if no table_data JSON block was found
  if (!tableData) {
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
          }
        }
      } catch {
        // Malformed table — ignore
      }
    }
  }
```

3. Update the `return` statement to include `tableData`:
```typescript
  return { displayText, suggestions, chartSpec, pivotSpec, cfSpec, sortFilterSpec, tableData };
```

Do NOT modify any existing parser blocks.

---

## FILE 2: `src/taskpane/hooks/useChat.ts`

### Current state
`Message` interface has `role`, `content`, `streaming?`. The `send()` function calls `parseSuggestions` once:
```typescript
const { displayText, suggestions } = parseSuggestions(rawText);
```
Then sets the message with `{ role: 'assistant', content: displayText, streaming: false }`.

### Changes needed

**A) Add import for `ParsedTable`** — combine with existing import:
```typescript
// BEFORE
import { parseSuggestions } from '../services/suggestionParser';

// AFTER
import { parseSuggestions, type ParsedTable } from '../services/suggestionParser';
```

**B) Add `tableData` to `Message` interface:**
```typescript
export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;  // ← NEW
}
```

**C) In the `send()` function, find this block (AFTER streaming completes):**
```typescript
      // Parse suggestions out of the raw response (new fields chartSpec/pivotSpec/cfSpec are unused here)
      const { displayText, suggestions } = parseSuggestions(rawText);

      // Finalise the assistant message (remove streaming flag)
      setMessages((prev) => {
        const next = [...prev];
        next[assistantIndex] = { role: 'assistant', content: displayText, streaming: false };
        return next;
      });
```

Replace with (ONE call to parseSuggestions, destructure all needed fields):
```typescript
      // Parse suggestions/tableData out of the raw response
      const { displayText, suggestions, tableData } = parseSuggestions(rawText);

      // Finalise the assistant message (remove streaming flag)
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

Do NOT change anything else in this file.

---

## FILE 3: `src/taskpane/components/MessageBubble.tsx`

### Current state
Has `MessageBubbleProps` with `message` and `streaming?`. Has `simpleMarkdown()` helper. Renders role label + bubble with `dangerouslySetInnerHTML` + streaming cursor + CSS animations.

### Changes needed

**A) Add import for `ParsedTable`** at the top:
```typescript
import type { ParsedTable } from '../services/suggestionParser';
```

**B) Update `MessageBubbleProps`:**
```typescript
interface MessageBubbleProps {
  message: Message;
  streaming?: boolean;
  onWriteTable?: (tableData: ParsedTable) => void;  // ← NEW
}
```

**C) Add `TableRenderer` sub-component** BEFORE the `MessageBubble` component (after `simpleMarkdown`):

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

**D) Replace the `MessageBubble` component entirely** with this version:

```typescript
const MessageBubble: React.FC<MessageBubbleProps> = ({ message, streaming, onWriteTable }) => {
  const isUser = message.role === 'user';
  const isStreaming = streaming ?? message.streaming ?? false;
  const hasTable = !isUser && !isStreaming && message.tableData != null;

  // For assistant messages with a parsed table, strip raw markdown table text from display
  let displayContent = message.content;
  if (hasTable && message.tableData) {
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
        {/* Text content — suppress when table present and no remaining text */}
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

Keep `simpleMarkdown()` unchanged. Keep `export default MessageBubble;` at the bottom.

---

## FILE 4: `src/taskpane/components/MessageList.tsx`

### Current state
Has `MessageListProps` with `messages` and `loading`. Renders `MessageBubble` for each message without any callbacks.

### Changes needed (minimal — prop threading only)

**A) Add import for `ParsedTable`:**
```typescript
import type { ParsedTable } from '../services/suggestionParser';
```

**B) Update `MessageListProps`:**
```typescript
interface MessageListProps {
  messages: Message[];
  loading: boolean;
  onWriteTable?: (tableData: ParsedTable) => void;  // ← NEW
}
```

**C) Update component destructuring:**
```typescript
const MessageList: React.FC<MessageListProps> = ({ messages, loading, onWriteTable }) => {
```

**D) Update the MessageBubble rendering in the map:**
```typescript
      {messages.map((msg, idx) => (
        <MessageBubble
          key={idx}
          message={msg}
          onWriteTable={msg.role === 'assistant' ? onWriteTable : undefined}
        />
      ))}
```

No other changes to this file.

---

## FILE 5: `src/taskpane/components/ChatPanel.tsx`

### Current state
Already imports `writeRangeData, WriteRangeError` from excelWriter (line 4) but never calls them.
Has state for CF, sort/filter, FORGE, chart, pivot.
Has `selectionInfo` state with `address`, `rows`, `cols`.
The `<MessageList>` render is: `<MessageList messages={messages} loading={loading} />`

### Changes needed

**A) Add import for `ParsedTable`** — add after the existing type imports:
```typescript
import type { ParsedTable } from '../services/suggestionParser';
```

**B) Add state for write-table flow** — add after the `sortFilterLoading` state block (keep Sprint 5 comment block, add Sprint 6 block after it):
```typescript
  // ── Sprint 6: Write Table state ───────────────────────────────────────────
  const [pendingTableData, setPendingTableData] = useState<ParsedTable | null>(null);
  const [writeTableTarget, setWriteTableTarget] = useState('');
  const [writeTableLoading, setWriteTableLoading] = useState(false);
  const [writeTableError, setWriteTableError] = useState<string | null>(null);
  const [writeTableSuccess, setWriteTableSuccess] = useState<string | null>(null);
  const writeTableInputRef = useRef<HTMLInputElement>(null);
```

**C) Add write-table handlers** — add AFTER `handleClearHistory` and BEFORE the `const modelLabel = ...` line:

```typescript
  // ── Sprint 6: Write Table ─────────────────────────────────────────────────
  const handleWriteTableRequest = (tableData: ParsedTable) => {
    setPendingTableData(tableData);
    setWriteTableTarget(selectionInfo?.address?.split(':')[0] ?? 'A1');
    setWriteTableError(null);
    setWriteTableSuccess(null);
    setTimeout(() => writeTableInputRef.current?.focus(), 50);
  };

  const handleWriteTableConfirm = async () => {
    if (!pendingTableData) return;
    const target = writeTableTarget.trim() || 'A1';

    setWriteTableLoading(true);
    setWriteTableError(null);
    setWriteTableSuccess(null);

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

**D) Add write-table JSX panels** — in the JSX, insert these TWO blocks. They go AFTER the sort/filter input panel block (`{showSortFilterInput && ( ... )}`) and BEFORE the `{/* FORGE search bar */}` block:

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

**E) Update the `<MessageList>` render** in the JSX (in the scrollable content area):
```typescript
            {/* Message list */}
            <MessageList
              messages={messages}
              loading={loading}
              onWriteTable={handleWriteTableRequest}
            />
```

No other changes to this file.

---

## FILE 6: `src/taskpane/components/WriteSuggestionsDialog.tsx`

### Current state
`handleAcceptCurrent` at line ~86 has:
```typescript
      if (msg.includes('dimension') || msg.includes('mismatch')) {
```

`handleAcceptAll` at line ~56 already has the correct 3-condition check.

### One-line fix only

Find in `handleAcceptCurrent`:
```typescript
      if (msg.includes('dimension') || msg.includes('mismatch')) {
```

Replace with:
```typescript
      if (msg.includes('dimension') || msg.includes('mismatch') || msg.includes('does not fit')) {
```

That is the ONLY change to this file. Do not touch anything else.

---

## Summary

- 6 files touched, 0 new packages, 0 new files
- `ParsedTable` exported from suggestionParser.ts
- `tableData` flows: parseSuggestions → useChat.Message → MessageBubble (renders as HTML table) → onWriteTable callback → ChatPanel (handles write)
- `writeRangeData` called with `[headers, ...rows]` — headers row first
- Enter confirms, Escape cancels the write prompt
- Success toast shows address + dimensions
- Error messages per WriteRangeError.code
