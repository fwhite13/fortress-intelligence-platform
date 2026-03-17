import React from 'react';

interface ShapePreviewProps {
  pendingText: string;
  targetShapeName: string;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
}

const ShapePreview: React.FC<ShapePreviewProps> = ({
  pendingText,
  targetShapeName,
  onAccept,
  onReject,
  loading = false,
}) => {
  return (
    <div
      style={{
        padding: '10px 12px',
        borderTop: '1px solid #2e3f54',
        background: '#0f1720',
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '8px',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          fontSize: '11px',
          fontWeight: '600',
          color: '#d4af37',
        }}
      >
        <span>▶</span>
        <span>Apply to: {targetShapeName || 'selected shape'}</span>
      </div>

      <div
        style={{
          background: '#131f2e',
          border: '1px solid #2e3f54',
          borderRadius: '4px',
          padding: '8px 10px',
          fontSize: '12px',
          color: '#e8edf3',
          lineHeight: 1.6,
          maxHeight: '120px',
          overflowY: 'auto',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
        }}
      >
        {pendingText}
      </div>

      <div style={{ display: 'flex', gap: '6px' }}>
        <button
          onClick={onAccept}
          disabled={loading}
          style={{
            flex: 1,
            background: '#d4af37',
            color: '#0f1720',
            border: 'none',
            borderRadius: '4px',
            padding: '6px 12px',
            fontSize: '12px',
            fontWeight: '700',
            cursor: loading ? 'not-allowed' : 'pointer',
            opacity: loading ? 0.6 : 1,
          }}
        >
          {loading ? 'Applying…' : '✓ Apply to Shape'}
        </button>
        <button
          onClick={onReject}
          disabled={loading}
          style={{
            background: '#2e3f54',
            color: '#e8edf3',
            border: 'none',
            borderRadius: '4px',
            padding: '6px 10px',
            fontSize: '12px',
            cursor: loading ? 'not-allowed' : 'pointer',
            opacity: loading ? 0.6 : 1,
          }}
        >
          Discard
        </button>
      </div>
    </div>
  );
};

export default ShapePreview;
