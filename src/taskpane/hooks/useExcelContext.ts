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
    let handler: any = null;

    // Register a selection change handler so ContextIndicator stays current
    Office.onReady(() => {
      try {
        Excel.run(async (ctx: any) => {
          handler = ctx.workbook.onSelectionChanged.add(async () => {
            try {
              const info = await getSelectedRange();
              setSelectionInfo({ address: info.address, rows: info.rows, cols: info.cols });
            } catch {
              // ignore — selection may be invalid/empty
            }
          });
          await ctx.sync();
        });
      } catch {
        // Excel JS not available (e.g., non-Excel host)
      }
    });

    return () => {
      // Cleanup: remove the handler if registered
      if (handler) {
        try {
          Excel.run(async (ctx: any) => {
            handler.remove();
            await ctx.sync();
          });
        } catch {
          // ignore cleanup errors
        }
      }
    };
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
