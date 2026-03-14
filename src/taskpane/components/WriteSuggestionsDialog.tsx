import React, { useState } from 'react';
import { applySuggestions, applySingleSuggestion } from '../services/excelWriter';

export interface CellSuggestion {
  address: string;
  value: number | string | null;
  formula: string | null;
  explanation: string;
  currentValue?: string;  // filled in by add-in when parsing
}

interface WriteSuggestionsDialogProps {
  suggestions: CellSuggestion[];
  onAcceptAll: () => void;
  onRejectAll: () => void;
  onReviewEach: () => void;
}

const cellStyle: React.CSSProperties = {
  padding: '6px 8px',
  borderBottom: '1px solid #2e3f54',
  fontSize: '12px',
  color: '#e8edf3',
  verticalAlign: 'top',
};

const headerCellStyle: React.CSSProperties = {
  ...cellStyle,
  fontWeight: '600',
  color: '#8899aa',
  fontSize: '11px',
  textTransform: 'uppercase',
  letterSpacing: '0.5px',
  background: '#0f1720',
};

const WriteSuggestionsDialog: React.FC<WriteSuggestionsDialogProps> = ({
  suggestions,
  onAcceptAll,
  onRejectAll,
  onReviewEach,
}) => {
  const [mode, setMode] = useState<'overview' | 'review'>('overview');
  const [currentIndex, setCurrentIndex] = useState(0);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleAcceptAll = async () => {
    setApplying(true);
    setError(null);
    try {
      await applySuggestions(suggestions);
      onAcceptAll();
    } catch (e) {
      setError('Failed to apply suggestions — check the active sheet and try again.');
    } finally {
      setApplying(false);
    }
  };

  const handleStartReview = () => {
    setMode('review');
    setCurrentIndex(0);
    onReviewEach();
  };

  const handleAcceptCurrent = async () => {
    const s = suggestions[currentIndex];
    setApplying(true);
    setError(null);
    try {
      await applySingleSuggestion(s);
      if (currentIndex < suggestions.length - 1) {
        setCurrentIndex((i) => i + 1);
      } else {
        onAcceptAll(); // done reviewing — close dialog
      }
    } catch (e) {
      setError(`Failed to apply cell ${s.address} — skipping.`);
      if (currentIndex < suggestions.length - 1) {
        setCurrentIndex((i) => i + 1);
      }
    } finally {
      setApplying(false);
    }
  };

  const handleSkipCurrent = () => {
    if (currentIndex < suggestions.length - 1) {
      setCurrentIndex((i) => i + 1);
    } else {
      onAcceptAll(); // done reviewing (all skipped is fine — we're done)
    }
  };

  const suggested = (s: CellSuggestion) =>
    s.formula ?? (s.value !== null && s.value !== undefined ? String(s.value) : '—');

  /* ── Review Each mode ── */
  if (mode === 'review') {
    const s = suggestions[currentIndex];
    return (
      <div style={overlayStyle}>
        <div style={dialogStyle}>
          <div style={titleBarStyle}>
            <span style={{ color: '#d4af37', fontWeight: '700' }}>FAIT Suggestions</span>
            <span style={{ color: '#8899aa', fontSize: '11px' }}>
              {currentIndex + 1} / {suggestions.length}
            </span>
          </div>

          <div style={{ padding: '16px' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  {['Cell', 'Current', 'Suggested', 'Explanation'].map((h) => (
                    <th key={h} style={headerCellStyle}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td style={{ ...cellStyle, fontFamily: 'monospace', color: '#d4af37' }}>{s.address}</td>
                  <td style={{ ...cellStyle, color: '#8899aa' }}>{s.currentValue ?? '—'}</td>
                  <td style={{ ...cellStyle, fontFamily: 'monospace', color: '#7ec8a0' }}>{suggested(s)}</td>
                  <td style={cellStyle}>{s.explanation}</td>
                </tr>
              </tbody>
            </table>

            {error && <div style={errorStyle}>{error}</div>}

            <div style={buttonRowStyle}>
              <button
                onClick={handleSkipCurrent}
                disabled={applying}
                style={secondaryBtnStyle}
              >
                Skip
              </button>
              <button
                onClick={handleAcceptCurrent}
                disabled={applying}
                style={primaryBtnStyle}
              >
                {applying ? 'Applying…' : '✓ Accept'}
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  /* ── Overview mode ── */
  return (
    <div style={overlayStyle}>
      <div style={dialogStyle}>
        <div style={titleBarStyle}>
          <span style={{ color: '#d4af37', fontWeight: '700' }}>FAIT Suggestions</span>
          <span style={{ color: '#8899aa', fontSize: '11px' }}>
            {suggestions.length} cell{suggestions.length !== 1 ? 's' : ''}
          </span>
        </div>

        <div style={{ padding: '0 16px 16px' }}>
          <div style={{ overflowX: 'auto', marginBottom: '12px' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '340px' }}>
              <thead>
                <tr>
                  {['Cell', 'Current', 'Suggested', 'Explanation'].map((h) => (
                    <th key={h} style={headerCellStyle}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {suggestions.map((s) => (
                  <tr key={s.address}>
                    <td style={{ ...cellStyle, fontFamily: 'monospace', color: '#d4af37' }}>
                      {s.address}
                    </td>
                    <td style={{ ...cellStyle, color: '#8899aa' }}>
                      {s.currentValue ?? '—'}
                    </td>
                    <td style={{ ...cellStyle, fontFamily: 'monospace', color: '#7ec8a0' }}>
                      {suggested(s)}
                    </td>
                    <td style={{ ...cellStyle, maxWidth: '140px', wordBreak: 'break-word' }}>
                      {s.explanation}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {error && <div style={errorStyle}>{error}</div>}

          <div style={buttonRowStyle}>
            <button onClick={onRejectAll} disabled={applying} style={dangerBtnStyle}>
              ✗ Reject All
            </button>
            <button onClick={handleStartReview} disabled={applying} style={secondaryBtnStyle}>
              Review Each
            </button>
            <button onClick={handleAcceptAll} disabled={applying} style={primaryBtnStyle}>
              {applying ? 'Applying…' : '✓ Accept All'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

/* ── Shared styles ── */
const overlayStyle: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  background: 'rgba(0,0,0,0.6)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  zIndex: 1000,
  padding: '12px',
};

const dialogStyle: React.CSSProperties = {
  background: '#1a2332',
  border: '1px solid #2e5080',
  borderRadius: '8px',
  width: '100%',
  maxWidth: '560px',
  maxHeight: '80vh',
  display: 'flex',
  flexDirection: 'column',
  overflow: 'hidden',
  boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
};

const titleBarStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '12px 16px',
  borderBottom: '1px solid #2e3f54',
  background: '#0f1720',
  flexShrink: 0,
};

const buttonRowStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  gap: '8px',
  marginTop: '12px',
};

const baseBtnStyle: React.CSSProperties = {
  padding: '7px 14px',
  borderRadius: '5px',
  fontSize: '12px',
  fontWeight: '600',
  cursor: 'pointer',
  border: 'none',
  transition: 'opacity 0.15s',
};

const primaryBtnStyle: React.CSSProperties = {
  ...baseBtnStyle,
  background: '#d4af37',
  color: '#0f1720',
};

const secondaryBtnStyle: React.CSSProperties = {
  ...baseBtnStyle,
  background: '#2e3f54',
  color: '#e8edf3',
};

const dangerBtnStyle: React.CSSProperties = {
  ...baseBtnStyle,
  background: '#3a1f1f',
  color: '#e07070',
  border: '1px solid #5a2020',
};

const errorStyle: React.CSSProperties = {
  marginBottom: '8px',
  padding: '8px 10px',
  background: '#2a1515',
  border: '1px solid #5a2020',
  borderRadius: '4px',
  color: '#e07070',
  fontSize: '12px',
};

export default WriteSuggestionsDialog;
