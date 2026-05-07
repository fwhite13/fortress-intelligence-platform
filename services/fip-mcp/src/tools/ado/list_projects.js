// src/tools/ado/list_projects.js
import { adoGet } from './ado-client.js';

/**
 * List all ADO projects in the organization.
 * @param {object} user - Decoded JWT user (unused for PAT-based tools, kept for signature consistency)
 * @param {{ top?: number }} options
 */
export async function listAdoProjects(user, { top = 100 } = {}) {
  const result = await adoGet(`/_apis/projects?api-version=7.1&$top=${top}`);
  return result.value?.map(p => ({
    id: p.id,
    name: p.name,
    description: p.description,
    state: p.state,
  })) ?? [];
}
