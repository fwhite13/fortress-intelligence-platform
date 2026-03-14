import React, { useState } from 'react';

export interface KbResult {
  content: string;
  source: string;
  score: number;
}

interface KbResultPanelProps {
  results: KbResult[];
  loading: boolean;
}

const TRUNCATE_LEN = 200;

const KbResultCard: React.FC<{ result: KbResult; index: number }> = ({ result, index }) => {
  const [expanded, setExpanded] = useState(false);
  const [showMore, setShowMore] = useState(false);

  const truncated = result.content.length > TRUNCATE_LEN && !showMore;
  const displayContent = truncated
    ? result.content.slice(0, TRUNCATE_LEN) + '…'
    : result.content;

  return (
    <div
      style={{
        border: '1px solid #2e3f54',
        borderRadius: '5px',
        marginBottom: '6px',
        overflow: 'hidden',
      }}
    >
      {/* Header / toggle */}
      <button
        onClick={() => setExpanded((v) => !v)}
        aria-expanded={expanded}
        style={{
          width: '100%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '7px 10px',
          background: expanded ? '#162030' : '#0f1720',
          border: 'none',
          cursor: 'pointer',
          textAlign: 'left',
          gap: '8px',
        }}
      >
        <span
          style={{
            fontSize: '11px',
            fontWeight: '600',
            color: '#d4af37',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            flex: 1,
          }}
          title={result.source}
        >
          {index + 1}. {result.source || 'Unknown source'}
        </span>
        <span
          style={{
            fontSize: '10px',
            color: '#8899aa',
            flexShrink: 0,
            fontFamily: 'monospace',
          }}
        >
          {(result.score * 100).toFixed(0)}%
        </span>
        <span style={{ color: '#556677', fontSize: '10px', flexShrink: 0 }}>
          {expanded ? '▲' : '▼'}
        </span>
      </button>

      {/* Body */}
      {expanded && (
        <div
          style={{
            padding: '8px 10px',
            background: '#111d2b',
            borderTop: '1px solid #2e3f54',
          }}
        >
          <p
            style={{
              margin: 0,
              fontSize: '12px',
              color: '#c8d8e8',
              lineHeight: 1.6,
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}
          >
            {displayContent}
          </p>
          {result.content.length > TRUNCATE_LEN && (
            <button
              onClick={(e) => {
                e.stopPropagation();
                setShowMore((v) => !v);
              }}
              style={{
                marginTop: '6px',
                background: 'none',
                border: 'none',
                color: '#d4af37',
                fontSize: '11px',
                cursor: 'pointer',
                padding: 0,
                textDecoration: 'underline',
              }}
            >
              {showMore ? 'show less' : 'show more'}
            </button>
          )}
        </div>
      )}
    </div>
  );
};

const KbResultPanel: React.FC<KbResultPanelProps> = ({ results, loading }) => {
  if (loading) {
    return (
      <div style={containerStyle}>
        <div style={headerStyle}>
          <span style={{ color: '#d4af37', fontWeight: '600', fontSize: '12px' }}>
            🔍 FORGE KB
          </span>
        </div>
        <div style={{ padding: '12px', textAlign: 'center', color: '#556677', fontSize: '12px' }}>
          Searching knowledge base…
        </div>
      </div>
    );
  }

  if (results.length === 0) {
    return (
      <div style={containerStyle}>
        <div style={headerStyle}>
          <span style={{ color: '#d4af37', fontWeight: '600', fontSize: '12px' }}>
            🔍 FORGE KB
          </span>
        </div>
        <div style={{ padding: '12px', color: '#556677', fontSize: '12px' }}>
          No results found.
        </div>
      </div>
    );
  }

  return (
    <div style={containerStyle}>
      <div style={headerStyle}>
        <span style={{ color: '#d4af37', fontWeight: '600', fontSize: '12px' }}>
          🔍 FORGE KB
        </span>
        <span style={{ color: '#556677', fontSize: '11px' }}>
          {results.length} result{results.length !== 1 ? 's' : ''}
        </span>
      </div>
      <div style={{ padding: '6px 8px' }}>
        {results.map((r, i) => (
          <KbResultCard key={`${r.source}-${i}`} result={r} index={i} />
        ))}
      </div>
    </div>
  );
};

const containerStyle: React.CSSProperties = {
  border: '1px solid #2e3f54',
  borderRadius: '6px',
  background: '#131e2b',
  overflow: 'hidden',
  margin: '6px 0',
};

const headerStyle: React.CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'center',
  padding: '7px 10px',
  background: '#0f1720',
  borderBottom: '1px solid #2e3f54',
};

export default KbResultPanel;
