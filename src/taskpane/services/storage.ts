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

const KEY = 'fait_api_key';

export async function getApiKey(): Promise<string | null> {
  try {
    const storage = getStorage();
    const value = await storage.getItem(KEY);
    return value ?? null;
  } catch {
    return null;
  }
}

export async function setApiKey(key: string): Promise<void> {
  try {
    const storage = getStorage();
    await storage.setItem(KEY, key);
  } catch {
    throw new Error('STORAGE_UNAVAILABLE');
  }
}

export async function clearApiKey(): Promise<void> {
  try {
    const storage = getStorage();
    await storage.removeItem(KEY);
  } catch {
    // ignore
  }
}
