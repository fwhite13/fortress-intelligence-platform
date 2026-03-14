import React, { useState, useEffect, useRef } from 'react';
import { useChat } from '../hooks/useChat';
import { useWriteBack } from '../hooks/useWriteBack';
import { getSelectedRange } from '../services/excelReader';
import { formatContext } from '../services/contextFormatter';
import { searchKb, sendChat } from '../services/faitApi';
import { scanRangeForIssues } from '../services/errorScanner';
import { parseSuggestions } from '../services/suggestionParser';
import { insertChart } from '../services/chartBuilder';
import { insertPivotTable } from '../services/pivotBuilder';
import { applyConditionalFormat } from '../services/cfBuilder';
import type { ChartSpec } from '../services/chartBuilder';
import type { PivotSpec } from '../services/pivotBuilder';
import type { CfSpec } from '../services/cfBuilder';
import type { KbResult } from './KbResultPanel';
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

  // Office JS operation error (chart/pivot/CF failures)
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

  const { messages, loading, error, pendingSuggestions, send, clearError, clearPendingSuggestions } =
    useChat(apiKey, model, kbToggles, projectId);

  const { suggestions: writeBackSuggestions, showDialog, offerSuggestions, acceptAll, reject } = useWriteBack();

  // When useChat detects suggestions in the FAIT response, surface the dialog
  useEffect(() => {
    if (pendingSuggestions && pendingSuggestions.length > 0) {
      offerSuggestions(pendingSuggestions);
      clearPendingSuggestions();
    }
  }, [pendingSuggestions]);

  // Refresh selection info on mount and periodically
  useEffect(() => {
    const refresh = async () => {
      try {
        const ctx = await getSelectedRange();
        setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
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

  const handleSend = async (text: string) => {
    let context: string | undefined;
    if (includeSelection) {
      try {
        const ctx = await getSelectedRange();
        if (ctx.rows > 0 && ctx.cols > 0) {
          context = formatContext(ctx);
          setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
        }
      } catch {
        // Non-fatal: proceed without context
      }
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
        // FAIT didn't return a chart spec — surface the text response as an info message
        setOfficeError(`FAIT responded: ${parsed.displayText.slice(0, 200)}${parsed.displayText.length > 200 ? '…' : ''}`);
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
        setOfficeError(`FAIT responded: ${parsed.displayText.slice(0, 200)}${parsed.displayText.length > 200 ? '…' : ''}`);
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
      // First click: show the inline input
      setShowCfInput(true);
      return;
    }
    // Second click (or submit): send to FAIT
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
    if (e.key === 'Escape') {
      setShowCfInput(false);
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

      {/* Context indicator bar */}
      {includeSelection && selectionInfo && (
        <div
          style={{
            padding: '4px 8px',
            borderBottom: '1px solid #2e3f54',
            background: '#1a2332',
            flexShrink: 0,
          }}
        >
          <ContextIndicator
            address={selectionInfo.address}
            rows={selectionInfo.rows}
            cols={selectionInfo.cols}
            visible={includeSelection}
          />
        </div>
      )}

      {/* Error banner */}
      {error && <ErrorBanner message={error} onDismiss={clearError} />}

      {/* Office JS operation error (chart/pivot/CF) */}
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
        <MessageList messages={messages} loading={loading} />
      </div>

      {/* Input area */}
      <ChatInput
        onSend={handleSend}
        disabled={loading}
        includeSelection={includeSelection}
        onToggleSelection={() => setIncludeSelection((v) => !v)}
      />

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
