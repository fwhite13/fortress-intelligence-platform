import { useState } from 'react';
import { sendChat, sendChatStreaming } from '../services/faitApi';

export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
}

export interface UseChatReturn {
  messages: Message[];
  loading: boolean;
  error: string | null;
  send: (text: string, context?: string) => Promise<void>;
  clearError: () => void;
  setMessages: React.Dispatch<React.SetStateAction<Message[]>>;
}

export function useChat(
  apiKey: string,
  model: 'haiku' | 'sonnet',
  kbToggles?: Record<string, boolean>,
  projectId?: string | null,
  initialMessages?: Message[]
): UseChatReturn {
  const [messages, setMessages] = useState<Message[]>(initialMessages ?? []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const buildKbTypes = (): string[] => {
    if (!kbToggles) return ['corp', 'personal'];
    const types = Object.entries(kbToggles)
      .filter(([, v]) => v)
      .map(([k]) => k);
    if (!types.includes('personal')) types.push('personal');
    return types;
  };

  const send = async (text: string, context?: string) => {
    const fullMessage = context ? `${context}\n\nUser question: ${text}` : text;

    setMessages((prev) => [...prev, { role: 'user', content: text }]);
    setLoading(true);
    setError(null);

    const assistantIndex = await new Promise<number>((resolve) => {
      setMessages((prev) => {
        resolve(prev.length);
        return [...prev, { role: 'assistant', content: '', streaming: true }];
      });
    });

    const kbTypes = buildKbTypes();

    try {
      let rawText = '';

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 30_000);

      try {
        await sendChatStreaming(
          fullMessage,
          apiKey,
          (chunk) => {
            rawText += chunk;
            setMessages((prev) => {
              const next = [...prev];
              next[assistantIndex] = {
                role: 'assistant',
                content: rawText,
                streaming: true,
              };
              return next;
            });
          },
          model,
          controller.signal,
          kbTypes,
          projectId
        );
        clearTimeout(timeout);
      } catch (streamErr) {
        clearTimeout(timeout);
        const msg = streamErr instanceof Error ? streamErr.message : '';
        if (msg === 'INVALID_KEY' || msg.startsWith('HTTP_')) {
          throw streamErr;
        }
        rawText = '';
        const { answer } = await sendChat(fullMessage, apiKey, model, undefined, kbTypes, projectId);
        rawText = answer;
      }

      setMessages((prev) => {
        const next = [...prev];
        next[assistantIndex] = {
          role: 'assistant',
          content: rawText,
          streaming: false,
        };
        return next;
      });
    } catch (e) {
      setMessages((prev) => prev.filter((_, i) => i !== assistantIndex));

      const msg = e instanceof Error ? e.message : 'Unknown error';
      if (msg === 'INVALID_KEY') {
        setError('Invalid API key — check Settings');
      } else if (msg === 'TIMEOUT') {
        setError('FAIT took too long — try a shorter question');
      } else if (msg === 'SERVICE_UNAVAILABLE') {
        setError('FAIT service unavailable — try again');
      } else {
        setError('FAIT unavailable — try again');
      }
    } finally {
      setLoading(false);
      setMessages((prev) =>
        prev.map((m) => (m.streaming ? { ...m, streaming: false } : m))
      );
    }
  };

  const clearError = () => setError(null);

  return { messages, loading, error, send, clearError, setMessages };
}
