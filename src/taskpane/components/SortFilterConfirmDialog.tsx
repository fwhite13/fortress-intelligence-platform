import React from 'react';
import type { SortFilterSpec } from '../services/sortFilterBuilder';
import { clearFilter } from '../services/sortFilterBuilder';

interface SortFilterConfirmDialogProps {
  spec: SortFilterSpec | null;
  onConfirm: () => void;
  onCancel: () => void;
  applying: boolean;
}

/** Human-readable description of a filter criterion */
function describeCriterion(criterion: {
  columnIndex: number;
  filterType: 'values' | 'top' | 'custom';
  values?: string[];
  topCount?: number;
  topPercent?: boolean;
  operator1?: string;
  value1?: string | number;
  operator2?: string;
  value2?: string | number;
}): string {
  switch (criterion.filterType) {
    case 'values':
      return criterion.values && criterion.values.length > 0
        ? `values: ${criterion.values.slice(0, 5).join(', ')}${criterion.values.length > 5 ? ` (+${criterion.values.length - 5} more)` : ''}`
        : 'selected values';
    case 'top':
      return criterion.topPercent
        ? `top ${criterion.topCount ?? 10}%`
        : `top ${criterion.topCount ?? 10} items`;
    case 'custom':
      if (criterion.operator1) {
        let desc = `${criterion.operator1} ${criterion.value1 ?? ''}`;
        if (criterion.operator2) {
          desc += ` and ${criterion.operator2} ${criterion.value2 ?? ''}`;
        }
        return desc;
      }
      return 'custom criteria';
    default:
      return 'criteria';
  }
}

const SortFilterConfirmDialog: React.FC<SortFilterConfirmDialogProps> = ({
  spec,
  onConfirm,
  onCancel,
  applying,
}) => {
  if (!spec) return null;

  const hasSortFields = spec.sort && spec.sort.fields.length > 0;
  const hasFilterCriteria = spec.filter && spec.filter.criteria.length > 0;

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        background: 'rgba(0,0,0,0.65)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 2000,
        padding: '16px',
      }}
    >
      <div
        style={{
          background: '#0f1720',
          border: '1px solid #2e3f54',
          borderRadius: '12px',
          padding: '20px',
          width: '100%',
          maxWidth: '340px',
          boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
        }}
      >
        {/* Title */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            marginBottom: '14px',
          }}
        >
          <span style={{ fontSize: '18px' }}>🔀</span>
          <span
            style={{
              color: '#d4af37',
              fontWeight: '700',
              fontSize: '14px',
            }}
          >
            Sort / Filter
          </span>
        </div>

        {/* Sort section */}
        {hasSortFields && (
          <div style={{ marginBottom: '14px' }}>
            <div
              style={{
                fontSize: '11px',
                color: '#556677',
                textTransform: 'uppercase',
                letterSpacing: '0.07em',
                fontWeight: '600',
                marginBottom: '6px',
              }}
            >
              Sort
            </div>
            {spec.sort!.fields.map((f, idx) => (
              <div
                key={idx}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '6px',
                  padding: '5px 8px',
                  background: '#1a2332',
                  borderRadius: '6px',
                  marginBottom: '4px',
                  fontSize: '12px',
                  color: '#c8d8e8',
                }}
              >
                <span
                  style={{
                    background: '#d4af37',
                    color: '#0f1720',
                    borderRadius: '3px',
                    padding: '1px 5px',
                    fontWeight: '700',
                    fontSize: '10px',
                  }}
                >
                  Col {f.columnIndex + 1}
                </span>
                <span>{f.ascending ? '↑ Ascending' : '↓ Descending'}</span>
              </div>
            ))}
            {spec.sort!.hasHeaders && (
              <div style={{ fontSize: '11px', color: '#556677', marginTop: '2px' }}>
                First row treated as header
              </div>
            )}
          </div>
        )}

        {/* Filter section */}
        {hasFilterCriteria && (
          <div style={{ marginBottom: '14px' }}>
            <div
              style={{
                fontSize: '11px',
                color: '#556677',
                textTransform: 'uppercase',
                letterSpacing: '0.07em',
                fontWeight: '600',
                marginBottom: '6px',
              }}
            >
              Filter
            </div>
            {spec.filter!.criteria.map((c, idx) => (
              <div
                key={idx}
                style={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  gap: '6px',
                  padding: '5px 8px',
                  background: '#1a2332',
                  borderRadius: '6px',
                  marginBottom: '4px',
                  fontSize: '12px',
                  color: '#c8d8e8',
                }}
              >
                <span
                  style={{
                    background: '#2e6b9e',
                    color: '#e8f0f8',
                    borderRadius: '3px',
                    padding: '1px 5px',
                    fontWeight: '700',
                    fontSize: '10px',
                    flexShrink: 0,
                  }}
                >
                  Col {c.columnIndex + 1}
                </span>
                <span style={{ lineHeight: 1.4 }}>{describeCriterion(c)}</span>
              </div>
            ))}
          </div>
        )}

        {/* Action buttons */}
        <div style={{ display: 'flex', gap: '8px', marginTop: '4px' }}>
          <button
            onClick={onConfirm}
            disabled={applying}
            style={{
              flex: 1,
              background: applying ? '#2e3f54' : '#d4af37',
              color: applying ? '#8899aa' : '#0f1720',
              border: 'none',
              borderRadius: '8px',
              padding: '9px 0',
              fontWeight: '700',
              fontSize: '13px',
              cursor: applying ? 'not-allowed' : 'pointer',
              transition: 'background 0.15s',
            }}
          >
            {applying ? 'Applying…' : 'Apply'}
          </button>
          <button
            onClick={onCancel}
            disabled={applying}
            style={{
              flex: 1,
              background: '#2e3f54',
              color: '#c8d8e8',
              border: 'none',
              borderRadius: '8px',
              padding: '9px 0',
              fontWeight: '600',
              fontSize: '13px',
              cursor: applying ? 'not-allowed' : 'pointer',
            }}
          >
            Cancel
          </button>
        </div>

        {/* Clear Filter button — independent, no confirmation */}
        <button
          onClick={async () => {
            try {
              await clearFilter();
            } catch {
              // silent — sheet may not have a filter active
            }
            onCancel();
          }}
          disabled={applying}
          style={{
            width: '100%',
            marginTop: '8px',
            background: 'transparent',
            color: '#8899aa',
            border: '1px solid #2e3f54',
            borderRadius: '8px',
            padding: '7px 0',
            fontSize: '12px',
            cursor: applying ? 'not-allowed' : 'pointer',
            transition: 'color 0.15s, border-color 0.15s',
          }}
          onMouseEnter={(e) => {
            (e.target as HTMLButtonElement).style.color = '#c8d8e8';
            (e.target as HTMLButtonElement).style.borderColor = '#8899aa';
          }}
          onMouseLeave={(e) => {
            (e.target as HTMLButtonElement).style.color = '#8899aa';
            (e.target as HTMLButtonElement).style.borderColor = '#2e3f54';
          }}
        >
          Clear Current Filter
        </button>
      </div>
    </div>
  );
};

export default SortFilterConfirmDialog;
