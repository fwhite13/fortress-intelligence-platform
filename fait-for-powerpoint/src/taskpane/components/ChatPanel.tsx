import React, { useState, useEffect, useRef } from 'react';
import { useChat } from '../hooks/useChat';
import { usePptContext } from '../hooks/usePptContext';
import { getSlideContext, formatSlideContext } from '../services/pptReader';
import { applyTextToShape, PptWriteError } from '../services/pptWriter';
import MessageList from './MessageList';
import ChatInput from './ChatInput';
import ErrorBanner from './ErrorBanner';
import ShapePreview from './ShapePreview';
import SlashCommandPicker from './SlashCommandPicker';

interface ChatPanelProps {
  apiKey: string;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
  onOpenSettings: () => void;
}

const ChatPanel: React.FC<ChatPanelProps> = ({
  apiKey,
  model,
  kbToggles,
  projectId,
  onOpenSettings,
}) => {
  const { slideContext, refresh: refreshSlideContext } = usePptContext();

  // Apply to Shape state
  const [pendingApplyText, setPendingApplyText] = useState<string | null>(null);
  const [applyLoading, setApplyLoading] = useState(false);
  const [applyError, setApplyError] = useState<string | null>(null);

  // Input state (lifted for slash commands)
  const [inputText, setInputText] = useState('');
  const chatInputAreaRef = useRef<HTMLDivElement>(null);

  const showSlashPicker = inputText.startsWith('/');
  const slashQuery = showSlashPicker ? inputText.slice(1) : '';

  const {
    messages,
    loading,
    error,
    send,
    clearError,
    setMessages,
  } = useChat(apiKey, model, kbToggles, projectId);

  const handleSend = async (text: string) => {
    let context: string | undefined;

    try {
      const ctx = await getSlideContext();
      if (ctx.slideNumber > 0) {
        context = formatSlideContext(ctx);
      }
    } catch {
      // Non-fatal
    }

    await send(text, context);
  };

  // Watch messages for Apply to Shape trigger
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
      const prevUserMsg = [...messages].reverse().find((m) => m.role === 'user');
      if (prevUserMsg) {
        const lower = prevUserMsg.content.toLowerCase();
        if (
          lower.includes('apply') ||
          lower.includes('write to shape') ||
          lower.includes('write to slide') ||
          lower.includes('update shape') ||
          lower.includes('put this in')
        ) {
          setPendingApplyText(lastMsg.content);
          setApplyError(null);
        }
      }
    }
  }, [messages]);

  const handleApplyToShape = async () => {
    if (!pendingApplyText || !slideContext?.selectedShapeId) {
      setApplyError(
        slideContext?.selectedShapeId
          ? 'No text to apply.'
          : 'Select a shape in PowerPoint first.'
      );
      return;
    }

    setApplyLoading(true);
    setApplyError(null);

    try {
      await applyTextToShape(slideContext.selectedShapeId, pendingApplyText);
      setPendingApplyText(null);
      await refreshSlideContext();
    } catch (e) {
      if (e instanceof PptWriteError) {
        if (e.code === 'SHAPE_NOT_FOUND') {
          setApplyError('Shape not found — re-select the shape and try again.');
        } else if (e.code === 'NO_TEXT_FRAME') {
          setApplyError('Selected shape cannot hold text.');
        } else {
          setApplyError('Write failed — try again.');
        }
      } else {
        setApplyError('Write failed — try again.');
      }
    } finally {
      setApplyLoading(false);
    }
  };

  const handleApplyDiscard = () => {
    setPendingApplyText(null);
    setApplyError(null);
  };

  const handleClearHistory = () => {
    setMessages([]);
  };

  const modelLabel = model === 'haiku' ? 'Haiku' : 'Sonnet';

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
          <span style={{ color: '#556677', fontSize: '11px' }}>for PowerPoint</span>
        </div>

        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
          {/* Clear History */}
          <button
            onClick={handleClearHistory}
            title="Clear conversation history"
            aria-label="Clear conversation history"
            style={headerBtnStyle}
          >
            🗑
          </button>

          {/* Model indicator */}
          <button
            onClick={onOpenSettings}
            title={`Model: ${modelLabel} — click to change in Settings`}
            style={{
              ...headerBtnStyle,
              fontSize: '11px',
              color: '#8899aa',
              display: 'flex',
              alignItems: 'center',
              gap: '3px',
            }}
          >
            <span style={{ color: '#556677' }}>Model:</span>{' '}
            <span style={{ color: '#d4af37' }}>{modelLabel}</span>
          </button>

          {/* Settings gear */}
          <button
            onClick={onOpenSettings}
            title="Settings"
            aria-label="Open settings"
            style={headerBtnStyle}
          >
            ⚙
          </button>
        </div>
      </div>

      {/* Slide context indicator */}
      {slideContext && (
        <div
          style={{
            padding: '4px 12px',
            borderBottom: '1px solid #2e3f54',
            background: '#0f1720',
            fontSize: '11px',
            color: slideContext.selectedShapeId ? '#d4af37' : '#556677',
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            flexShrink: 0,
          }}
        >
          <span>🖼</span>
          <span>
            Slide {slideContext.slideNumber}
            {slideContext.title ? ` — ${slideContext.title.slice(0, 40)}` : ''}
            {slideContext.selectedShapeId
              ? ` · ✓ shape selected`
              : ` · no shape selected`}
          </span>
        </div>
      )}

      {/* Error banner */}
      {error && <ErrorBanner message={error} onDismiss={clearError} />}

      {/* Scrollable message area */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <MessageList
          messages={messages}
          loading={loading}
        />
      </div>

      {/* Shape preview (Apply to Shape) */}
      {pendingApplyText && (
        <ShapePreview
          pendingText={pendingApplyText}
          targetShapeName={
            slideContext?.shapes.find((s) => s.isSelected)?.name ?? 'selected shape'
          }
          onAccept={handleApplyToShape}
          onReject={handleApplyDiscard}
          loading={applyLoading}
        />
      )}

      {applyError && (
        <div
          style={{
            padding: '4px 12px',
            background: '#1a0f0f',
            color: '#e07070',
            fontSize: '11px',
            flexShrink: 0,
          }}
        >
          {applyError}
        </div>
      )}

      {/* Input area */}
      <div ref={chatInputAreaRef} style={{ position: 'relative', flexShrink: 0 }}>
        {showSlashPicker && (
          <SlashCommandPicker
            query={slashQuery}
            onSelect={(prompt, _name) => {
              setInputText(prompt);
            }}
            onClose={() => setInputText('')}
          />
        )}

        <ChatInput
          value={inputText}
          onChange={setInputText}
          onSend={(text) => {
            setInputText('');
            handleSend(text);
          }}
          disabled={loading}
          includeSelection={true}
          onToggleSelection={() => {}}
        />
      </div>

      <style>{`
        @keyframes fadeIn {
          from { opacity: 0; transform: translateY(4px); }
          to   { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  );
};

const headerBtnStyle: React.CSSProperties = {
  background: 'none',
  border: 'none',
  color: '#8899aa',
  cursor: 'pointer',
  fontSize: '14px',
  padding: '2px 4px',
  borderRadius: '4px',
  lineHeight: 1,
};

export default ChatPanel;
