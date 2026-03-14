import React, { useEffect, useRef } from 'react';
import type { Message } from '../hooks/useChat';
import MessageBubble from './MessageBubble';
import LoadingDots from './LoadingDots';

interface MessageListProps {
  messages: Message[];
  loading: boolean;
}

const MessageList: React.FC<MessageListProps> = ({ messages, loading }) => {
  const bottomRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom when messages or loading state changes
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, loading]);

  if (messages.length === 0 && !loading) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          color: '#556677',
          fontSize: '13px',
          gap: '8px',
          padding: '16px',
          textAlign: 'center',
        }}
      >
        <span style={{ fontSize: '32px' }}>🏰</span>
        <span>Select cells and ask FAIT anything</span>
        <span style={{ fontSize: '11px' }}>e.g. "What's the trend in column B?" or "Explain these formulas"</span>
      </div>
    );
  }

  return (
    <div
      style={{
        flex: 1,
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: '4px',
        padding: '8px 0',
        background: '#0f1720',
      }}
    >
      {messages.map((msg, idx) => (
        <MessageBubble key={idx} message={msg} />
      ))}
      {loading && <LoadingDots />}
      <div ref={bottomRef} />
    </div>
  );
};

export default MessageList;
