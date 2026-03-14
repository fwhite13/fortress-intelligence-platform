/* eslint-disable @typescript-eslint/no-explicit-any */
declare const OfficeRuntime: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

const KEY = 'fait_api_key';

export async function getApiKey(): Promise<string | null> {
  try {
    const value = await OfficeRuntime.storage.getItem(KEY);
    return value ?? null;
  } catch {
    return null;
  }
}

export async function setApiKey(key: string): Promise<void> {
  try {
    await OfficeRuntime.storage.setItem(KEY, key);
  } catch {
    throw new Error('STORAGE_UNAVAILABLE');
  }
}

export async function clearApiKey(): Promise<void> {
  try {
    await OfficeRuntime.storage.removeItem(KEY);
  } catch {
    // ignore
  }
}
