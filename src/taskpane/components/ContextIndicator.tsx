import React from 'react';

interface ContextIndicatorProps {
  address: string | null;
  rows: number;
  cols: number;
  visible: boolean;
  tableName?: string | null;   // NEW
}

const ContextIndicator: React.FC<ContextIndicatorProps> = ({
  address,
  rows,
  cols,
  visible,
  tableName,
}) => {
  if (!visible) return null;

  if (!address) {
    return (
      <div
        title="No range selected — click a cell or range in Excel to include context"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          padding: '2px 8px',
          background: '#1e2b3a',
          border: '1px solid #2e3f54',
          borderRadius: '12px',
          fontSize: '11px',
          color: '#556677',
          whiteSpace: 'nowrap',
        }}
      >
        <span>📊</span>
        <span>No selection — click a cell to include context</span>
      </div>
    );
  }

  // Table detection: show green Table badge instead of plain address
  if (tableName) {
    return (
      <div
        title={`Excel Table "${tableName}" detected — ${address} (${rows}×${cols})`}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '4px',
          padding: '2px 8px',
          background: '#1a3020',
          border: '1px solid #2e5040',
          borderRadius: '12px',
          fontSize: '11px',
          color: '#6fcf97',
          whiteSpace: 'nowrap',
          maxWidth: '100%',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        <span>📋</span>
        <span>Table: {tableName} ({rows}×{cols})</span>
      </div>
    );
  }

  // Plain range — existing gold badge
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
