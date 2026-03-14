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
