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
