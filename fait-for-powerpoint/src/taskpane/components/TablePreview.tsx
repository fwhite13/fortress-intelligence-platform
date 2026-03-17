import React from 'react';
import type { PptTableSpec } from '../services/pptSpecParser';

interface TablePreviewProps {
  spec: PptTableSpec;
  onAccept: () => void;
  onReject: () => void;
  loading?: boolean;
}

const TablePreview: React.FC<TablePreviewProps> = ({
  spec,
  onAccept,
  onReject,
  loading = false,
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
      📊 Table Preview — {spec.rowCount} rows × {spec.columnCount} cols
    </div>
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: '10px', width: '100%' }}>
        <thead>
          <tr>
            {spec.headers.map((h, i) => (
              <th
                key={i}
                style={{
                  background: '#1F3864',
                  color: '#fff',
                  padding: '3px 6px',
                  border: '1px solid #2e3f54',
                  fontWeight: '600',
                  whiteSpace: 'nowrap',
                }}
              >
                {h || '—'}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {spec.values.slice(0, 3).map((row, ri) => (
            <tr key={ri}>
              {row.map((cell, ci) => (
                <td
                  key={ci}
                  style={{
                    padding: '2px 6px',
                    border: '1px solid #2e3f54',
                    color: '#c8d8e8',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {cell || '—'}
                </td>
              ))}
            </tr>
          ))}
          {spec.values.length > 3 && (
            <tr>
              <td
                colSpan={spec.columnCount}
                style={{
                  padding: '2px 6px',
                  color: '#556677',
                  fontSize: '10px',
                  border: '1px solid #2e3f54',
                }}
              >
                +{spec.values.length - 3} more rows
              </td>
            </tr>
          )}
        </tbody>
      </table>
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
        {loading ? 'Creating…' : '✓ Create Table'}
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

export default TablePreview;
