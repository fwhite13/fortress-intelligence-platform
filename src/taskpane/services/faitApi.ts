const FAIT_BASE = 'https://fait.dev.fortressam.ai';

export interface ChatResponse {
  answer: string;
  sources: string[];
}

export async function sendChat(
  message: string,
  apiKey: string,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal
): Promise<ChatResponse> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 30_000);

  // Use caller's signal or our timeout signal
  const combinedSignal = signal ?? controller.signal;

  try {
    const resp = await fetch(`${FAIT_BASE}/api/haven/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-api-key': apiKey,
      },
      body: JSON.stringify({ message, model }),
      signal: combinedSignal,
    });

    if (resp.status === 401) throw new Error('INVALID_KEY');
    if (resp.status === 502 || resp.status === 503) throw new Error('SERVICE_UNAVAILABLE');
    if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

    return await resp.json();
  } catch (err) {
    if (err instanceof Error && err.name === 'AbortError') {
      throw new Error('TIMEOUT');
    }
    throw err;
  } finally {
    clearTimeout(timeout);
  }
}

/**
 * SSE streaming version of sendChat.
 * Calls onChunk for each text token as it arrives.
 * Throws 'INVALID_KEY' on 401, 'HTTP_NNN' for other HTTP errors.
 */
export async function sendChatStreaming(
  message: string,
  apiKey: string,
  onChunk: (text: string) => void,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal
): Promise<void> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/chat`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
      Accept: 'text/event-stream',
    },
    body: JSON.stringify({ message, model }),
    signal,
  });

  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

  // If the server didn't actually send SSE (e.g. old backend returns JSON),
  // fall through to the caller which can handle this gracefully.
  const contentType = resp.headers.get('content-type') ?? '';
  if (!contentType.includes('text/event-stream')) {
    // Non-streaming response — parse as JSON and emit full answer as one chunk
    const data: ChatResponse = await resp.json();
    if (data.answer) onChunk(data.answer);
    return;
  }

  const reader = resp.body!.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split('\n');
    buffer = lines.pop() ?? '';

    for (const line of lines) {
      if (line.startsWith('data: ') && line.trim() !== 'data: [DONE]') {
        try {
          onChunk(JSON.parse(line.slice(6)));
        } catch {
          /* ignore parse errors */
        }
      }
    }
  }
}

export interface KbSearchResponse {
  results: Array<{
    content: string;
    source: string;
    score: number;
  }>;
}

export async function searchKb(
  query: string,
  apiKey: string,
  projectId?: string
): Promise<KbSearchResponse> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
    },
    body: JSON.stringify({ query, projectId: projectId ?? null }),
  });

  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

  return resp.json();
}
