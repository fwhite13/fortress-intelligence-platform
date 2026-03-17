# CC Brief: WI824 — FfE S8: Named Range Registration

You are implementing Sprint 8 of the FAIT for Excel add-in. This adds named range registration:
after writing data to a cell range, FAIT offers to name it; named ranges can be referenced
by name in future prompts; Settings panel gains a Named Ranges management section.

**Working directory: /home/fredw/projects/fait-for-excel/**
**1 new file + 4 modified. No new npm packages.**
**Do NOT touch any other files. Do NOT touch ~/projects/fip/ or any other repo.**

---

## Files to Create/Modify

1. **NEW** `src/taskpane/services/namedRangeStorage.ts`
2. **MODIFY** `src/taskpane/services/excelWriter.ts`
3. **MODIFY** `src/taskpane/components/ChatPanel.tsx`
4. **MODIFY** `src/taskpane/components/SettingsPanel.tsx`
5. **MODIFY** `src/taskpane/services/contextFormatter.ts`

---

## Task 1 — CREATE `src/taskpane/services/namedRangeStorage.ts`

This is a pure XML read/write service. NO Excel API calls, NO `Excel.run()`.
Only uses `Office.context.document.customXmlParts` (Office.js Common API).

Create this file with exactly this content:

```typescript
/* global Office */

export interface FaitNamedRange {
  name: string;     // e.g. "FAIT_output_20260316_143022"
  address: string;  // absolute address, e.g. "Sheet1!$A$1:$D$11"
  created: string;  // ISO 8601 date string
}

const NAMESPACE = 'https://fait.dev.fortressam.ai/excel-addin/named-ranges';

/** Generate a name like FAIT_output_20260316_143022 */
export function generateFaitName(slug: string): string {
  const now = new Date();
  const date = now.toISOString().slice(0, 10).replace(/-/g, '');
  const time = now.toTimeString().slice(0, 8).replace(/:/g, '');
  const safeslug = slug
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_|_$/g, '')
    .slice(0, 20) || 'output';
  return `FAIT_${safeslug}_${date}_${time}`;
}

/**
 * Convert an A1-style address to absolute $-prefixed form.
 * "Sheet1!A1:D11" → "$Sheet1!$A$1:$D$11" (strip sheet prefix and add $ to cells)
 * IMPORTANT: This returns ONLY the cell part with $ signs — NOT the = prefix.
 * The = prefix is added by the caller in excelWriter.ts.
 *
 * Handles multi-letter columns: AA10 → $AA$10, BC20 → $BC$20, XFD1048576 → $XFD$1048576
 */
export function toAbsoluteReference(address: string): string {
  // Strip existing sheet prefix (Sheet1! part)
  const cellPart = address.includes('!') ? address.split('!').pop()! : address;
  // Replace each cell ref: A1 → $A$1, AA10 → $AA$10
  // Regex: ([A-Z]+) captures one or more uppercase letters (multi-letter columns)
  //        (\d+)   captures one or more digits (row number)
  // Replacement: $$$1$$$2 — '$$' is literal $, '$1' is column capture, '$$' is literal $, '$2' is row capture
  return cellPart.replace(/([A-Z]+)(\d+)/g, '$$$1$$$2');
}

/**
 * Convert an absolute-reference address back to plain A1 notation.
 * "Sheet1!$A$1:$D$11" → "Sheet1!A1:D11"
 * Used when calling worksheet.getRange() which doesn't need $ signs.
 */
export function toA1Address(absAddress: string): string {
  return absAddress.replace(/\$([A-Z])/g, '$1').replace(/\$(\d)/g, '$1');
}

/** Load all FAIT named ranges from the workbook's custom XML store. */
export async function loadNamedRanges(): Promise<FaitNamedRange[]> {
  return new Promise((resolve) => {
    Office.context.document.customXmlParts.getByNamespaceAsync(NAMESPACE, (result) => {
      if (
        result.status !== Office.AsyncResultStatus.Succeeded ||
        !result.value ||
        result.value.length === 0
      ) {
        resolve([]);
        return;
      }
      result.value[0].getXmlAsync((xmlResult) => {
        if (xmlResult.status !== Office.AsyncResultStatus.Succeeded) {
          resolve([]);
          return;
        }
        try {
          const parser = new DOMParser();
          const doc = parser.parseFromString(xmlResult.value, 'text/xml');
          const nodes = doc.getElementsByTagName('range');
          const ranges: FaitNamedRange[] = [];
          for (let i = 0; i < nodes.length; i++) {
            const node = nodes[i];
            const name = node.getAttribute('name') ?? '';
            const address = node.getAttribute('address') ?? '';
            const created = node.getAttribute('created') ?? '';
            if (name && address) {
              ranges.push({ name, address, created });
            }
          }
          resolve(ranges);
        } catch {
          resolve([]);
        }
      });
    });
  });
}

/** Persist the full list of named ranges to the workbook's custom XML store. */
async function saveNamedRanges(ranges: FaitNamedRange[]): Promise<void> {
  return new Promise((resolve) => {
    const xml =
      `<faitNamedRanges xmlns="${NAMESPACE}">` +
      ranges
        .map(
          (r) =>
            `<range name="${escapeXml(r.name)}" address="${escapeXml(r.address)}" created="${escapeXml(r.created)}" />`
        )
        .join('') +
      `</faitNamedRanges>`;

    Office.context.document.customXmlParts.getByNamespaceAsync(NAMESPACE, (existing) => {
      const doWrite = () => {
        Office.context.document.customXmlParts.addAsync(xml, () => resolve());
      };
      if (existing.value && existing.value.length > 0) {
        existing.value[0].deleteAsync(doWrite);
      } else {
        doWrite();
      }
    });
  });
}

/** Add a named range to the registry. */
export async function addNamedRange(range: FaitNamedRange): Promise<void> {
  const existing = await loadNamedRanges();
  // Deduplicate by name (replace if same name exists)
  const filtered = existing.filter((r) => r.name !== range.name);
  await saveNamedRanges([...filtered, range]);
}

/** Remove a named range from the registry by name. */
export async function removeNamedRange(name: string): Promise<void> {
  const existing = await loadNamedRanges();
  await saveNamedRanges(existing.filter((r) => r.name !== name));
}

/** Update the name of a registry entry (rename). */
export async function renameNamedRange(oldName: string, newName: string): Promise<void> {
  const existing = await loadNamedRanges();
  const updated = existing.map((r) =>
    r.name === oldName ? { ...r, name: newName } : r
  );
  await saveNamedRanges(updated);
}

/**
 * Sync registry against live workbook names — remove entries whose Excel names were deleted.
 * GUARD: Only syncs if liveNames array is non-empty. If Excel.run() returned empty (possible
 * failure), we do NOT sync to avoid wiping the registry with a false empty list.
 */
export async function syncRegistry(liveNames: string[]): Promise<void> {
  // Guard: never sync with empty liveNames — could be an Excel.run() failure
  if (liveNames.length === 0) return;
  const existing = await loadNamedRanges();
  const live = new Set(liveNames.map((n) => n.toLowerCase()));
  const valid = existing.filter((r) => live.has(r.name.toLowerCase()));
  if (valid.length !== existing.length) {
    await saveNamedRanges(valid);
  }
}

function escapeXml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/"/g, '&quot;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}
```

---

## Task 2 — MODIFY `src/taskpane/services/excelWriter.ts`

ADD the following code at the END of the file (after the existing `WriteTableError` class).
Do NOT change any existing code.

```typescript
// ── Sprint 8: Named Range operations ──────────────────────────────────────

export class NamedRangeError extends Error {
  constructor(
    message: string,
    public readonly code: 'DUPLICATE_NAME' | 'INVALID_NAME' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'NamedRangeError';
  }
}

/**
 * Create a workbook-scoped named range pointing to the given address.
 *
 * @param name      Name for the range (e.g. "FAIT_output_20260316_143022").
 * @param address   Address as returned by writeRangeData (e.g. "Sheet1!A1:D11").
 *                  Automatically converted to absolute format ("=Sheet1!$A$1:$D$11").
 * @param comment   Optional comment to attach to the named range.
 * @throws NamedRangeError with .code "DUPLICATE_NAME" | "INVALID_NAME" | "EXCEL_ERROR"
 */
export async function createNamedRange(
  name: string,
  address: string,
  comment?: string
): Promise<void> {
  // Convert address to absolute reference: "Sheet1!A1:D11" → "=Sheet1!$A$1:$D$11"
  // Step 1: make columns and rows absolute by prepending $
  // Regex ([A-Z]+)(\d+) captures multi-letter columns (AA, BC, XFD) and row numbers
  const absAddr = address.replace(/([A-Z]+)(\d+)/g, '$$$1$$$2');
  // Step 2: prepend the required = prefix
  const formula = `=${absAddr}`;

  return Excel.run(async (ctx: any) => {
    // CRITICAL: Check for duplicate BEFORE calling names.add()
    // names.add() on a duplicate throws a runtime error that's hard to distinguish
    const existing = ctx.workbook.names.getItemOrNullObject(name);
    existing.load('isNullObject');
    await ctx.sync();

    if (!existing.isNullObject) {
      throw new NamedRangeError(
        `Name "${name}" already exists in this workbook`,
        'DUPLICATE_NAME'
      );
    }

    // Now safe to add
    ctx.workbook.names.add(name, formula);

    if (comment) {
      // Re-fetch the item to set comment after creation
      const item = ctx.workbook.names.getItem(name);
      item.comment = comment;
    }

    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof NamedRangeError) throw e;
    // Excel throws "InvalidArgument" if name contains invalid chars
    const msg: string = e?.message ?? '';
    if (msg.includes('InvalidArgument') || msg.includes('invalid') || msg.includes('name')) {
      throw new NamedRangeError(
        `Invalid range name "${name}" — names cannot contain spaces, start with a digit, or duplicate existing names`,
        'INVALID_NAME'
      );
    }
    throw new NamedRangeError(e?.message ?? 'Named range creation failed', 'EXCEL_ERROR');
  });
}

/**
 * Delete a workbook named range by name. Silent if the name doesn't exist.
 */
export async function deleteNamedRange(name: string): Promise<void> {
  return Excel.run(async (ctx: any) => {
    const item = ctx.workbook.names.getItemOrNullObject(name);
    item.load('isNullObject');
    await ctx.sync();
    if (!item.isNullObject) {
      item.delete();
      await ctx.sync();
    }
  }).catch((e: any) => {
    // Non-fatal — if deletion fails, log and continue
    console.warn('FAIT: deleteNamedRange failed:', e?.message);
  });
}

/**
 * Rename a workbook named range. Deletes the old name and recreates with new name + same address.
 * item.value is the formula string (e.g. "=Sheet1!$A$1:$D$11") — loaded before delete.
 */
export async function renameWorkbookNamedRange(
  oldName: string,
  newName: string
): Promise<void> {
  return Excel.run(async (ctx: any) => {
    const item = ctx.workbook.names.getItemOrNullObject(oldName);
    item.load(['isNullObject', 'value']);
    await ctx.sync();

    if (item.isNullObject) {
      throw new NamedRangeError(`Name "${oldName}" not found`, 'EXCEL_ERROR');
    }

    // item.value is the formula string (e.g. "=Sheet1!$A$1:$D$11")
    const formula = item.value as string;
    item.delete();
    ctx.workbook.names.add(newName, formula);
    await ctx.sync();
  }).catch((e: any) => {
    if (e instanceof NamedRangeError) throw e;
    throw new NamedRangeError(e?.message ?? 'Rename failed', 'EXCEL_ERROR');
  });
}

/**
 * List all workbook named range names (for registry sync validation).
 * Returns empty array on Excel.run() failure — caller must guard against this.
 */
export async function listWorkbookNamedRanges(): Promise<string[]> {
  return Excel.run(async (ctx: any) => {
    const names = ctx.workbook.names;
    names.load('items/name');
    await ctx.sync();
    return (names.items as any[]).map((item: any) => item.name as string);
  }).catch(() => [] as string[]);
}
```

---

## Task 3 — MODIFY `src/taskpane/components/ChatPanel.tsx`

### 3a — Add imports at the top of the file

After the existing import block (after the last `import` line), add:

```typescript
import {
  createNamedRange,
  deleteNamedRange,
  renameWorkbookNamedRange,
  listWorkbookNamedRanges,
  NamedRangeError,
} from '../services/excelWriter';
import {
  loadNamedRanges,
  addNamedRange,
  removeNamedRange,
  renameNamedRange,
  syncRegistry,
  generateFaitName,
  toAbsoluteReference,
  toA1Address,
} from '../services/namedRangeStorage';
import type { FaitNamedRange } from '../services/namedRangeStorage';
```

### 3b — Add Sprint 8 state

After the Sprint 6 write-table state block (after `const writeTableInputRef = useRef<HTMLInputElement>(null);`), add:

```typescript
  // ── Sprint 8: Named Range state ───────────────────────────────────────────
  const [pendingNameAddress, setPendingNameAddress] = useState<string | null>(null);
  const [namedRangeName, setNamedRangeName] = useState('');
  const [namedRangeLoading, setNamedRangeLoading] = useState(false);
  const [namedRangeError, setNamedRangeError] = useState<string | null>(null);
  const [namedRanges, setNamedRanges] = useState<FaitNamedRange[]>([]);
  const namedRangeInputRef = useRef<HTMLInputElement>(null);
```

### 3c — Load named ranges on mount

Add a new useEffect after the existing mount/save useEffects (after the `saveConversation` useEffect block):

```typescript
  // ── Sprint 8: Load named ranges from workbook custom XML on mount ─────────
  useEffect(() => {
    loadNamedRanges().then(setNamedRanges).catch(() => null);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
```

### 3d — Update handleSend() to resolve FAIT named range references

Replace the existing `handleSend` function:

```typescript
  const handleSend = async (text: string) => {
    let context: string | undefined;

    // Sprint 8: Resolve FAIT named range references in the user's message
    const faitRefMatches = text.match(/\bFAIT_\w+/g) ?? [];
    const resolvedRanges: string[] = [];

    if (faitRefMatches.length > 0 && namedRanges.length > 0) {
      for (const ref of faitRefMatches) {
        const entry = namedRanges.find(
          (r) => r.name.toLowerCase() === ref.toLowerCase()
        );
        if (entry) {
          try {
            const rangeCtx = await Excel.run(async (ctx: any) => {
              // Use workbook.names to resolve — correct for cross-sheet references
              const namedItem = ctx.workbook.names.getItemOrNullObject(entry.name);
              namedItem.load('isNullObject');
              await ctx.sync();

              if (namedItem.isNullObject) {
                throw new Error('NAME_NOT_FOUND');
              }

              const range = namedItem.getRange();
              range.load(['values', 'formulas', 'address', 'rowCount', 'columnCount']);
              await ctx.sync();

              return {
                address: range.address as string,
                rows: range.rowCount as number,
                cols: range.columnCount as number,
                values: range.values as unknown[][],
                formulas: range.formulas as string[][],
              };
            });
            const rangeContext = formatContext(rangeCtx, entry.name);
            resolvedRanges.push(`[Named Range: ${ref}]\n${rangeContext}`);
          } catch {
            resolvedRanges.push(`[Named Range: ${ref} — could not read; range may have been moved or deleted]`);
          }
        } else {
          resolvedRanges.push(`[Named Range: ${ref} — not found in FAIT registry]`);
        }
      }
    }

    if (includeSelection) {
      try {
        const ctx = await getSelectedRange();
        if (ctx.rows > 0 && ctx.cols > 0) {
          // Check if the current selection matches a named range
          const matchingRange = namedRanges.find(
            (r) => toA1Address(r.address) === ctx.address || r.address === ctx.address
          );
          context = formatContext(ctx, matchingRange?.name);
          setSelectionInfo({
            address: ctx.address,
            rows: ctx.rows,
            cols: ctx.cols,
            tableName: ctx.tableInfo?.name ?? null,
          });
        }
      } catch {
        // Non-fatal: proceed without context
      }
    }

    // Prepend resolved named range contexts to the regular context
    if (resolvedRanges.length > 0) {
      const rangeBlock = resolvedRanges.join('\n\n');
      context = context ? `${rangeBlock}\n\n${context}` : rangeBlock;
    }

    await send(text, context);
  };
```

NOTE: The `handleSend` replacement references `Excel` — add `/* global Excel */` comment if not already at top of the component file. Check if it's already there from other usage in the file.

### 3e — Add Sprint 8 handlers after `handleWriteTableKeyDown`

After the `handleWriteTableKeyDown` function, add:

```typescript
  // ── Sprint 8: Named Range handlers ───────────────────────────────────────
  const handleNameRangeRequest = (address: string) => {
    const suggestion = generateFaitName('output');
    setPendingNameAddress(address);
    setNamedRangeName(suggestion);
    setNamedRangeError(null);
    setTimeout(() => namedRangeInputRef.current?.focus(), 50);
  };

  const handleNameRangeConfirm = async () => {
    if (!pendingNameAddress) return;
    const name = namedRangeName.trim();
    if (!name) {
      setNamedRangeError('Please enter a name.');
      return;
    }

    setNamedRangeLoading(true);
    setNamedRangeError(null);

    try {
      await createNamedRange(name, pendingNameAddress, 'Created by FAIT');
      const entry: FaitNamedRange = {
        name,
        address: toAbsoluteReference(pendingNameAddress),
        created: new Date().toISOString(),
      };
      await addNamedRange(entry);
      setNamedRanges((prev) => [...prev.filter((r) => r.name !== name), entry]);
      setPendingNameAddress(null);
    } catch (e) {
      if (e instanceof NamedRangeError) {
        if (e.code === 'DUPLICATE_NAME') {
          setNamedRangeError(`"${name}" already exists — choose a different name.`);
        } else if (e.code === 'INVALID_NAME') {
          setNamedRangeError('Invalid name — no spaces, cannot start with a digit.');
        } else {
          setNamedRangeError('Failed to create named range.');
        }
      } else {
        setNamedRangeError('Failed to create named range.');
      }
    } finally {
      setNamedRangeLoading(false);
    }
  };

  const handleNameRangeSkip = () => {
    setPendingNameAddress(null);
    setNamedRangeError(null);
  };

  const handleNameRangeKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') void handleNameRangeConfirm();
    if (e.key === 'Escape') handleNameRangeSkip();
  };
```

### 3f — Trigger the name prompt in handleWriteTableConfirm() — CELL ADDRESS BRANCH ONLY

In `handleWriteTableConfirm()`, find the cell-address branch (the `else` block). It has:
```typescript
        setWriteTableSuccess(successMsg);
        setPendingTableData(null);
```

Change it to:
```typescript
        setWriteTableSuccess(successMsg);
        setPendingTableData(null);
        handleNameRangeRequest(result.address);  // Sprint 8: offer to name the range
```

IMPORTANT: This ONLY goes in the cell-address branch (the `else` block). Do NOT add it to the `isTableTarget` branch (writeToTable — tables already have names).

### 3g — Update SettingsPanel render to pass named range props

Find the JSX section that renders the SettingsPanel. It currently looks like:
```typescript
            onClick={onOpenSettings}
```
(SettingsPanel is opened via `onOpenSettings` callback — it's rendered in the parent component App.tsx, not directly in ChatPanel. Look for where SettingsPanel is rendered.)

WAIT — re-reading the code: ChatPanel receives `onOpenSettings` as a prop and calls it. SettingsPanel is rendered in `App.tsx` or `taskpane.tsx`. Let me check...

Actually, looking at the grep results: `onOpenSettings` is a prop on ChatPanel. The SettingsPanel is rendered elsewhere (App level). Skip 3g — the namedRanges props will be passed from wherever SettingsPanel is rendered. Instead, just note that SettingsPanel needs the props (Task 4 adds them to the interface).

REVISED 3g: In the ChatPanel JSX (the `return` block), find the write-table success toast rendering. After it (or near it), add the Sprint 8 name range prompt:

Find this pattern in the JSX (the write table success toast area around line 800-900):
```typescript
          {writeTableSuccess && (
```

After the closing `)}` of the writeTableSuccess block, add:

```typescript
          {/* ── Sprint 8: Name range prompt (shown after successful writeRangeData()) ── */}
          {pendingNameAddress && (
            <div
              style={{
                padding: '8px 10px',
                borderBottom: '1px solid #2e3f54',
                background: '#111d2b',
                flexShrink: 0,
              }}
            >
              <div style={{ fontSize: '11px', color: '#8899aa', marginBottom: '4px' }}>
                Name this range for future reference? (optional)
              </div>
              <div style={{ display: 'flex', gap: '6px' }}>
                <input
                  ref={namedRangeInputRef}
                  value={namedRangeName}
                  onChange={(e) => setNamedRangeName(e.target.value)}
                  onKeyDown={handleNameRangeKeyDown}
                  placeholder="e.g. FAIT_revenue_q1"
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
                  onClick={() => void handleNameRangeConfirm()}
                  disabled={namedRangeLoading}
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
                  {namedRangeLoading ? '…' : 'Save'}
                </button>
                <button
                  onClick={handleNameRangeSkip}
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
                  Skip
                </button>
              </div>
              {namedRangeError && (
                <div style={{ marginTop: '4px', fontSize: '11px', color: '#e07070' }}>
                  {namedRangeError}
                </div>
              )}
            </div>
          )}
```

### 3h — Export namedRanges state for use by parent (App-level SettingsPanel)

Since SettingsPanel is not rendered in ChatPanel, we need to expose the named range state and handlers via a callback/prop. Add to `ChatPanelProps`:

```typescript
interface ChatPanelProps {
  apiKey: string;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
  onOpenSettings: () => void;
  // Sprint 8: Named ranges (passed up to App for SettingsPanel)
  onNamedRangesChange?: (ranges: FaitNamedRange[]) => void;
}
```

And in the component destructuring, add `onNamedRangesChange` (optional).

Then add a useEffect to call `onNamedRangesChange` whenever `namedRanges` changes:

```typescript
  useEffect(() => {
    onNamedRangesChange?.(namedRanges);
  }, [namedRanges]); // eslint-disable-line react-hooks/exhaustive-deps
```

ALTERNATIVELY — if this is too complex and App.tsx is not in scope — simply do NOT add the callback and instead load namedRanges directly in SettingsPanel from customXmlParts on mount. This is simpler and keeps SettingsPanel self-contained. Go with this simpler approach.

---

## Task 4 — MODIFY `src/taskpane/components/SettingsPanel.tsx`

### 4a — Add import for FaitNamedRange type

After the existing imports, add:

```typescript
import { loadNamedRanges, syncRegistry, removeNamedRange, renameNamedRange, toA1Address } from '../services/namedRangeStorage';
import { deleteNamedRange, renameWorkbookNamedRange, listWorkbookNamedRanges } from '../services/excelWriter';
import type { FaitNamedRange } from '../services/namedRangeStorage';
```

### 4b — Update SettingsPanelProps interface

Change:
```typescript
interface SettingsPanelProps {
  onClose: () => void;
  apiKey: string;
  onKeyChange: (key: string) => void;
}
```

To:
```typescript
interface SettingsPanelProps {
  onClose: () => void;
  apiKey: string;
  onKeyChange: (key: string) => void;
  // Sprint 8: Named ranges — optional props, panel loads its own data if not provided
  namedRanges?: FaitNamedRange[];
  onDeleteNamedRange?: (name: string) => Promise<void>;
  onRenameNamedRange?: (oldName: string, newName: string) => Promise<void>;
}
```

### 4c — Update component destructuring

Change:
```typescript
const SettingsPanel: React.FC<SettingsPanelProps> = ({ onClose, apiKey, onKeyChange }) => {
```

To:
```typescript
const SettingsPanel: React.FC<SettingsPanelProps> = ({
  onClose,
  apiKey,
  onKeyChange,
  namedRanges: namedRangesProp,
  onDeleteNamedRange,
  onRenameNamedRange,
}) => {
```

### 4d — Add Sprint 8 state inside SettingsPanel

After the existing state declarations (after `const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');`), add:

```typescript
  // ── Sprint 8: Named Ranges section ────────────────────────────────────────
  const [localNamedRanges, setLocalNamedRanges] = useState<FaitNamedRange[]>(namedRangesProp ?? []);
  const [renamingName, setRenamingName] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [renameLoading, setRenameLoading] = useState(false);
  const [rangeActionError, setRangeActionError] = useState<string | null>(null);

  // Derive which list to show: prop-provided or locally loaded
  const displayedRanges = namedRangesProp ?? localNamedRanges;
```

### 4e — Load and sync named ranges on mount

In the existing `useEffect` that loads persisted settings, or as a new separate `useEffect`, add loading named ranges:

```typescript
  // ── Sprint 8: Load named ranges and sync with workbook on panel open ──────
  useEffect(() => {
    loadNamedRanges().then(async (ranges) => {
      // Only set local state if parent didn't provide ranges
      if (!namedRangesProp) {
        setLocalNamedRanges(ranges);
      }
      // Validate registry against live workbook names (background sync)
      const liveNames = await listWorkbookNamedRanges();
      // syncRegistry guard: only sync if liveNames is non-empty
      if (liveNames.length > 0) {
        await syncRegistry(liveNames);
        // Reload after sync to get cleaned-up list
        const cleaned = await loadNamedRanges();
        if (!namedRangesProp) {
          setLocalNamedRanges(cleaned);
        }
      }
    }).catch(() => null);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
```

### 4f — Add delete and rename handlers

After the existing state declarations, add:

```typescript
  const handleDeleteRange = async (name: string) => {
    setRangeActionError(null);
    try {
      if (onDeleteNamedRange) {
        await onDeleteNamedRange(name);
      } else {
        await deleteNamedRange(name);
        await removeNamedRange(name);
        setLocalNamedRanges((prev) => prev.filter((r) => r.name !== name));
      }
    } catch {
      setRangeActionError('Delete failed.');
    }
  };

  const handleRenameRange = async (oldName: string, newName: string) => {
    setRenameLoading(true);
    setRangeActionError(null);
    try {
      if (onRenameNamedRange) {
        await onRenameNamedRange(oldName, newName);
      } else {
        await renameWorkbookNamedRange(oldName, newName);
        await renameNamedRange(oldName, newName);
        setLocalNamedRanges((prev) =>
          prev.map((r) => (r.name === oldName ? { ...r, name: newName } : r))
        );
      }
      setRenamingName(null);
    } catch {
      setRangeActionError('Rename failed — name may already exist or be invalid.');
    } finally {
      setRenameLoading(false);
    }
  };
```

### 4g — Add Named Ranges JSX section

Find the end of the Settings panel JSX — the closing section before `</div>` at the bottom (after the Model section). Add the Named Ranges section there.

In the SettingsPanel's JSX, find the Model section ending (around line 405-428). After it, before the closing `</div>` of the scrollable content area, add:

```typescript
        {/* ── Section: Named Ranges ─────────────────────────────────────── */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Named Ranges</div>
          <p style={labelStyle}>
            Ranges FAIT has written to this workbook. Reference them by name in your prompts.
          </p>

          {rangeActionError && (
            <div style={{ color: '#e07070', fontSize: '11px', padding: '4px 0' }}>
              {rangeActionError}
            </div>
          )}

          {displayedRanges.length === 0 ? (
            <p style={{ ...labelStyle, color: '#445566' }}>
              No named ranges yet. After writing a table to the sheet, FAIT will offer to name the range.
            </p>
          ) : (
            displayedRanges.map((r, idx) => (
              <div
                key={r.name}
                style={{
                  ...toggleRowStyle,
                  borderBottom: idx < displayedRanges.length - 1 ? '1px solid #2e3f54' : 'none',
                  flexDirection: 'column' as const,
                  alignItems: 'flex-start' as const,
                  gap: '6px',
                  padding: '8px 0',
                }}
              >
                {renamingName === r.name ? (
                  <div style={{ width: '100%', display: 'flex', gap: '6px' }}>
                    <input
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void handleRenameRange(r.name, renameValue.trim());
                        if (e.key === 'Escape') setRenamingName(null);
                      }}
                      autoFocus
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
                      disabled={renameLoading}
                      onClick={() => void handleRenameRange(r.name, renameValue.trim())}
                      style={{
                        background: '#d4af37',
                        color: '#0f1720',
                        border: 'none',
                        borderRadius: '4px',
                        padding: '4px 8px',
                        fontSize: '11px',
                        fontWeight: '600',
                        cursor: 'pointer',
                      }}
                    >
                      {renameLoading ? '…' : 'OK'}
                    </button>
                    <button
                      onClick={() => setRenamingName(null)}
                      style={{
                        background: '#2e3f54',
                        color: '#e8edf3',
                        border: 'none',
                        borderRadius: '4px',
                        padding: '4px 6px',
                        fontSize: '11px',
                        cursor: 'pointer',
                      }}
                    >
                      ✕
                    </button>
                  </div>
                ) : (
                  <div style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <div>
                      <div style={{ color: '#e8edf3', fontSize: '12px', fontWeight: '600', fontFamily: 'monospace' }}>
                        {r.name}
                      </div>
                      <div style={{ color: '#556677', fontSize: '11px', marginTop: '1px' }}>
                        {r.address}
                      </div>
                    </div>
                    <div style={{ display: 'flex', gap: '4px', flexShrink: 0 }}>
                      <button
                        title="Rename"
                        onClick={() => {
                          setRenamingName(r.name);
                          setRenameValue(r.name);
                          setRangeActionError(null);
                        }}
                        style={{
                          background: '#1e2d3e',
                          color: '#8899aa',
                          border: '1px solid #2e3f54',
                          borderRadius: '4px',
                          padding: '3px 6px',
                          fontSize: '11px',
                          cursor: 'pointer',
                        }}
                      >
                        ✏
                      </button>
                      <button
                        title="Delete"
                        onClick={() => void handleDeleteRange(r.name)}
                        style={{
                          background: '#2d1515',
                          color: '#e07070',
                          border: '1px solid #4a1515',
                          borderRadius: '4px',
                          padding: '3px 6px',
                          fontSize: '11px',
                          cursor: 'pointer',
                        }}
                      >
                        🗑
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))
          )}
        </div>
```

---

## Task 5 — MODIFY `src/taskpane/services/contextFormatter.ts`

### 5a — Update formatContext signature to accept optional namedRangeName

Change:
```typescript
export function formatContext(ctx: SpreadsheetContext): string {
```

To:
```typescript
export function formatContext(ctx: SpreadsheetContext, namedRangeName?: string): string {
```

### 5b — Add Named range line after the Sheet range header

Find:
```typescript
  let out = `[SPREADSHEET CONTEXT]\nSheet range: ${ctx.address} | ${ctx.rows} rows × ${ctx.cols} cols\n`;
```

Change to:
```typescript
  let out = `[SPREADSHEET CONTEXT]\nSheet range: ${ctx.address} | ${ctx.rows} rows × ${ctx.cols} cols\n`;

  if (namedRangeName) {
    out += `Named range: ${namedRangeName}\n`;
  }
```

---

## Critical Implementation Reminders

1. **`toAbsoluteReference()` in namedRangeStorage.ts** returns ONLY the cell part with $ signs (no `=` prefix). The `=` is added in `createNamedRange()` in excelWriter.ts.

2. **In `handleNameRangeConfirm()`** in ChatPanel.tsx, when building the `entry.address` for the registry:
   ```typescript
   address: toAbsoluteReference(pendingNameAddress),
   ```
   This stores e.g. `Sheet1!$A$1:$D$11` (without `=`) in the registry — correct.

3. **Duplicate check order in `createNamedRange()`**:
   - Load existing → sync → check isNullObject → THEN add → sync
   - Never call `names.add()` before verifying the name doesn't exist

4. **Reference resolution in handleSend()** uses `namedItem.getRange()` — NOT `worksheet.getRange(address)`. This is critical for cross-sheet correctness.

5. **Name prompt only fires in cell-address branch** of `handleWriteTableConfirm()`. The `isTableTarget` branch must NOT call `handleNameRangeRequest()`.

6. **syncRegistry() guard**: The function in namedRangeStorage.ts already guards against empty arrays. Additionally, the SettingsPanel useEffect should only call syncRegistry when liveNames.length > 0.

7. **`/* global Excel */`** comment is already present in ChatPanel.tsx from other Excel operations. Verify before adding.

---

## Build Verification

After implementing, run:
```bash
cd /home/fredw/projects/fait-for-excel
npm run build
```

The build must complete with 0 errors. TypeScript warnings about unused vars are acceptable but errors are not.
