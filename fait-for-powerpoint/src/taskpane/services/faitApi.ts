const FAIT_BASE = 'https://fait.dev.fortressam.ai';

export interface ChatResponse {
  answer: string;
  sources: string[];
}

export async function sendChat(
  message: string,
  apiKey: string,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal,
  kbTypes?: string[],
  projectId?: string | null
): Promise<ChatResponse> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 30_000);

  const combinedSignal = signal ?? controller.signal;

  try {
    const resp = await fetch(`${FAIT_BASE}/api/haven/chat`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'x-api-key': apiKey,
      },
      body: JSON.stringify({
        message,
        model,
        kbTypes: kbTypes ?? undefined,
        projectId: projectId ?? undefined,
      }),
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

export async function sendChatStreaming(
  message: string,
  apiKey: string,
  onChunk: (text: string) => void,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal,
  kbTypes?: string[],
  projectId?: string | null
): Promise<void> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/chat`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
      Accept: 'text/event-stream',
    },
    body: JSON.stringify({
      message,
      model,
      kbTypes: kbTypes ?? undefined,
      projectId: projectId ?? undefined,
    }),
    signal,
  });

  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

  const contentType = resp.headers.get('content-type') ?? '';
  if (!contentType.includes('text/event-stream')) {
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

export interface KbInfo {
  id: string;
  name: string;
  type: string;
  alwaysOn: boolean;
  available: boolean;
}

export interface ProjectInfo {
  id: string;
  name: string;
}

export async function fetchKbList(apiKey: string): Promise<KbInfo[]> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-list`, {
    headers: { 'x-api-key': apiKey },
  });
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.kbs ?? [];
}

export async function fetchProjectList(apiKey: string): Promise<ProjectInfo[]> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/project-list`, {
    headers: { 'x-api-key': apiKey },
  });
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.projects ?? [];
}
