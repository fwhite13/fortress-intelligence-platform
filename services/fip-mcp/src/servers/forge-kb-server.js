import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { z } from 'zod';

import { searchKb } from '../tools/search_kb.js';
import { listKbs } from '../tools/list_kbs.js';
import { addToKb } from '../tools/add_to_kb.js';
import { getKbMetadata } from '../tools/get_kb_metadata.js';
import { getJobStatus } from '../tools/get_job_status.js';
import { listKbFiles } from '../tools/list_kb_files.js';

function handleToolError(err) {
  if (err.code && err.status) {
    return { error: { code: err.code, message: err.message } };
  }
  console.error('[forge-kb] Tool error:', err);
  return { error: { code: 'INTERNAL_ERROR', message: err.message ?? 'Internal server error' } };
}

/**
 * Factory: create a KB-only McpServer with the 6 forge-kb tools.
 *
 * @param {{ user_id: string, groups: string[], tid: string, roles: string[] }} user
 * @param {string} _rawToken - unused by KB tools, kept for signature parity
 */
export function createForgeKbServer(user, _rawToken) {
  const server = new McpServer({
    name: 'forge-kb',
    version: '1.0.0',
  });

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
