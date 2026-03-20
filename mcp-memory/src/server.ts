import express from 'express';
import * as path from 'path';
import dotenv from 'dotenv';
dotenv.config();

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { CallToolRequestSchema, ListToolsRequestSchema } from '@modelcontextprotocol/sdk/types.js';

import { initDb } from './db';
import { authenticate } from './auth';
import { memorySearch } from './tools/search';
import { memoryAdd } from './tools/add';
import { memoryList } from './tools/list';
import { memoryDelete } from './tools/delete';

const app = express();
app.use(express.json());

app.get('/cli/memory.py', (_req, res) => {
  res.setHeader('Content-Type', 'text/plain');
  res.sendFile(path.join(__dirname, '../cli/memory.py'));
});

app.get('/health', (_req, res) => res.json({ status: 'ok' }));

app.all('/mcp', async (req, res) => {
  const user = await authenticate(req);
  if (!user) {
    res.status(401).json({ error: 'Unauthorized' });
    return;
  }

  const server = new Server(
    { name: 'fip-memory', version: '1.0.0' },
    { capabilities: { tools: {} } }
  );

  server.setRequestHandler(ListToolsRequestSchema, async () => ({
    tools: [
      {
        name: 'memory_search',
        description: 'Search org-level and personal memory for relevant context. Call at session start for background on a topic.',
        inputSchema: {
          type: 'object' as const,
          properties: {
            query: { type: 'string', description: 'What to search for' },
            project: { type: 'string', description: 'Filter to a specific project (e.g. iaapa, firm). Omit for global.' },
            limit: { type: 'number', description: 'Max results (default 10, max 20)' },
          },
          required: ['query'],
        },
      },
      {
        name: 'memory_add',
        description: 'Store a decision, lesson learned, or context for future sessions. Use at session end for anything worth remembering.',
        inputSchema: {
          type: 'object' as const,
          properties: {
            content: { type: 'string', description: 'The memory to store (1-3 sentences)' },
            entry_type: { type: 'string', enum: ['decision', 'lesson', 'context', 'note'] },
            project: { type: 'string', description: 'Project tag (e.g. iaapa, firm)' },
            scope: { type: 'string', enum: ['personal', 'org'], description: 'personal = only you; org = shared. Default: personal.' },
            confirmed: { type: 'boolean', description: 'Set true to confirm org write after confirmation_required response' },
          },
          required: ['content'],
        },
      },
      {
        name: 'memory_list',
        description: 'List recent memory entries for a project or scope.',
        inputSchema: {
          type: 'object' as const,
          properties: {
            project: { type: 'string' },
            scope: { type: 'string', enum: ['personal', 'org', 'all'] },
            limit: { type: 'number', description: 'Default 20, max 50' },
          },
        },
      },
      {
        name: 'memory_delete',
        description: 'Delete a personal memory entry by ID. Admins can delete any entry.',
        inputSchema: {
          type: 'object' as const,
          properties: {
            id: { type: 'string', description: 'Entry UUID to delete' },
          },
          required: ['id'],
        },
      },
    ],
  }));

  server.setRequestHandler(CallToolRequestSchema, async (request) => {
    const { name, arguments: args } = request.params;
    let result: unknown;

    switch (name) {
      case 'memory_search':
        result = await memorySearch(args as { query: string; project?: string; limit?: number }, user);
        break;
      case 'memory_add':
        result = await memoryAdd(args as { content: string; entry_type?: string; project?: string; scope?: string; confirmed?: boolean }, user);
        break;
      case 'memory_list':
        result = await memoryList(args as { project?: string; scope?: string; limit?: number }, user);
        break;
      case 'memory_delete':
        result = await memoryDelete(args as { id: string }, user);
        break;
      default:
        throw new Error(`Unknown tool: ${name}`);
    }

    return {
      content: [{ type: 'text' as const, text: JSON.stringify(result, null, 2) }],
    };
  });

  const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
  await server.connect(transport);
  await transport.handleRequest(req, res, req.body);
  await server.close();
});

const PORT = parseInt(process.env.PORT ?? '8080', 10);

async function main(): Promise<void> {
  await initDb();
  app.listen(PORT, () => {
    console.log(`[mcp-memory] listening on port ${PORT}`);
  });
}

main().catch(console.error);
