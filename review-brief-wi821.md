# Review Brief: WI821 — Wire writeRangeData to UI (Write Table to Sheet)

You are a senior code reviewer (Hawkeye). Review the WI821 implementation that wires `writeRangeData()` to the UI by adding a markdown table parser, HTML table renderer, "Write to Sheet" button in message bubbles, and a target-cell prompt panel in ChatPanel.

## Files Changed (exactly 6, confirmed by git diff HEAD~1 --name-only)

1. `src/taskpane/services/suggestionParser.ts`
2. `src/taskpane/hooks/useChat.ts`
3. `src/taskpane/components/MessageBubble.tsx`
4. `src/taskpane/components/MessageList.tsx`
5. `src/taskpane/components/ChatPanel.tsx`
6. `src/taskpane/components/WriteSuggestionsDialog.tsx`

**excelWriter.ts is NOT changed (confirmed).**

## Priority Check Results (pre-analyzed)

### HIGH: Markdown table regex handles both pipe forms
The `parseRow()` in `suggestionParser.ts`:
```typescript
const parseRow = (line: string): string[] =>
  line
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((c) => c.trim());
```
- `| Col1 | Col2 |` → strip `^|` → ` Col1 | Col2 ` → strip `|$` → ` Col1 | Col2` → split('|') → [' Col1 ', ' Col2'] → trim → ['Col1', 'Col2'] ✅
- `Col1 | Col2` → no leading/trailing pipe → strip no-ops → split('|') → ['Col1 ', ' Col2'] → trim → ['Col1', 'Col2'] ✅
- VERDICT: ✅ Handles both pipe forms correctly.

### HIGH: writeRangeData receives [headers, ...rows]
In `ChatPanel.handleWriteTableConfirm`:
```typescript
const data: (string | number | boolean | null)[][] = [
  pendingTableData.headers,
  ...pendingTableData.rows,
];
```
- Headers row is row 1. ✅
- VERDICT: ✅ Correct construction.

### HIGH: getResizedRange uses data.length (incl. header row)
`writeRangeData` is called with `data` which is `[headers, ...rows]`. So `data.length = rows.length + 1`. Since `writeRangeData` uses `data.length` internally (not `data.length - 1`), the full data including the header row is passed. VERDICT: ✅ Correct.

### MEDIUM: ParsedTable is exported
```typescript
export interface ParsedTable {
  headers: string[];
  rows: (string | number | boolean | null)[][];
}
```
VERDICT: ✅ `export` keyword present.

### MEDIUM: WriteSuggestionsDialog.handleAcceptCurrent has the fix
`handleAcceptCurrent` (around line ~86, in review mode):
```typescript
if (msg.includes('dimension') || msg.includes('mismatch') || msg.includes('does not fit')) {
```
`handleAcceptAll` (around line ~56) also has all 3 conditions. Both match. VERDICT: ✅ Correct.

### LOW: Success toast dismissal
```tsx
<button
  onClick={() => setWriteTableSuccess(null)}
  style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
>
  ✕
</button>
```
VERDICT: ✅ `onClick={() => setWriteTableSuccess(null)}` present.

### LOW: Numeric coercion in markdown table parser
```typescript
const n = Number(cell.replace(/,/g, ''));
return cell !== '' && !isNaN(n) && isFinite(n) ? n : cell;
```
- `"1,234"` → replace commas → `"1234"` → `Number("1234")` = `1234` → coerced to number ✅
- VERDICT: ✅ Comma stripping before Number() is correct.

## Additional Checks

### parseSuggestions called once in useChat.ts
In `useChat.ts`, `parseSuggestions` is imported and called exactly once:
```typescript
const { displayText, suggestions, tableData } = parseSuggestions(rawText);
```
All three fields destructured in one call. VERDICT: ✅

### simpleMarkdown() unchanged
The `simpleMarkdown` function in `MessageBubble.tsx`:
```typescript
function simpleMarkdown(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/`([^`]+)`/g, '<code style="background:#0f1720;padding:1px 4px;border-radius:3px;font-size:12px;">$1</code>')
    .replace(/\n/g, '<br />');
}
```
This is unchanged (same pattern as prior sprints). VERDICT: ✅

### writeRangeData() unchanged — confirmed, excelWriter.ts not in git diff. ✅

### No new npm packages
package.json not in git diff. VERDICT: ✅

### Only 6 specified files changed — confirmed by git diff. ✅

### onWriteTable only on assistant messages
In `MessageList.tsx`:
```tsx
<MessageBubble
  key={idx}
  message={msg}
  onWriteTable={msg.role === 'assistant' ? onWriteTable : undefined}
/>
```
VERDICT: ✅ Only assistant messages get onWriteTable.

### hasTable check includes !isStreaming
In `MessageBubble.tsx`:
```typescript
const hasTable = !isUser && !isStreaming && message.tableData != null;
```
VERDICT: ✅ `!isStreaming` is included.

### Raw pipe text stripped from displayContent
In `MessageBubble.tsx`:
```typescript
let displayContent = message.content;
if (hasTable && message.tableData) {
  displayContent = message.content
    .replace(/\|.+\|\s*\n\|[-| :]+\|\s*\n(?:\|.+\|\s*\n?)+/g, '')
    .trim();
}
```
VERDICT: ✅ Markdown table pipe text stripped before simpleMarkdown() call.

### ParsedTable type consistent across all 6 files
- `suggestionParser.ts`: `export interface ParsedTable { headers: string[]; rows: (string | number | boolean | null)[][] }`
- `ParseResult`: `tableData: ParsedTable | null` ✅
- `useChat.ts`: imports `type ParsedTable` from suggestionParser; `Message.tableData?: ParsedTable | null` ✅
- `MessageBubble.tsx`: props `onWriteTable?: (tableData: ParsedTable) => void` ✅
- `MessageList.tsx`: props `onWriteTable?: (tableData: ParsedTable) => void` ✅
- `ChatPanel.tsx`: `handleWriteTableRequest = (tableData: ParsedTable) => void` ✅
- data array: `[pendingTableData.headers, ...pendingTableData.rows]` ✅

## Issues to Analyze

Please analyze the following potential concern:

1. **Markdown table regex in `suggestionParser.ts`**: The regex used to detect markdown tables is:
   ```typescript
   const mdTableRegex = /(\|.+\|\s*\n\|[-| :]+\|\s*\n(?:\|.+\|\s*\n?)+)/g;
   ```
   This requires lines to start with `|`. If a table uses the form `Col1 | Col2` (no leading pipe), this regex would NOT match at all. The `parseRow()` function handles both forms, but the *detection regex* only matches tables where lines start with `|`. Is this a concern? Does `parseRow` ever get called for non-pipe-start tables?

2. **`pendingTableData.headers` type**: `ParsedTable.headers` is `string[]`, but `data` is typed as `(string | number | boolean | null)[][]`. When constructing `[pendingTableData.headers, ...pendingTableData.rows]`, TypeScript must accept `string[]` as an element of `(string | number | boolean | null)[][]`. Since `string` is a subset of `string | number | boolean | null`, this is valid. Confirm this is fine.

3. **The prompt panel for Write Table target**: It shows up when `pendingTableData` is set. After a successful write, `setPendingTableData(null)` is called, which would hide the input panel and show the success toast (since `writeTableSuccess && !pendingTableData`). This sequence is correct. Confirm.

4. **Edge case: `writeTableTarget` default**: When the write table request is made, the target is set to `selectionInfo?.address?.split(':')[0] ?? 'A1'`. This uses the first cell of the selected range as the default, which is ergonomic. Confirm this is correct behavior.

5. **WriteSuggestionsDialog fix specificity**: The fix was supposed to be in `handleAcceptCurrent` (review mode, ~line 86), NOT `handleAcceptAll` (which was already correct). Both now have the same 3-condition check. Verify that `handleAcceptCurrent` is the one with the fix and that it was previously missing `|| msg.includes('does not fit')`.

Please analyze these files and provide your assessment. Focus on correctness, type safety, and whether the implementation matches the spec. Provide a brief verdict: PASS, NEEDS-CHANGES, or FAIL with reasoning.
