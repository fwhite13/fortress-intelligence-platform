const FAIT_BASE = 'https://fait.dev.fortressam.ai';

export interface ChatResponse {
  answer: string;
  sources: string[];
}

export async function sendChat(
  message: string,
  authHeader: Record<string, string>,
  model: 'haiku' | 'sonnet' = 'sonnet',
  signal?: AbortSignal,
  kbTypes?: string[],
  projectId?: string | null
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
        ...authHeader,
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

/**
 * SSE streaming version of sendChat.
 * Calls onChunk for each text token as it arrives.
 * Throws 'INVALID_KEY' on 401, 'HTTP_NNN' for other HTTP errors.
 */
export async function sendChatStreaming(
  message: string,
  authHeader: Record<string, string>,
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
      ...authHeader,
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
  authHeader: Record<string, string>,
  projectId?: string,
  kbTypes?: string[]
): Promise<KbSearchResponse> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...authHeader,
    },
    body: JSON.stringify({
      query,
      projectId: projectId ?? null,
      kbTypes: kbTypes ?? undefined,
    }),
  });

  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);

  return resp.json();
}

// ── Sprint 3: KB list + Project list ─────────────────────────────────────────

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

export async function fetchKbList(authHeader: Record<string, string>): Promise<KbInfo[]> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-list`, {
    headers: { ...authHeader },
  });
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.kbs ?? [];
}

export async function fetchProjectList(authHeader: Record<string, string>): Promise<ProjectInfo[]> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/project-list`, {
    headers: { ...authHeader },
  });
  if (!resp.ok) return [];
  const data = await resp.json();
  return data.projects ?? [];
}

// ── WI863: Dev KB upload/list/delete ─────────────────────────────────────────

export interface DevKbDocument {
  key: string;       // S3 key, e.g. "kb-docs/dev/firm-architecture.md"
  filename: string;  // just the filename part, e.g. "firm-architecture.md"
  size: number;      // bytes
  lastModified: string; // ISO 8601 date string
}

export interface DevKbListResponse {
  documents: DevKbDocument[];
}

export async function listDevKbDocuments(
  authHeader: Record<string, string>
): Promise<DevKbListResponse> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-documents?tier=developer`, {
    headers: { ...authHeader },
  });
  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);
  return resp.json();
}

export async function uploadDevKbDocument(
  file: File,
  authHeader: Record<string, string>,
  onProgress?: (pct: number) => void
): Promise<void> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('tier', 'developer');

  // Use XMLHttpRequest for progress tracking
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${FAIT_BASE}/api/haven/kb-upload`);

    // Set auth header(s)
    for (const [key, value] of Object.entries(authHeader)) {
      xhr.setRequestHeader(key, value);
    }

    if (onProgress) {
      xhr.upload.addEventListener('progress', (e) => {
        if (e.lengthComputable) {
          onProgress(Math.round((e.loaded / e.total) * 100));
        }
      });
    }

    xhr.onload = () => {
      if (xhr.status === 401) return reject(new Error('INVALID_KEY'));
      if (xhr.status >= 200 && xhr.status < 300) return resolve();
      reject(new Error(`HTTP_${xhr.status}`));
    };
    xhr.onerror = () => reject(new Error('NETWORK_ERROR'));
    xhr.send(formData);
  });
}

export async function deleteDevKbDocument(
  filename: string,
  authHeader: Record<string, string>
): Promise<void> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-document`, {
    method: 'DELETE',
    headers: {
      'Content-Type': 'application/json',
      ...authHeader,
    },
    body: JSON.stringify({ filename, tier: 'developer' }),
  });
  if (resp.status === 401) throw new Error('INVALID_KEY');
  if (!resp.ok) throw new Error(`HTTP_${resp.status}`);
}
