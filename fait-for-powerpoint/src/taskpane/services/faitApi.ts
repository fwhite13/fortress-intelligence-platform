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
  projectId?: string,
  kbTypes?: string[]
): Promise<KbSearchResponse> {
  const resp = await fetch(`${FAIT_BASE}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key': apiKey,
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

// ── Sprint 3: Template fetch ──────────────────────────────────────────────

export interface TemplateResult {
  id: string;
  name: string;
  description: string;
}

export async function fetchTemplateBase64(
  templateId: string,
  _apiKey: string
): Promise<string> {
  // TODO: DO NOT SHIP — /api/haven/template-fetch not yet implemented
  // Hardcoded test template for development only
  // Replace with real fetch when FORGE template backend is ready:
  // const resp = await fetch(`${FAIT_BASE}/api/haven/template-fetch`, {
  //   method: 'POST',
  //   headers: { 'Content-Type': 'application/json', 'x-api-key': _apiKey },
  //   body: JSON.stringify({ id: templateId }),
  // });
  // if (resp.status === 401) throw new Error('INVALID_KEY');
  // if (resp.status === 404) throw new Error('TEMPLATE_NOT_FOUND');
  // if (!resp.ok) throw new Error(`HTTP_${resp.status}`);
  // const { base64 } = await resp.json();
  // return base64;
  console.warn(`fetchTemplateBase64: using hardcoded test template for id="${templateId}" — backend not yet implemented`);
  // Minimal valid 1-slide PPTX (base64) for development testing
  return Promise.resolve(TEST_PPTX_BASE64);
}

/**
 * Minimal 1-slide PPTX fragment for development/testing only.
 * DO NOT SHIP — replace with real FORGE template fetch.
 */
const TEST_PPTX_BASE64 =
  'UEsDBBQABgAIAAAAIQDfpNJsWgEAACAFAAATAAgCW0NvbnRlbnRfVHlwZXNdLnhtbCCiBAIo' +
  'oAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA' +
  'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
