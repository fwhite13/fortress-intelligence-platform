// ESM module
const BRAVE_API_KEY = process.env.BRAVE_API_KEY;
const BRAVE_BASE_URL = 'https://api.search.brave.com/res/v1';

export function isAPIKeyConfigured() {
  return !!BRAVE_API_KEY;
}

export async function braveWebSearch({ query, count = 10, country = 'US', search_lang = 'en' }) {
  if (!BRAVE_API_KEY) {
    throw new Error('[fip-mcp] BRAVE_API_KEY env var not set — web search unavailable');
  }

  const params = new URLSearchParams({
    q: query,
    count: String(Math.min(count, 20)), // Brave max is 20
    country,
    search_lang,
  });

  const response = await fetch(`${BRAVE_BASE_URL}/web/search?${params}`, {
    headers: {
      'Accept': 'application/json',
      'Accept-Encoding': 'gzip',
      'X-Subscription-Token': BRAVE_API_KEY,
    },
  });

  if (!response.ok) {
    throw new Error(`[fip-mcp] Brave Search API error: ${response.status} ${response.statusText}`);
  }

  const data = await response.json();
  return data;
}
