import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { z } from 'zod';

import { createGraphClient } from '../tools/ms365/graph-client.js';
import { listEmails } from '../tools/ms365/list_emails.js';
import { getEmail } from '../tools/ms365/get_email.js';
import { sendEmail } from '../tools/ms365/send_email.js';
import { listCalendarEvents } from '../tools/ms365/list_calendar_events.js';
import { createCalendarEvent } from '../tools/ms365/create_calendar_event.js';
import { listTeamsChats } from '../tools/ms365/list_teams_chats.js';
import { sendTeamsMessage } from '../tools/ms365/send_teams_message.js';
import { handleGraphError } from '../utils/graph-error.js';

/**
 * Factory: create an MS365-only McpServer with 7 MS365 tools.
 *
 * @param {{ user_id: string, groups: string[], tid: string, roles: string[] }} user
 * @param {string} rawToken - Raw Bearer token for Microsoft Graph API calls
 */
export function createMs365Server(user, rawToken) {
  const server = new McpServer({
    name: 'ms365',
    version: '1.0.0',
  });

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

  return server;
}
