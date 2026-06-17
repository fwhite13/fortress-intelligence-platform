import React, { useState } from 'react';
import { sendChat, sendChatStreaming } from '../services/faitApi';
import type { OfficeAction } from '../services/faitApi';
import { parseSuggestions, type ParsedTable } from '../services/suggestionParser';
import type { ReportSpec } from '../services/reportBuilder';
import type { FormulaSpec } from '../services/formulaBuilder';
import type { CellSuggestion } from '../components/WriteSuggestionsDialog';

export interface Message {
  role: 'user' | 'assistant';
  content: string;
  streaming?: boolean;
  tableData?: ParsedTable | null;
  reportSpec?: ReportSpec | null;   // Sprint 10
  formulaSpec?: FormulaSpec | null;   // Sprint 11
}

export type { OfficeAction };

export interface UseChatReturn {
  messages: Message[];
  loading: boolean;
  error: string | null;
  pendingSuggestions: CellSuggestion[] | null;
  officeActions: OfficeAction[];
  officeActionsStreaming: boolean;
  send: (text: string, context?: string, extraBody?: Record<string, unknown>) => Promise<void>;
  clearError: () => void;
  clearPendingSuggestions: () => void;
  clearOfficeActions: () => void;
  setMessages: React.Dispatch<React.SetStateAction<Message[]>>;
}

export function useChat(
  authHeader: Record<string, string>,
  model: 'haiku' | 'sonnet',
  kbToggles?: Record<string, boolean>,
  projectId?: string | null,
  initialMessages?: Message[]
): UseChatReturn {
  const [messages, setMessages] = useState<Message[]>(initialMessages ?? []);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pendingSuggestions, setPendingSuggestions] = useState<CellSuggestion[] | null>(null);
  const [officeActions, setOfficeActions] = useState<OfficeAction[]>([]);
  const [officeActionsStreaming, setOfficeActionsStreaming] = useState(false);

  /** Build the kbTypes array from toggles — personal is always included. */
  const buildKbTypes = (): string[] => {
    if (!kbToggles) return ['corp', 'personal'];
    const types = Object.entries(kbToggles)
      .filter(([, v]) => v)
      .map(([k]) => k);
    if (!types.includes('personal')) types.push('personal');
    return types;
  };

  const send = async (text: string, context?: string, extraBody?: Record<string, unknown>) => {
    const fullMessage = context ? `${context}\n\nUser question: ${text}` : text;

    // Show user message immediately
    setMessages((prev) => [...prev, { role: 'user', content: text }]);
    setLoading(true);
    setError(null);

    // Add a placeholder assistant message for streaming
    const assistantIndex = await new Promise<number>((resolve) => {
      setMessages((prev) => {
        resolve(prev.length); // index of the new message
        return [...prev, { role: 'assistant', content: '', streaming: true }];
      });
    });

    const kbTypes = buildKbTypes();

    try {
      // Try SSE streaming first
      let rawText = '';

      const controller = new AbortController();
      const timeout = setTimeout(() => controller.abort(), 30_000);

      try {
        await sendChatStreaming(
          fullMessage,
          authHeader,
          {
            onTextChunk: (chunk) => {
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
            onOfficeAction: (action) => {
              setOfficeActions((prev) => [...prev, action]);
              setOfficeActionsStreaming(true);
            },
          },
          model,
          controller.signal,
          kbTypes,
          projectId,
          extraBody
        );
        setOfficeActionsStreaming(false);
        clearTimeout(timeout);
      } catch (streamErr) {
        clearTimeout(timeout);
        const msg = streamErr instanceof Error ? streamErr.message : '';
        if (msg === 'INVALID_KEY' || msg.startsWith('HTTP_')) {
          throw streamErr; // re-throw known errors
        }
        // SSE not supported or network issue — fall back to buffered
        rawText = '';
        const { answer } = await sendChat(fullMessage, authHeader, model, undefined, kbTypes, projectId);
        rawText = answer;
      }

      // Parse suggestions/tableData out of the raw response
      const { displayText, suggestions, tableData, reportSpec, formulaSpec } = parseSuggestions(rawText);

      // Finalise the assistant message (remove streaming flag)
      setMessages((prev) => {
        const next = [...prev];
        next[assistantIndex] = {
          role: 'assistant',
          content: displayText,
          streaming: false,
          tableData: tableData ?? null,
          reportSpec: reportSpec ?? null,
          formulaSpec: formulaSpec ?? null,   // Sprint 11
        };
        return next;
      });

      if (suggestions && suggestions.length > 0) {
        setPendingSuggestions(suggestions);
      }
    } catch (e) {
      // Remove the empty placeholder on error
      setMessages((prev) => prev.filter((_, i) => i !== assistantIndex));

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
      // Ensure streaming flag is cleared even if we hit an odd path
      setMessages((prev) =>
        prev.map((m) => (m.streaming ? { ...m, streaming: false } : m))
      );
    }
  };

  const clearError = () => setError(null);
  const clearPendingSuggestions = () => setPendingSuggestions(null);
  const clearOfficeActions = () => setOfficeActions([]);

  return { messages, loading, error, pendingSuggestions, officeActions, officeActionsStreaming, send, clearError, clearPendingSuggestions, clearOfficeActions, setMessages };
}
