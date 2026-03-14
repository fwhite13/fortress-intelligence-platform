import { useState, useEffect } from 'react';
import { getSelectedRange } from '../services/excelReader';
import type { SpreadsheetContext } from '../services/excelReader';

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Excel: any;
declare const Office: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

export function useExcelContext() {
  const [selectionInfo, setSelectionInfo] = useState<{ address: string; rows: number; cols: number } | null>(null);

  useEffect(() => {
    // Poll selection every 2s — simpler and reliable across all Excel versions.
    // onSelectionChanged registration inside Excel.run() produces a proxy that cannot
    // be safely removed from a different Excel.run() context on cleanup, causing
    // memory leaks. Polling avoids this entirely.
    const interval = setInterval(async () => {
      try {
        const info = await getSelectedRange();
        setSelectionInfo({ address: info.address, rows: info.rows, cols: info.cols });
      } catch {
        // ignore — no selection or Excel unavailable
      }
    }, 2000);

    return () => clearInterval(interval);
  }, []);

  const readSelection = async (): Promise<SpreadsheetContext | null> => {
    try {
      return await getSelectedRange();
    } catch {
      return null;
    }
  };

  return { selectionInfo, readSelection };
}
