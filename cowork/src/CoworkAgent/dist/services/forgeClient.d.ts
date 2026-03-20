export interface ForgeResult {
    content: string;
    source: string;
    score: number;
}
/**
 * Search FORGE knowledge base with optional topK override.
 */
export declare function searchForge(query: string, userId: string, userEmail: string, options?: {
    topK?: number;
}): Promise<ForgeResult[]>;
/**
 * Format FORGE results for system prompt upfront injection.
 */
export declare function formatForgeContextBlock(results: ForgeResult[]): string;
/**
 * Format FORGE results for SearchForge tool result (returned to the agent).
 */
export declare function formatForgeToolResult(results: ForgeResult[]): string;
/**
 * Query FORGE for context relevant to the task prompt.
 * Convenience wrapper — backward compat.
 */
export declare function queryForgeContext(prompt: string, userId: string, userEmail: string): Promise<string>;
/**
 * Redis-cached wrapper around queryForgeContext.
 * Cache key includes userId for user isolation.
 * TTL: 600 seconds (10 minutes).
 */
export declare function queryForgeContextCached(prompt: string, userId: string, userEmail: string): Promise<string>;
/**
 * Factory function that builds a SearchForge SDK MCP server per-task.
 * userId and userEmail are captured in closure — NOT module-level.
 * Returns a McpSdkServerConfigWithInstance suitable for Options.mcpServers.
 */
export declare function buildSearchForgeMcpServer(userId: string, userEmail: string): import("@anthropic-ai/claude-agent-sdk", { with: { "resolution-mode": "import" } }).McpSdkServerConfigWithInstance;
