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
import { getAuthHeader } from '../services/authService';
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
import { createReportSheet } from '../services/reportBuilder';
import type { ReportSpec } from '../services/reportBuilder';
import { previewFormula, writeFormula, formatPreviewValue } from '../services/formulaBuilder';
import type { FormulaSpec, FormulaPreviewResult } from '../services/formulaBuilder';
import { exportWorkbookState } from '../services/workbookStateExporter';
import { executeOfficeActions } from '../services/officeActionExecutor';
import ConfirmationPanel from './ConfirmationPanel';
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
  authHeader: Record<string, string>;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
  onOpenSettings: () => void;
}

const ChatPanel: React.FC<ChatPanelProps> = ({
  authHeader,
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

  // ── Sprint 10: Report generation state ────────────────────────────────────
  const [showReportConfig, setShowReportConfig] = useState(false);
  const [reportConfigTitle, setReportConfigTitle] = useState('');
  const [reportConfigChartType, setReportConfigChartType] = useState<'column' | 'bar' | 'line' | 'pie'>('column');
  const [pendingReportSpec, setPendingReportSpec] = useState<ReportSpec | null>(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [reportError, setReportError] = useState<string | null>(null);
  const [reportSuccess, setReportSuccess] = useState<string | null>(null);
  const [reportSourceAddress, setReportSourceAddress] = useState('');

  // ── Sprint 11: Formula intelligence state ──────────────────────────────────
  const [showFormulaConfig, setShowFormulaConfig] = useState(false);
  const [formulaDescription, setFormulaDescription] = useState('');
  const [pendingFormulaSpec, setPendingFormulaSpec] = useState<FormulaSpec | null>(null);
  const [formulaPreview, setFormulaPreview] = useState<FormulaPreviewResult | null>(null);
  const [formulaPreviewLoading, setFormulaPreviewLoading] = useState(false);
  const [formulaWriteLoading, setFormulaWriteLoading] = useState(false);
  const [formulaError, setFormulaError] = useState<string | null>(null);
  const [formulaSuccess, setFormulaSuccess] = useState<string | null>(null);
  const formulaInputRef = useRef<HTMLInputElement>(null);

  // ── Sprint 5: Chat input text (lifted state for slash commands) ───────────
  const [inputText, setInputText] = useState('');
  const chatInputAreaRef = useRef<HTMLDivElement>(null);

  // Slash command picker visibility
  const showSlashPicker = inputText.startsWith('/');
  const slashQuery = showSlashPicker ? inputText.slice(1) : '';

  const [showConfirmPanel, setShowConfirmPanel] = useState(false);

  // ── WI #5212: Task mode state ─────────────────────────────────────────────
  const [taskModeActive, setTaskModeActive] = useState(false);

  const {
    messages,
    loading,
    error,
    pendingSuggestions,
    officeActions,
    officeActionsStreaming,
    send,
    clearError,
    clearPendingSuggestions,
    clearOfficeActions,
    setMessages,
  } = useChat(authHeader, model, kbToggles, projectId);

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

  useEffect(() => {
    if (officeActions.length > 0) {
      setShowConfirmPanel(true);
    }
  }, [officeActions]);

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

  // ── Sprint 10: Capture reportSpec from latest assistant message ───────────
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (
      lastMsg?.role === 'assistant' &&
      !lastMsg.streaming &&
      lastMsg.reportSpec &&
      !pendingReportSpec
    ) {
      setPendingReportSpec(lastMsg.reportSpec);
      setReportConfigTitle(lastMsg.reportSpec.title);
      const specType = lastMsg.reportSpec.chartSpec?.type;
      const validTypes = ['column', 'bar', 'line', 'pie'] as const;
      setReportConfigChartType(
        validTypes.includes(specType as any) ? (specType as 'column' | 'bar' | 'line' | 'pie') : 'column'
      );
    }
  }, [messages]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Sprint 11: Watch for formula_spec in the latest assistant message ─────
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (
      lastMsg?.role === 'assistant' &&
      !lastMsg.streaming &&
      lastMsg.formulaSpec &&
      !pendingFormulaSpec
    ) {
      setPendingFormulaSpec(lastMsg.formulaSpec);
      setFormulaPreview(null);
      if (lastMsg.formulaSpec.previewable) {
        void handleFormulaPreview(lastMsg.formulaSpec);
      }
    }
  }, [messages]); // eslint-disable-line react-hooks/exhaustive-deps

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

    // ── WI #5212: Task mode detection + workbook state capture ──────────────
    const isTaskMode = text.trim().toLowerCase().startsWith('task mode:');
    let extraBody: Record<string, unknown> | undefined;

    if (isTaskMode) {
      setTaskModeActive(true);
      try {
        const { stateBlock } = await exportWorkbookState();
        // Prepend workbook state to context
        context = context ? `${stateBlock}\n\n${context}` : stateBlock;
        extraBody = { taskMode: true, workbookState: stateBlock };
      } catch (err) {
        console.warn('[FAIT] exportWorkbookState failed:', err);
        extraBody = { taskMode: true };
      }
    }

    await send(text, context, extraBody);

    // Reset task mode indicator after send completes
    if (isTaskMode) {
      setTaskModeActive(false);
    }
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
      const hdr = await getAuthHeader();
      const { results } = await searchKb(
        forgeQuery.trim(),
        hdr,
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

      const hdr = await getAuthHeader();
      const { answer } = await sendChat(prompt, hdr, model, undefined, buildKbTypes(), projectId);
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

      const hdr = await getAuthHeader();
      const { answer } = await sendChat(prompt, hdr, model, undefined, buildKbTypes(), projectId);
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

      const hdr = await getAuthHeader();
      const { answer } = await sendChat(prompt, hdr, model, undefined, buildKbTypes(), projectId);
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

      const hdr = await getAuthHeader();
      const { answer } = await sendChat(prompt, hdr, model, undefined, buildKbTypes(), projectId);
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

  // ── Sprint 10: Report handlers ─────────────────────────────────────────────

  const handleReportAnalyze = async () => {
    setShowReportConfig(false);
    setReportError(null);
    setReportSuccess(null);
    setPendingReportSpec(null);

    let context: string | undefined;
    let address = '';
    try {
      const ctx = await getSelectedRange();
      if (ctx.rows > 0 && ctx.cols > 0) {
        context = formatContext(ctx);
        address = ctx.address;
        setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
      }
    } catch {
      // Non-fatal
    }

    setReportSourceAddress(address);

    const reportPrompt = `Please analyze the selected spreadsheet data and return a structured report_spec JSON block.
Return a JSON block with key "report_spec" containing:
- title: string — a concise report title (max 50 chars)
- summary: string — 2-4 sentences describing the data, key trends, and notable findings
- keyMetrics: array of { label: string, value: string, note?: string } — max 8 metrics
- chartSpec: object with keys: type ("column"|"bar"|"line"|"pie"), title (string), dataRange (string — use "A7:B14" as a placeholder since the actual range will be computed), hasHeaders (true), seriesBy ("columns")

The keyMetrics should highlight the most important numbers from the data.
Return ONLY the JSON block, no prose before or after it.`;

    await send(reportPrompt, context);
  };

  const handleCreateReportSheet = async () => {
    if (reportLoading || !pendingReportSpec) return;

    const spec = pendingReportSpec;
    setReportLoading(true);
    setReportError(null);
    setReportSuccess(null);
    setPendingReportSpec(null);

    setFaitWriting(true);
    try {
      const result = await createReportSheet(
        spec,
        reportSourceAddress,
        reportConfigTitle.trim() || undefined,
        reportConfigChartType
      );

      // Sprint 8 integration: register as named range (graceful degradation)
      try {
        const nameForReport = generateFaitName('report');
        await createNamedRange(nameForReport, result.reportAddress, 'FAIT report sheet');
        const entry: FaitNamedRange = {
          name: nameForReport,
          address: toAbsoluteReference(result.reportAddress),
          created: new Date().toISOString(),
        };
        await addNamedRange(entry);
        setNamedRanges((prev) => [...prev, entry]);
      } catch {
        // S8 named range registration failed — report still created successfully
      }

      setReportSuccess(`Report sheet created: "${result.sheetName}"`);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      setReportError(`Report creation failed: ${msg}`);
    } finally {
      setFaitWriting(false);
      setReportLoading(false);
    }
  };

  // ── Sprint 11: Formula Intelligence handlers ──────────────────────────────

  const handleFormulaGenerate = async () => {
    if (!formulaDescription.trim()) return;
    setShowFormulaConfig(false);
    setFormulaError(null);
    setFormulaSuccess(null);
    setPendingFormulaSpec(null);
    setFormulaPreview(null);

    let context: string | undefined;
    try {
      const ctx = await getSelectedRange();
      if (ctx.rows > 0 && ctx.cols > 0) {
        context = formatContext(ctx);
        setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
      }
    } catch {
      // Non-fatal
    }

    const formulaPrompt = `The user wants a formula for: "${formulaDescription.trim()}"

Please generate an Excel formula and return it as a formula_spec JSON block:
\`\`\`json
{
  "formula_spec": {
    "formula": "=THE_FORMULA_HERE",
    "explanation": "Plain-English explanation of what this formula does",
    "functionNames": ["LIST", "OF", "FUNCTIONS", "USED"],
    "targetCell": "__SELECTED__",
    "previewable": true
  }
}
\`\`\`

Rules:
- formula must start with = and use en-US Excel function names
- If the formula uses volatile functions (NOW, TODAY, RAND, RANDBETWEEN, OFFSET, INDIRECT, INFO, CELL), set previewable: false
- If you cannot generate a valid formula, return a prose explanation instead of the JSON block
Return ONLY the JSON block.`;

    await send(formulaPrompt, context);
  };

  const handleFormulaPreview = async (spec: FormulaSpec) => {
    if (!spec.previewable) {
      setFormulaPreview({
        value: null,
        valueType: 'String',
        isError: false,
        errorMessage: 'Preview unavailable for this formula type',
      });
      return;
    }

    setFormulaPreviewLoading(true);

    try {
      const activeSheetName = await Excel.run(async (ctx: any) => {
        const sheet = ctx.workbook.worksheets.getActiveWorksheet();
        sheet.load('name');
        await ctx.sync();
        return sheet.name as string;
      });

      const result = await previewFormula(spec.formula, activeSheetName);
      setFormulaPreview(result);
    } catch (e) {
      setFormulaPreview({
        value: null,
        valueType: 'Error',
        isError: true,
        errorMessage: e instanceof Error ? e.message : 'Preview failed',
      });
    } finally {
      setFormulaPreviewLoading(false);
    }
  };

  const handleFormulaWrite = async (spec: FormulaSpec) => {
    if (!spec) return;
    if (!selectionInfo?.address) {
      setFormulaError('Select a target cell first.');
      return;
    }

    setFormulaWriteLoading(true);
    setFormulaError(null);

    const targetAddress = selectionInfo.address.split(':')[0];

    try {
      await writeFormula(spec.formula, targetAddress, spec.explanation);
      setFormulaSuccess(`Formula written to ${targetAddress}`);
      setPendingFormulaSpec(null);
      setFormulaPreview(null);
    } catch (e) {
      setFormulaError(e instanceof Error ? e.message : 'Failed to write formula');
    } finally {
      setFormulaWriteLoading(false);
    }
  };

  const handleFormulaDismiss = () => {
    setPendingFormulaSpec(null);
    setFormulaPreview(null);
    setFormulaError(null);
  };

  const handleFormulaInputKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') void handleFormulaGenerate();
    if (e.key === 'Escape') setShowFormulaConfig(false);
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

      {/* ── WI #5212: Task mode indicator ── */}
      {taskModeActive && (
        <div
          style={{
            padding: '4px 12px',
            borderBottom: '1px solid #1a2d3e',
            background: '#0d1520',
            flexShrink: 0,
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
          }}
        >
          <span
            style={{
              display: 'inline-block',
              width: '6px',
              height: '6px',
              borderRadius: '50%',
              background: '#d4af37',
              animation: 'watchPulse 1.5s ease-in-out infinite',
            }}
          />
          <span style={{ fontSize: '11px', color: '#d4af37', fontWeight: '600' }}>
            ⚡ Task mode — capturing workbook state...
          </span>
        </div>
      )}

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

      {/* ── Sprint 10: Report config panel ── */}
      {showReportConfig && (
        <div
          style={{
            padding: '10px 12px',
            borderBottom: '1px solid #2e3f54',
            background: '#111d2b',
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: '8px',
          }}
        >
          <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
            📊 Generate Report Sheet
          </div>
          <div style={{ fontSize: '11px', color: '#8899aa' }}>
            FAIT will analyze the selected range and create a formatted report sheet.
          </div>
          <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
            <span style={{ fontSize: '11px', color: '#8899aa', flexShrink: 0 }}>Chart:</span>
            {(['column', 'bar', 'line', 'pie'] as const).map((type) => (
              <button
                key={type}
                onClick={() => setReportConfigChartType(type)}
                style={{
                  background: reportConfigChartType === type ? '#1e3a5f' : '#1a2332',
                  border: `1px solid ${reportConfigChartType === type ? '#2e5080' : '#2e3f54'}`,
                  borderRadius: '4px',
                  color: reportConfigChartType === type ? '#d4af37' : '#556677',
                  fontSize: '11px',
                  padding: '3px 8px',
                  cursor: 'pointer',
                  textTransform: 'capitalize',
                }}
              >
                {type}
              </button>
            ))}
          </div>
          <div style={{ display: 'flex', gap: '6px' }}>
            <button
              onClick={() => void handleReportAnalyze()}
              disabled={!selectionInfo}
              style={{
                background: selectionInfo ? '#1a3020' : '#1e2d3e',
                border: `1px solid ${selectionInfo ? '#2e5040' : '#2e3f54'}`,
                borderRadius: '4px',
                color: selectionInfo ? '#6fcf97' : '#445566',
                fontSize: '11px',
                fontWeight: '600',
                padding: '5px 12px',
                cursor: selectionInfo ? 'pointer' : 'not-allowed',
              }}
            >
              {selectionInfo ? `Analyze ${selectionInfo.address}` : 'Select a range first'}
            </button>
            <button
              onClick={() => setShowReportConfig(false)}
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

      {/* ── Sprint 11: Formula config panel ── */}
      {showFormulaConfig && (
        <div
          style={{
            padding: '10px 12px',
            borderBottom: '1px solid #2e3f54',
            background: '#111d2b',
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: '8px',
          }}
        >
          <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
            ƒx Formula Generator
          </div>
          <input
            ref={formulaInputRef}
            value={formulaDescription}
            onChange={(e) => setFormulaDescription(e.target.value)}
            onKeyDown={handleFormulaInputKeyDown}
            placeholder="e.g. sum revenue where region is North and quarter > 0"
            style={{
              background: '#1a2332',
              border: '1px solid #2e3f54',
              borderRadius: '4px',
              color: '#e8edf3',
              padding: '6px 8px',
              fontSize: '12px',
              outline: 'none',
              width: '100%',
              boxSizing: 'border-box',
            }}
          />
          <div style={{ display: 'flex', gap: '6px' }}>
            <button
              onClick={() => void handleFormulaGenerate()}
              disabled={!formulaDescription.trim()}
              style={{
                background: formulaDescription.trim() ? '#1a3020' : '#1e2d3e',
                border: `1px solid ${formulaDescription.trim() ? '#2e5040' : '#2e3f54'}`,
                borderRadius: '4px',
                color: formulaDescription.trim() ? '#6fcf97' : '#445566',
                fontSize: '11px',
                fontWeight: '600',
                padding: '5px 12px',
                cursor: formulaDescription.trim() ? 'pointer' : 'not-allowed',
              }}
            >
              Generate Formula
            </button>
            <button
              onClick={() => setShowFormulaConfig(false)}
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

      {/* ── Sprint 10: Create Report Sheet action bar ── */}
      {pendingReportSpec && !reportLoading && (
        <div
          style={{
            padding: '8px 10px',
            borderBottom: '1px solid #2e3f54',
            background: '#0f1720',
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: '6px',
          }}
        >
          <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
            <input
              value={reportConfigTitle}
              onChange={(e) => setReportConfigTitle(e.target.value)}
              placeholder="Report title"
              maxLength={45}
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
              onClick={() => void handleCreateReportSheet()}
              style={{
                background: '#1a3020',
                border: '1px solid #2e5040',
                borderRadius: '4px',
                color: '#6fcf97',
                fontSize: '11px',
                fontWeight: '600',
                padding: '5px 12px',
                cursor: 'pointer',
                whiteSpace: 'nowrap',
              }}
            >
              📋 Create Report Sheet
            </button>
            <button
              onClick={() => { setPendingReportSpec(null); setReportError(null); }}
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
              ✕
            </button>
          </div>
          {reportError && (
            <div style={{ fontSize: '11px', color: '#e07070' }}>{reportError}</div>
          )}
        </div>
      )}

      {reportLoading && (
        <div
          style={{
            padding: '6px 10px',
            fontSize: '11px',
            color: '#8899aa',
            borderBottom: '1px solid #2e3f54',
            flexShrink: 0,
          }}
        >
          Creating report sheet…
        </div>
      )}

      {reportSuccess && !pendingReportSpec && (
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
          <span>✓ {reportSuccess}</span>
          <button
            onClick={() => setReportSuccess(null)}
            style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
          >
            ✕
          </button>
        </div>
      )}

      {/* ── Sprint 11: Formula preview + write action bar ── */}
      {pendingFormulaSpec && (
        <div
          style={{
            padding: '8px 10px',
            borderBottom: '1px solid #2e3f54',
            background: '#0f1720',
            flexShrink: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: '6px',
          }}
        >
          <div
            style={{
              fontFamily: 'monospace',
              fontSize: '12px',
              color: '#d4af37',
              background: '#131f2e',
              padding: '5px 8px',
              borderRadius: '4px',
              border: '1px solid #2e3f54',
              wordBreak: 'break-all',
            }}
          >
            {pendingFormulaSpec.formula}
          </div>

          <div style={{ fontSize: '11px', color: '#8899aa' }}>
            {pendingFormulaSpec.explanation}
          </div>

          {pendingFormulaSpec.functionNames.length > 0 && (
            <div style={{ fontSize: '10px', color: '#556677' }}>
              Uses: {pendingFormulaSpec.functionNames.join(', ')}
            </div>
          )}

          <div style={{ fontSize: '11px', display: 'flex', alignItems: 'center', gap: '6px' }}>
            {formulaPreviewLoading ? (
              <span style={{ color: '#556677' }}>Computing preview…</span>
            ) : formulaPreview ? (
              <span
                style={{
                  color: formulaPreview.isError ? '#e07070' : '#6fcf97',
                  fontFamily: 'monospace',
                  fontWeight: '600',
                }}
              >
                {formatPreviewValue(formulaPreview)}
                {formulaPreview.isError && (
                  <span style={{ color: '#556677', fontFamily: 'sans-serif', fontWeight: 'normal' }}>
                    {' '}(preview error — formula may still be valid)
                  </span>
                )}
              </span>
            ) : !pendingFormulaSpec.previewable ? (
              <span style={{ color: '#556677' }}>Preview unavailable (volatile function)</span>
            ) : null}
          </div>

          <div style={{ display: 'flex', gap: '6px', alignItems: 'center' }}>
            <span style={{ fontSize: '11px', color: '#556677', flexShrink: 0 }}>
              Write to: {selectionInfo?.address?.split(':')[0] ?? '(select a cell)'}
            </span>
            <div style={{ flex: 1 }} />
            <button
              onClick={() => void handleFormulaWrite(pendingFormulaSpec)}
              disabled={formulaWriteLoading || !selectionInfo}
              style={{
                background: selectionInfo ? '#d4af37' : '#2e3f54',
                color: selectionInfo ? '#0f1720' : '#445566',
                border: 'none',
                borderRadius: '4px',
                padding: '5px 12px',
                fontSize: '11px',
                fontWeight: '600',
                cursor: selectionInfo ? 'pointer' : 'not-allowed',
              }}
            >
              {formulaWriteLoading ? '…' : 'Write Formula'}
            </button>
            <button
              onClick={handleFormulaDismiss}
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
              ✕
            </button>
          </div>

          {formulaError && (
            <div style={{ fontSize: '11px', color: '#e07070' }}>{formulaError}</div>
          )}
        </div>
      )}

      {formulaSuccess && !pendingFormulaSpec && (
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
          <span>✓ {formulaSuccess}</span>
          <button
            onClick={() => setFormulaSuccess(null)}
            style={{ background: 'none', border: 'none', color: '#8899aa', cursor: 'pointer', fontSize: '12px' }}
          >
            ✕
          </button>
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

      {/* ConfirmationPanel — shown when office_action events are buffered */}
      {showConfirmPanel && officeActions.length > 0 && (
        <ConfirmationPanel
          actions={officeActions}
          streaming={officeActionsStreaming}
          onApplyAll={async (actionsToApply) => {
            try {
              const summary = await executeOfficeActions(actionsToApply);
              const resultMsg = summary.successCount > 0
                ? `✅ Applied ${summary.successCount} action${summary.successCount > 1 ? 's' : ''} successfully.${summary.failureCount > 0 ? ` (${summary.failureCount} failed)` : ''}`
                : `⚠️ All ${summary.failureCount} actions failed.`;
              setMessages(prev => [...prev, {
                role: 'assistant' as const,
                content: resultMsg,
                streaming: false,
              }]);
            } catch (err) {
              const errMsg = err instanceof Error ? err.message : 'Unknown error';
              setMessages(prev => [...prev, {
                role: 'assistant' as const,
                content: `⚠️ Action execution failed: ${errMsg}`,
                streaming: false,
              }]);
            } finally {
              clearOfficeActions();
              setShowConfirmPanel(false);
            }
          }}
          onRejectAll={() => {
            clearOfficeActions();
            setShowConfirmPanel(false);
          }}
          onReviewEach={(_actions) => {
            // Review Each handled internally by ConfirmationPanel
          }}
        />
      )}

      {/* Input area — positioned relatively so slash picker can anchor to it */}
      <div ref={chatInputAreaRef} style={{ position: 'relative', flexShrink: 0 }}>
        {/* Slash command picker overlay — Sprint 5 */}
        {showSlashPicker && (
          <SlashCommandPicker
            query={slashQuery}
            onSelect={(prompt, name) => {
              if (name === 'report') {
                setInputText('');
                setShowReportConfig(true);
                setReportError(null);
                setReportSuccess(null);
              } else if (name === 'formula') {
                setInputText('');
                setShowFormulaConfig(true);
                setFormulaError(null);
                setFormulaSuccess(null);
                setFormulaDescription('');
                setPendingFormulaSpec(null);
                setFormulaPreview(null);
                setTimeout(() => formulaInputRef.current?.focus(), 50);
              } else {
                setInputText(prompt);
              }
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
