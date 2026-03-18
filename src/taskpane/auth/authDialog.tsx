// src/taskpane/auth/authDialog.tsx
// Runs inside the Office dialog window (not the taskpane).
// Uses MSAL to perform the auth code redirect, then posts result back to taskpane.

import * as msal from '@azure/msal-browser';

// Read params passed in query string from signIn()
const params = new URLSearchParams(window.location.search);
const clientId  = params.get('clientId')  ?? '';
const tenantId  = params.get('tenantId')  ?? '';
const scope     = params.get('scope')     ?? '';
const authority = `https://login.microsoftonline.com/${tenantId}`;

// REDIRECT URI must point back to THIS page — the dialog completes the flow here
const redirectUri = `${window.location.origin}/excel-addin/auth-dialog.html`;

const msalConfig: msal.Configuration = {
  auth: {
    clientId,
    authority,
    redirectUri,
  },
  cache: {
    cacheLocation: 'sessionStorage',   // sessionStorage only — dialog has its own session
  },
};

const msalInstance = new msal.PublicClientApplication(msalConfig);

async function run() {
  const statusEl = document.getElementById('status');

  try {
    // MSAL v3+ requires explicit initialization before use
    await msalInstance.initialize();

    // Step 1: Check if we're returning from a redirect (hash contains code/token)
    const result = await msalInstance.handleRedirectPromise();

    if (result) {
      // We got a token — extract claims and post back to taskpane
      const oid   = (result.idTokenClaims as any)?.oid ?? result.uniqueId;
      const email = result.account?.username ?? '';
      const name  = result.account?.name     ?? email;

      if (statusEl) statusEl.textContent = 'Signed in! Closing…';

      // Post result back to taskpane via Office messageParent
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any).Office.context.ui.messageParent(JSON.stringify({
        type:        'auth_success',
        accessToken: result.accessToken,
        expiresIn:   Math.floor(((result.expiresOn?.getTime() ?? Date.now() + 3600_000) - Date.now()) / 1000),
        oid,
        email,
        name,
      }));
    } else {
      // Step 2: No token yet — initiate login redirect
      if (statusEl) statusEl.textContent = 'Redirecting to Microsoft sign-in…';
      await msalInstance.loginRedirect({
        scopes: [scope, 'openid', 'profile', 'email'],
        prompt: 'select_account',
      });
      // loginRedirect() navigates away — rest of this function doesn't run
    }
  } catch (error: any) {
    if (statusEl) statusEl.textContent = 'Sign-in failed.';
    // Post error back
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (window as any).Office?.context?.ui?.messageParent(JSON.stringify({
      type:  'auth_error',
      error: error.message ?? 'Unknown error',
    }));
  }
}

// Wait for Office.js to be ready in the dialog context
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(window as any).Office.onReady(() => run());
