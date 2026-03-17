import React from 'react';

interface ChartPreviewProps {
  base64DataUrl: string;
  title: string;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
  error?: string | null;
}

const ChartPreview: React.FC<ChartPreviewProps> = ({
  base64DataUrl,
  title,
  onAccept,
  onReject,
  loading = false,
  error,
}) => (
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
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      📈 Chart Preview — {title}
    </div>
    <img
      src={base64DataUrl}
      alt={title}
      style={{
        width: '100%',
        maxHeight: '180px',
        objectFit: 'contain',
        border: '1px solid #2e3f54',
        borderRadius: '3px',
      }}
    />
    {error && <div style={{ color: '#e07070', fontSize: '11px' }}>{error}</div>}
    <div style={{ display: 'flex', gap: '6px' }}>
      <button
        onClick={onAccept}
        disabled={loading || !!error}
        style={{
          flex: 1,
          background: '#d4af37',
          color: '#0f1720',
          border: 'none',
          borderRadius: '4px',
          padding: '6px 12px',
          fontSize: '12px',
          fontWeight: '700',
          cursor: loading || !!error ? 'not-allowed' : 'pointer',
          opacity: loading || !!error ? 0.6 : 1,
        }}
      >
        {loading ? 'Inserting…' : '✓ Insert Chart'}
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

export default ChartPreview;
