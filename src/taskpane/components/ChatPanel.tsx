import React, { useState, useEffect, useRef } from 'react';
import { useChat } from '../hooks/useChat';
import { useWriteBack } from '../hooks/useWriteBack';
import { getSelectedRange } from '../services/excelReader';
import { writeRangeData, WriteRangeError, writeToTable, WriteTableError, createNamedRange, deleteNamedRange, renameWorkbookNamedRange, listWorkbookNamedRanges, NamedRangeError, registerWatchHandler, unregisterWatchHandler } from '../services/excelWriter';
import { setFaitWriting, isFaitWriting } from '../services/watchMode';
import { formatContext } from '../services/contextFormatter';
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
import { searchKb, sendChat } from '../services/faitApi';
import { scanRangeForIssues } from '../services/errorScanner';
import { parseSuggestions } from '../services/suggestionParser';
import { insertChart } from '../services/chartBuilder';
import { insertPivotTable } from '../services/pivotBuilder';
import { applyConditionalFormat } from '../services/cfBuilder';
import { applySortFilter } from '../services/sortFilterBuilder';
import { saveConversation, loadConversation, clearConversation } from '../services/sessionStorage';
import type { ChartSpec } from '../services/chartBuilder';
import type { PivotSpec } from '../services/pivotBuilder';
import type { CfSpec } from '../services/cfBuilder';
import type { SortFilterSpec } from '../services/sortFilterBuilder';
import type { KbResult } from './KbResultPanel';
import type { ParsedTable } from '../services/suggestionParser';
import MessageList from './MessageList';
import ChatInput from './ChatInput';
import ContextIndicator from './ContextIndicator';
import ErrorBanner from './ErrorBanner';
import WriteSuggestionsDialog from './WriteSuggestionsDialog';
import KbResultPanel from './KbResultPanel';
import ErrorSummaryCard from './ErrorSummaryCard';
import ChartConfirmDialog from './ChartConfirmDialog';
import PivotConfirmDialog from './PivotConfirmDialog';
import CfConfirmDialog from './CfConfirmDialog';
import SortFilterConfirmDialog from './SortFilterConfirmDialog';
import SlashCommandPicker from './SlashCommandPicker';
import type { CellIssue } from './ErrorSummaryCard';

interface ChatPanelProps {
  apiKey: string;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
  onOpenSettings: () => void;
}

const ChatPanel: React.FC<ChatPanelProps> = ({
  apiKey,
  model,
  kbToggles,
  projectId,
  onOpenSettings,
}) => {
  const [includeSelection, setIncludeSelection] = useState(true);
  const [selectionInfo, setSelectionInfo] = useState<{
    address: string;
    rows: number;
    cols: number;
    tableName?: string | null;
  } | null>(null);

  // FORGE KB search state
  const [showForgeSearch, setShowForgeSearch] = useState(false);
  const [forgeQuery, setForgeQuery] = useState('');
  const [forgeLoading, setForgeLoading] = useState(false);
  const [forgeResults, setForgeResults] = useState<KbResult[] | null>(null);
  const forgeInputRef = useRef<HTMLInputElement>(null);

  // Error scanner state
  const [scanIssues, setScanIssues] = useState<CellIssue[] | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);

  // Office JS operation error (chart/pivot/CF/sort-filter failures)
  const [officeError, setOfficeError] = useState<string | null>(null);

  // ── Sprint 4: Chart state ──────────────────────────────────────────────────
  const [chartSpec, setChartSpec] = useState<ChartSpec | null>(null);
  const [showChartDialog, setShowChartDialog] = useState(false);
  const [chartLoading, setChartLoading] = useState(false);

  // ── Sprint 4: Pivot state ─────────────────────────────────────────────────
  const [pivotSpec, setPivotSpec] = useState<PivotSpec | null>(null);
  const [showPivotDialog, setShowPivotDialog] = useState(false);
  const [pivotLoading, setPivotLoading] = useState(false);

  // ── Sprint 4: Conditional Formatting state ────────────────────────────────
  const [cfSpec, setCfSpec] = useState<CfSpec | null>(null);
  const [showCfDialog, setShowCfDialog] = useState(false);
  const [cfLoading, setCfLoading] = useState(false);
  const [cfPrompt, setCfPrompt] = useState('highlight values above average in red');
  const [showCfInput, setShowCfInput] = useState(false);
  const cfInputRef = useRef<HTMLInputElement>(null);

  // ── Sprint 5: Sort/Filter state ───────────────────────────────────────────
  const [sortFilterSpec, setSortFilterSpec] = useState<SortFilterSpec | null>(null);
  const [showSortFilterDialog, setShowSortFilterDialog] = useState(false);
  const [sortFilterLoading, setSortFilterLoading] = useState(false);
  const [sortFilterPrompt, setSortFilterPrompt] = useState('sort by the first numeric column descending');
  const [showSortFilterInput, setShowSortFilterInput] = useState(false);
  const sortFilterInputRef = useRef<HTMLInputElement>(null);

  // ── Sprint 6: Write Table state ───────────────────────────────────────────
  const [pendingTableData, setPendingTableData] = useState<ParsedTable | null>(null);
  const [writeTableTarget, setWriteTableTarget] = useState('');
  const [writeTableLoading, setWriteTableLoading] = useState(false);
  const [writeTableError, setWriteTableError] = useState<string | null>(null);
  const [writeTableSuccess, setWriteTableSuccess] = useState<string | null>(null);
  const writeTableInputRef = useRef<HTMLInputElement>(null);

  // ── Sprint 8: Named Range state ───────────────────────────────────────────
  const [pendingNameAddress, setPendingNameAddress] = useState<string | null>(null);
  const [namedRangeName, setNamedRangeName] = useState('');
  const [namedRangeLoading, setNamedRangeLoading] = useState(false);
  const [namedRangeError, setNamedRangeError] = useState<string | null>(null);
  const [namedRanges, setNamedRanges] = useState<FaitNamedRange[]>([]);
  const namedRangeInputRef = useRef<HTMLInputElement>(null);

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

  // ── Sprint 5: Chat input text (lifted state for slash commands) ───────────
  const [inputText, setInputText] = useState('');
  const chatInputAreaRef = useRef<HTMLDivElement>(null);

  // Slash command picker visibility
  const showSlashPicker = inputText.startsWith('/');
  const slashQuery = showSlashPicker ? inputText.slice(1) : '';

  const {
    messages,
    loading,
    error,
    pendingSuggestions,
    send,
    clearError,
    clearPendingSuggestions,
    setMessages,
  } = useChat(apiKey, model, kbToggles, projectId);

  const { suggestions: writeBackSuggestions, showDialog, offerSuggestions, acceptAll, reject } = useWriteBack();

  // ── Sprint 5: Session persistence — load on mount ────────────────────────
  useEffect(() => {
    loadConversation()
      .then((persisted) => {
        if (persisted.length > 0) {
          setMessages(persisted);
        }
      })
      .catch(() => {
        /* ignore storage errors */
      });
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Sprint 5: Session persistence — save on messages change (debounced) ──
  const saveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (messages.length === 0) return;
    if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    saveTimerRef.current = setTimeout(() => {
      saveConversation(messages.filter((m) => !m.streaming));
    }, 1000);
    return () => {
      if (saveTimerRef.current) clearTimeout(saveTimerRef.current);
    };
  }, [messages]);

  // ── Sprint 8: Load named ranges from workbook custom XML on mount ─────────
  useEffect(() => {
    loadNamedRanges().then(setNamedRanges).catch(() => null);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // When useChat detects suggestions in the FAIT response, surface the dialog
  useEffect(() => {
    if (pendingSuggestions && pendingSuggestions.length > 0) {
      offerSuggestions(pendingSuggestions);
      clearPendingSuggestions();
    }
  }, [pendingSuggestions]); // eslint-disable-line react-hooks/exhaustive-deps

  // Refresh selection info on mount and periodically
  useEffect(() => {
    const refresh = async () => {
      try {
        const ctx = await getSelectedRange();
        setSelectionInfo({
          address: ctx.address,
          rows: ctx.rows,
          cols: ctx.cols,
          tableName: ctx.tableInfo?.name ?? null,
        });
      } catch {
        setSelectionInfo(null);
      }
    };
    refresh();
    const interval = setInterval(refresh, 2000);
    return () => clearInterval(interval);
  }, []);

  // Focus FORGE input when it appears
  useEffect(() => {
    if (showForgeSearch) {
      forgeInputRef.current?.focus();
    }
  }, [showForgeSearch]);

  // Focus CF input when it appears
  useEffect(() => {
    if (showCfInput) {
      cfInputRef.current?.focus();
    }
  }, [showCfInput]);

  // Focus Sort/Filter input when it appears
  useEffect(() => {
    if (showSortFilterInput) {
      sortFilterInputRef.current?.focus();
    }
  }, [showSortFilterInput]);

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

  // ── Check for Issues ─────────────────────────────────────────────────────────
  const handleCheckIssues = async () => {
    setScanIssues(null);
    setScanError(null);
    try {
      const issues = await scanRangeForIssues();
      setScanIssues(issues);
    } catch {
      setScanError("Couldn't scan range — make sure a range is selected.");
    }
  };

  // ── Ask FORGE ────────────────────────────────────────────────────────────────
  const buildKbTypes = (): string[] => {
    const types = Object.entries(kbToggles)
      .filter(([, v]) => v)
      .map(([k]) => k);
    // Personal is always included
    if (!types.includes('personal')) types.push('personal');
    return types;
  };

  const handleForgeSearch = async () => {
    if (!forgeQuery.trim()) return;
    setForgeLoading(true);
    setForgeResults(null);
    try {
      const { results } = await searchKb(
        forgeQuery.trim(),
        apiKey,
        projectId ?? undefined,
        buildKbTypes()
      );
      setForgeResults(results);
    } catch {
      setForgeResults([]);
    } finally {
      setForgeLoading(false);
    }
  };

  const handleForgeKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleForgeSearch();
    if (e.key === 'Escape') {
      setShowForgeSearch(false);
      setForgeQuery('');
      setForgeResults(null);
    }
  };

  // ── Sprint 4: Chart ───────────────────────────────────────────────────────
  const handleChart = async () => {
    setChartLoading(true);
    clearError();
    try {
      const ctx = await getSelectedRange();
      const ctxBlock = formatContext(ctx);
      const prompt =
        `${ctxBlock}\n\nPlease analyze this data and suggest a chart. Return a chart_spec JSON block with: ` +
        `type (bar/line/pie/scatter/column), title, dataRange, hasHeaders, seriesBy (rows/columns), ` +
        `and optional xAxis/yAxis titles.\n\nUser request: create a chart for this data`;

      const { answer } = await sendChat(prompt, apiKey, model, undefined, buildKbTypes(), projectId);
      const parsed = parseSuggestions(answer);

      if (parsed.chartSpec) {
        setChartSpec(parsed.chartSpec);
        setShowChartDialog(true);
      } else {
        setOfficeError(
          `FAIT responded: ${parsed.displayText.slice(0, 200)}${parsed.displayText.length > 200 ? '…' : ''}`
        );
      }
    } catch {
      setOfficeError('Chart generation failed — check your selection and try again');
    } finally {
      setChartLoading(false);
    }
  };

  // ── Sprint 4: Pivot ───────────────────────────────────────────────────────
  const handlePivot = async () => {
    setPivotLoading(true);
    clearError();
    try {
      const ctx = await getSelectedRange();
      const ctxBlock = formatContext(ctx);
      const prompt =
        `${ctxBlock}\n\nPlease analyze this data and suggest a pivot table structure. ` +
        `Return a pivot_spec JSON block with: name (string), sourceRange, targetCell ` +
        `(place it 2 columns to the right of the data), rows (array of field names), ` +
        `columns (array, can be empty), values (array of {field, aggregation}).\n\n` +
        `User request: create a pivot table for this data`;

      const { answer } = await sendChat(prompt, apiKey, model, undefined, buildKbTypes(), projectId);
      const parsed = parseSuggestions(answer);

      if (parsed.pivotSpec) {
        setPivotSpec(parsed.pivotSpec);
        setShowPivotDialog(true);
      } else {
        setOfficeError(
          `FAIT responded: ${parsed.displayText.slice(0, 200)}${parsed.displayText.length > 200 ? '…' : ''}`
        );
      }
    } catch {
      setOfficeError('Pivot table generation failed — check your selection and try again');
    } finally {
      setPivotLoading(false);
    }
  };

  // ── Sprint 4: Conditional Formatting ─────────────────────────────────────
  const handleFormat = async () => {
    if (!showCfInput) {
      setShowCfInput(true);
      return;
    }
    setShowCfInput(false);
    setCfLoading(true);
    clearError();
    try {
      const ctx = await getSelectedRange();
      const ctxBlock = formatContext(ctx);
      const userRule = cfPrompt.trim() || 'highlight the key values';
      const prompt =
        `${ctxBlock}\n\nPlease suggest conditional formatting for this data range. ` +
        `Return a cf_spec JSON block with: range (same as selected), rule ` +
        `(with kind: colorScale/dataBar/topN/formula/cellValue and appropriate params).\n\n` +
        `User request: ${userRule}`;

      const { answer } = await sendChat(prompt, apiKey, model, undefined, buildKbTypes(), projectId);
      const parsed = parseSuggestions(answer);

      if (parsed.cfSpec) {
        setCfSpec(parsed.cfSpec);
        setShowCfDialog(true);
      }
    } catch {
      // silent
    } finally {
      setCfLoading(false);
    }
  };

  const handleCfKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleFormat();
    if (e.key === 'Escape') setShowCfInput(false);
  };

  // ── Sprint 5: Sort/Filter ─────────────────────────────────────────────────
  const handleSortFilter = async () => {
    if (!showSortFilterInput) {
      setShowSortFilterInput(true);
      return;
    }
    setShowSortFilterInput(false);
    setSortFilterLoading(true);
    clearError();
    try {
      const ctx = await getSelectedRange();
      const ctxBlock = formatContext(ctx);
      const userRequest = sortFilterPrompt.trim() || 'sort by the first numeric column descending';
      const prompt =
        `${ctxBlock}\n\nPlease analyze this data and return a sort/filter specification. ` +
        `Return a sort_filter_spec JSON block with optional "sort" (fields array with columnIndex 0-based ` +
        `and ascending bool, hasHeaders) and optional "filter" (criteria array with columnIndex, filterType, ` +
        `and values/operator/value params).\n\nUser request: ${userRequest}`;

      const { answer } = await sendChat(prompt, apiKey, model, undefined, buildKbTypes(), projectId);
      const parsed = parseSuggestions(answer);

      if (parsed.sortFilterSpec) {
        setSortFilterSpec(parsed.sortFilterSpec);
        setShowSortFilterDialog(true);
      } else {
        setOfficeError(
          `FAIT responded: ${parsed.displayText.slice(0, 200)}${parsed.displayText.length > 200 ? '…' : ''}`
        );
      }
    } catch {
      setOfficeError('Sort/filter generation failed — check your selection and try again');
    } finally {
      setSortFilterLoading(false);
    }
  };

  const handleSortFilterKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleSortFilter();
    if (e.key === 'Escape') setShowSortFilterInput(false);
  };

  // ── Sprint 5: Clear History ───────────────────────────────────────────────
  const handleClearHistory = async () => {
    try {
      await clearConversation();
    } catch {
      /* ignore */
    }
    setMessages([]);
  };

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

    // Determine write mode: Table name vs cell address
    // Strip optional sheet prefix (e.g. "Sheet1!SalesData" → "SalesData", "Sheet1!B3" → "B3")
    const stripped = target.includes('!') ? target.split('!').pop()! : target;
    // Excel columns are max 3 letters (A–XFD), rows max 7 digits (1–1048576)
    // This prevents "SalesData2023" (9 letters before digit) from matching as a cell address
    const isCellAddress = /^\$?[A-Z]{1,3}\$?\d{1,7}$/i.test(stripped);
    const isTableTarget = !isCellAddress;

    if (isTableTarget) {
      // Write to named Table — append rows only (NOT [headers, ...rows])
      try {
        setFaitWriting(true);
        let result: Awaited<ReturnType<typeof writeToTable>>;
        try {
          result = await writeToTable(target, pendingTableData.rows);
        } finally {
          setFaitWriting(false);
        }
        setWriteTableSuccess(
          `Appended ${result!.rowsAdded} rows to Table "${target}" (${result!.tableAddress})`
        );
        setPendingTableData(null);
      } catch (e) {
        if (e instanceof WriteTableError) {
          if (e.code === 'TABLE_NOT_FOUND') {
            setWriteTableError(`Table "${target}" not found on active worksheet. Use a cell address (e.g. A1) to write as a new range.`);
          } else if (e.code === 'EMPTY_ROWS') {
            setWriteTableError('No rows to append.');
          } else {
            setWriteTableError('Table write failed — Excel error.');
          }
        } else {
          setWriteTableError('Write failed.');
        }
      } finally {
        setWriteTableLoading(false);
      }
    } else {
      // Write to cell address — include headers row
      const data: (string | number | boolean | null)[][] = [
        pendingTableData.headers,
        ...pendingTableData.rows,
      ];

      try {
        setFaitWriting(true);
        let result: Awaited<ReturnType<typeof writeRangeData>>;
        try {
          result = await writeRangeData(target, data);
        } finally {
          setFaitWriting(false);
        }
        let successMsg = `Written to ${result!.address} (${result!.rows} rows × ${result!.cols} cols)`;
        if (result!.warning) {
          successMsg += ` ⚠️ ${result!.warning}`;
        }
        setWriteTableSuccess(successMsg);
        setPendingTableData(null);
        handleNameRangeRequest(result!.address);  // Sprint 8: offer to name the range
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

  // Model display label
  const modelLabel = model === 'haiku' ? 'Haiku' : 'Sonnet';

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minWidth: '300px',
        background: '#1a2332',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 12px',
          borderBottom: '1px solid #2e3f54',
          background: '#0f1720',
          flexShrink: 0,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ color: '#d4af37', fontWeight: '700', fontSize: '14px' }}>
            🏰 FAIT
          </span>
          <span style={{ color: '#556677', fontSize: '11px' }}>for Excel</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
          {/* Check for Issues button */}
          <button
            onClick={handleCheckIssues}
            title="Scan selected range for formula errors"
            aria-label="Check selected range for issues"
            style={headerBtnStyle}
          >
            ⚠
          </button>

          {/* Ask FORGE button */}
          <button
            onClick={() => setShowForgeSearch((v) => !v)}
            title="Search FORGE knowledge base"
            aria-label="Ask FORGE"
            style={{
              ...headerBtnStyle,
              color: showForgeSearch ? '#d4af37' : '#8899aa',
            }}
          >
            🔍
          </button>

          {/* Chart button */}
          <button
            onClick={handleChart}
            disabled={chartLoading}
            title="Generate a chart from selected data"
            aria-label="Insert chart"
            style={{
              ...headerBtnStyle,
              color: chartLoading ? '#d4af37' : '#8899aa',
            }}
          >
            {chartLoading ? '…' : '📊'}
          </button>

          {/* Pivot button */}
          <button
            onClick={handlePivot}
            disabled={pivotLoading}
            title="Create a pivot table from selected data"
            aria-label="Create pivot table"
            style={{
              ...headerBtnStyle,
              color: pivotLoading ? '#d4af37' : '#8899aa',
            }}
          >
            {pivotLoading ? '…' : '🔄'}
          </button>

          {/* Conditional Formatting button */}
          <button
            onClick={handleFormat}
            disabled={cfLoading}
            title="Apply conditional formatting to selected range"
            aria-label="Apply conditional formatting"
            style={{
              ...headerBtnStyle,
              color: cfLoading || showCfInput ? '#d4af37' : '#8899aa',
            }}
          >
            {cfLoading ? '…' : '🎨'}
          </button>

          {/* Sort/Filter button — Sprint 5 */}
          <button
            onClick={handleSortFilter}
            disabled={sortFilterLoading}
            title="Sort or filter selected data"
            aria-label="Sort or filter data"
            style={{
              ...headerBtnStyle,
              color: sortFilterLoading || showSortFilterInput ? '#d4af37' : '#8899aa',
            }}
          >
            {sortFilterLoading ? '…' : '🔀'}
          </button>

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

          {/* Clear History button — Sprint 5 */}
          <button
            onClick={handleClearHistory}
            title="Clear conversation history"
            aria-label="Clear conversation history"
            style={headerBtnStyle}
          >
            🗑
          </button>

          {/* Model read-only indicator */}
          <button
            onClick={onOpenSettings}
            title={`Model: ${modelLabel} — click to change in Settings`}
            style={{
              ...headerBtnStyle,
              fontSize: '11px',
              color: '#8899aa',
              display: 'flex',
              alignItems: 'center',
              gap: '3px',
            }}
          >
            <span style={{ color: '#556677' }}>Model:</span>{' '}
            <span style={{ color: '#d4af37' }}>{modelLabel}</span>
          </button>

          {/* Settings gear */}
          <button
            onClick={onOpenSettings}
            title="Settings"
            aria-label="Open settings"
            style={headerBtnStyle}
          >
            ⚙
          </button>
        </div>
      </div>

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

      {/* CF inline prompt input */}
      {showCfInput && (
        <div
          style={{
            padding: '6px 10px',
            borderBottom: '1px solid #2e3f54',
            background: '#111d2b',
            display: 'flex',
            gap: '6px',
            flexShrink: 0,
          }}
        >
          <input
            ref={cfInputRef}
            value={cfPrompt}
            onChange={(e) => setCfPrompt(e.target.value)}
            onKeyDown={handleCfKeyDown}
            placeholder="Describe the formatting rule…"
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
            onClick={handleFormat}
            disabled={cfLoading}
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
            Go
          </button>
          <button
            onClick={() => setShowCfInput(false)}
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
      )}

      {/* Sort/Filter inline prompt input — Sprint 5 */}
      {showSortFilterInput && (
        <div
          style={{
            padding: '6px 10px',
            borderBottom: '1px solid #2e3f54',
            background: '#111d2b',
            display: 'flex',
            gap: '6px',
            flexShrink: 0,
          }}
        >
          <input
            ref={sortFilterInputRef}
            value={sortFilterPrompt}
            onChange={(e) => setSortFilterPrompt(e.target.value)}
            onKeyDown={handleSortFilterKeyDown}
            placeholder="Describe sort/filter operation…"
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
            onClick={handleSortFilter}
            disabled={sortFilterLoading}
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
            Go
          </button>
          <button
            onClick={() => setShowSortFilterInput(false)}
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
      )}

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
            Writing {pendingTableData.rows.length} rows × {pendingTableData.headers.length} cols
            — cell address or Table name:
          </div>
          <div style={{ display: 'flex', gap: '6px' }}>
            <input
              ref={writeTableInputRef}
              value={writeTableTarget}
              onChange={(e) => setWriteTableTarget(e.target.value)}
              onKeyDown={handleWriteTableKeyDown}
              placeholder="e.g. A1, Sheet1!B3, or SalesData"
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

      {/* FORGE search bar */}
      {showForgeSearch && (
        <div
          style={{
            padding: '6px 10px',
            borderBottom: '1px solid #2e3f54',
            background: '#111d2b',
            display: 'flex',
            gap: '6px',
            flexShrink: 0,
          }}
        >
          <input
            ref={forgeInputRef}
            value={forgeQuery}
            onChange={(e) => setForgeQuery(e.target.value)}
            onKeyDown={handleForgeKeyDown}
            placeholder="Search FORGE knowledge base…"
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
            onClick={handleForgeSearch}
            disabled={forgeLoading || !forgeQuery.trim()}
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
            {forgeLoading ? '…' : 'Go'}
          </button>
        </div>
      )}

      {/* Context indicator bar — always show when include toggle is on */}
      {includeSelection && (
        <div
          style={{
            padding: '4px 8px',
            borderBottom: '1px solid #2e3f54',
            background: '#1a2332',
            flexShrink: 0,
          }}
        >
          <ContextIndicator
            address={selectionInfo?.address ?? null}
            rows={selectionInfo?.rows ?? 0}
            cols={selectionInfo?.cols ?? 0}
            visible={true}
            tableName={selectionInfo?.tableName ?? null}
          />
        </div>
      )}

      {/* Error banner */}
      {error && <ErrorBanner message={error} onDismiss={clearError} />}

      {/* Office JS operation error (chart/pivot/CF/sort-filter) */}
      {officeError && (
        <ErrorBanner message={officeError} onDismiss={() => setOfficeError(null)} />
      )}

      {/* Scan error */}
      {scanError && (
        <div
          style={{
            padding: '6px 10px',
            background: '#2a1515',
            color: '#e07070',
            fontSize: '12px',
            borderBottom: '1px solid #5a2020',
            flexShrink: 0,
          }}
        >
          {scanError}{' '}
          <button
            onClick={() => setScanError(null)}
            style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
          >
            ✕
          </button>
        </div>
      )}

      {/* Scrollable content area */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {/* Error summary card */}
        {scanIssues !== null && (
          <div style={{ padding: '4px 8px', flexShrink: 0 }}>
            <ErrorSummaryCard
              issues={scanIssues}
              onClose={() => setScanIssues(null)}
            />
          </div>
        )}

        {/* FORGE KB results */}
        {(forgeLoading || forgeResults !== null) && (
          <div style={{ padding: '4px 8px', flexShrink: 0 }}>
            <KbResultPanel results={forgeResults ?? []} loading={forgeLoading} />
          </div>
        )}

        {/* Message list */}
        <MessageList
          messages={messages}
          loading={loading}
          onWriteTable={handleWriteTableRequest}
        />
      </div>

      {/* Input area — positioned relatively so slash picker can anchor to it */}
      <div ref={chatInputAreaRef} style={{ position: 'relative', flexShrink: 0 }}>
        {/* Slash command picker overlay — Sprint 5 */}
        {showSlashPicker && (
          <SlashCommandPicker
            query={slashQuery}
            onSelect={(prompt) => {
              setInputText(prompt);
            }}
            onClose={() => setInputText('')}
          />
        )}

        <ChatInput
          value={inputText}
          onChange={setInputText}
          onSend={(text) => {
            setInputText('');
            handleSend(text);
          }}
          disabled={loading}
          includeSelection={includeSelection}
          onToggleSelection={() => setIncludeSelection((v) => !v)}
        />
      </div>

      {/* Write-back dialog (modal overlay) */}
      {showDialog && writeBackSuggestions && (
        <WriteSuggestionsDialog
          suggestions={writeBackSuggestions}
          onAcceptAll={acceptAll}
          onRejectAll={reject}
          onReviewEach={() => {
            /* review mode is managed internally in the dialog */
          }}
        />
      )}

      {/* ── Sprint 4 Dialogs ── */}

      {/* Chart confirmation dialog */}
      {showChartDialog && chartSpec && (
        <ChartConfirmDialog
          spec={chartSpec}
          applying={chartLoading}
          onConfirm={async () => {
            setChartLoading(true);
            try {
              await insertChart(chartSpec);
              setShowChartDialog(false);
            } catch {
              setOfficeError('Failed to insert chart — check the data range and try again');
            } finally {
              setChartLoading(false);
            }
          }}
          onCancel={() => setShowChartDialog(false)}
        />
      )}

      {/* Pivot confirmation dialog */}
      {showPivotDialog && pivotSpec && (
        <PivotConfirmDialog
          spec={pivotSpec}
          applying={pivotLoading}
          onConfirm={async () => {
            setPivotLoading(true);
            try {
              await insertPivotTable(pivotSpec);
              setShowPivotDialog(false);
            } catch {
              setOfficeError('Failed to create pivot table — ensure field names match your data headers');
            } finally {
              setPivotLoading(false);
            }
          }}
          onCancel={() => setShowPivotDialog(false)}
        />
      )}

      {/* Conditional Formatting confirmation dialog */}
      {showCfDialog && cfSpec && (
        <CfConfirmDialog
          spec={cfSpec}
          applying={cfLoading}
          onConfirm={async () => {
            setCfLoading(true);
            try {
              await applyConditionalFormat(cfSpec);
              setShowCfDialog(false);
            } catch {
              setOfficeError('Failed to apply conditional formatting — check the range and rule settings');
            } finally {
              setCfLoading(false);
            }
          }}
          onCancel={() => setShowCfDialog(false)}
        />
      )}

      {/* ── Sprint 5 Dialogs ── */}

      {/* Sort/Filter confirmation dialog */}
      {showSortFilterDialog && sortFilterSpec && (
        <SortFilterConfirmDialog
          spec={sortFilterSpec}
          applying={sortFilterLoading}
          onConfirm={async () => {
            setSortFilterLoading(true);
            try {
              await applySortFilter(sortFilterSpec);
              setShowSortFilterDialog(false);
            } catch {
              setOfficeError('Failed to apply sort/filter — check the range and try again');
            } finally {
              setSortFilterLoading(false);
            }
          }}
          onCancel={() => setShowSortFilterDialog(false)}
        />
      )}

      <style>{`
        @keyframes watchPulse {
          0%, 100% { opacity: 1; transform: scale(1); }
          50% { opacity: 0.4; transform: scale(0.7); }
        }
      `}</style>
    </div>
  );
};

const headerBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: '#8899aa',
  cursor: 'pointer',
  fontSize: '14px',
  padding: '2px 4px',
  borderRadius: '4px',
  lineHeight: 1,
};

export default ChatPanel;
