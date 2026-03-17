import React, { useState, useEffect, useRef } from 'react';
import { useChat } from '../hooks/useChat';
import { usePptContext } from '../hooks/usePptContext';
import { getSlideContext, formatSlideContext, getAllSlidesContext, getSlideNotes, formatDeckContext } from '../services/pptReader';
import { applyTextToShape, PptWriteError, writeNotes, PptNotesError } from '../services/pptWriter';
import { parseNotesSpec, stripAllSpecs } from '../services/pptNotesParser';
import { searchKb } from '../services/faitApi';
import type { KbResult } from './KbResultPanel';
import KbResultPanel from './KbResultPanel';
import NotesPreview from './NotesPreview';
import type { PptNotesSpec } from '../services/pptNotesParser';
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

  // ── Sprint 2: FORGE search ────────────────────────────────────────────────
  const [showForgeSearch, setShowForgeSearch] = useState(false);
  const [forgeQuery, setForgeQuery] = useState('');
  const [forgeLoading, setForgeLoading] = useState(false);
  const [forgeResults, setForgeResults] = useState<KbResult[] | null>(null);
  const forgeInputRef = useRef<HTMLInputElement>(null);

  // ── Sprint 2: Speaker notes ───────────────────────────────────────────────
  const [pendingNotes, setPendingNotes] = useState<PptNotesSpec | null>(null);
  const [notesLoading, setNotesLoading] = useState(false);
  const [notesError, setNotesError] = useState<string | null>(null);

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

  useEffect(() => {
    if (showForgeSearch) {
      setTimeout(() => forgeInputRef.current?.focus(), 50);
    }
  }, [showForgeSearch]);

  const handleSend = async (text: string) => {
    let context: string | undefined;

    try {
      const ctx = await getSlideContext();
      if (ctx.slideNumber > 0) {
        context = formatSlideContext(ctx);
      }

      // Full deck context (Sprint 2)
      const snapshots = await getAllSlidesContext();
      if (snapshots.length > 0) {
        const deckBlock = formatDeckContext(snapshots);
        context = context ? `${context}\n\n${deckBlock}` : deckBlock;
      }

      // If /notes command: inject existing notes for rewrite context
      const isNotesCommand = text.includes('ppt_notes_spec block');
      if (isNotesCommand) {
        try {
          const existingNotes = await getSlideNotes();
          if (existingNotes) {
            context = (context ?? '') + `\n\nExisting speaker notes:\n${existingNotes}`;
          }
        } catch {
          // Non-fatal
        }
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

  // Sprint 2: Detect ppt_notes_spec block in last assistant message
  useEffect(() => {
    const lastMsg = messages[messages.length - 1];
    if (lastMsg?.role === 'assistant' && !lastMsg.streaming && lastMsg.content.trim()) {
      const spec = parseNotesSpec(lastMsg.content);
      if (spec) {
        setPendingNotes(spec);
        setNotesError(null);
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

  // ── Sprint 2: FORGE handlers ──────────────────────────────────────────────
  const buildKbTypes = (): string[] => {
    const types = Object.entries(kbToggles)
      .filter(([, v]) => v)
      .map(([k]) => k);
    if (!types.includes('personal')) types.push('personal');
    return types;
  };

  const handleForgeSearch = async () => {
    if (!forgeQuery.trim()) return;
    setForgeLoading(true);
    setForgeResults(null);
    try {
      const { results } = await searchKb(
        forgeQuery.trim(),
        apiKey,
        projectId ?? undefined,
        buildKbTypes()
      );
      setForgeResults(results);
    } catch {
      setForgeResults([]);
    } finally {
      setForgeLoading(false);
    }
  };

  const handleForgeKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleForgeSearch();
    if (e.key === 'Escape') {
      setShowForgeSearch(false);
      setForgeQuery('');
      setForgeResults(null);
    }
  };

  const handleForgeInsertToChat = (content: string) => {
    setInputText((prev) =>
      prev ? `${prev}\n\nFORGE context:\n${content}` : `FORGE context:\n${content}`
    );
  };

  const handleForgeApplyToShape = async (content: string, source: string) => {
    if (!slideContext?.selectedShapeId) return;
    try {
      await applyTextToShape(slideContext.selectedShapeId, content, source);
      await refreshSlideContext();
    } catch {
      // Silent failure — shape may have been deselected
    }
  };

  // ── Sprint 2: Notes handlers ──────────────────────────────────────────────
  const handleNotesAccept = async () => {
    if (!pendingNotes) return;
    setNotesLoading(true);
    setNotesError(null);

    try {
      await writeNotes(pendingNotes.speakerNotes);
      setPendingNotes(null);
    } catch (e) {
      if (e instanceof PptNotesError) {
        if (e.code === 'NO_SLIDE') {
          setNotesError('No slide selected — navigate to a slide and try again.');
        } else if (e.code === 'NOTES_UNAVAILABLE') {
          setNotesError('Notes API unavailable on this slide.');
        } else {
          setNotesError('Notes write failed — try again.');
        }
      } else {
        setNotesError('Notes write failed — try again.');
      }
    } finally {
      setNotesLoading(false);
    }
  };

  const handleNotesDiscard = () => {
    setPendingNotes(null);
    setNotesError(null);
  };

  const handleClearHistory = () => {
    setMessages([]);
  };

  const modelLabel = model === 'haiku' ? 'Haiku' : 'Sonnet';

  // Strip ppt_notes_spec blocks from display (raw content stays in state for history)
  const displayMessages = messages.map((msg) =>
    msg.role === 'assistant'
      ? { ...msg, content: stripAllSpecs(msg.content) }
      : msg
  );

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

          {/* FORGE search toggle */}
          <button
            onClick={() => setShowForgeSearch((v) => !v)}
            title="Search FORGE knowledge base"
            aria-label="Ask FORGE"
            style={{
              ...headerBtnStyle,
              color: showForgeSearch ? '#d4af37' : '#8899aa',
            }}
          >
            🔍
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

      {/* FORGE search bar */}
      {showForgeSearch && (
        <div
          style={{
            display: 'flex',
            gap: '6px',
            padding: '6px 12px',
            borderBottom: '1px solid #2e3f54',
            background: '#0f1720',
            flexShrink: 0,
          }}
        >
          <input
            ref={forgeInputRef}
            value={forgeQuery}
            onChange={(e) => setForgeQuery(e.target.value)}
            onKeyDown={handleForgeKeyDown}
            placeholder="Search FORGE knowledge base…"
            style={{
              flex: 1,
              background: '#1a2332',
              border: '1px solid #2e3f54',
              borderRadius: '4px',
              color: '#e8edf3',
              padding: '5px 8px',
              fontSize: '12px',
              outline: 'none',
            }}
          />
          <button
            onClick={handleForgeSearch}
            disabled={forgeLoading || !forgeQuery.trim()}
            style={{
              background: '#d4af37',
              color: '#0f1720',
              border: 'none',
              borderRadius: '4px',
              padding: '5px 10px',
              fontSize: '12px',
              fontWeight: '600',
              cursor: 'pointer',
            }}
          >
            {forgeLoading ? '…' : 'Go'}
          </button>
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
        {/* FORGE KB results */}
        {(forgeLoading || forgeResults !== null) && (
          <div style={{ padding: '4px 8px', flexShrink: 0 }}>
            <KbResultPanel
              results={forgeResults ?? []}
              loading={forgeLoading}
              onInsertToChat={handleForgeInsertToChat}
              onApplyToShape={handleForgeApplyToShape}
              selectedShapeId={slideContext?.selectedShapeId ?? null}
            />
          </div>
        )}

        <MessageList
          messages={displayMessages}
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

      {/* Speaker notes preview (Sprint 2) */}
      {pendingNotes && (
        <NotesPreview
          pendingNotes={pendingNotes.speakerNotes}
          sources={pendingNotes.sources}
          onAccept={handleNotesAccept}
          onReject={handleNotesDiscard}
          loading={notesLoading}
        />
      )}
      {notesError && (
        <div
          style={{
            padding: '4px 12px',
            background: '#1a0f0f',
            color: '#e07070',
            fontSize: '11px',
            flexShrink: 0,
          }}
        >
          {notesError}
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
