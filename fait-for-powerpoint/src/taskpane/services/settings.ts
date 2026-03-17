// localStorage shim — used when OfficeRuntime is not available (plain browser / dev)
const localStorageShim = {
  getItem: (key: string): Promise<string | null> =>
    Promise.resolve(localStorage.getItem(key)),
  setItem: (key: string, value: string): Promise<void> =>
    Promise.resolve(void localStorage.setItem(key, value)),
  removeItem: (key: string): Promise<void> =>
    Promise.resolve(void localStorage.removeItem(key)),
};

// Safe accessor — checks at call time, not module load time.
function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? localStorageShim;
}

export interface FaitSettings {
  apiKey: string | null;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>;
  projectId: string | null;
}

export async function loadSettings(): Promise<FaitSettings> {
  const storage = getStorage();
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
      corp: corpToggle !== 'false',
      team: teamToggle === 'true',
    },
    projectId: projectId || null,
  };
}

export async function saveSetting(key: string, value: string): Promise<void> {
  const storage = getStorage();
  await storage.setItem(key, value).catch(() => {
    throw new Error('STORAGE_UNAVAILABLE');
  });
}

export async function setApiKey(key: string): Promise<void> {
  await saveSetting('fait_api_key', key);
}
