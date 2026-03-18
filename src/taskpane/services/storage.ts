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

export const AUTH_TOKEN_KEY  = 'fait_entra_token';
export const AUTH_EXPIRY_KEY = 'fait_entra_expiry';
export const AUTH_USER_KEY   = 'fait_entra_user';
export const APIKEY_KEY = 'fait_api_key';

const KEY = APIKEY_KEY;

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
