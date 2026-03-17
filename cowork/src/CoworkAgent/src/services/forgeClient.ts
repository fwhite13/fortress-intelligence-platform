const FORGE_API_URL = process.env.FORGE_API_URL ?? 'https://fait.dev.fortressam.ai';
const FORGE_API_KEY = process.env.FORGE_API_KEY ?? '';

/**
 * Query FORGE for context relevant to the task prompt.
 * Passes x-user-id so FAIT can scope KB results to the user in future sprints.
 */
export async function queryForgeContext(prompt: string, userId: string, userEmail: string): Promise<string> {
  if (!FORGE_API_KEY) return '';

  const resp = await fetch(`${FORGE_API_URL}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type':  'application/json',
      'x-api-key':     FORGE_API_KEY,
      'x-user-id':     userId,
      'x-user-email':  userEmail,
    },
    body: JSON.stringify({
      query: prompt.slice(0, 500),
      topK: 3,
      kbTypes: ['document', 'note'],
    }),
  });

  if (!resp.ok) return '';

  const { results } = await resp.json() as { results: Array<{ content: string; source: string }> };
  if (!results?.length) return '';

  return results.map((r, i) => `[${i + 1}] Source: ${r.source}\n${r.content.slice(0, 500)}`).join('\n\n');
}
