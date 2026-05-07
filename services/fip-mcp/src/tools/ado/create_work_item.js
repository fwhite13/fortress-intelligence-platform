// src/tools/ado/create_work_item.js
import { adoPatch, getAdoOrg } from './ado-client.js';

/**
 * Create a work item in an ADO project.
 * @param {object} user
 * @param {{ project: string, type: string, title: string, description?: string, assignedTo?: string, iterationPath?: string, priority?: number, parentId?: number }} options
 */
export async function createAdoWorkItem(user, { project, type, title, description, assignedTo, iterationPath, priority, parentId } = {}) {
  if (!project) throw new Error('[ADO] project is required for create_work_item');
  if (!type) throw new Error('[ADO] type is required for create_work_item');
  if (!title) throw new Error('[ADO] title is required for create_work_item');

  const ops = [];

  ops.push({ op: 'add', path: '/fields/System.Title', value: title });
  if (description !== undefined) ops.push({ op: 'add', path: '/fields/System.Description', value: description });
  if (assignedTo !== undefined) ops.push({ op: 'add', path: '/fields/System.AssignedTo', value: assignedTo });
  if (iterationPath !== undefined) ops.push({ op: 'add', path: '/fields/System.IterationPath', value: iterationPath });
  if (priority !== undefined) ops.push({ op: 'add', path: '/fields/Microsoft.VSTS.Common.Priority', value: priority });

  if (parentId !== undefined) {
    const org = getAdoOrg();
    ops.push({
      op: 'add',
      path: '/relations/-',
      value: {
        rel: 'System.LinkTypes.Hierarchy-Reverse',
        url: `https://dev.azure.com/${org}/_apis/wit/workitems/${parentId}`,
      },
    });
  }

  // Work item type in URL: $ + encoded type name
  const typeSegment = `$${encodeURIComponent(type)}`;
  const wi = await adoPatch(`/${encodeURIComponent(project)}/_apis/wit/workitems/${typeSegment}?api-version=7.1`, ops);
  const f = wi.fields ?? {};

  return {
    id: wi.id,
    title: f['System.Title'],
    type: f['System.WorkItemType'],
    state: f['System.State'],
    url: wi._links?.html?.href ?? null,
    createdDate: f['System.CreatedDate'] ?? null,
  };
}
