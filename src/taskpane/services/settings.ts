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
// In Excel Online, OfficeRuntime.storage IS backed by localStorage anyway,
// so the shim is semantically equivalent for the web scenario.
function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? localStorageShim;
}

export interface FaitSettings {
  apiKey: string | null;
  model: 'haiku' | 'sonnet';
  kbToggles: Record<string, boolean>; // { corp: true, team: false, ... }
  projectId: string | null;
  authMode: 'entra' | 'appkey';  // NEW
}

export async function loadSettings(): Promise<FaitSettings> {
  const storage = getStorage();
  const [apiKey, model, projectId, corpToggle, teamToggle, entraToken] = await Promise.all([
    storage.getItem('fait_api_key').catch(() => null),
    storage.getItem('fait_model').catch(() => null),
    storage.getItem('fait_project_id').catch(() => null),
    storage.getItem('fait_kb_corp').catch(() => null),
    storage.getItem('fait_kb_team').catch(() => null),
    storage.getItem('fait_entra_token').catch(() => null),
  ]);
  return {
    apiKey: apiKey ?? null,
    model: model === 'haiku' ? 'haiku' : 'sonnet',
    kbToggles: {
      corp: corpToggle !== 'false', // default ON
      team: teamToggle === 'true',  // default OFF
    },
    projectId: projectId || null,
    authMode: entraToken ? 'entra' : 'appkey',
  };
}

export async function saveSetting(key: string, value: string): Promise<void> {
  const storage = getStorage();
  await storage.setItem(key, value).catch(() => {
    throw new Error('STORAGE_UNAVAILABLE');
  });
}
