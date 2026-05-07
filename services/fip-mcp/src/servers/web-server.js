import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { z } from 'zod';

import { webSearch } from '../tools/search/web_search.js';
import { isAPIKeyConfigured } from '../tools/search/search-client.js';

/**
 * Factory: create a web-search-only McpServer with the web_search tool.
 *
 * @param {{ user_id: string, groups: string[], tid: string, roles: string[] }} user
 * @param {string} _rawToken - unused by web search tools, kept for signature parity
 */
export function createWebServer(user, _rawToken) {
  const server = new McpServer({
    name: 'web',
    version: '1.0.0',
  });

  server.tool(
    'web_search',
    'Search the web using Brave Search. Returns titles, URLs, and snippets for the top results.',
    {
      query: z.string().min(1).describe('Search query'),
      count: z.number().int().min(1).max(20).optional().describe('Number of results (1-20, default 10)'),
      country: z.string().optional().describe('2-letter country code for results (e.g. US, GB). Default: US'),
    },
    async ({ query, count, country }) => {
      try {
        if (!isAPIKeyConfigured()) {
          return { content: [{ type: 'text', text: 'Web search not configured: BRAVE_API_KEY env var missing' }], isError: true };
        }
        const results = await webSearch(user, { query, count, country });
        return { content: [{ type: 'text', text: JSON.stringify(results, null, 2) }] };
      } catch (err) {
        console.error('[web] web_search error:', err);
        return { content: [{ type: 'text', text: `Search error: ${err.message}` }], isError: true };
      }
    }
  );

  return server;
}
