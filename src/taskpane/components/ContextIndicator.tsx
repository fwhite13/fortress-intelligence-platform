import React from 'react';

interface ContextIndicatorProps {
  address: string | null;
  rows: number;
  cols: number;
  visible: boolean;
}

const ContextIndicator: React.FC<ContextIndicatorProps> = ({ address, rows, cols, visible }) => {
  if (!visible || !address) return null;

  return (
    <div
      title={`Spreadsheet context will be included: ${address}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        padding: '2px 8px',
        background: '#243447',
        border: '1px solid #2e3f54',
        borderRadius: '12px',
        fontSize: '11px',
        color: '#d4af37',
        whiteSpace: 'nowrap',
        maxWidth: '100%',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
      }}
    >
      <span>📊</span>
      <span>Using: {address} ({rows}×{cols})</span>
    </div>
  );
};

export default ContextIndicator;
