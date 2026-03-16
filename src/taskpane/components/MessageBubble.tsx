import React from 'react';
import type { Message } from '../hooks/useChat';
import type { ParsedTable } from '../services/suggestionParser';

interface MessageBubbleProps {
  message: Message;
  streaming?: boolean;
  onWriteTable?: (tableData: ParsedTable) => void;
}

/** Very lightweight markdown → HTML: handles **bold**, `code`, and newlines. No full parser needed for MVP. */
function simpleMarkdown(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/`([^`]+)`/g, '<code style="background:#0f1720;padding:1px 4px;border-radius:3px;font-size:12px;">$1</code>')
    .replace(/\n/g, '<br />');
}

const TableRenderer: React.FC<{
  tableData: ParsedTable;
  onWrite: () => void;
}> = ({ tableData, onWrite }) => {
  return (
    <div style={{ marginTop: '6px', overflowX: 'auto', maxWidth: '100%' }}>
      <table
        style={{
          borderCollapse: 'collapse',
          fontSize: '11px',
          width: '100%',
          color: '#e8edf3',
        }}
      >
        <thead>
          <tr>
            {tableData.headers.map((h, i) => (
              <th
                key={i}
                style={{
                  padding: '4px 8px',
                  background: '#1a3a5f',
                  borderBottom: '1px solid #2e5080',
                  textAlign: 'left',
                  fontWeight: '600',
                  whiteSpace: 'nowrap',
                  color: '#d4af37',
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {tableData.rows.map((row, ri) => (
            <tr
              key={ri}
              style={{
                background: ri % 2 === 0 ? '#131f2e' : '#0f1720',
              }}
            >
              {row.map((cell, ci) => (
                <td
                  key={ci}
                  style={{
                    padding: '3px 8px',
                    borderBottom: '1px solid #1a2840',
                    whiteSpace: 'nowrap',
                    textAlign: typeof cell === 'number' ? 'right' : 'left',
                  }}
                >
                  {cell === null || cell === undefined ? '' : String(cell)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>

      {/* Write to Sheet button */}
      <button
        onClick={onWrite}
        title="Write this table to the active worksheet"
        style={{
          marginTop: '6px',
          padding: '4px 10px',
          background: '#1e3a5f',
          border: '1px solid #2e5080',
          borderRadius: '4px',
          color: '#d4af37',
          fontSize: '11px',
          fontWeight: '600',
          cursor: 'pointer',
          display: 'flex',
          alignItems: 'center',
          gap: '4px',
        }}
      >
        <span>↓</span>
        <span>Write to Sheet</span>
      </button>
    </div>
  );
};

const MessageBubble: React.FC<MessageBubbleProps> = ({ message, streaming, onWriteTable }) => {
  const isUser = message.role === 'user';
  const isStreaming = streaming ?? message.streaming ?? false;
  const hasTable = !isUser && !isStreaming && message.tableData != null;

  // For assistant messages with a parsed table, strip raw markdown table text from display
  let displayContent = message.content;
  if (hasTable && message.tableData) {
    displayContent = message.content
      .replace(/\|.+\|\s*\n\|[-| :]+\|\s*\n(?:\|.+\|\s*\n?)+/g, '')
      .trim();
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: isUser ? 'flex-end' : 'flex-start',
        padding: '4px 8px',
        animation: 'fadeIn 0.2s ease-out',
      }}
    >
      {/* Role label */}
      <span
        style={{
          fontSize: '10px',
          fontWeight: '600',
          color: isUser ? '#8899aa' : '#d4af37',
          marginBottom: '2px',
          textTransform: 'uppercase',
          letterSpacing: '0.5px',
        }}
      >
        {isUser ? 'You' : 'FAIT'}
      </span>

      {/* Bubble */}
      <div
        style={{
          maxWidth: '90%',
          padding: '8px 12px',
          borderRadius: isUser ? '12px 12px 4px 12px' : '12px 12px 12px 4px',
          background: isUser ? '#243447' : '#1e3a5f',
          border: `1px solid ${isUser ? '#2e3f54' : '#2e5080'}`,
          color: '#e8edf3',
          fontSize: '13px',
          lineHeight: 1.6,
          wordBreak: 'break-word',
          position: 'relative',
        }}
      >
        {/* Text content — suppress when table present and no remaining text */}
        {(displayContent.length > 0 || isStreaming) && (
          <span dangerouslySetInnerHTML={{ __html: simpleMarkdown(displayContent) }} />
        )}

        {/* Streaming cursor */}
        {isStreaming && (
          <span
            aria-hidden="true"
            style={{
              display: 'inline-block',
              width: '2px',
              height: '13px',
              background: '#d4af37',
              marginLeft: '2px',
              verticalAlign: 'text-bottom',
              animation: 'blink 1s step-end infinite',
            }}
          />
        )}

        {/* Rendered table + Write button */}
        {hasTable && message.tableData && onWriteTable && (
          <TableRenderer
            tableData={message.tableData}
            onWrite={() => onWriteTable(message.tableData!)}
          />
        )}
      </div>

      <style>{`
        @keyframes blink {
          0%, 100% { opacity: 1; }
          50% { opacity: 0; }
        }
        @keyframes fadeIn {
          from { opacity: 0; transform: translateY(4px); }
          to   { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};

export default MessageBubble;
