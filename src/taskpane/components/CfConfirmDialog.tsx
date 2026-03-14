import React from 'react';
import type { CfSpec } from '../services/cfBuilder';

interface CfConfirmDialogProps {
  spec: CfSpec | null;
  onConfirm: () => void;
  onCancel: () => void;
  applying: boolean;
}

function describeRule(spec: CfSpec): string {
  const rule = spec.rule;
  switch (rule.kind) {
    case 'colorScale':
      return `Color scale from ${rule.min} to ${rule.max}`;
    case 'dataBar':
      return `Data bars (${rule.color})`;
    case 'iconSet':
      return `Icon set: ${rule.style}`;
    case 'topN':
      return rule.percent
        ? `Top ${rule.rank}% highlighted`
        : `Top ${rule.rank} items highlighted`;
    case 'formula':
      return `Custom formula: ${rule.formula}`;
    case 'cellValue':
      return `Cell values ${rule.operator} ${rule.value1}${rule.value2 !== undefined ? ` and ${rule.value2}` : ''}`;
    default:
      return 'Custom rule';
  }
}

const CfConfirmDialog: React.FC<CfConfirmDialogProps> = ({
  spec,
  onConfirm,
  onCancel,
  applying,
}) => {
  if (!spec) return null;

  return (
    <div style={overlayStyle}>
      <div style={dialogStyle}>
        {/* Title bar */}
        <div style={titleBarStyle}>
          <span style={{ color: '#d4af37', fontWeight: '700', fontSize: '13px' }}>
            🎨 Apply Conditional Formatting
          </span>
        </div>

        {/* Body */}
        <div style={{ padding: '16px' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <tbody>
              <Row label="Range" value={spec.range} mono />
              <Row label="Rule"  value={describeRule(spec)} />
            </tbody>
          </table>

          {/* Buttons */}
          <div style={buttonRowStyle}>
            <button onClick={onCancel} disabled={applying} style={secondaryBtnStyle}>
              Cancel
            </button>
            <button onClick={onConfirm} disabled={applying} style={primaryBtnStyle}>
              {applying ? 'Applying…' : 'Apply Formatting'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};

const Row: React.FC<{ label: string; value: string; mono?: boolean }> = ({ label, value, mono }) => (
  <tr>
    <td style={labelCellStyle}>{label}</td>
    <td style={{ ...valueCellStyle, ...(mono ? { fontFamily: 'monospace', color: '#d4af37' } : {}) }}>
      {value}
    </td>
  </tr>
);

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
  maxWidth: '400px',
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

const labelCellStyle: React.CSSProperties = {
  padding: '6px 8px',
  fontSize: '11px',
  color: '#8899aa',
  fontWeight: '600',
  textTransform: 'uppercase',
  letterSpacing: '0.5px',
  width: '60px',
  verticalAlign: 'top',
  borderBottom: '1px solid #2e3f54',
};

const valueCellStyle: React.CSSProperties = {
  padding: '6px 8px',
  fontSize: '12px',
  color: '#e8edf3',
  borderBottom: '1px solid #2e3f54',
  wordBreak: 'break-word',
};

const buttonRowStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  gap: '8px',
  marginTop: '14px',
};

const baseBtnStyle: React.CSSProperties = {
  padding: '7px 14px',
  borderRadius: '5px',
  fontSize: '12px',
  fontWeight: '600',
  cursor: 'pointer',
  border: 'none',
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

export default CfConfirmDialog;
