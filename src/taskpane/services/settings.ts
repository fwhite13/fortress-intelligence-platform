/* eslint-disable @typescript-eslint/no-explicit-any */
declare const OfficeRuntime: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

const storage = OfficeRuntime.storage;

export interface FaitSettings {
  apiKey: string | null;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>; // { corp: true, team: false, ... }
  projectId: string | null;
}

export async function loadSettings(): Promise<FaitSettings> {
  const [apiKey, model, projectId, corpToggle, teamToggle] = await Promise.all([
    storage.getItem('fait_api_key').catch(() => null),
    storage.getItem('fait_model').catch(() => null),
    storage.getItem('fait_project_id').catch(() => null),
    storage.getItem('fait_kb_corp').catch(() => null),
    storage.getItem('fait_kb_team').catch(() => null),
  ]);
  return {
    apiKey: apiKey ?? null,
    model: model === 'haiku' ? 'haiku' : 'sonnet',
    kbToggles: {
      corp: corpToggle !== 'false', // default ON
      team: teamToggle === 'true',  // default OFF
    },
    projectId: projectId || null,
  };
}

export async function saveSetting(key: string, value: string): Promise<void> {
  await storage.setItem(key, value).catch(() => {
    throw new Error('STORAGE_UNAVAILABLE');
  });
}
