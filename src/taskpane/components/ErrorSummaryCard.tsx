import React from 'react';

export interface CellIssue {
  address: string;
  type: 'error' | 'hardcoded';
  detail: string;
}

interface ErrorSummaryCardProps {
  issues: CellIssue[];
  onClose: () => void;
}

const ErrorSummaryCard: React.FC<ErrorSummaryCardProps> = ({ issues, onClose }) => {
  const errors = issues.filter((i) => i.type === 'error');
  const hardcoded = issues.filter((i) => i.type === 'hardcoded');

  return (
    <div
      style={{
        border: '1px solid #5a2e00',
        borderRadius: '6px',
        background: '#1a1208',
        overflow: 'hidden',
        margin: '6px 0',
      }}
    >
      {/* Title bar */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          padding: '7px 10px',
          background: '#0f0d07',
          borderBottom: '1px solid #5a2e00',
        }}
      >
        <span style={{ color: '#e09040', fontWeight: '600', fontSize: '12px' }}>
          ⚠ Formula Issues — {issues.length} found
        </span>
        <button
          onClick={onClose}
          aria-label="Close error summary"
          style={{
            background: 'none',
            border: 'none',
            color: '#8899aa',
            cursor: 'pointer',
            fontSize: '14px',
            lineHeight: 1,
            padding: '0 2px',
          }}
        >
          ✕
        </button>
      </div>

      <div style={{ padding: '8px 10px' }}>
        {/* Error group */}
        {errors.length > 0 && (
          <IssueGroup
            title={`Formula Errors (${errors.length})`}
            issues={errors}
            color="#e07070"
            bgColor="#2a1515"
            borderColor="#5a2020"
          />
        )}

        {/* Hardcoded group */}
        {hardcoded.length > 0 && (
          <IssueGroup
            title={`Hardcoded Values in Formula Columns (${hardcoded.length})`}
            issues={hardcoded}
            color="#e09040"
            bgColor="#1e1810"
            borderColor="#5a3e00"
          />
        )}

        {issues.length === 0 && (
          <p style={{ color: '#7ec8a0', fontSize: '12px', margin: 0 }}>
            ✓ No issues found in selected range.
          </p>
        )}
      </div>
    </div>
  );
};

interface IssueGroupProps {
  title: string;
  issues: CellIssue[];
  color: string;
  bgColor: string;
  borderColor: string;
}

const IssueGroup: React.FC<IssueGroupProps> = ({
  title,
  issues,
  color,
  bgColor,
  borderColor,
}) => (
  <div style={{ marginBottom: '8px' }}>
    <p
      style={{
        fontSize: '11px',
        fontWeight: '600',
        color,
        marginBottom: '4px',
        textTransform: 'uppercase',
        letterSpacing: '0.4px',
      }}
    >
      {title}
    </p>
    {issues.map((issue) => (
      <div
        key={`${issue.address}-${issue.type}`}
        style={{
          display: 'flex',
          gap: '8px',
          alignItems: 'flex-start',
          padding: '5px 8px',
          background: bgColor,
          border: `1px solid ${borderColor}`,
          borderRadius: '4px',
          marginBottom: '3px',
        }}
      >
        <span
          style={{
            fontFamily: 'monospace',
            fontWeight: '700',
            fontSize: '12px',
            color,
            flexShrink: 0,
          }}
        >
          {issue.address}
        </span>
        <span style={{ fontSize: '12px', color: '#c8c0b0', wordBreak: 'break-word' }}>
          {issue.detail}
        </span>
      </div>
    ))}
  </div>
);

export default ErrorSummaryCard;
