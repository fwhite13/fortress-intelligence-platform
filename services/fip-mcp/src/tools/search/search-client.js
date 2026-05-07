// ESM module
const BRAVE_BASE_URL = 'https://api.search.brave.com/res/v1';

function getAPIKey() {
  return process.env.BRAVE_API_KEY;
}

export function isAPIKeyConfigured() {
  return !!getAPIKey();
}

export async function braveWebSearch({ query, count = 10, country = 'US', search_lang = 'en' }) {
  const apiKey = getAPIKey();
  if (!apiKey) {
    throw new Error('[fip-mcp] BRAVE_API_KEY env var not set — web search unavailable');
  }

  const params = new URLSearchParams({
    q: query,
    count: String(Math.min(count, 20)), // Brave max is 20
    country,
    search_lang,
  });

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 10000); // 10s timeout
  try {
    const response = await fetch(`${BRAVE_BASE_URL}/web/search?${params}`, {
      signal: controller.signal,
      headers: {
        'Accept': 'application/json',
        'Accept-Encoding': 'gzip',
        'X-Subscription-Token': apiKey,
      },
    });

    if (!response.ok) {
      throw new Error(`[fip-mcp] Brave Search API error: ${response.status} ${response.statusText}`);
    }

    const data = await response.json();
    return data;
  } finally {
    clearTimeout(timeout);
  }
}
