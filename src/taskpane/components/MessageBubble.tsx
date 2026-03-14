import React from 'react';
import type { Message } from '../hooks/useChat';

interface MessageBubbleProps {
  message: Message;
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

const MessageBubble: React.FC<MessageBubbleProps> = ({ message }) => {
  const isUser = message.role === 'user';

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
        }}
        // Rendering assistant markdown — cell values are already sanitized before being sent
        dangerouslySetInnerHTML={{ __html: simpleMarkdown(message.content) }}
      />
    </div>
  );
};

export default MessageBubble;
