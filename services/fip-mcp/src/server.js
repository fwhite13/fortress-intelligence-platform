import express from 'express';
import dotenv from 'dotenv';
import { fileURLToPath } from 'url';
import path from 'path';

dotenv.config();

import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { SSEServerTransport } from '@modelcontextprotocol/sdk/server/sse.js';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { z } from 'zod';

import { authMiddleware } from './auth.js';
import { searchKb } from './tools/search_kb.js';
import { listKbs, getEntitlements } from './tools/list_kbs.js';
import { addToKb } from './tools/add_to_kb.js';
import { getKbMetadata } from './tools/get_kb_metadata.js';
import { getJobStatus } from './tools/get_job_status.js';
import { listKbFiles } from './tools/list_kb_files.js';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const VERSION = '1.0.0';
const PORT = parseInt(process.env.PORT ?? '3000', 10);

// --- CORS ---
const ALLOWED_ORIGINS = [
  'https://fait.dev.fortressam.ai',
  'https://fait.fortressam.ai',
  'https://firm.dev.fortressam.ai',
  'https://nexus.fortressam.ai',
];
const WILDCARD_ORIGIN_RE = /^https:\/\/[a-zA-Z0-9-]+\.fortressam\.ai$/;

function corsMiddleware(req, res, next) {
  const origin = req.headers.origin;
  if (origin && (ALLOWED_ORIGINS.includes(origin) || WILDCARD_ORIGIN_RE.test(origin))) {
    res.setHeader('Access-Control-Allow-Origin', origin);
    res.setHeader('Vary', 'Origin');
  }
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Authorization, Content-Type, Accept');
  res.setHeader('Access-Control-Allow-Credentials', 'true');
  res.setHeader('Access-Control-Max-Age', '86400');
  if (req.method === 'OPTIONS') {
    res.status(204).end();
    return;
  }
  next();
}

// --- Tool error handler ---
function handleToolError(err) {
  if (err.code && err.status) {
    return { error: { code: err.code, message: err.message } };
  }
  console.error('[fip-mcp] Tool error:', err);
  return { error: { code: 'INTERNAL_ERROR', message: err.message ?? 'Internal server error' } };
}

/**
 * Factory: create a new McpServer with all tools registered.
 * User is captured via closure — no metadata injection required.
 */
function createMcpServer(user) {
  const server = new McpServer({
    name: 'fip-mcp',
    version: VERSION,
  });

  // Tool: search_kb
  server.tool(
    'search_kb',
    'Retrieve semantically relevant chunks from a FORGE KB.',
    {
      query: z.string().describe('The search query'),
      kb_id: z.string().describe('Target KB ID from the KB inventory'),
      top_k: z.number().optional().default(5).describe('Number of results to return (default 5)'),
      filters: z.record(z.any()).optional().describe('Optional metadata filters — security filters are auto-injected'),
    },
    async ({ query, kb_id, top_k, filters }) => {
      try {
        const result = await searchKb({ query, kb_id, top_k, filters }, user);
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleToolError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: list_kbs
  server.tool(
    'list_kbs',
    'Returns the list of FORGE KBs the caller is entitled to read.',
    {},
    async () => {
      try {
        const result = await listKbs({}, user);
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleToolError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: add_to_kb
  server.tool(
    'add_to_kb',
    'Ingest new content into a FORGE KB. Async — returns a job_id to poll with get_job_status.',
    {
      kb_id: z.string().describe('Target KB ID'),
      content: z.string().describe('Text content to ingest'),
      metadata: z.object({
        source: z.string().describe('Source identifier for the content'),
        created_by: z.string().describe('Who created this content'),
      }).passthrough().describe('Metadata — must include source and created_by'),
    },
    async ({ kb_id, content, metadata }) => {
      try {
        const result = await addToKb({ kb_id, content, metadata }, user);
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleToolError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: get_kb_metadata
  server.tool(
    'get_kb_metadata',
    'Get stats and configuration about a specific FORGE KB.',
    {
      kb_id: z.string().describe('KB ID to inspect'),
    },
    async ({ kb_id }) => {
      try {
        const result = await getKbMetadata({ kb_id }, user);
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleToolError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: get_job_status
  server.tool(
    'get_job_status',
    'Universal async polling tool for FIP MCP async operations. Poll with the job_id returned by add_to_kb.',
    {
      job_id: z.string().describe('Job ID returned from an async tool call'),
    },
    async ({ job_id }) => {
      try {
        const result = await getJobStatus({ job_id }, user);
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleToolError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: list_kb_files
  server.tool(
    'list_kb_files',
    'List files in a user\'s knowledge base. Returns filenames with extensions, sizes, and last modified dates. Use this when the user asks what documents are in their KB.',
    {
      kb_id: z.string().describe('KB ID from list_kbs'),
      team_id: z.string().optional().describe('Team ID (required for Team KB)'),
    },
    async ({ kb_id, team_id }) => {
      try {
        const result = await listKbFiles({ kb_id, team_id }, user);
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleToolError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  return server;
}

// --- SSE session map ---
const sseSessions = new Map(); // sessionId → SSEServerTransport

// --- Express app ---
const app = express();
app.use(corsMiddleware);
app.use(express.json({ limit: '4mb' }));

// Health check — no auth
app.get('/health', (_req, res) => {
  res.json({ status: 'ok', version: VERSION });
});

// Health check at /mcp/health — public, no auth (ALB rule priority 14 bypasses Entra)
app.get('/mcp/health', (_req, res) => {
  res.json({ status: 'ok', version: VERSION });
});

// POST /mcp — JSON-RPC 2.0 MCP tool calls (Streamable HTTP transport)
app.post('/mcp', authMiddleware, async (req, res) => {
  const user = req.user;

  try {
    const server = createMcpServer(user);
    const transport = new StreamableHTTPServerTransport({
      sessionIdGenerator: undefined,
    });

    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
  } catch (err) {
    console.error('[fip-mcp] POST /mcp error:', err);
    if (!res.headersSent) {
      res.status(500).json({ error: 'Internal server error' });
    }
  }
});

// GET /mcp/sse — SSE stream
app.get('/mcp/sse', authMiddleware, async (req, res) => {
  const user = req.user;

  try {
    const server = createMcpServer(user);
    const transport = new SSEServerTransport('/mcp/sse', res);

    // Store session for message routing
    const sessionId = transport.sessionId;
    if (sessionId) {
      sseSessions.set(sessionId, transport);
    }

    transport.onclose = () => {
      if (sessionId) sseSessions.delete(sessionId);
    };

    await server.connect(transport);
  } catch (err) {
    console.error('[fip-mcp] GET /mcp/sse error:', err);
    if (!res.headersSent) {
      res.status(500).json({ error: 'Internal server error' });
    }
  }
});

// Admin: GET /admin/entitlements — requires forge-kb-admin role
app.get('/admin/entitlements', authMiddleware, async (req, res) => {
  if (!req.user.roles.includes('forge-kb-admin')) {
    return res.status(403).json({ error: 'Forbidden', message: 'forge-kb-admin role required' });
  }
  res.json({ message: 'Admin entitlements endpoint — Phase 0 static config only' });
});

// --- Start ---
app.listen(PORT, () => {
  console.log(`[fip-mcp] FORGE KB MCP Server v${VERSION} listening on port ${PORT}`);
  console.log(`[fip-mcp] Entra tenant: ${process.env.ENTRA_TENANT_ID}`);
  console.log(`[fip-mcp] Entra client: ${process.env.ENTRA_CLIENT_ID}`);
  console.log(`[fip-mcp] Bedrock region: ${process.env.BEDROCK_REGION ?? 'us-east-1'}`);
  console.log(`[fip-mcp] Entitlements config: ${process.env.FALLBACK_ENTITLEMENTS_CONFIG ?? '(bundled default)'}`);
});
