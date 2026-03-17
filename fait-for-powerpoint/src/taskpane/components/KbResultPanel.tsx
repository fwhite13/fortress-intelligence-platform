import React, { useState } from 'react';

export interface KbResult {
  content: string;
  source: string;
  score: number;
}

interface KbResultCardProps {
  result: KbResult;
  index: number;
  onInsertToChat?: (content: string) => void;
  onApplyToShape?: (content: string, source: string) => void;
  selectedShapeId?: string | null;
}

const TRUNCATE_LEN = 200;

const KbResultCard: React.FC<KbResultCardProps> = ({
  result,
  index,
  onInsertToChat,
  onApplyToShape,
  selectedShapeId,
}) => {
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

          {/* Action buttons */}
          {(onInsertToChat || (onApplyToShape && selectedShapeId)) && (
            <div style={{ display: 'flex', gap: '6px', marginTop: '8px' }}>
              {onInsertToChat && (
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    onInsertToChat(result.content);
                  }}
                  style={{
                    flex: 1,
                    background: '#1e3050',
                    border: '1px solid #2e4a6a',
                    color: '#a8c8e8',
                    borderRadius: '3px',
                    padding: '4px 8px',
                    fontSize: '11px',
                    cursor: 'pointer',
                  }}
                >
                  ↳ Insert to Chat
                </button>
              )}
              {onApplyToShape && selectedShapeId && (
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    onApplyToShape(result.content, result.source);
                  }}
                  style={{
                    flex: 1,
                    background: '#1e3820',
                    border: '1px solid #2e5230',
                    color: '#88c888',
                    borderRadius: '3px',
                    padding: '4px 8px',
                    fontSize: '11px',
                    cursor: 'pointer',
                  }}
                >
                  ▶ Apply to Shape
                </button>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  );
};

interface KbResultPanelProps {
  results: KbResult[];
  loading: boolean;
  onInsertToChat?: (content: string) => void;
  onApplyToShape?: (content: string, source: string) => void;
  selectedShapeId?: string | null;
}

const KbResultPanel: React.FC<KbResultPanelProps> = ({
  results,
  loading,
  onInsertToChat,
  onApplyToShape,
  selectedShapeId,
}) => {
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
          <KbResultCard
            key={`${r.source}-${i}`}
            result={r}
            index={i}
            onInsertToChat={onInsertToChat}
            onApplyToShape={onApplyToShape}
            selectedShapeId={selectedShapeId}
          />
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
