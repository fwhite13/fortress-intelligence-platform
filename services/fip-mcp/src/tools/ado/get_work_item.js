// src/tools/ado/get_work_item.js
import { adoGet } from './ado-client.js';

/**
 * Get full work item detail by ID.
 * @param {object} user
 * @param {{ id: number|string }} options
 */
export async function getAdoWorkItem(user, { id } = {}) {
  if (!id) throw new Error('[ADO] id is required for get_work_item');

  const wi = await adoGet(`/_apis/wit/workitems/${id}?$expand=all&api-version=7.1`);
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
