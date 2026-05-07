// src/tools/ado/list_work_items.js
import { adoPost, adoGet } from './ado-client.js';

/**
 * Escape user-supplied values for safe WIQL interpolation.
 * Doubles single quotes to prevent ADO 400 errors (e.g. O'Brien → O''Brien).
 */
function wiqlEscape(value) {
  return value.replace(/'/g, "''");
}

/**
 * Query work items by project with optional filters.
 * @param {object} user
 * @param {{ project: string, state?: string, type?: string, assignedTo?: string, iteration?: string, top?: number }} options
 */
export async function listAdoWorkItems(user, { project, state, type, assignedTo, iteration, top = 50 } = {}) {
  if (!project) throw new Error('[ADO] project is required for list_work_items');

  // Build WIQL query dynamically — only include clauses for provided filters
  const conditions = [`[System.TeamProject] = '${wiqlEscape(project)}'`];
  if (state) conditions.push(`[System.State] = '${wiqlEscape(state)}'`);
  if (type) conditions.push(`[System.WorkItemType] = '${wiqlEscape(type)}'`);
  if (assignedTo) conditions.push(`[System.AssignedTo] = '${wiqlEscape(assignedTo)}'`);
  if (iteration) conditions.push(`[System.IterationPath] UNDER '${wiqlEscape(iteration)}'`);

  const wiql = `SELECT [System.Id], [System.Title], [System.State], [System.AssignedTo], [System.WorkItemType], [System.IterationPath] FROM WorkItems WHERE ${conditions.join(' AND ')} ORDER BY [System.ChangedDate] DESC`;

  const wiqlResult = await adoPost(`/${encodeURIComponent(project)}/_apis/wit/wiql?api-version=7.1&$top=${top}`, { query: wiql });

  const ids = wiqlResult.workItems?.map(wi => wi.id) ?? [];
  if (ids.length === 0) return [];

  // Batch fetch fields (ADO limit: 200 IDs per request — WIQL $top keeps us under this)
  const fields = 'System.Id,System.Title,System.State,System.AssignedTo,System.WorkItemType,System.IterationPath';
  const batchResult = await adoGet(`/_apis/wit/workitems?ids=${ids.join(',')}&fields=${fields}&api-version=7.1`);

  return (batchResult.value ?? []).map(wi => ({
    id: wi.id,
    title: wi.fields['System.Title'],
    state: wi.fields['System.State'],
    assignedTo: wi.fields['System.AssignedTo']?.displayName ?? wi.fields['System.AssignedTo'] ?? null,
    type: wi.fields['System.WorkItemType'],
    iteration: wi.fields['System.IterationPath'],
  }));
}
