import { useState } from 'react';
import { sendChat } from '../services/faitApi';

export interface Message {
  role: 'user' | 'assistant';
  content: string;
}

export function useChat(apiKey: string, model: 'haiku' | 'sonnet') {
  const [messages, setMessages] = useState<Message[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const send = async (text: string, context?: string) => {
    // Build full message with optional spreadsheet context prepended
    const fullMessage = context ? `${context}\n\nUser question: ${text}` : text;

    // Append user message (show only user's visible text, not the context block)
    setMessages((prev) => [...prev, { role: 'user', content: text }]);
    setLoading(true);
    setError(null);

    try {
      const { answer } = await sendChat(fullMessage, apiKey, model);
      setMessages((prev) => [...prev, { role: 'assistant', content: answer }]);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      if (msg === 'INVALID_KEY') {
        setError('Invalid API key — check Settings');
      } else if (msg === 'TIMEOUT') {
        setError('FAIT took too long — try a shorter question or smaller selection');
      } else if (msg === 'SERVICE_UNAVAILABLE') {
        setError('FAIT service unavailable — try again');
      } else {
        setError('FAIT unavailable — try again');
      }
    } finally {
      setLoading(false);
    }
  };

  const clearError = () => setError(null);

  return { messages, loading, error, send, clearError };
}
