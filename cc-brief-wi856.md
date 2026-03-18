Build a CC Memory MCP Server at ~/projects/fip/mcp-memory/src/.

## Purpose
pgvector-backed memory service for Claude Code users (Rob, Len, Leslie).
Four MCP tools over HTTP. Bearer token auth. Per-user isolation.

## DB Connection (existing openclaw-rag container on localhost)
PG_HOST=localhost
PG_PORT=5433
PG_DB=rag
PG_USER=jarvis
PG_PASSWORD=lGxWwQYRsIcUOLJeuzcTNkn4lJOBLk7e

## Files to create

### src/db.ts
- pg Pool using env vars (PG_HOST, PG_PORT, PG_DB, PG_USER, PG_PASSWORD)
- Export pool and a runMigrations() function
- runMigrations() runs:
  CREATE EXTENSION IF NOT EXISTS vector;
  CREATE TABLE IF NOT EXISTS cc_memory_users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(64) NOT NULL UNIQUE,
    email VARCHAR(256) NOT NULL UNIQUE,
    api_token VARCHAR(128) NOT NULL UNIQUE,  -- bcrypt hash
    scope VARCHAR(20) NOT NULL DEFAULT 'user',
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_used_at TIMESTAMPTZ
  );
  CREATE INDEX IF NOT EXISTS idx_ccmu_token ON cc_memory_users (api_token);
  CREATE TABLE IF NOT EXISTS cc_memory_entries (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES cc_memory_users(id) ON DELETE CASCADE,
    scope VARCHAR(20) NOT NULL,
    project VARCHAR(64),
    content TEXT NOT NULL,
    entry_type VARCHAR(32) NOT NULL DEFAULT 'note',
    source VARCHAR(32) NOT NULL DEFAULT 'manual',
    tags TEXT[] DEFAULT '{}',
    embedding vector(1536),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ
  );
  CREATE INDEX IF NOT EXISTS idx_cme_user ON cc_memory_entries (user_id);
  CREATE INDEX IF NOT EXISTS idx_cme_scope ON cc_memory_entries (scope);

### src/embed.ts
- embedText(text: string): Promise<number[]>
- Uses @aws-sdk/client-bedrock-runtime BedrockRuntimeClient
- Model: amazon.titan-embed-text-v2:0
- Region: us-east-1
- Returns embedding array (1536 dims)

### src/auth.ts
- validateToken(token: string): Promise<{ id: string, username: string, scope: string } | null>
- Queries cc_memory_users WHERE is_active = true
- Uses bcrypt.compare(token, stored_hash) — iterate all active tokens (or cache)
- Cache valid users for 5 minutes (Map<string, {user, expires}>)
- Export: authenticateRequest(req) middleware that sets req.user or returns 401

### src/tools/search.ts
- memory_search tool
- Input: { query: string, scope?: 'personal'|'org'|'both', project?: string, topK?: number }
- Embed the query, cosine similarity search against cc_memory_entries
- personal: WHERE user_id = $userId AND scope = 'personal'
- org: WHERE scope = 'org' AND user_id IS NULL
- both: UNION of above
- Return top topK results (default 5, max 10)
- NEVER return another user's personal entries

### src/tools/add.ts
- memory_add tool
- Input: { content: string, scope?: 'personal'|'org', project?: string, entry_type?: string, tags?: string[] }
- Default scope: 'personal'
- For personal: INSERT with user_id = authenticated user's UUID
- For org: INSERT with user_id = NULL; org entries are shared
- Embed the content before inserting

### src/tools/list.ts
- memory_list tool
- Input: { scope?: 'personal'|'org'|'both', project?: string, limit?: number }
- Returns recent entries (no vector search), ordered by created_at DESC
- Same user_id isolation as search

### src/tools/delete.ts
- memory_delete tool
- Input: { id: string }
- DELETE FROM cc_memory_entries WHERE id = $id AND user_id = $userId
- Only the owner can delete their own entries (personal)
- Admin scope users can delete org entries

### src/admin.ts
- CLI for admin operations
- Commands: add-user, reset-token, list-users
- add-user: INSERT into cc_memory_users, generate random token, bcrypt hash it, return plaintext once
- reset-token: generate new token, update bcrypt hash
- Usage: node dist/admin.js add-user --username rob --email rob@example.com

### src/server.ts
- Express app on port 3100 (or process.env.PORT)
- Call runMigrations() on startup
- MCP HTTP transport using @modelcontextprotocol/sdk McpServer
- Register all 4 tools (search, add, list, delete)
- Bearer token auth middleware on all MCP routes
- GET /health → { status: 'ok' }
- GET /cli/memory.py → serve the memory.py file
- Log startup: "CC Memory MCP Server listening on :3100"

### cli/memory.py
- Python CLI for manual memory operations
- Commands: configure, add, search, list, delete
- Stores config in ~/.config/cc-memory/config.json (server URL + token)
- Uses httpx for HTTP requests
- configure: save server + token to config file
- add: POST /mcp with memory_add tool call
- search: POST /mcp with memory_search tool call
- list: POST /mcp with memory_list tool call

## Critical rules
1. NEVER store plaintext tokens — only bcrypt hash in DB
2. user_id MUST be in WHERE clause for all personal queries
3. Org entries (user_id IS NULL) are readable by all authenticated users
4. Port 3100
5. TypeScript strict mode — all types explicit
