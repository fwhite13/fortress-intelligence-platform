import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { z } from 'zod';

import { listAdoProjects } from '../tools/ado/list_projects.js';
import { listAdoWorkItems } from '../tools/ado/list_work_items.js';
import { getAdoWorkItem } from '../tools/ado/get_work_item.js';
import { createAdoWorkItem } from '../tools/ado/create_work_item.js';
import { updateAdoWorkItem } from '../tools/ado/update_work_item.js';
import { addAdoComment } from '../tools/ado/add_comment.js';
import { listAdoIterations } from '../tools/ado/list_iterations.js';
import { isPATConfigured } from '../tools/ado/ado-client.js';

/**
 * Factory: create an ADO-only McpServer with 7 ADO tools.
 *
 * @param {{ user_id: string, groups: string[], tid: string, roles: string[] }} user
 * @param {string} _rawToken - unused by ADO tools, kept for signature parity
 */
export function createAdoServer(user, _rawToken) {
  const server = new McpServer({
    name: 'ado',
    version: '1.0.0',
  });

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

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
        console.error('[ado] ADO error:', err);
        return { content: [{ type: 'text', text: `ADO error: ${err.message}` }], isError: true };
      }
    }
  );

  return server;
}
