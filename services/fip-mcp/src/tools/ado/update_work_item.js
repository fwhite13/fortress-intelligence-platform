// src/tools/ado/update_work_item.js
import { adoPatch } from './ado-client.js';

/**
 * Update work item fields.
 * @param {object} user
 * @param {{ id: number|string, state?: string, title?: string, assignedTo?: string, iterationPath?: string, priority?: number }} options
 */
export async function updateAdoWorkItem(user, { id, state, title, assignedTo, iterationPath, priority } = {}) {
  if (!id) throw new Error('[ADO] id is required for update_work_item');

  const ops = [];
  if (state !== undefined) ops.push({ op: 'add', path: '/fields/System.State', value: state });
  if (title !== undefined) ops.push({ op: 'add', path: '/fields/System.Title', value: title });
  if (assignedTo !== undefined) ops.push({ op: 'add', path: '/fields/System.AssignedTo', value: assignedTo });
  if (iterationPath !== undefined) ops.push({ op: 'add', path: '/fields/System.IterationPath', value: iterationPath });
  if (priority !== undefined) ops.push({ op: 'add', path: '/fields/Microsoft.VSTS.Common.Priority', value: priority });

  if (ops.length === 0) throw new Error('[ADO] No fields provided to update_work_item');

  const wi = await adoPatch(`/_apis/wit/workitems/${id}?api-version=7.1`, ops);
  const f = wi.fields ?? {};

  // Extract parentId from relations if present
  const parentRelation = wi.relations?.find(r => r.rel === 'System.LinkTypes.Hierarchy-Reverse');
  const parentId = parentRelation
    ? parseInt(parentRelation.url.split('/').pop(), 10)
    : null;

  return {
    id: wi.id,
    type: f['System.WorkItemType'],
    title: f['System.Title'],
    state: f['System.State'],
    assignedTo: f['System.AssignedTo']?.displayName ?? f['System.AssignedTo'] ?? null,
    description: f['System.Description'] ?? null,
    iteration: f['System.IterationPath'],
    areaPath: f['System.AreaPath'],
    priority: f['Microsoft.VSTS.Common.Priority'] ?? null,
    tags: f['System.Tags'] ?? null,
    createdBy: f['System.CreatedBy']?.displayName ?? f['System.CreatedBy'] ?? null,
    createdDate: f['System.CreatedDate'] ?? null,
    changedDate: f['System.ChangedDate'] ?? null,
    commentCount: f['System.CommentCount'] ?? 0,
    parentId,
    url: wi._links?.html?.href ?? null,
  };
}
