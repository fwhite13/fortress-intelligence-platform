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

// MS365 tool imports
import { createGraphClient } from './tools/ms365/graph-client.js';
import { listEmails } from './tools/ms365/list_emails.js';
import { getEmail } from './tools/ms365/get_email.js';
import { sendEmail } from './tools/ms365/send_email.js';
import { listCalendarEvents } from './tools/ms365/list_calendar_events.js';
import { createCalendarEvent } from './tools/ms365/create_calendar_event.js';
import { listTeamsChats } from './tools/ms365/list_teams_chats.js';
import { sendTeamsMessage } from './tools/ms365/send_teams_message.js';
import { handleGraphError } from './utils/graph-error.js';

// ADO tool imports
import { listAdoProjects } from './tools/ado/list_projects.js';
import { listAdoWorkItems } from './tools/ado/list_work_items.js';
import { getAdoWorkItem } from './tools/ado/get_work_item.js';
import { createAdoWorkItem } from './tools/ado/create_work_item.js';
import { updateAdoWorkItem } from './tools/ado/update_work_item.js';
import { addAdoComment } from './tools/ado/add_comment.js';
import { listAdoIterations } from './tools/ado/list_iterations.js';
import { isPATConfigured } from './tools/ado/ado-client.js';

// Search tool imports
import { webSearch } from './tools/search/web_search.js';
import { isAPIKeyConfigured } from './tools/search/search-client.js';

// Path-routed server factories
import { createForgeKbServer } from './servers/forge-kb-server.js';
import { createMs365Server } from './servers/ms365-server.js';
import { createAdoServer } from './servers/ado-server.js';
import { createWebServer } from './servers/web-server.js';

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

// --- Tool error handler (forge-kb tools) ---
function handleToolError(err) {
  if (err.code && err.status) {
    return { error: { code: err.code, message: err.message } };
  }
  console.error('[fip-mcp] Tool error:', err);
  return { error: { code: 'INTERNAL_ERROR', message: err.message ?? 'Internal server error' } };
}

/**
 * Factory: create a new McpServer with all tools registered.
 * User and rawToken are captured via closure — no metadata injection required.
 *
 * @param {{ user_id: string, groups: string[], tid: string, roles: string[] }} user - Decoded JWT claims
 * @param {string} rawToken - Raw Bearer token for Microsoft Graph API calls
 */
function createMcpServer(user, rawToken) {
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

  // ---- MS365 Tools ----

  // Tool: list_emails
  server.tool(
    'list_emails',
    'List emails from the user\'s inbox via Microsoft Graph. Returns subject, sender, preview, and read status.',
    {
      top: z.number().int().min(1).max(50).optional().default(10).describe('Max emails to return (default 10, max 50)'),
      filter: z.string().optional().describe('OData filter expression (e.g. "isRead eq false")'),
      search: z.string().optional().describe('Search query string (searches subject, body, sender)'),
    },
    async ({ top, filter, search }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await listEmails(client, { top, filter, search });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: get_email
  server.tool(
    'get_email',
    'Get the full content of a specific email by ID via Microsoft Graph.',
    {
      messageId: z.string().describe('Email message ID (from list_emails)'),
    },
    async ({ messageId }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await getEmail(client, { messageId });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: send_email
  server.tool(
    'send_email',
    'Send an email on behalf of the user via Microsoft Graph.',
    {
      to: z.array(z.string().email()).describe('Recipient email addresses'),
      subject: z.string().describe('Email subject'),
      body: z.string().describe('Email body (HTML supported)'),
      cc: z.array(z.string().email()).optional().default([]).describe('CC recipients (optional)'),
    },
    async ({ to, subject, body, cc }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await sendEmail(client, { to, subject, body, cc });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: list_calendar_events
  server.tool(
    'list_calendar_events',
    'List calendar events in a date range via Microsoft Graph calendarView.',
    {
      startDateTime: z.string().describe('Start of range in ISO 8601 format (e.g. "2026-05-07T00:00:00Z")'),
      endDateTime: z.string().describe('End of range in ISO 8601 format (e.g. "2026-05-08T00:00:00Z")'),
      top: z.number().int().min(1).max(100).optional().default(10).describe('Max events to return (default 10)'),
    },
    async ({ startDateTime, endDateTime, top }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await listCalendarEvents(client, { startDateTime, endDateTime, top });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: create_calendar_event
  server.tool(
    'create_calendar_event',
    'Create a calendar event, optionally as a Teams meeting, via Microsoft Graph.',
    {
      subject: z.string().describe('Event subject/title'),
      start: z.string().describe('Start time in ISO 8601 UTC (e.g. "2026-05-08T14:00:00Z")'),
      end: z.string().describe('End time in ISO 8601 UTC (e.g. "2026-05-08T15:00:00Z")'),
      attendees: z.array(z.string().email()).optional().default([]).describe('Attendee email addresses'),
      body: z.string().optional().default('').describe('Event body/description (HTML supported)'),
      location: z.string().optional().default('').describe('Location display name'),
      isTeamsMeeting: z.boolean().optional().default(false).describe('Create as Teams online meeting'),
    },
    async ({ subject, start, end, attendees, body, location, isTeamsMeeting }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await createCalendarEvent(client, { subject, start, end, attendees, body, location, isTeamsMeeting });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: list_teams_chats
  server.tool(
    'list_teams_chats',
    'List Teams chats the user is a member of via Microsoft Graph.',
    {
      top: z.number().int().min(1).max(50).optional().default(10).describe('Max chats to return (default 10)'),
    },
    async ({ top }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await listTeamsChats(client, { top });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // Tool: send_teams_message
  server.tool(
    'send_teams_message',
    'Send a message to a Teams chat via Microsoft Graph.',
    {
      chatId: z.string().describe('Teams chat ID (from list_teams_chats)'),
      content: z.string().describe('Message content (HTML supported)'),
    },
    async ({ chatId, content }) => {
      try {
        const client = createGraphClient(rawToken);
        const result = await sendTeamsMessage(client, { chatId, content });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        const e = handleGraphError(err);
        return { content: [{ type: 'text', text: JSON.stringify(e, null, 2) }], isError: true };
      }
    }
  );

  // ---- ADO Tools ----

  // Tool: list_ado_projects
  server.tool(
    'list_ado_projects',
    'List all Azure DevOps projects in the organization.',
    {
      top: z.number().int().min(1).max(500).optional().describe('Max projects to return (default 100)'),
    },
    async ({ top }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await listAdoProjects(user, { top });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  // Tool: list_ado_work_items
  server.tool(
    'list_ado_work_items',
    'Query Azure DevOps work items by project with optional filters (state, type, assignedTo, iteration).',
    {
      project: z.string().describe('ADO project name'),
      state: z.string().optional().describe('Filter by state (e.g. "Active", "Resolved", "Closed")'),
      type: z.string().optional().describe('Filter by work item type (e.g. "Task", "Bug", "User Story")'),
      assignedTo: z.string().optional().describe('Filter by assigned user (display name or email)'),
      iteration: z.string().optional().describe('Filter by iteration path (uses UNDER — matches sub-iterations)'),
      top: z.number().int().min(1).max(200).optional().describe('Max work items to return (default 50)'),
    },
    async ({ project, state, type, assignedTo, iteration, top }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await listAdoWorkItems(user, { project, state, type, assignedTo, iteration, top });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  // Tool: get_ado_work_item
  server.tool(
    'get_ado_work_item',
    'Get full details of an Azure DevOps work item by ID.',
    {
      id: z.number().int().describe('Work item ID'),
    },
    async ({ id }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await getAdoWorkItem(user, { id });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  // Tool: create_ado_work_item
  server.tool(
    'create_ado_work_item',
    'Create a new Azure DevOps work item.',
    {
      project: z.string().describe('ADO project name'),
      type: z.string().describe('Work item type (e.g. "Task", "Bug", "User Story", "Feature")'),
      title: z.string().describe('Work item title'),
      description: z.string().optional().describe('Work item description (HTML supported)'),
      assignedTo: z.string().optional().describe('Assign to user (display name or email)'),
      iterationPath: z.string().optional().describe('Iteration path (e.g. "MyProject\\Sprint 3")'),
      priority: z.number().int().min(1).max(4).optional().describe('Priority (1=Critical, 2=High, 3=Medium, 4=Low)'),
      parentId: z.number().int().optional().describe('Parent work item ID (creates hierarchy link)'),
    },
    async ({ project, type, title, description, assignedTo, iterationPath, priority, parentId }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await createAdoWorkItem(user, { project, type, title, description, assignedTo, iterationPath, priority, parentId });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  // Tool: update_ado_work_item
  server.tool(
    'update_ado_work_item',
    'Update fields on an Azure DevOps work item.',
    {
      id: z.number().int().describe('Work item ID to update'),
      state: z.string().optional().describe('New state (e.g. "Active", "Resolved", "Closed")'),
      title: z.string().optional().describe('New title'),
      assignedTo: z.string().optional().describe('Reassign to user (display name or email)'),
      iterationPath: z.string().optional().describe('New iteration path'),
      priority: z.number().int().min(1).max(4).optional().describe('New priority (1=Critical, 2=High, 3=Medium, 4=Low)'),
    },
    async ({ id, state, title, assignedTo, iterationPath, priority }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await updateAdoWorkItem(user, { id, state, title, assignedTo, iterationPath, priority });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  // Tool: add_ado_comment
  server.tool(
    'add_ado_comment',
    'Add a comment to an Azure DevOps work item.',
    {
      project: z.string().describe('ADO project name'),
      id: z.number().int().describe('Work item ID'),
      text: z.string().describe('Comment text (HTML supported)'),
    },
    async ({ project, id, text }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await addAdoComment(user, { project, id, text });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  // Tool: list_ado_iterations
  server.tool(
    'list_ado_iterations',
    'List iterations/sprints for an Azure DevOps project.',
    {
      project: z.string().describe('ADO project name'),
      team: z.string().optional().describe('Team name (defaults to project name — the ADO default team)'),
      timeframe: z.string().optional().describe('Filter by timeframe: "current", "past", or "future"'),
    },
    async ({ project, team, timeframe }) => {
      if (!isPATConfigured()) return { content: [{ type: 'text', text: 'ADO connector not configured: AZDO_PAT env var missing' }], isError: true };
      try {
        const result = await listAdoIterations(user, { project, team, timeframe });
        return { content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] };
      } catch (err) {
        console.error('[fip-mcp] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );


  // ---- Search Tools ----

  // Tool: web_search
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
        console.error('[fip-mcp] web_search error:', err);
        return { content: [{ type: 'text', text: `Search error: ${err.message}` }], isError: true };
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
  const rawToken = req.user.rawToken;

  try {
    const server = createMcpServer(user, rawToken);
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
  const rawToken = req.user.rawToken;

  try {
    const server = createMcpServer(user, rawToken);
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

// --- forge-kb path-routed server ---
const forgeKbSseSessions = new Map();

// Health check at /mcp/forge-kb/health — public, no auth
app.get('/mcp/forge-kb/health', (_req, res) => {
  res.json({ status: 'ok', server: 'forge-kb', version: VERSION });
});

// POST /mcp/forge-kb — KB-only MCP server (Streamable HTTP transport)
app.post('/mcp/forge-kb', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;

  try {
    const server = createForgeKbServer(user, rawToken);
    const transport = new StreamableHTTPServerTransport({
      sessionIdGenerator: undefined,
    });

    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
  } catch (err) {
    console.error('[fip-mcp] POST /mcp/forge-kb error:', err);
    if (!res.headersSent) {
      res.status(500).json({ error: 'Internal server error' });
    }
  }
});

// GET /mcp/forge-kb/sse — SSE stream for KB-only server
app.get('/mcp/forge-kb/sse', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;

  try {
    const server = createForgeKbServer(user, rawToken);
    const transport = new SSEServerTransport('/mcp/forge-kb/sse', res);

    const sessionId = transport.sessionId;
    if (sessionId) {
      forgeKbSseSessions.set(sessionId, transport);
    }

    transport.onclose = () => {
      if (sessionId) forgeKbSseSessions.delete(sessionId);
    };

    await server.connect(transport);
  } catch (err) {
    console.error('[fip-mcp] GET /mcp/forge-kb/sse error:', err);
    if (!res.headersSent) {
      res.status(500).json({ error: 'Internal server error' });
    }
  }
});

// --- ms365 path-routed server ---
const ms365SseSessions = new Map();

app.get('/mcp/ms365/health', (_req, res) => {
  res.json({ status: 'ok', server: 'ms365', version: VERSION });
});

app.post('/mcp/ms365', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createMs365Server(user, rawToken);
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
  } catch (err) {
    console.error('[fip-mcp] POST /mcp/ms365 error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});

app.get('/mcp/ms365/sse', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createMs365Server(user, rawToken);
    const transport = new SSEServerTransport('/mcp/ms365/sse', res);
    const sessionId = transport.sessionId;
    if (sessionId) ms365SseSessions.set(sessionId, transport);
    transport.onclose = () => { if (sessionId) ms365SseSessions.delete(sessionId); };
    await server.connect(transport);
  } catch (err) {
    console.error('[fip-mcp] GET /mcp/ms365/sse error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});

// --- ado path-routed server ---
const adoSseSessions = new Map();

app.get('/mcp/ado/health', (_req, res) => {
  res.json({ status: 'ok', server: 'ado', version: VERSION });
});

app.post('/mcp/ado', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createAdoServer(user, rawToken);
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
  } catch (err) {
    console.error('[fip-mcp] POST /mcp/ado error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});

app.get('/mcp/ado/sse', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createAdoServer(user, rawToken);
    const transport = new SSEServerTransport('/mcp/ado/sse', res);
    const sessionId = transport.sessionId;
    if (sessionId) adoSseSessions.set(sessionId, transport);
    transport.onclose = () => { if (sessionId) adoSseSessions.delete(sessionId); };
    await server.connect(transport);
  } catch (err) {
    console.error('[fip-mcp] GET /mcp/ado/sse error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});

// --- web path-routed server ---
const webSseSessions = new Map();

app.get('/mcp/web/health', (_req, res) => {
  res.json({ status: 'ok', server: 'web', version: VERSION });
});

app.post('/mcp/web', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createWebServer(user, rawToken);
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
  } catch (err) {
    console.error('[fip-mcp] POST /mcp/web error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});

app.get('/mcp/web/sse', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createWebServer(user, rawToken);
    const transport = new SSEServerTransport('/mcp/web/sse', res);
    const sessionId = transport.sessionId;
    if (sessionId) webSseSessions.set(sessionId, transport);
    transport.onclose = () => { if (sessionId) webSseSessions.delete(sessionId); };
    await server.connect(transport);
  } catch (err) {
    console.error('[fip-mcp] GET /mcp/web/sse error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
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
  console.log(`[fip-mcp] FIP MCP Server v${VERSION} listening on port ${PORT}`);
  console.log(`[fip-mcp] Path routes: /mcp (monolith), /mcp/forge-kb, /mcp/ms365, /mcp/ado, /mcp/web`);
  console.log(`[fip-mcp] Entra tenant: ${process.env.ENTRA_TENANT_ID}`);
  console.log(`[fip-mcp] Entra client: ${process.env.ENTRA_CLIENT_ID}`);
  console.log(`[fip-mcp] Bedrock region: ${process.env.BEDROCK_REGION ?? 'us-east-1'}`);
  console.log(`[fip-mcp] Entitlements config: ${process.env.FALLBACK_ENTITLEMENTS_CONFIG ?? '(bundled default)'}`);
});
