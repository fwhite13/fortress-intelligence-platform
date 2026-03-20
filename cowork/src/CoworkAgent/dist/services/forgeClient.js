"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.searchForge = searchForge;
exports.formatForgeContextBlock = formatForgeContextBlock;
exports.formatForgeToolResult = formatForgeToolResult;
exports.queryForgeContext = queryForgeContext;
exports.queryForgeContextCached = queryForgeContextCached;
exports.buildSearchForgeMcpServer = buildSearchForgeMcpServer;
const crypto_1 = __importDefault(require("crypto"));
const v4_1 = require("zod/v4");
const claude_agent_sdk_1 = require("@anthropic-ai/claude-agent-sdk");
const taskStore_js_1 = require("./taskStore.js");
const FORGE_API_URL = process.env.FORGE_API_URL ?? 'https://fait.dev.fortressam.ai';
const FORGE_API_KEY = process.env.FORGE_API_KEY ?? '';
/**
 * Search FORGE knowledge base with optional topK override.
 */
async function searchForge(query, userId, userEmail, options) {
    if (!FORGE_API_KEY)
        return [];
    const resp = await fetch(`${FORGE_API_URL}/api/haven/kb-search`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'x-api-key': FORGE_API_KEY,
            'x-user-id': userId,
            'x-user-email': userEmail,
        },
        body: JSON.stringify({
            query: query.slice(0, 500),
            topK: options?.topK ?? 3,
            kbTypes: ['document', 'note'],
        }),
    });
    if (!resp.ok)
        return [];
    const { results } = await resp.json();
    if (!results?.length)
        return [];
    return results.map(r => ({
        content: r.content,
        source: r.source,
        score: r.score ?? 0,
    }));
}
/**
 * Format FORGE results for system prompt upfront injection.
 */
function formatForgeContextBlock(results) {
    if (!results.length)
        return '';
    return results.map((r, i) => `[${i + 1}] Source: ${r.source}\n${r.content.slice(0, 500)}`).join('\n\n');
}
/**
 * Format FORGE results for SearchForge tool result (returned to the agent).
 */
function formatForgeToolResult(results) {
    if (!results.length)
        return 'No results found.';
    return results
        .map((r, i) => `[${i + 1}] Source: ${r.source} (score: ${r.score.toFixed(3)})\n${r.content.slice(0, 800)}`)
        .join('\n\n');
}
/**
 * Query FORGE for context relevant to the task prompt.
 * Convenience wrapper — backward compat.
 */
async function queryForgeContext(prompt, userId, userEmail) {
    const results = await searchForge(prompt, userId, userEmail, { topK: 3 });
    return formatForgeContextBlock(results);
}
/**
 * Redis-cached wrapper around queryForgeContext.
 * Cache key includes userId for user isolation.
 * TTL: 600 seconds (10 minutes).
 */
async function queryForgeContextCached(prompt, userId, userEmail) {
    const redis = await (0, taskStore_js_1.getRedis)();
    const hash = crypto_1.default.createHash('sha256').update(prompt.slice(0, 200)).digest('hex').slice(0, 16);
    const cacheKey = `cowork:forge-cache:${userId}:${hash}`;
    const cached = await redis.get(cacheKey);
    if (cached)
        return cached;
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
function buildSearchForgeMcpServer(userId, userEmail) {
    return (0, claude_agent_sdk_1.createSdkMcpServer)({
        name: 'forge',
        tools: [
            {
                name: 'SearchForge',
                description: `Search the FORGE knowledge base for relevant documents, notes, and context.
Use this when you need information about Fortress AM's funds, strategies, clients, policies, or past work.
The FORGE knowledge base contains internal documents and analysis — prefer it over guessing from general knowledge.
Returns the top matching results with source attribution.`,
                inputSchema: {
                    query: v4_1.z.string().describe('The search query — describe what information you need in natural language'),
                    topK: v4_1.z.number().optional().describe('Number of results to return (1-8, default 5)'),
                },
                handler: async (args) => {
                    const results = await searchForge(String(args['query'] ?? ''), userId, userEmail, {
                        topK: Math.min(Number(args['topK'] ?? 5), 8),
                    });
                    return { content: [{ type: 'text', text: formatForgeToolResult(results) }] };
                },
            },
        ],
    });
}
//# sourceMappingURL=forgeClient.js.map