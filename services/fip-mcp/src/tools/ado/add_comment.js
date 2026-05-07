// src/tools/ado/add_comment.js
import { adoPost } from './ado-client.js';

/**
 * Add a comment to an ADO work item.
 * @param {object} user
 * @param {{ project: string, id: number|string, text: string }} options
 */
export async function addAdoComment(user, { project, id, text } = {}) {
  if (!project) throw new Error('[ADO] project is required for add_comment');
  if (!id) throw new Error('[ADO] id is required for add_comment');
  if (!text) throw new Error('[ADO] text is required for add_comment');

  const result = await adoPost(
    `/${encodeURIComponent(project)}/_apis/wit/workitems/${id}/comments?api-version=7.1-preview.3`,
    { text }
  );

  return {
    id: result.id,
    workItemId: result.workItemId,
    text: result.text,
    createdBy: result.createdBy?.displayName ?? result.createdBy ?? null,
    createdDate: result.createdDate,
  };
}
