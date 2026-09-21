/**
 * Microsoft Teams authentication — Entra ID sign-in, session warming, state dumps
 *
 * WI #7287: Inverted auth flow — sign into login.microsoftonline.com first,
 * warm the Teams app session until authtoken/ringFinder cookies are present,
 * then navigate to the meeting link. The meeting route now loads already-authenticated.
 */

import type { BrowserContext, Page } from 'playwright';

/**
 * Log authentication state without throwing (cookies, localStorage, URL)
 */
export async function dumpAuthState(
  context: BrowserContext,
  page: Page,
  label: string
): Promise<void> {
  try {
    console.log(`[Teams][AUTH-STATE] === ${label} ===`);
    console.log(`[Teams][AUTH-STATE] URL: ${page.url()}`);

    const cookies = await context.cookies();
    const authCookies = cookies.filter(c =>
      /ESTSAUTH|SignInStateCookie|authtoken|ringFinder|msal/i.test(c.name)
    );
    console.log(`[Teams][AUTH-STATE] Auth cookies (${authCookies.length}):`);
    authCookies.forEach(c => {
      console.log(`  - ${c.name} (domain: ${c.domain})`);
    });

    // Count msal.* localStorage keys on Teams origins
    try {
      const msalKeyCount = await page.evaluate(() => {
        if (!window.location.hostname.includes('teams.microsoft.com') &&
            !window.location.hostname.includes('cloud.microsoft')) {
          return 0;
        }
        let count = 0;
        for (let i = 0; i < localStorage.length; i++) {
          const key = localStorage.key(i);
          if (key && key.startsWith('msal.')) count++;
        }
        return count;
      });
      if (msalKeyCount > 0) {
        console.log(`[Teams][AUTH-STATE] localStorage msal.* keys: ${msalKeyCount}`);
      }
    } catch {
      // Page may not be on a Teams origin yet
    }
  } catch (err) {
    console.log(`[Teams][AUTH-STATE] dumpAuthState(${label}) error (non-fatal):`, err);
  }
}

/**
 * Sign in to Microsoft (Entra ID) and leave the page open for reuse.
 * Navigates to login.microsoftonline.com, completes the email/password flow,
 * handles interrupts (KMSI, account picker, etc.), and waits for ESTSAUTH cookie.
 */
export async function signInToMicrosoft(
  page: Page,
  email: string,
  password: string
): Promise<void> {
  console.log('[Teams][AUTH] Starting Microsoft sign-in...');

  // Navigate to Microsoft login
  await page.goto('https://login.microsoftonline.com/', {
    waitUntil: 'domcontentloaded',
    timeout: 30000
  });

  console.log('[Teams][AUTH] On Microsoft login page');

  // Fill email
  const emailInput = page.locator('input[name="loginfmt"], input[type="email"]').first();
  await emailInput.waitFor({ state: 'visible', timeout: 15000 });
  await emailInput.fill(email);
  console.log('[Teams][AUTH] Email entered');

  // Click Next
  const nextButton = page.locator('#idSIButton9, input[type="submit"]').first();
  await nextButton.click();
  console.log('[Teams][AUTH] Clicked Next after email');

  await page.waitForTimeout(1500);

  // Fill password
  const passwordInput = page.locator('input[name="passwd"], input[type="password"]').first();
  await passwordInput.waitFor({ state: 'visible', timeout: 20000 });
  await passwordInput.fill(password);
  console.log('[Teams][AUTH] Password entered');

  // Click Sign in
  const signInButton = page.locator('#idSIButton9, input[type="submit"]').first();
  await signInButton.click();
  console.log('[Teams][AUTH] Clicked Sign in');

  await page.waitForTimeout(1500);

  // Handle interrupts (KMSI, account picker, consent, etc.)
  await handleInterrupts(page, email);

  // Wait for ESTSAUTH or ESTSAUTHPERSISTENT cookie
  console.log('[Teams][AUTH] Waiting for ESTSAUTH cookie...');
  let cookieFound = false;
  for (let i = 0; i < 45; i++) {
    const cookies = await page.context().cookies();
    const hasAuth = cookies.some(c =>
      c.name === 'ESTSAUTH' || c.name === 'ESTSAUTHPERSISTENT'
    );
    if (hasAuth) {
      cookieFound = true;
      break;
    }
    await page.waitForTimeout(1000);
  }

  if (!cookieFound) {
    console.log('[Teams][AUTH] WARNING: ESTSAUTH cookie not found after 45s');
  } else {
    console.log('[Teams][AUTH] ESTSAUTH cookie confirmed');
  }
}

/**
 * Handle Microsoft sign-in interrupts: KMSI, SAOTCC, account picker, consent.
 * Loops up to 3 times since dismissing one can reveal another.
 */
async function handleInterrupts(page: Page, email: string): Promise<void> {
  for (let attempt = 1; attempt <= 3; attempt++) {
    console.log(`[Teams][AUTH] Checking for interrupts (attempt ${attempt}/3)...`);
    let handled = false;

    // a. KMSI "Stay signed in?" — detect by checkbox OR body text, NOT by #idSIButton9 alone
    try {
      const kmsiCheckbox = page.locator('#KmsiCheckboxField');
      const bodyText = await page.evaluate(() => document.body?.innerText || '');
      const onKmsi =
        (await kmsiCheckbox.isVisible({ timeout: 3000 }).catch(() => false)) ||
        /stay signed in/i.test(bodyText);

      if (onKmsi) {
        console.log('[Teams][AUTH] KMSI page detected');
        const yesButton = page.locator('#idSIButton9').first();
        await yesButton.click({ timeout: 4000 }).catch(err => {
          console.log('[Teams][AUTH] Could not click KMSI Yes button (non-fatal):', err);
        });
        console.log('[Teams][AUTH] Handled KMSI prompt');
        handled = true;
        await page.waitForTimeout(1500);
      }
    } catch (err) {
      console.log('[Teams][AUTH] KMSI handler check failed (non-fatal):', err);
    }

    // b. "Don't lose access to your account" (security info update prompt)
    if (!handled) {
      try {
        const saotccTitle = page.locator('#idDiv_SAOTCC_Title');
        const bodyText = await page.evaluate(() => document.body?.innerText || '');
        const onSaotcc =
          (await saotccTitle.isVisible({ timeout: 2000 }).catch(() => false)) ||
          /don't lose access/i.test(bodyText);

        if (onSaotcc) {
          console.log('[Teams][AUTH] "Don\'t lose access" prompt detected');
          const dismissSelectors = [
            '#idBtn_Back',
            'a:has-text("Not now")',
            'button:has-text("Not now")',
            'a:has-text("Skip")',
            'button:has-text("Skip")',
          ];
          for (const selector of dismissSelectors) {
            const el = page.locator(selector).first();
            if (await el.isVisible({ timeout: 2000 }).catch(() => false)) {
              await el.click();
              break;
            }
          }
          console.log('[Teams][AUTH] Handled "Don\'t lose access" prompt');
          handled = true;
          await page.waitForTimeout(1500);
        }
      } catch (err) {
        console.log('[Teams][AUTH] SAOTCC handler check failed (non-fatal):', err);
      }
    }

    // c. Account picker
    if (!handled) {
      try {
        const tile = page.locator('[data-test-id="tile"]').first();
        if (await tile.isVisible({ timeout: 2000 }).catch(() => false)) {
          console.log('[Teams][AUTH] Account picker detected');
          const matchingTile = page.locator('[data-test-id="tile"]').filter({ hasText: email }).first();
          if (await matchingTile.isVisible({ timeout: 2000 }).catch(() => false)) {
            await matchingTile.click();
          } else {
            console.log('[Teams][AUTH] No tile matched email — clicking first tile');
            await tile.click();
          }
          console.log('[Teams][AUTH] Handled account picker');
          handled = true;
          await page.waitForTimeout(1500);
        }
      } catch (err) {
        console.log('[Teams][AUTH] Account picker handler check failed (non-fatal):', err);
      }
    }

    // d. Consent page
    if (!handled) {
      try {
        const acceptButton = page.locator('button[value="Accept"]').first();
        if (await acceptButton.isVisible({ timeout: 2000 }).catch(() => false)) {
          await acceptButton.click();
          console.log('[Teams][AUTH] Handled consent screen');
          handled = true;
          await page.waitForTimeout(1500);
        }
      } catch (err) {
        console.log('[Teams][AUTH] Consent handler check failed (non-fatal):', err);
      }
    }

    if (!handled) {
      // Nothing recognized — stop looping
      break;
    }
  }
}

/**
 * Warm the Teams app session by navigating to teams.microsoft.com and polling
 * until authtoken/ringFinder cookies appear (or timeout).
 * Handles net::ERR_ABORTED retries and any KMSI/account-picker interrupts.
 *
 * @returns true if session is warmed (cookies present), false on timeout
 */
export async function warmTeamsSession(
  page: Page,
  timeoutMs: number = 30000
): Promise<boolean> {
  console.log(`[Teams][AUTH] Warming Teams session (timeout: ${timeoutMs}ms)...`);

  // Navigate to Teams home with retry on ERR_ABORTED
  let navigated = false;
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      await page.goto('https://teams.microsoft.com/', {
        waitUntil: 'domcontentloaded',
        timeout: 20000
      });
      navigated = true;
      break;
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (message.includes('net::ERR_ABORTED') && attempt < 3) {
        console.log(`[Teams][AUTH] Navigation ERR_ABORTED, retry ${attempt + 1}/3...`);
        await page.waitForTimeout(2000);
      } else {
        throw err;
      }
    }
  }

  if (!navigated) {
    console.log('[Teams][AUTH] Failed to navigate to Teams after 3 attempts');
    return false;
  }

  console.log('[Teams][AUTH] On Teams URL, polling for authtoken/ringFinder...');

  const startTime = Date.now();
  while (Date.now() - startTime < timeoutMs) {
    // Check current URL and cookies
    const url = page.url();
    const onTeamsHost = url.includes('teams.microsoft.com') || url.includes('cloud.microsoft');
    const onAuthEndpoint = /authv2|login\.microsoftonline|login\.live/i.test(url);

    const cookies = await page.context().cookies();
    const hasAuthToken = cookies.some(c =>
      (c.name === 'authtoken' || c.name === 'ringFinder') &&
      (c.domain.includes('teams.microsoft.com') || c.domain.includes('cloud.microsoft'))
    );

    if (onTeamsHost && !onAuthEndpoint && hasAuthToken) {
      console.log('[Teams][AUTH] Teams session warmed (authtoken/ringFinder present)');
      await dumpAuthState(page.context(), page, 'after-warm-success');
      return true;
    }

    // Handle any interrupts that appear during warming
    try {
      await handleInterrupts(page, '');
    } catch {
      // non-fatal
    }

    await page.waitForTimeout(500);
  }

  console.log('[Teams][AUTH] Teams session warm timeout — cookies not found');
  await dumpAuthState(page.context(), page, 'after-warm-timeout');
  return false;
}

/**
 * Open a new page, warm the session, then close it.
 * Used to refresh a stale session when we land on signed_out pre-join.
 *
 * @returns true if session was refreshed successfully, false otherwise
 */
export async function refreshTeamsSession(context: BrowserContext): Promise<boolean> {
  console.log('[Teams][AUTH] Refreshing Teams session in new page...');
  const tempPage = await context.newPage();
  try {
    const result = await warmTeamsSession(tempPage, 20000);
    return result;
  } finally {
    await tempPage.close().catch(() => {});
  }
}
