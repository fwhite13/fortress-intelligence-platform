import React, { useState, useEffect } from 'react';
import { useChat } from '../hooks/useChat';
import { getSelectedRange } from '../services/excelReader';
import { formatContext } from '../services/contextFormatter';
import MessageList from './MessageList';
import ChatInput from './ChatInput';
import ModelPicker from './ModelPicker';
import ContextIndicator from './ContextIndicator';
import ErrorBanner from './ErrorBanner';

interface ChatPanelProps {
  apiKey: string;
  onOpenSettings: () => void;
}

const ChatPanel: React.FC<ChatPanelProps> = ({ apiKey, onOpenSettings }) => {
  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');
  const [includeSelection, setIncludeSelection] = useState(true);
  const [selectionInfo, setSelectionInfo] = useState<{
    address: string;
    rows: number;
    cols: number;
  } | null>(null);

  const { messages, loading, error, send, clearError } = useChat(apiKey, model);

  // Refresh selection info on mount and periodically
  useEffect(() => {
    const refresh = async () => {
      try {
        const ctx = await getSelectedRange();
        setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
      } catch {
        setSelectionInfo(null);
      }
    };
    refresh();

    // Refresh every 2s to keep indicator current (lightweight — just loads address + counts)
    const interval = setInterval(refresh, 2000);
    return () => clearInterval(interval);
  }, []);

  const handleSend = async (text: string) => {
    let context: string | undefined;

    if (includeSelection) {
      try {
        const ctx = await getSelectedRange();
        if (ctx.rows > 0 && ctx.cols > 0) {
          context = formatContext(ctx);
          setSelectionInfo({ address: ctx.address, rows: ctx.rows, cols: ctx.cols });
        }
      } catch {
        // Non-fatal: proceed without context
      }
    }

    await send(text, context);
  };

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        minWidth: '300px',
        background: '#1a2332',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 12px',
          borderBottom: '1px solid #2e3f54',
          background: '#0f1720',
          flexShrink: 0,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ color: '#d4af37', fontWeight: '700', fontSize: '14px' }}>
            🏰 FAIT
          </span>
          <span style={{ color: '#556677', fontSize: '11px' }}>for Excel</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <ModelPicker model={model} onChange={setModel} />
          <button
            onClick={onOpenSettings}
            title="Settings"
            aria-label="Open settings"
            style={{
              background: 'none',
              border: 'none',
              color: '#8899aa',
              cursor: 'pointer',
              fontSize: '14px',
              padding: '2px 4px',
              borderRadius: '4px',
              lineHeight: 1,
            }}
          >
            ⚙
          </button>
        </div>
      </div>

      {/* Context indicator bar */}
      {includeSelection && selectionInfo && (
        <div
          style={{
            padding: '4px 8px',
            borderBottom: '1px solid #2e3f54',
            background: '#1a2332',
            flexShrink: 0,
          }}
        >
          <ContextIndicator
            address={selectionInfo.address}
            rows={selectionInfo.rows}
            cols={selectionInfo.cols}
            visible={includeSelection}
          />
        </div>
      )}

      {/* Error banner */}
      {error && (
        <ErrorBanner message={error} onDismiss={clearError} />
      )}

      {/* Message list */}
      <MessageList messages={messages} loading={loading} />

      {/* Input area */}
      <ChatInput
        onSend={handleSend}
        disabled={loading}
        includeSelection={includeSelection}
        onToggleSelection={() => setIncludeSelection((v) => !v)}
      />
    </div>
  );
};

export default ChatPanel;
