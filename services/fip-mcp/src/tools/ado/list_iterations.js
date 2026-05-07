// src/tools/ado/list_iterations.js
import { adoGet } from './ado-client.js';

/**
 * List iterations/sprints for a project.
 * @param {object} user
 * @param {{ project: string, team?: string, timeframe?: string }} options
 */
export async function listAdoIterations(user, { project, team, timeframe } = {}) {
  if (!project) throw new Error('[ADO] project is required for list_iterations');

  // ADO default team name is the project name
  const teamSegment = encodeURIComponent(team ?? project);
  const projectSegment = encodeURIComponent(project);

  let path = `/${projectSegment}/${teamSegment}/_apis/work/teamsettings/iterations?api-version=7.1`;
  if (timeframe) path += `&$timeframe=${encodeURIComponent(timeframe)}`;

  const result = await adoGet(path);

  return (result.value ?? []).map(it => ({
    id: it.id,
    name: it.name,
    path: it.path,
    startDate: it.attributes?.startDate ?? null,
    finishDate: it.attributes?.finishDate ?? null,
  }));
}
