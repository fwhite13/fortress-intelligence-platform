import React, { useState } from 'react';
import type { OfficeAction } from '../services/faitApi';

interface ConfirmationPanelProps {
  actions: OfficeAction[];
  streaming: boolean;
  onApplyAll: (actions: OfficeAction[]) => void;
  onRejectAll: () => void;
  onReviewEach: (actions: OfficeAction[]) => void;
}

function summarizeAction(action: OfficeAction): string {
  switch (action.type) {
    case 'write_cells': {
      const range = typeof action.range === 'string' ? action.range : '';
      const values = action.values;
      let count = 0;
      if (Array.isArray(values)) {
        for (const row of values) {
          if (Array.isArray(row)) count += row.length;
          else count += 1;
        }
      }
      return `write_cells → ${range} (${count} cell${count !== 1 ? 's' : ''})`;
    }
    case 'apply_formatting': {
      const range = typeof action.range === 'string' ? action.range : '';
      return `apply_formatting → ${range}`;
    }
    case 'create_sheet': {
      const name = typeof action.name === 'string' ? action.name : '';
      return `create_sheet: "${name}"`;
    }
    case 'create_chart': {
      const chartType = typeof action.chartType === 'string' ? action.chartType : '';
      const dataRange = typeof action.dataRange === 'string' ? action.dataRange : '';
      return `create_chart: ${chartType} from ${dataRange}`;
    }
    default:
      return String(action.type);
  }
}

const ConfirmationPanel: React.FC<ConfirmationPanelProps> = ({
  actions,
  streaming,
  onApplyAll,
  onRejectAll,
  onReviewEach,
}) => {
  const [reviewIndex, setReviewIndex] = useState<number | null>(null);

  const isReviewing = reviewIndex !== null;
  const currentAction = isReviewing ? actions[reviewIndex!] : null;

  const handleApplyThis = () => {
    if (currentAction) {
      onApplyAll([currentAction]);
    }
    const next = reviewIndex! + 1;
    if (next >= actions.length) {
      onRejectAll();
    } else {
      setReviewIndex(next);
    }
  };

  const handleSkip = () => {
    const next = reviewIndex! + 1;
    if (next >= actions.length) {
      onRejectAll();
    } else {
      setReviewIndex(next);
    }
  };

  const handleCancelReview = () => {
    setReviewIndex(null);
  };

  const handleReviewEach = () => {
    setReviewIndex(0);
    onReviewEach(actions);
  };

  return (
    <div
      style={{
        margin: '6px 8px',
        background: '#111d2b',
        border: '1px solid #2e3f54',
        borderRadius: '6px',
        flexShrink: 0,
        overflow: 'hidden',
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: '8px 10px',
          borderBottom: '1px solid #2e3f54',
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
        }}
      >
        <span style={{ color: '#d4af37', fontWeight: 700, fontSize: '12px' }}>
          ⚡ Task Mode Actions
        </span>
        {streaming && (
          <span
            style={{
              fontSize: '10px',
              color: '#8899aa',
              animation: 'pulse 1.5s ease-in-out infinite',
            }}
          >
            ⏳ Receiving actions...
          </span>
        )}
      </div>

      {isReviewing && currentAction ? (
        /* Review Each mode */
        <div style={{ padding: '8px 10px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
          <div style={{ fontSize: '11px', color: '#8899aa' }}>
            Action {reviewIndex! + 1} of {actions.length}
          </div>
          <pre
            style={{
              margin: 0,
              background: '#0f1720',
              border: '1px solid #2e3f54',
              borderRadius: '4px',
              padding: '6px 8px',
              fontSize: '11px',
              color: '#e8edf3',
              overflowX: 'auto',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-all',
            }}
          >
            {JSON.stringify(currentAction, null, 2)}
          </pre>
          <div style={{ display: 'flex', gap: '6px' }}>
            <button onClick={handleApplyThis} style={applyBtnStyle}>
              Apply This ✓
            </button>
            <button onClick={handleSkip} style={rejectBtnStyle}>
              Skip ✗
            </button>
            <button onClick={handleCancelReview} style={reviewBtnStyle}>
              Cancel Review
            </button>
          </div>
        </div>
      ) : (
        /* Summary mode */
        <div style={{ padding: '8px 10px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
          <div style={{ fontSize: '11px', color: '#8899aa' }}>
            {actions.length} action{actions.length !== 1 ? 's' : ''} ready:
          </div>
          <ul style={{ margin: 0, padding: '0 0 0 16px' }}>
            {actions.map((action, i) => (
              <li key={i} style={{ fontSize: '11px', color: '#e8edf3', marginBottom: '2px' }}>
                {summarizeAction(action)}
              </li>
            ))}
          </ul>
          <div style={{ display: 'flex', gap: '6px', marginTop: '2px' }}>
            <button onClick={() => onApplyAll(actions)} style={applyBtnStyle}>
              Apply All ✓
            </button>
            <button onClick={handleReviewEach} style={reviewBtnStyle}>
              Review Each →
            </button>
            <button onClick={onRejectAll} style={rejectBtnStyle}>
              Reject All ✗
            </button>
          </div>
        </div>
      )}

      <style>{`
        @keyframes pulse {
          0%, 100% { opacity: 1; }
          50% { opacity: 0.4; }
        }
      `}</style>
    </div>
  );
};

const applyBtnStyle: React.CSSProperties = {
  background: '#1a3020',
  border: '1px solid #2e5040',
  borderRadius: '4px',
  color: '#6fcf97',
  fontWeight: 600,
  fontSize: '11px',
  padding: '5px 10px',
  cursor: 'pointer',
};

const reviewBtnStyle: React.CSSProperties = {
  background: '#1a2332',
  border: '1px solid #2e3f54',
  borderRadius: '4px',
  color: '#d4af37',
  fontSize: '11px',
  padding: '5px 10px',
  cursor: 'pointer',
};

const rejectBtnStyle: React.CSSProperties = {
  background: '#1e2d3e',
  border: '1px solid #3a2020',
  borderRadius: '4px',
  color: '#cc4444',
  fontSize: '11px',
  padding: '5px 10px',
  cursor: 'pointer',
};

export default ConfirmationPanel;
