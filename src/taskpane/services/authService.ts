// FIP Entra app registration — same clientId as FAIT/FIRM
const CLIENT_ID  = '887206bc-fac1-436a-a8ed-2150418d76c0';
const TENANT_ID  = 'd2bf3425-f8ab-451c-83bd-1e0ebd9508fe';
// The FAIT backend validates tokens issued for this scope
// The scope must be registered on the FIP app registration as an exposed API scope
const SCOPE      = 'api://887206bc-fac1-436a-a8ed-2150418d76c0/FfE.Access';

// The dialog page URL — must be served from the same origin as the add-in
// In production: https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html
const DIALOG_URL_BASE = `${window.location.origin}/excel-addin/auth-dialog.html`;

const AUTH_TOKEN_KEY   = 'fait_entra_token';
const AUTH_EXPIRY_KEY  = 'fait_entra_expiry';
const AUTH_USER_KEY    = 'fait_entra_user';   // JSON: { userId, email, name, oid }
const APIKEY_KEY       = 'fait_api_key';

function getStorage() {
  return (window as any).OfficeRuntime?.storage ?? {
    getItem: (k: string) => Promise.resolve(localStorage.getItem(k)),
    setItem: (k: string, v: string) => Promise.resolve(void localStorage.setItem(k, v)),
    removeItem: (k: string) => Promise.resolve(void localStorage.removeItem(k)),
  };
}

export interface FaitUser {
  userId: string;   // FAIT AppUser GUID (resolved from backend after first auth)
  email:  string;
  name:   string;
  oid:    string;   // Entra Object ID
}

// ── Silent token refresh ──────────────────────────────────────────────────────

/** Get a valid token from storage. Returns null if missing or expired. */
export async function getStoredToken(): Promise<string | null> {
  const storage = getStorage();
  const [token, expiry] = await Promise.all([
    storage.getItem(AUTH_TOKEN_KEY).catch(() => null),
    storage.getItem(AUTH_EXPIRY_KEY).catch(() => null),
  ]);
  if (!token || !expiry) return null;
  // Treat as expired 5 minutes before actual expiry (buffer for clock skew)
  if (Date.now() > parseInt(expiry, 10) - 5 * 60 * 1000) return null;
  return token;
}

/** Store a new token and its expiry. */
export async function storeToken(token: string, expiresInSeconds: number): Promise<void> {
  const storage = getStorage();
  const expiry = Date.now() + expiresInSeconds * 1000;
  await Promise.all([
    storage.setItem(AUTH_TOKEN_KEY, token),
    storage.setItem(AUTH_EXPIRY_KEY, String(expiry)),
  ]);
}

export async function getStoredUser(): Promise<FaitUser | null> {
  const storage = getStorage();
  const raw = await storage.getItem(AUTH_USER_KEY).catch(() => null);
  if (!raw) return null;
  try { return JSON.parse(raw) as FaitUser; }
  catch { return null; }
}

export async function storeUser(user: FaitUser): Promise<void> {
  const storage = getStorage();
  await storage.setItem(AUTH_USER_KEY, JSON.stringify(user));
}

export async function clearAuth(): Promise<void> {
  const storage = getStorage();
  await Promise.all([
    storage.removeItem(AUTH_TOKEN_KEY),
    storage.removeItem(AUTH_EXPIRY_KEY),
    storage.removeItem(AUTH_USER_KEY),
  ]);
}

// ── AppKey fallback ───────────────────────────────────────────────────────────

export async function getApiKey(): Promise<string | null> {
  return getStorage().getItem(APIKEY_KEY).catch(() => null);
}

// ── Auth header for faitApi.ts ────────────────────────────────────────────────

/**
 * Returns the correct auth header for FAIT API calls.
 * Priority: Entra token > AppKey > empty (will get 401).
 * Callers don't need to know which mode is active.
 */
export async function getAuthHeader(): Promise<Record<string, string>> {
  const token = await getStoredToken();
  if (token) return { 'Authorization': `Bearer ${token}` };

  const apiKey = await getApiKey();
  if (apiKey) return { 'x-api-key': apiKey };

  return {};
}

// ── Interactive sign-in via Office Dialog API ─────────────────────────────────

export interface SignInResult {
  success: boolean;
  user?: FaitUser;
  error?: string;
}

/**
 * Launch the Entra sign-in flow via Office.context.ui.displayDialogAsync.
 * Opens authDialog.html in a top-level dialog window (not an iframe).
 * The dialog runs the MSAL redirect flow and posts the result back via
 * Office.context.ui.messageParent().
 */
export function signIn(): Promise<SignInResult> {
  return new Promise((resolve) => {
    const dialogUrl = `${DIALOG_URL_BASE}?clientId=${CLIENT_ID}&tenantId=${TENANT_ID}&scope=${encodeURIComponent(SCOPE)}`;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const Office = (window as any).Office;
    Office.context.ui.displayDialogAsync(
      dialogUrl,
      { height: 60, width: 30, promptBeforeOpen: false },
      (asyncResult: any) => {
        if (asyncResult.status === Office.AsyncResultStatus.Failed) {
          resolve({ success: false, error: `Dialog failed to open: ${asyncResult.error.message}` });
          return;
        }

        const dialog = asyncResult.value;

        dialog.addEventHandler(
          Office.EventType.DialogMessageReceived,
          async (args: any) => {
            dialog.close();
            try {
              const msg = JSON.parse(args.message) as DialogMessage;
              if (msg.type === 'auth_success') {
                await storeToken(msg.accessToken, msg.expiresIn);
                // Resolve user identity from FAIT backend
                const user = await resolveUserIdentity(msg.accessToken, msg.oid, msg.email, msg.name);
                await storeUser(user);
                resolve({ success: true, user });
              } else {
                resolve({ success: false, error: msg.error ?? 'Sign-in cancelled' });
              }
            } catch (e: any) {
              resolve({ success: false, error: e.message });
            }
          }
        );

        dialog.addEventHandler(
          Office.EventType.DialogEventReceived,
          (args: any) => {
            if (args.error === 12006) {
              // User closed the dialog manually
              dialog.close();
              resolve({ success: false, error: 'Sign-in cancelled' });
            }
          }
        );
      }
    );
  });
}

interface DialogMessage {
  type: 'auth_success' | 'auth_error';
  accessToken: string;
  expiresIn: number;
  oid: string;
  email: string;
  name: string;
  error?: string;
}

/**
 * After successful sign-in, call FAIT backend to resolve the FAIT userId
 * (may create a new user on first login — mirrors FIP portal behaviour).
 * Returns a FaitUser with the FAIT-internal userId populated.
 */
async function resolveUserIdentity(accessToken: string, oid: string, email: string, name: string): Promise<FaitUser> {
  try {
    const resp = await fetch('https://fait.dev.fortressam.ai/api/excel/whoami', {
      method: 'GET',
      headers: { 'Authorization': `Bearer ${accessToken}` },
    });
    if (resp.ok) {
      const body = await resp.json() as { userId: string; email: string; name: string };
      return { userId: body.userId, email: body.email, name: body.name, oid };
    }
  } catch { /* Non-fatal — use Entra claims as fallback */ }
  // Fallback: use oid as userId (backend will reconcile on next API call)
  return { userId: oid, email, name, oid };
}

