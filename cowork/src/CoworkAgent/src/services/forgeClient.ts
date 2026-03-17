import crypto from 'crypto';
import { z } from 'zod/v4';
import { createSdkMcpServer } from '@anthropic-ai/claude-agent-sdk';
import { getRedis } from './taskStore.js';

const FORGE_API_URL = process.env.FORGE_API_URL ?? 'https://fait.dev.fortressam.ai';
const FORGE_API_KEY = process.env.FORGE_API_KEY ?? '';

export interface ForgeResult {
  content: string;
  source: string;
  score: number;
}

/**
 * Search FORGE knowledge base with optional topK override.
 */
export async function searchForge(
  query: string,
  userId: string,
  userEmail: string,
  options?: { topK?: number }
): Promise<ForgeResult[]> {
  if (!FORGE_API_KEY) return [];

  const resp = await fetch(`${FORGE_API_URL}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type':  'application/json',
      'x-api-key':     FORGE_API_KEY,
      'x-user-id':     userId,
      'x-user-email':  userEmail,
    },
    body: JSON.stringify({
      query: query.slice(0, 500),
      topK: options?.topK ?? 3,
      kbTypes: ['document', 'note'],
    }),
  });

  if (!resp.ok) return [];

  const { results } = await resp.json() as { results: Array<{ content: string; source: string; score?: number }> };
  if (!results?.length) return [];

  return results.map(r => ({
    content: r.content,
    source:  r.source,
    score:   r.score ?? 0,
  }));
}

/**
 * Format FORGE results for system prompt upfront injection.
 */
export function formatForgeContextBlock(results: ForgeResult[]): string {
  if (!results.length) return '';
  return results.map((r, i) => `[${i + 1}] Source: ${r.source}\n${r.content.slice(0, 500)}`).join('\n\n');
}

/**
 * Format FORGE results for SearchForge tool result (returned to the agent).
 */
export function formatForgeToolResult(results: ForgeResult[]): string {
  if (!results.length) return 'No results found.';
  return results
    .map((r, i) => `[${i + 1}] Source: ${r.source} (score: ${r.score.toFixed(3)})\n${r.content.slice(0, 800)}`)
    .join('\n\n');
}

/**
 * Query FORGE for context relevant to the task prompt.
 * Convenience wrapper — backward compat.
 */
export async function queryForgeContext(prompt: string, userId: string, userEmail: string): Promise<string> {
  const results = await searchForge(prompt, userId, userEmail, { topK: 3 });
  return formatForgeContextBlock(results);
}

/**
 * Redis-cached wrapper around queryForgeContext.
 * Cache key includes userId for user isolation.
 * TTL: 600 seconds (10 minutes).
 */
export async function queryForgeContextCached(
  prompt: string,
  userId: string,
  userEmail: string
): Promise<string> {
  const redis = await getRedis();
  const hash = crypto.createHash('sha256').update(prompt.slice(0, 200)).digest('hex').slice(0, 16);
  const cacheKey = `cowork:forge-cache:${userId}:${hash}`;
  const cached = await redis.get(cacheKey);
  if (cached) return cached;
  const context = await queryForgeContext(prompt, userId, userEmail);
  if (context) {
    await redis.set(cacheKey, context, { EX: 600 });
  }
  return context;
}

/**
 * Factory function that builds a SearchForge SDK MCP server per-task.
 * userId and userEmail are captured in closure — NOT module-level.
 * Returns a McpSdkServerConfigWithInstance suitable for Options.mcpServers.
 */
export function buildSearchForgeMcpServer(userId: string, userEmail: string) {
  return createSdkMcpServer({
    name: 'forge',
    tools: [
      {
        name: 'SearchForge',
        description: `Search the FORGE knowledge base for relevant documents, notes, and context.
Use this when you need information about Fortress AM's funds, strategies, clients, policies, or past work.
The FORGE knowledge base contains internal documents and analysis — prefer it over guessing from general knowledge.
Returns the top matching results with source attribution.`,
        inputSchema: {
          query: z.string().describe('The search query — describe what information you need in natural language'),
          topK: z.number().optional().describe('Number of results to return (1-8, default 5)'),
        },
        handler: async (args: Record<string, unknown>) => {
          const results = await searchForge(String(args['query'] ?? ''), userId, userEmail, {
            topK: Math.min(Number(args['topK'] ?? 5), 8),
          });
          return { content: [{ type: 'text' as const, text: formatForgeToolResult(results) }] };
        },
      },
    ],
  });
}
