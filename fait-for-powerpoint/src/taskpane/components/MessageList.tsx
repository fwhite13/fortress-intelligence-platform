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
        <span>Select a shape and ask FAIT anything</span>
        <span style={{ fontSize: '11px' }}>e.g. "Summarize this slide" or "Write a bullet list for the selected shape"</span>
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
        <MessageBubble
          key={idx}
          message={msg}
        />
      ))}
      {loading && <LoadingDots />}
      <div ref={bottomRef} />
    </div>
  );
};

export default MessageList;
