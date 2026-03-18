# FfE Sprint 8 Spec — Named Range Registration

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-16  
**Status:** Ready for Implementation  
**Prerequisite:** Sprint 6 (Write Table) must be landed — `writeRangeData()` exists and returns `{ address, rows, cols }`  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)

---

## Pre-Read: What the Source Shows

### Storage landscape

Two storage mechanisms exist:

| Mechanism | What stores it | Scope | Used for |
|-----------|---------------|-------|---------|
| `OfficeRuntime.storage` (via `settings.ts`) | Key-value strings | Device-local | API key, model, KB toggles, project ID |
| `Office.context.document.customXmlParts` (via `sessionStorage.ts`) | XML blobs | Workbook-scoped (travels with the file) | Conversation history |

**Named range registry needs workbook-scoped storage.** If a user creates a named range in workbook A and opens workbook B, the registry should show only ranges from B — not A. This rules out `OfficeRuntime.storage`.

**Decision:** Store the FAIT named range registry in `customXmlParts` (same as conversation history, different namespace). The registry travels with the workbook — exactly correct behavior.

**Alternative considered:** `workbook.names` itself as the registry. Why not just iterate all names and filter by `FAIT_` prefix? Because `workbook.names` is an Excel API call (async, `Excel.run()`) and settings panel would need to go through `Excel.run()` to list them. We want the settings panel to load the list from local storage first (fast, no round-trip), then optionally validate against live workbook names. **Decision: use both — custom XML as the fast registry, `workbook.names` as the source of truth. On settings panel open, validate the registry against live workbook names and drop any that have been manually deleted.**

### Current write flow (post-S6)

```typescript
// handleWriteTableConfirm() — current
const result = await writeRangeData(target, data);
setWriteTableSuccess(`Written to ${result.address} (${result.rows} rows × ${result.cols} cols)`);
setPendingTableData(null);
```

After S8: the success path asks the user for a name (optional), creates the named range, updates the registry, and shows the name in the success message.

### Named range API — ExcelApi

`workbook.names.add(name, reference)` — ExcelApi **1.4** ✅ (baseline 1.13)  
`workbook.names.getItemOrNullObject(name)` — ExcelApi **1.4** ✅  
`namedItem.getRange()` — ExcelApi **1.4** ✅  
`namedItem.delete()` — ExcelApi **1.4** ✅  
`namedItem.name` — ExcelApi **1.1** ✅  
`namedItem.comment` — ExcelApi **1.4** ✅ (used to tag FAIT ranges)  
`workbook.names.load('count')` — collection iteration — **1.1** ✅  

`workbook.names.add(name, reference)` takes a string `reference` in the format `"=Sheet1!$A$1:$D$11"` (with `=` prefix, absolute addresses). The address returned by `writeRangeData` (`result.address`) is `"Sheet1!A1:D11"` — needs conversion to absolute reference format.

`addFormulaLocal` — NOT needed. `names.add(name, reference)` uses R1C1 or A1 notation with the `=` prefix. Stick with `add(name, "=" + absoluteAddress)`.

**Name constraints (Excel):**
- Max 255 characters
- Cannot start with a digit or look like a cell address (`A1`, `R2C3`)
- Cannot contain spaces (use underscores)
- Case-insensitive
- Cannot duplicate an existing name in the workbook
- Cannot use reserved words (C, R, etc.)

**FAIT naming convention: `FAIT_[slug]_[timestamp]`**  
- `FAIT_` prefix — makes FAIT ranges easy to find/filter  
- `slug` — sanitized user-provided label or auto-generated from content (e.g. `output`, `table`, `analysis`)  
- `timestamp` — `YYYYMMDD_HHMMSS` format — ensures uniqueness  

Examples: `FAIT_output_20260316_143022`, `FAIT_revenue_table_20260316_143022`

---

## What Sprint 8 Delivers

1. **After every successful write**, FAIT offers to name the range (optional inline prompt, pre-filled with a suggested name)
2. **If the user accepts**, FAIT creates a workbook named range via `workbook.names.add()` and stores it in the registry (custom XML)
3. **In follow-up prompts**, if the user types a FAIT range name (e.g. "update FAIT_output_20260316"), `handleSend()` intercepts it, resolves the range, reads the values, and injects context — no manual re-selection needed
4. **Settings panel** gains a "Named Ranges" section listing FAIT-created ranges with rename and delete buttons
5. **Context formatter** includes a named range hint when the selected range matches a FAIT range

---

## Design Decisions

### Decision 1: Opt-in naming, not auto-name on every write

Auto-naming every write would clutter the workbook's name list. The user controls whether to name a range. After a successful write, a prompt appears below the success message:

```
✓ Written to Sheet1!A1:D11 (4 rows × 4 cols)
  Name this range? [FAIT_output_20260316_143022    ] [Save] [Skip]
```

If the user presses Skip or closes without naming, no named range is created. Name it default to the auto-generated suggestion — user can edit before saving.

### Decision 2: Registry format (custom XML)

```xml
<faitNamedRanges xmlns="https://fait.dev.fortressam.ai/excel-addin/named-ranges">
  <range name="FAIT_output_20260316_143022" address="Sheet1!$A$1:$D$11" created="2026-03-16T14:30:22" />
  <range name="FAIT_revenue_20260316_150012" address="Sheet1!$B$3:$E$8" created="2026-03-16T15:00:12" />
</faitNamedRanges>
```

Separate namespace from session storage (`sessionStorage.ts`). New service: `namedRangeStorage.ts`.

### Decision 3: Reference resolution in prompts — pattern match, not NLP

When `handleSend()` receives user text that contains a token matching `FAIT_[word]+` (the naming prefix), resolve it before sending to FAIT API:

1. Find all `FAIT_*` tokens in the user's message
2. For each, look up in registry; if found, call `getSelectedRange()` but targeting that named range address via `getRange(address)` instead of `getSelectedRange()`
3. Inject the resolved range context alongside the regular selection context

Keep it simple: regex match `\bFAIT_\w+\b` in the user text. No NLP, no fuzzy match. If the name isn't in the registry or can't be resolved (deleted from workbook), inject a warning note into the context instead.

### Decision 4: Settings panel integration — no full reload on open

The settings panel currently fetches KB list and project list from the FAIT API. Adding a named range section does NOT require an API call — the data is local (custom XML + `workbook.names`). Load the registry from custom XML on panel open; validate against live workbook names in a background `Excel.run()` call. If validation finds a name that no longer exists in the workbook (user deleted it manually), remove it from the registry silently.

### Decision 5: Rename via Excel + registry update

Renaming a FAIT named range requires:
1. Delete the old `workbook.names` entry via `namedItem.delete()`
2. Re-add with the new name pointing to the same address via `workbook.names.add(newName, address)`
3. Update the registry XML

This is 3 operations — all synchronous within one `Excel.run()`. Simple enough.

### Decision 6: `writeToTable()` naming (S7 future)

When writing to an Excel Table via `writeToTable()` (Sprint 7), the Table already has a name. No need to create a separate named range — the Table name IS the stable reference. The name prompt does NOT appear after `writeToTable()` successes — only after `writeRangeData()` successes (plain range writes).

---

## Data Model

### Registry entry (in-memory)

```typescript
// namedRangeStorage.ts
export interface FaitNamedRange {
  name: string;        // e.g. "FAIT_output_20260316_143022"
  address: string;     // absolute address, e.g. "Sheet1!$A$1:$D$11"
  created: string;     // ISO 8601, e.g. "2026-03-16T14:30:22"
}
```

### New state in `ChatPanel.tsx`

```typescript
// Sprint 8: Named range post-write prompt
const [pendingNamedRangeAddress, setPendingNamedRangeAddress] = useState<string | null>(null);
const [namedRangeSuggestion, setNamedRangeSuggestion] = useState('');
const [namedRangeLoading, setNamedRangeLoading] = useState(false);
const [namedRangeError, setNamedRangeError] = useState<string | null>(null);
const namedRangeInputRef = useRef<HTMLInputElement>(null);
```

### New props added to `SettingsPanel`

```typescript
interface SettingsPanelProps {
  onClose: () => void;
  apiKey: string;
  onKeyChange: (key: string) => void;
  // Sprint 8: Named ranges
  onNamedRangesChange?: () => void;  // optional callback — trigger context refresh on change
}
```

---

## Parallelization Map

```
Single sequential CC session. 5 files total.

  Task 1: namedRangeStorage.ts   NEW FILE — registry CRUD (custom XML), name generation,
                                   address → absolute-reference conversion

  Task 2: excelWriter.ts         Add createNamedRange() function; extends existing file

  Task 3: ChatPanel.tsx          Name-range prompt after successful write; reference resolution
                                   in handleSend(); pass namedRangeList down to SettingsPanel

  Task 4: SettingsPanel.tsx      Add "Named Ranges" section; list, rename, delete

  Task 5: contextFormatter.ts    Include named range name in context when selection matches
                                   a FAIT range (minor addition)
```

---

## File-Level Spec

### Task 1 (NEW): `src/taskpane/services/namedRangeStorage.ts`

This is the registry service. It reads and writes the custom XML store for FAIT named ranges. This is a **new file** — does not exist in the codebase.

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

/** Convert an A1-style address to absolute $-prefixed form for workbook.names.add().
 *  "Sheet1!A1:D11" → "=Sheet1!$A$1:$D$11"
 *  "Sheet1!A1"     → "=Sheet1!$A$1"
 */
export function toAbsoluteReference(address: string): string {
  // Replace column letter(s) and row number(s) with $-prefixed versions
  const withAbs = address.replace(/([A-Z]+)(\d+)/g, '$$$$1$$$2');
  return `=${withAbs}`;
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

/** Sync registry against live workbook names — remove entries whose Excel names were deleted. */
export async function syncRegistry(liveNames: string[]): Promise<void> {
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

### Task 2: `src/taskpane/services/excelWriter.ts`

Add `createNamedRange()` function. No changes to existing functions.

```typescript
/**
 * Create a workbook-scoped named range pointing to the given address.
 *
 * @param name      Name for the range (e.g. "FAIT_output_20260316_143022").
 *                  Must satisfy Excel name constraints: no spaces, no leading digit,
 *                  max 255 chars, must not duplicate an existing name.
 * @param address   The address string as returned by writeRangeData (e.g. "Sheet1!A1:D11").
 *                  Automatically converted to absolute format ("=Sheet1!$A$1:$D$11").
 * @param comment   Optional comment to attach to the named range (e.g. "Created by FAIT").
 * @throws NamedRangeError with .code "DUPLICATE_NAME" | "INVALID_NAME" | "EXCEL_ERROR"
 */
export async function createNamedRange(
  name: string,
  address: string,
  comment?: string
): Promise<void> {
  // Convert to absolute reference format
  const ref = address.replace(/([A-Z]+)(\d+)/g, '$$$1$$$2');
  const formula = `=${ref}`;

  return Excel.run(async (ctx: any) => {
    // Check for duplicate name
    const existing = ctx.workbook.names.getItemOrNullObject(name);
    existing.load('isNullObject');
    await ctx.sync();

    if (!existing.isNullObject) {
      throw new NamedRangeError(
        `Name "${name}" already exists in this workbook`,
        'DUPLICATE_NAME'
      );
    }

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
 */
export async function listWorkbookNamedRanges(): Promise<string[]> {
  return Excel.run(async (ctx: any) => {
    const names = ctx.workbook.names;
    names.load('items/name');
    await ctx.sync();
    return (names.items as any[]).map((item: any) => item.name as string);
  }).catch(() => [] as string[]);
}

export class NamedRangeError extends Error {
  constructor(
    message: string,
    public readonly code: 'DUPLICATE_NAME' | 'INVALID_NAME' | 'EXCEL_ERROR'
  ) {
    super(message);
    this.name = 'NamedRangeError';
  }
}
```

**Do NOT change** any existing functions in `excelWriter.ts`.

---

### Task 3: `src/taskpane/components/ChatPanel.tsx`

Four targeted additions. No restructuring.

**Change 1: Add imports**

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
} from '../services/namedRangeStorage';
import type { FaitNamedRange } from '../services/namedRangeStorage';
```

**Change 2: Add Sprint 8 state**

After the Sprint 6 write-table state block, add:

```typescript
// ── Sprint 8: Named Range state ───────────────────────────────────────────
const [pendingNameAddress, setPendingNameAddress] = useState<string | null>(null);
const [namedRangeName, setNamedRangeName] = useState('');
const [namedRangeLoading, setNamedRangeLoading] = useState(false);
const [namedRangeError, setNamedRangeError] = useState<string | null>(null);
const [namedRanges, setNamedRanges] = useState<FaitNamedRange[]>([]);
const namedRangeInputRef = useRef<HTMLInputElement>(null);
```

**Change 3: Load named ranges on mount**

In the existing `useEffect` that runs on mount (the one that loads the API key / settings), add:

```typescript
// Load named ranges from workbook custom XML on mount
loadNamedRanges().then(setNamedRanges).catch(() => null);
```

Or as a separate `useEffect(() => { ... }, [])`.

**Change 4: Add Sprint 8 handlers**

After `handleWriteTableKeyDown`, add:

```typescript
// ── Sprint 8: Named Range handlers ───────────────────────────────────────
const handleNameRangeRequest = (address: string) => {
  // Called after a successful writeRangeData() — offer to name the range
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
      address: toAbsoluteReference(pendingNameAddress).slice(1), // strip leading '='
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
  if (e.key === 'Enter') handleNameRangeConfirm();
  if (e.key === 'Escape') handleNameRangeSkip();
};
```

**Change 5: Trigger the name prompt after successful `writeRangeData()`**

In `handleWriteTableConfirm()`, after `setWriteTableSuccess(...)` and `setPendingTableData(null)`, add:

```typescript
// After: setPendingTableData(null);
// ADD:
handleNameRangeRequest(result.address);
```

Full updated success block in the cell-address write branch:

```typescript
const result = await writeRangeData(target, data);
let successMsg = `Written to ${result.address} (${result.rows} rows × ${result.cols} cols)`;
if (result.warning) {
  successMsg += ` ⚠️ ${result.warning}`;
}
setWriteTableSuccess(successMsg);
setPendingTableData(null);
handleNameRangeRequest(result.address);  // ← Sprint 8
```

The name prompt appears below the success toast. Both are visible simultaneously — success toast is green at top, name prompt is grey below it.

**Change 6: Resolve FAIT named range references in `handleSend()`**

Update `handleSend()`:

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
          // Read the values at the stored address
          const rangeCtx = await Excel.run(async (ctx: any) => {
            const range = ctx.workbook.worksheets
              .getActiveWorksheet()
              .getRange(entry.address.replace(/^\$/, '').replace(/\$([A-Z])/g, '$1').replace(/\$(\d)/g, '$1'));
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
          const rangeContext = formatContext(rangeCtx);
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
        context = formatContext(ctx);
        setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
      }
    } catch {
      // Non-fatal
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

**Note on address stripping for `getRange()`:** The absolute reference stored in the registry (`Sheet1!$A$1:$D$11`) needs the `$` signs stripped for `worksheet.getRange()` calls. The inline strip `entry.address.replace(/\$([A-Z])/g, '$1').replace(/\$(\d)/g, '$1')` handles this. However, this is messy inline. Better: store both formats in `FaitNamedRange` or add a utility function `toA1Address(absAddress)` in `namedRangeStorage.ts`:

```typescript
// Add to namedRangeStorage.ts
export function toA1Address(absAddress: string): string {
  return absAddress.replace(/\$([A-Z])/g, '$1').replace(/\$(\d)/g, '$1');
}
```

Then in `handleSend()`:
```typescript
import { ..., toA1Address } from '../services/namedRangeStorage';
// ...
const range = ctx.workbook.worksheets.getActiveWorksheet().getRange(toA1Address(entry.address));
```

**Wait — named ranges are workbook-scoped, not worksheet-scoped.** `workbook.names.getItem(name).getRange()` is the correct way to read a named range, not `worksheet.getRange(address)`. Use this instead:

```typescript
// Better: use the workbook name itself to locate the range
const rangeCtx = await Excel.run(async (ctx: any) => {
  const namedItem = ctx.workbook.names.getItemOrNullObject(entry.name);
  namedItem.load('isNullObject');
  await ctx.sync();

  if (namedItem.isNullObject) {
    // Name was deleted from workbook — fall through to error
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
```

This is the correct pattern — uses the workbook name's own range, automatically resolves even if the sheet name has changed.

**Change 7: Pass `namedRanges` into `SettingsPanel`**

Find the `<SettingsPanel>` render in JSX. Update:

```typescript
// BEFORE
{showSettings && (
  <SettingsPanel
    onClose={() => setShowSettings(false)}
    apiKey={apiKey}
    onKeyChange={setApiKey}
  />
)}

// AFTER
{showSettings && (
  <SettingsPanel
    onClose={() => setShowSettings(false)}
    apiKey={apiKey}
    onKeyChange={setApiKey}
    namedRanges={namedRanges}
    onDeleteNamedRange={async (name) => {
      await deleteNamedRange(name);
      await removeNamedRange(name);
      setNamedRanges((prev) => prev.filter((r) => r.name !== name));
    }}
    onRenameNamedRange={async (oldName, newName) => {
      await renameWorkbookNamedRange(oldName, newName);
      await renameNamedRange(oldName, newName);
      setNamedRanges((prev) =>
        prev.map((r) => (r.name === oldName ? { ...r, name: newName } : r))
      );
    }}
  />
)}
```

**Change 8: Add name prompt panel to JSX**

Below the Sprint 6 success toast, add:

```typescript
{/* ── Sprint 8: Name range prompt (shown after successful write) ── */}
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
        onClick={handleNameRangeConfirm}
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

---

### Task 4: `src/taskpane/components/SettingsPanel.tsx`

Add "Named Ranges" section. Update `SettingsPanelProps`.

**Updated props:**

```typescript
import type { FaitNamedRange } from '../services/namedRangeStorage';

interface SettingsPanelProps {
  onClose: () => void;
  apiKey: string;
  onKeyChange: (key: string) => void;
  // Sprint 8
  namedRanges?: FaitNamedRange[];
  onDeleteNamedRange?: (name: string) => Promise<void>;
  onRenameNamedRange?: (oldName: string, newName: string) => Promise<void>;
}
```

**New state (inside `SettingsPanel` component):**

```typescript
// ── Sprint 8: Named Ranges section state ──────────────────────────────────
const [renamingName, setRenamingName] = useState<string | null>(null);  // which range is being renamed
const [renameValue, setRenameValue] = useState('');
const [renameLoading, setRenameLoading] = useState(false);
const [rangeActionError, setRangeActionError] = useState<string | null>(null);
```

**New section — add after the "Model" section, before the footer note:**

```typescript
{/* ── Section: Named Ranges ────────────────────────────── */}
{namedRanges && namedRanges.length > 0 && (
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

    {namedRanges.map((r, idx) => (
      <div
        key={r.name}
        style={{
          ...toggleRowStyle,
          borderBottom: idx < namedRanges.length - 1 ? '1px solid #2e3f54' : 'none',
          flexDirection: 'column',
          alignItems: 'flex-start',
          gap: '6px',
          padding: '8px 0',
        }}
      >
        {renamingName === r.name ? (
          /* Rename inline input */
          <div style={{ width: '100%', display: 'flex', gap: '6px' }}>
            <input
              value={renameValue}
              onChange={(e) => setRenameValue(e.target.value)}
              onKeyDown={async (e) => {
                if (e.key === 'Enter') {
                  setRenameLoading(true);
                  setRangeActionError(null);
                  try {
                    await onRenameNamedRange?.(r.name, renameValue.trim());
                    setRenamingName(null);
                  } catch {
                    setRangeActionError('Rename failed — name may already exist or be invalid.');
                  } finally {
                    setRenameLoading(false);
                  }
                }
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
              onClick={async () => {
                setRenameLoading(true);
                setRangeActionError(null);
                try {
                  await onRenameNamedRange?.(r.name, renameValue.trim());
                  setRenamingName(null);
                } catch {
                  setRangeActionError('Rename failed — name may already exist or be invalid.');
                } finally {
                  setRenameLoading(false);
                }
              }}
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
              style={{ background: '#2e3f54', color: '#e8edf3', border: 'none', borderRadius: '4px', padding: '4px 6px', fontSize: '11px', cursor: 'pointer' }}
            >
              ✕
            </button>
          </div>
        ) : (
          /* Normal display row */
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
                onClick={() => { setRenamingName(r.name); setRenameValue(r.name); setRangeActionError(null); }}
                style={{ background: '#1e2d3e', color: '#8899aa', border: '1px solid #2e3f54', borderRadius: '4px', padding: '3px 6px', fontSize: '11px', cursor: 'pointer' }}
              >
                ✏
              </button>
              <button
                title="Delete"
                onClick={async () => {
                  setRangeActionError(null);
                  try {
                    await onDeleteNamedRange?.(r.name);
                  } catch {
                    setRangeActionError('Delete failed.');
                  }
                }}
                style={{ background: '#2d1515', color: '#e07070', border: '1px solid #4a1515', borderRadius: '4px', padding: '3px 6px', fontSize: '11px', cursor: 'pointer' }}
              >
                🗑
              </button>
            </div>
          </div>
        )}
      </div>
    ))}
  </div>
)}

{(!namedRanges || namedRanges.length === 0) && (
  <div style={sectionStyle}>
    <div style={sectionHeadingStyle}>Named Ranges</div>
    <p style={{ ...labelStyle, color: '#445566' }}>
      No named ranges yet. After writing a table to the sheet, FAIT will offer to name the range.
    </p>
  </div>
)}
```

---

### Task 5: `src/taskpane/services/contextFormatter.ts`

Minor addition: include named range name in context when it's relevant.

`formatContext()` receives `SpreadsheetContext`. The context doesn't currently know about named ranges. We don't want to add a dependency on `namedRangeStorage.ts` to `contextFormatter.ts` (that would require loading the registry on every format call — too slow).

**Better approach:** Pass an optional `namedRangeName` string to `formatContext()` from the call site. The call site (`handleSend()`) already knows the named range list.

**Update `formatContext()` signature:**

```typescript
export function formatContext(ctx: SpreadsheetContext, namedRangeName?: string): string {
```

**Add to the output header block, after the `Sheet range:` line:**

```typescript
if (namedRangeName) {
  out += `Named range: ${namedRangeName}\n`;
}
```

Full updated header section:

```typescript
let out = `[SPREADSHEET CONTEXT]\nSheet range: ${ctx.address} | ${ctx.rows} rows × ${ctx.cols} cols\n`;

if (namedRangeName) {
  out += `Named range: ${namedRangeName}\n`;
}

if (ctx.tableInfo) {
  // ... Table-aware path (unchanged)
```

**Update `handleSend()` in ChatPanel to pass the name when the current selection matches a FAIT range:**

```typescript
// After getting ctx from getSelectedRange():
const matchingRange = namedRanges.find(
  (r) => toA1Address(r.address) === ctx.address ||
         r.address === ctx.address
);
context = formatContext(ctx, matchingRange?.name);
```

The `toA1Address` utility strips `$` signs for comparison.

**Do NOT change** the rest of `contextFormatter.ts`. `getCellAddr()` untouched.

---

## Files Changed Summary

| File | Change type | Description |
|------|-------------|-------------|
| `src/taskpane/services/namedRangeStorage.ts` | **NEW** | Registry CRUD via custom XML; `generateFaitName()`; `toAbsoluteReference()`; `toA1Address()` |
| `src/taskpane/services/excelWriter.ts` | Modify | Add `createNamedRange()`, `deleteNamedRange()`, `renameWorkbookNamedRange()`, `listWorkbookNamedRanges()`, `NamedRangeError` |
| `src/taskpane/components/ChatPanel.tsx` | Modify | Sprint 8 state; name-range prompt; `handleSend()` reference resolution; pass props to `SettingsPanel` |
| `src/taskpane/components/SettingsPanel.tsx` | Modify | Add Named Ranges section; rename/delete handlers |
| `src/taskpane/services/contextFormatter.ts` | Modify | Accept optional `namedRangeName` param; emit `Named range:` line in output |

**1 new file. 4 modified files. No new npm packages.**

---

## UX Flow — Exact Sequences

### Flow A: User writes a table, names the range

```
1. User sends "Give me Q1–Q4 revenue by region"
2. FAIT responds with markdown table
3. User clicks "↓ Write to Sheet" → types "C5" → clicks Write
4. writeRangeData("C5", data) succeeds → address: "Sheet1!C5:G9"
5. Success toast: "✓ Written to Sheet1!C5:G9 (5 rows × 5 cols)"
6. Below the toast: "Name this range for future reference? (optional)"
   Input pre-filled: "FAIT_output_20260316_143022"
   User changes to: "FAIT_revenue_q1" → presses Enter
7. createNamedRange("FAIT_revenue_q1", "Sheet1!C5:G9") creates workbook named range
8. addNamedRange() stores in custom XML registry
9. Prompt closes silently — no extra toast (success was already shown)
```

### Flow B: User references a named range in a follow-up

```
1. (Previous session or same session)
   namedRanges state: [{ name: "FAIT_revenue_q1", address: "Sheet1!$C$5:$G$9", ... }]
2. User types: "Update FAIT_revenue_q1 to include Q4 actuals"
3. handleSend() detects "FAIT_revenue_q1" via regex \bFAIT_\w+\b
4. Looks up in namedRanges → found
5. Excel.run() → workbook.names.getItemOrNullObject("FAIT_revenue_q1").getRange()
   → reads values, formulas, address, rowCount, columnCount
6. formatContext() called → produces context block for that range
7. Context injected BEFORE regular selection context:
   "[Named Range: FAIT_revenue_q1]\n[SPREADSHEET CONTEXT]\n..."
8. FAIT answers with awareness of FAIT_revenue_q1's content
```

### Flow C: User opens Settings → views and deletes a range

```
1. User opens ⚙ Settings
2. SettingsPanel receives namedRanges=[{name: "FAIT_revenue_q1", ...}] prop
3. "Named Ranges" section shows:
   [FAIT_revenue_q1]  [Sheet1!$C$5:$G$9]  [✏] [🗑]
4. User clicks 🗑
5. onDeleteNamedRange("FAIT_revenue_q1") called in ChatPanel
   → deleteNamedRange() removes from workbook.names
   → removeNamedRange() removes from custom XML registry
   → setNamedRanges updates state → Settings re-renders with empty list
```

### Flow D: Named range resolution fails (range deleted from workbook)

```
1. User has "FAIT_output_20260316" in the registry but manually deleted the
   named range from Excel's Name Manager
2. User types: "What's in FAIT_output_20260316?"
3. handleSend() finds the registry entry → tries workbook.names.getItemOrNullObject()
4. isNullObject = true → catch block fires
5. Injected into context: "[Named Range: FAIT_output_20260316 — not found; range may have been moved or deleted]"
6. FAIT answers: "I can see you referenced FAIT_output_20260316, but I couldn't access the range — it may have been deleted or renamed."
```

---

## Acceptance Criteria

1. **After successful `writeRangeData()`**, the name-range prompt appears below the success toast with an auto-generated name pre-filled
2. **User can accept, edit, or skip** the name prompt (Enter = save, Skip button = dismiss, Escape = dismiss)
3. **Named range is created** in the Excel workbook (`workbook.names.add()`) and stored in the custom XML registry
4. **`FAIT_*` tokens in user messages** are resolved: the range is read via `workbook.names.getItem(name).getRange()` and injected into the context block
5. **Settings panel** shows all FAIT named ranges with address, rename button, and delete button
6. **Rename** updates both the workbook name and the custom XML registry atomically (delete + re-add in one `Excel.run()`)
7. **Delete** removes from workbook names AND custom XML registry
8. **Context formatter** emits `Named range: FAIT_revenue_q1` when the selection matches a registry entry
9. **Graceful failure:** if a name was deleted from the workbook but remains in the registry, the reference resolution injects a "not found" message instead of throwing
10. **No prompt after `writeToTable()` successes** — Tables have their own names; no additional named range needed
11. **ExcelApi unchanged:** manifest.xml stays at `MinVersion="1.13"`

---

## ExcelApi Requirement Analysis

| API | Min version | Used in Sprint 8 |
|-----|-------------|-----------------|
| `workbook.names.add(name, reference)` | **1.4** | ✅ Create named range |
| `workbook.names.getItemOrNullObject(name)` | **1.4** | ✅ Safe lookup |
| `namedItem.getRange()` | **1.4** | ✅ Read range by name |
| `namedItem.delete()` | **1.4** | ✅ Delete named range |
| `namedItem.comment` | **1.4** | ✅ Tag as FAIT-created |
| `workbook.names.load('items/name')` | **1.1** | ✅ List all names |
| `Office.context.document.customXmlParts` | **Office.js Common** | ✅ Registry storage |

**All APIs ≤ ExcelApi 1.4. Baseline is 1.13. No manifest change required.**

---

## Constraints for CC

- Touch only the 5 files listed (1 new, 4 modified)
- `namedRangeStorage.ts` is a pure XML read/write service — no Excel API calls, no `Excel.run()`. It only uses `Office.context.document.customXmlParts` (Office.js Common, not Excel-specific)
- All Excel API calls (create/delete/rename named ranges) belong in `excelWriter.ts`
- Do NOT add named-range creation to `writeRangeData()` itself — it's a separate user action, not automatic
- The name prompt must NOT appear after `writeToTable()` successes — only after `writeRangeData()` successes
- `generateFaitName('output')` is the default slug — the prompt input is editable, so user can set a custom name before saving
- Named range reference resolution (`\bFAIT_\w+\b` regex) runs ONLY in `handleSend()` — not in any other handler (chart, pivot, CF, sort/filter)
- Do NOT modify the existing `sessionStorage.ts` — named range registry uses a separate XML namespace
- `SettingsPanel` named ranges section must render `namedRanges.length === 0` as a "no ranges yet" message (not an empty div)

---

## Clint Review Priorities

```
⚠️  HIGH: Verify createNamedRange() checks for duplicate BEFORE calling names.add().
          names.add() on a duplicate throws a runtime error that's hard to distinguish
          from other errors. The getItemOrNullObject() + ctx.sync() guard must happen
          before names.add(). Check sync order.

⚠️  HIGH: Verify the address format passed to names.add().
          Must be "=Sheet1!$A$1:$D$11" (with '=' prefix and '$' absolute refs).
          toAbsoluteReference("Sheet1!A1:D11") must produce "=Sheet1!$A$1:$D$11".
          Test: "Sheet1!A1" → "=Sheet1!$A$1". Test: "Sheet1!AA10:BC20" → "=Sheet1!$AA$10:$BC$20".
          Multi-letter columns must also get the '$' treatment.

⚠️  HIGH: Confirm namedItem.getRange() is used in handleSend() for reference resolution
          (not worksheet.getRange(address)). workbook.names resolves cross-sheet references
          correctly. worksheet.getRange() is sheet-specific and could read the wrong data
          if the range is on a different sheet than the active one.

⚠️  MEDIUM: Verify renameWorkbookNamedRange() uses item.value (the formula string) to
            re-add with new name — not a hard-coded address. item.value is loaded before
            item.delete() is called. Check that the load/sync/delete order is correct.

⚠️  MEDIUM: Confirm namedRangeStorage.ts escapeXml() handles all edge cases:
            range names with & or " characters (unlikely but possible).
            Verify it's called on all three attributes: name, address, created.

⚠️  MEDIUM: The registry sync (syncRegistry) is called on settings panel open — confirm
            it fires. If listWorkbookNamedRanges() returns an empty array (Excel.run() fail),
            syncRegistry() would delete all registry entries. Guard: only sync if
            liveNames.length > 0 OR the Excel.run() succeeded without error.

⚠️  LOW: Confirm the name prompt does NOT appear after writeToTable() successes.
         handleWriteTableConfirm() has two branches: isTableTarget and cell-address.
         handleNameRangeRequest() should only be called in the cell-address branch.
         Check both branches in the spec.

⚠️  LOW: Named range section in settings shows empty state even when namedRanges = [].
         Both branches must render — the "no ranges" message must not be hidden.
```

---

## Architectural Note: Two Storage Tiers

Sprint 8 uses both storage tiers deliberately:

**Custom XML (workbook-scoped)** for the registry — because the named ranges belong to a specific workbook. If the user opens a different workbook, they should see that workbook's FAIT ranges, not the previous one's. Custom XML travels with the file, so it's the right store.

**`workbook.names` (Excel native)** for the actual named range definitions — because Excel's name manager is the source of truth. The registry is a cache/metadata overlay. If a user manually deletes a name from Excel's Name Manager, the registry becomes stale — `syncRegistry()` handles this on settings panel open.

This two-tier design means the settings panel can show the list immediately (from custom XML, no Excel API round-trip) and then validate it asynchronously. For a taskpane with limited screen space and a 2-second polling loop, this responsiveness matters.

---

_Spec by Reed Richards | Sprint 8 is 1 new file + 4 edits. The core insight: named ranges need workbook-scoped storage (custom XML, same as session history) — not device-local OfficeRuntime.storage — so the registry travels with the workbook._
