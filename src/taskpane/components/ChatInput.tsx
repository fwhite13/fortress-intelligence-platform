import React, { useRef } from 'react';

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: (text: string) => void;
  disabled: boolean;
  includeSelection: boolean;
  onToggleSelection: () => void;
}

const ChatInput: React.FC<ChatInputProps> = ({
  value,
  onChange,
  onSend,
  disabled,
  includeSelection,
  onToggleSelection,
}) => {
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleSend = () => {
    const trimmed = value.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    onChange('');
    // Reset textarea height
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto';
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleInput = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    onChange(e.target.value);
    // Auto-grow textarea
    const ta = e.target;
    ta.style.height = 'auto';
    ta.style.height = `${Math.min(ta.scrollHeight, 120)}px`;
  };

  return (
    <div
      style={{
        borderTop: '1px solid #2e3f54',
        padding: '8px',
        background: '#1a2332',
        display: 'flex',
        flexDirection: 'column',
        gap: '6px',
      }}
    >
      {/* Context toggle */}
      <label
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '6px',
          fontSize: '11px',
          color: '#8899aa',
          cursor: 'pointer',
          userSelect: 'none',
        }}
      >
        <input
          type="checkbox"
          checked={includeSelection}
          onChange={onToggleSelection}
          style={{ accentColor: '#d4af37', cursor: 'pointer' }}
        />
        Include selection
      </label>

      {/* Input row */}
      <div style={{ display: 'flex', gap: '6px', alignItems: 'flex-end' }}>
        <textarea
          ref={textareaRef}
          value={value}
          onChange={handleInput}
          onKeyDown={handleKeyDown}
          placeholder="Ask FAIT…"
          disabled={disabled}
          rows={1}
          style={{
            flex: 1,
            resize: 'none',
            background: '#243447',
            border: '1px solid #2e3f54',
            borderRadius: '8px',
            color: '#e8edf3',
            fontFamily: 'Inter, sans-serif',
            fontSize: '13px',
            padding: '8px 10px',
            lineHeight: 1.5,
            outline: 'none',
            transition: 'border-color 0.15s',
            minHeight: '36px',
            maxHeight: '120px',
            overflowY: 'auto',
          }}
          onFocus={(e) => (e.target.style.borderColor = '#d4af37')}
          onBlur={(e) => (e.target.style.borderColor = '#2e3f54')}
        />
        <button
          onClick={handleSend}
          disabled={disabled || !value.trim()}
          aria-label="Send message"
          style={{
            background: disabled || !value.trim() ? '#243447' : '#d4af37',
            border: 'none',
            borderRadius: '8px',
            color: disabled || !value.trim() ? '#556677' : '#1a2332',
            cursor: disabled || !value.trim() ? 'not-allowed' : 'pointer',
            fontWeight: '600',
            fontSize: '16px',
            width: '36px',
            height: '36px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            flexShrink: 0,
            transition: 'background 0.15s',
          }}
        >
          ↑
        </button>
      </div>
    </div>
  );
};

export default ChatInput;
