/**
 * Microsoft Teams specific join logic
 * 
 * Teams v2 (New Teams) — Classic Teams (/_#/ URLs) was retired July 1, 2025.
 * 
 * The correct approach for new Teams (confirmed working via ScreenApp's
 * open-source meeting bot, production 2026):
 * 
 * 1. Navigate directly to the original meeting URL (no URL rewriting)
 *    - /meet/ID?p=TOKEN (short format)
 *    - /l/meetup-join/... (long format)
 * 
 * 2. Teams shows a launcher page. Click "Continue on this browser" with force:true
 *    - Use multiple button selectors (aria-label variations)
 *    - The button DOES work when clicked with force:true in headed Playwright
 * 
 * 3. Wait for the pre-join screen (up to 120s)
 *    - Look for data-tid="prejoin-display-name-input" (name field)
 *    - This is the reliable indicator we're past the launcher
 * 
 * 4. Fill name, toggle devices, click "Join now"
 * 
 * Key requirements:
 *   - Headed mode (headless: false) — Teams requires it
 *   - StealthPlugin or anti-detection measures  
 *   - Linux X11 user agent (Chrome/135 on X11; Linux x86_64)
 *   - --kiosk --start-maximized flags for Teams
 *   - --use-fake-ui-for-media-stream --use-fake-device-for-media-stream
 *   - force:true on launcher button click
 *   - Long timeout (120s) for pre-join screen to load
 * 
 * Reference: https://github.com/screenappai/meeting-bot (MIT, production)
 */

import { Page, Locator } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import { S3Service } from '../transcribe/s3.js';

export class LobbyTimeoutError extends Error {
  constructor() {
    super('Bot was not admitted to the Teams meeting lobby within 3 minutes');
    this.name = 'LobbyTimeoutError';
  }
}

const SCREENSHOTS_DIR = process.env.RECORDINGS_DIR || '/app/recordings';

export class TeamsHandler {

  /**
   * Save a debug screenshot with sequential numbering and upload to S3
   */
  private static async screenshot(
    page: Page,
    label: string,
    s3?: S3Service | null,
    meetingId?: string | null
  ): Promise<void> {
    try {
      const filename = `debug-${label}-${Date.now()}.png`;
      const filepath = path.join(SCREENSHOTS_DIR, filename);
      await page.screenshot({ path: filepath, fullPage: true });
      console.log(`[Teams] Screenshot saved: ${filename}`);

      // Always upload to S3 when service is available
      if (s3 && meetingId) {
        try {
          const key = `debug/screenshots/${meetingId}/${filename}`;
          await s3.uploadWithKey(filepath, key);
          console.log(`[Teams] Screenshot uploaded to S3: ${key}`);
        } catch (uploadErr) {
          console.log(`[Teams] WARNING: S3 screenshot upload failed (${label}):`, uploadErr);
        }
      }
    } catch (e) {
      console.log(`[Teams] Screenshot failed: ${e}`);
    }
  }

  /**
   * Run a page.evaluate() call with a single retry if the execution context
   * gets destroyed mid-evaluate — the Teams v2 SPA can still be hash-routing
   * when we try to read the DOM, which kills the in-flight context. On that
   * specific error, re-wait for the page to settle and try once more.
   */
  private static async evaluateWithNavRetry<T>(page: Page, fn: () => T): Promise<T> {
    try {
      return await page.evaluate(fn);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (!message.includes('Execution context was destroyed')) {
        throw err;
      }
      console.log('[Teams] page.evaluate() hit a navigation race, re-waiting and retrying once...');
      try {
        await page.waitForLoadState('domcontentloaded', { timeout: 15000 });
      } catch (waitErr) {
        console.log('[Teams] WARNING: waitForLoadState(domcontentloaded) timed out on retry, continuing anyway:', waitErr);
      }
      await page.waitForTimeout(1000);
      return await page.evaluate(fn);
    }
  }

  /**
   * Process a Teams meeting URL (pass-through with validation).
   */
  static async processTeamsMeetingUrl(meetingUrl: string): Promise<string> {
    console.log('[Teams] Processing meeting URL:', meetingUrl);
    try {
      new URL(meetingUrl); // validate
      console.log('[Teams] Processed URL (pass-through):', meetingUrl);
      return meetingUrl;
    } catch (error) {
      console.log('[Teams] URL processing failed, using original:', error);
      return meetingUrl;
    }
  }

  /**
   * Click through the Teams launcher page.
   * 
   * The launcher page shows "Join your Teams meeting" with options to open
   * the desktop app or continue in the browser. We need to click the browser
   * option. The button text/aria-label varies by Teams version.
   * 
   * Key: use force:true to bypass any overlay/interception issues.
   * Returns true if a button was clicked, false if no button found.
   */
  private static async clickLauncherButton(page: Page): Promise<boolean> {
    console.log('[Teams] Looking for launcher "Continue on this browser" button...');

    const launcherButtonSelectors = [
      'button[aria-label="Join meeting from this browser"]',
      'button[aria-label="Continue on this browser"]',
      'button[aria-label="Join on this browser"]',
      'a[aria-label="Join meeting from this browser"]',
      'a[aria-label="Continue on this browser"]',
      'a[aria-label="Join on this browser"]',
      'button:has-text("Continue on this browser")',
      'button:has-text("Join from browser")',
      'button:has-text("Join on the web")',
      'a:has-text("Continue on this browser")',
      'a:has-text("Join on the web instead")',
    ];

    for (const selector of launcherButtonSelectors) {
      try {
        const element = page.locator(selector).first();
        // Short timeout — if the button exists, it should be visible quickly
        if (await element.isVisible({ timeout: 3000 })) {
          console.log(`[Teams] Found launcher button: ${selector}`);
          // force:true is critical — without it, Playwright may not trigger
          // the click handler due to overlay/interception issues
          await element.click({ force: true });
          console.log('[Teams] Clicked launcher button with force:true');
          return true;
        }
      } catch {
        continue;
      }
    }

    // Fallback: try to find ANY clickable element with matching text
    try {
      const fallbackTexts = ['Continue on this browser', 'Join on the web', 'Join from browser'];
      for (const text of fallbackTexts) {
        try {
          const el = page.getByText(text, { exact: false }).first();
          if (await el.isVisible({ timeout: 2000 })) {
            await el.click({ force: true });
            console.log(`[Teams] Clicked fallback text element: "${text}"`);
            return true;
          }
        } catch {
          continue;
        }
      }
    } catch {
      // ignore
    }

    console.log('[Teams] No launcher button found');
    return false;
  }

  /**
   * Wait for the pre-join screen to appear.
   * 
   * The pre-join screen is where you enter your name and toggle devices.
   * The most reliable indicator is the name input field with
   * data-tid="prejoin-display-name-input".
   * 
   * Uses a long timeout (120s) because Teams can be slow to load,
   * especially for anonymous/guest joins.
   */
  private static async waitForPreJoinScreen(page: Page, timeoutMs: number = 120000): Promise<boolean> {
    console.log(`[Teams] Waiting for pre-join screen (timeout: ${timeoutMs / 1000}s)...`);

    // Primary indicator: the name input field
    try {
      const nameInput = page.locator('input[data-tid="prejoin-display-name-input"]');
      await nameInput.waitFor({ state: 'visible', timeout: timeoutMs });
      console.log('[Teams] Pre-join screen detected (found name input field)');
      return true;
    } catch {
      console.log('[Teams] Name input not found within timeout');
    }

    // Secondary indicators: check for other pre-join elements
    const secondaryIndicators = [
      'input[placeholder*="name" i]',
      'input[placeholder*="Enter your name" i]',
      'button:has-text("Join now")',
      '[data-tid="prejoin-join-button"]',
    ];

    for (const selector of secondaryIndicators) {
      try {
        const el = page.locator(selector).first();
        if (await el.isVisible({ timeout: 5000 })) {
          console.log(`[Teams] Pre-join screen detected via secondary indicator: ${selector}`);
          return true;
        }
      } catch {
        continue;
      }
    }

    // Also check for waiting room / lobby text (means we're past the launcher)
    try {
      const bodyText = await page.evaluate(() => document.body?.innerText || '');
      const lobbyPhrases = [
        'Someone will let you in shortly',
        'Waiting to be admitted',
        'waiting room',
      ];
      for (const phrase of lobbyPhrases) {
        if (bodyText.toLowerCase().includes(phrase.toLowerCase())) {
          console.log(`[Teams] Pre-join/lobby detected via text: "${phrase}"`);
          return true;
        }
      }
    } catch {
      // ignore
    }

    console.log('[Teams] Pre-join screen NOT detected');
    return false;
  }

  /**
   * Sign in with Microsoft 365 from the Teams pre-join screen (WI #7101,
   * corrected per WI #7125's live browser test).
   *
   * Contract: `page` must already be on the Teams light-meetings pre-join
   * screen (anonymous state) when this is called — NOT
   * login.microsoftonline.com. Teams web auth starts from a "Sign in" link
   * on the pre-join screen itself; navigating to Microsoft's login domain
   * directly (the old approach) does not carry the meeting context and
   * doesn't reflect how the Teams web client actually authenticates.
   *
   * Flow (confirmed via live test 2026-09-16): click "Sign in"
   * (button[data-tid="auth-sign-in-link"]) -> in-page Fluent UI email
   * dialog -> enter email -> Next -> the entire page navigates to
   * login.microsoftonline.com (not an iframe or popup) -> enter password ->
   * Sign in -> KMSI ("Stay signed in?") -> Microsoft redirects back to
   * teams.microsoft.com/v2, which shows the pre-join screen reloaded
   * authenticated (name input gone, Join button + account identity present).
   *
   * Never throws — sign-in problems (MFA challenge, conditional access,
   * changed page layout, etc.) are logged and reported as `false` so the
   * caller can fall back to the anonymous join path rather than losing the
   * meeting entirely.
   *
   * @param s3         S3Service instance for uploading auth debug screenshots.
   *                   Pass null to skip screenshot uploads (unit tests).
   * @param meetingId  meeting id string for namespacing S3 screenshot keys.
   */
  static async signInWithM365(
    page: Page,
    email: string,
    password: string,
    s3: S3Service | null,
    meetingId: string
  ): Promise<boolean> {
    console.log('[Teams] Attempting M365 sign-in from pre-join screen...');

    // URL navigation logging
    const navLog: string[] = [];
    page.on('framenavigated', frame => {
      if (frame === page.mainFrame()) {
        const url = page.url();
        console.log(`[Teams][AUTH-NAV] URL changed: ${url}`);
        navLog.push(url);
      }
    });

    try {
      // Step 1: click the "Sign in" button on the pre-join screen. Confirmed
      // via live test 2026-09-16: a plain Playwright .click() intermittently
      // fails to trigger the Fluent UI dialog on this button, so we invoke
      // the click through page.evaluate() instead.
      console.log(`[Teams][AUTH] Step: before-signin-click | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-00-before-signin-click', s3, meetingId);

      const signInBtnExists = await page.evaluate(() => {
        return !!document.querySelector('[data-tid="auth-sign-in-link"]');
      });
      console.log(`[Teams][AUTH] Sign in button found: ${signInBtnExists} | URL: ${page.url()}`);

      await page.evaluate(() => {
        const btn = document.querySelector('[data-tid="auth-sign-in-link"]') as HTMLElement | null;
        if (btn) btn.click();
      });
      console.log('[Teams] Clicked Sign in button via evaluate');

      console.log(`[Teams][AUTH] Step: after-signin-click | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-01-after-signin-click', s3, meetingId);

      try {
        await page.waitForSelector('input[data-testid="emailInput"]', { state: 'visible', timeout: 10000 });
      } catch {
        console.log('[Teams] No "Sign in" dialog appeared on pre-join screen — cannot authenticate');
        console.log(`[Teams][AUTH] Step: no-signin-dialog | URL: ${page.url()}`);
        await this.screenshot(page, 'auth-no-signin-dialog', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      // Step 2: in-page Fluent UI email dialog. The email input has
      // data-testid="emailInput" and placeholder="Enter your email" — NOT
      // type="email" (confirmed via live test 2026-09-16).
      console.log(`[Teams][AUTH] Step: before-email | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-02-before-email', s3, meetingId);

      const emailInput = page.locator('input[data-testid="emailInput"], input[placeholder="Enter your email"]').first();
      try {
        await emailInput.waitFor({ state: 'visible', timeout: 10000 });
        const isVisible = await emailInput.isVisible();
        console.log(`[Teams][AUTH] Email input found: input[data-testid="emailInput"] | visible: ${isVisible} | URL: ${page.url()}`);
      } catch (err) {
        console.log(`[Teams][AUTH] Email input not found | URL: ${page.url()} | Error: ${err}`);
        await this.screenshot(page, 'auth-email-input-not-found', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      await emailInput.fill(email);
      console.log(`[Teams][AUTH] Step: after-email | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-03-after-email', s3, meetingId);

      console.log(`[Teams][AUTH] Step: before-email-submit | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-04-before-email-submit', s3, meetingId);
      await this.clickTeamsAuthNext(page);

      console.log(`[Teams][AUTH] Step: after-email-submit | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-05-after-email-submit', s3, meetingId);

      // Step 3: after Next, the entire page navigates to
      // login.microsoftonline.com — confirmed via live test 2026-09-16 to be
      // a full top-level navigation, not an iframe or popup as previously
      // assumed (WI #7101).
      console.log('[Teams] Email submitted — waiting for Microsoft login page...');
      try {
        await page.waitForURL('**/login.microsoftonline.com/**', { timeout: 30000 });
      } catch (err) {
        console.log(`[Teams] Did not navigate to login.microsoftonline.com — falling back to anonymous join | URL: ${page.url()} | Error: ${err}`);
        await this.screenshot(page, 'auth-no-msft-navigation', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }
      console.log('[Teams] On Microsoft login page:', page.url());
      console.log(`[Teams][AUTH] Step: after-msft-navigation | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-06-on-msft-login', s3, meetingId);

      // Step 4: password on login.microsoftonline.com
      console.log(`[Teams][AUTH] Step: before-password | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-07-before-password', s3, meetingId);

      const passwordInput = page.locator('input[type="password"], input[name="passwd"]').first();
      try {
        await passwordInput.waitFor({ state: 'visible', timeout: 20000 });
        const isVisible = await passwordInput.isVisible();
        console.log(`[Teams][AUTH] Password input found: input[type="password"] | visible: ${isVisible} | URL: ${page.url()}`);
      } catch (err) {
        console.log(`[Teams][AUTH] Password input not found | URL: ${page.url()} | Error: ${err}`);
        await this.screenshot(page, 'auth-password-input-not-found', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      await passwordInput.fill(password);
      console.log(`[Teams][AUTH] Step: after-password | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-08-after-password', s3, meetingId);

      console.log(`[Teams][AUTH] Step: before-password-submit | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-09-before-password-submit', s3, meetingId);
      await this.clickM365Button(page);

      console.log(`[Teams][AUTH] Step: after-password-submit | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-10-after-password-submit', s3, meetingId);

      // Step 5: KMSI and other known Microsoft interrupt pages on the
      // top-level page.
      console.log(`[Teams][AUTH] Step: before-interrupts | URL: ${page.url()}`);
      await this.handleM365Interrupts(page, email, s3, meetingId);
      console.log(`[Teams][AUTH] Step: after-interrupts | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-13-after-kmsi', s3, meetingId);

      // Step 6: Microsoft redirects to teams.microsoft.com/v2/authv2, which
      // Teams forwards internally to /v2/ — confirm we're back on Teams.
      await page.waitForFunction(
        () => window.location.hostname === 'teams.microsoft.com' && window.location.pathname.startsWith('/v2'),
        { timeout: 30000 }
      ).catch((err) => {
        console.log(`[Teams] WARNING: Did not redirect back to teams.microsoft.com/v2 after sign-in | URL: ${page.url()} | Error: ${err}`);
      });

      if (!page.url().includes('teams.microsoft.com')) {
        console.log(`[Teams][AUTH] Step: not-on-teams | URL: ${page.url()}`);
        await this.screenshot(page, 'auth-at-warning', s3, meetingId);
        console.log('[Teams] M365 sign-in did not complete — still on:', page.url());
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      // After KMSI handling and back on Teams URL, wait for auth to fully complete in SPA
      // The Sign in link disappearing is the reliable indicator that auth is recognized
      try {
        await page.waitForFunction(
          () => {
            const signInLink = document.querySelector('[data-tid="auth-sign-in-link"]');
            return !signInLink || (signInLink as HTMLElement).offsetParent === null;
          },
          { timeout: 15000 }
        );
        console.log('[Teams] Authenticated UI state confirmed (sign-in link gone)');
      } catch {
        console.log('[Teams] WARNING: Timed out waiting for authenticated UI state — proceeding anyway');
        // Non-fatal: if Teams doesn't transition, the join attempt will fail naturally
      }

      console.log('[Teams] M365 sign-in complete, back on Teams:', page.url());
      console.log(`[Teams][AUTH] Step: final-state | URL: ${page.url()}`);
      await this.screenshot(page, 'auth-14-final-state', s3, meetingId);
      console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
      return true;
    } catch (err) {
      console.log('[Teams] M365 sign-in failed, falling back to anonymous join:', err);
      console.log(`[Teams][AUTH] Error at URL: ${page.url()}`);
      await this.screenshot(page, 'auth-signin-failed', s3, meetingId);
      console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
      return false;
    }
  }

  /**
   * Click the "Next" button on the in-page Teams email entry modal (the
   * Teams-styled overlay shown after clicking "Sign in" on the pre-join
   * screen — distinct from the Microsoft-branded password step).
   */
  private static async clickTeamsAuthNext(page: Page): Promise<void> {
    const selectors = [
      '[role="dialog"] button[type="submit"]',
      '[role="dialog"] button:has-text("Next")',
      '[role="dialog"] button:has-text("Sign in")',
      'button:has-text("Next")',
      'input[type="submit"]',
    ];
    for (const selector of selectors) {
      try {
        const btn = page.locator(selector).first();
        if (await btn.isVisible({ timeout: 5000 })) {
          await btn.click();
          return;
        }
      } catch {
        continue;
      }
    }
    console.log('[Teams] WARNING: could not find Next button on Teams email entry modal');
  }


  /**
   * Handle known Microsoft sign-in interrupt pages that can appear after the
   * password step (KMSI, security info nag, account picker, consent) on
   * login.microsoftonline.com (WI #7101, corrected per WI #7125 — this is
   * always the top-level page, confirmed via live test 2026-09-16 to be a
   * full navigation rather than an iframe or popup). Loops up to 3 times
   * since dismissing one interrupt (e.g. KMSI) can reveal another.
   *
   * Root cause of meeting 113 (2026-09-15) that motivated the original
   * multi-interrupt handling: the old code only checked for the KMSI prompt.
   * When Microsoft showed "Don't lose access to your account" instead, the
   * KMSI check found nothing (fast no-op), and the bot burned the entire 30s
   * waitForFunction budget stuck on that page before falling back to an
   * anonymous join the tenant then rejected.
   *
   * Never throws. Best-effort — once no recognized interrupt shows up, the
   * caller's own wait for the authenticated pre-join screen is the final
   * authoritative check that sign-in actually completed.
   */
  private static async handleM365Interrupts(page: Page, email: string, s3: S3Service | null, meetingId: string): Promise<void> {
    const locate = (selector: string) => page.locator(selector);

    for (let attempt = 1; attempt <= 3; attempt++) {
      console.log(`[Teams] Checking for M365 interrupt pages (attempt ${attempt}/3)...`);
      console.log(`[Teams][AUTH] Step: interrupt-attempt-${attempt} | URL: ${page.url()}`);
      let handled = false;

      // a. KMSI "Stay signed in?"
      try {
        const staySignedIn = locate('#idSIButton9');
        const kmsiCheckbox = locate('#KmsiCheckboxField');
        const onKmsi =
          (await staySignedIn.isVisible({ timeout: 4000 }).catch(() => false)) ||
          (await kmsiCheckbox.isVisible({ timeout: 2000 }).catch(() => false));
        if (onKmsi) {
          console.log(`[Teams][AUTH] KMSI page detected | URL: ${page.url()}`);
          await this.screenshot(page, 'auth-12-kmsi-page', s3, meetingId);
          await staySignedIn.click({ timeout: 4000 }).catch((err) => {
            console.log('[Teams] Could not click KMSI Yes button (non-fatal):', err);
          });
          console.log('[Teams] Handled KMSI prompt');
          handled = true;
        }
      } catch (err) {
        console.log('[Teams] KMSI handler check failed (non-fatal):', err);
      }

      // b. "Don't lose access to your account" (security info update prompt)
      if (!handled) {
        try {
          const saotccTitle = locate('#idDiv_SAOTCC_Title');
          if (await saotccTitle.isVisible({ timeout: 3000 }).catch(() => false)) {
            const dismissSelectors = [
              '#idBtn_Back',
              'a:has-text("Not now")',
              'button:has-text("Not now")',
              'a:has-text("Skip")',
              'button:has-text("Skip")',
            ];
            for (const selector of dismissSelectors) {
              const el = locate(selector).first();
              if (await el.isVisible({ timeout: 3000 }).catch(() => false)) {
                await el.click();
                break;
              }
            }
            console.log('[Teams] Handled "Don\'t lose access" security prompt');
            handled = true;
          }
        } catch (err) {
          console.log('[Teams] "Don\'t lose access" handler check failed (non-fatal):', err);
        }
      }

      // c. Account picker ("Pick an account" / "You have multiple accounts")
      if (!handled) {
        try {
          const tile = locate('[data-test-id="tile"]').first();
          if (await tile.isVisible({ timeout: 3000 }).catch(() => false)) {
            const matchingTile = locate('[data-test-id="tile"]').filter({ hasText: email }).first();
            if (await matchingTile.isVisible({ timeout: 3000 }).catch(() => false)) {
              await matchingTile.click();
            } else {
              console.log('[Teams] No tile matched BOT_EMAIL — clicking first available tile');
              await tile.click();
            }
            console.log('[Teams] Handled account picker');
            handled = true;
          }
        } catch (err) {
          console.log('[Teams] Account picker handler check failed (non-fatal):', err);
        }
      }

      // d. Consent / permissions screen ("Permissions requested" / "Review permissions")
      if (!handled) {
        try {
          const acceptButton = locate('button[value="Accept"]').first();
          if (await acceptButton.isVisible({ timeout: 3000 }).catch(() => false)) {
            await acceptButton.click();
            console.log('[Teams] Handled consent/permissions screen');
            handled = true;
          }
        } catch (err) {
          console.log('[Teams] Consent handler check failed (non-fatal):', err);
        }
      }

      if (!handled) {
        // Nothing recognized this pass — the auth surface may already be
        // closing as Teams transitions back to the authenticated pre-join
        // screen. Stop looping; the caller's own wait is authoritative.
        await this.screenshot(page, `auth-11${String.fromCharCode(96 + attempt)}-interrupt-attempt-${attempt}`, s3, meetingId);
        break;
      }
      await this.screenshot(page, `auth-11${String.fromCharCode(96 + attempt)}-interrupt-attempt-${attempt}`, s3, meetingId);
      await new Promise((resolve) => setTimeout(resolve, 1500));
    }
  }

  /**
   * Click the Next/Sign in submit button on the Microsoft password step.
   * The same button id (#idSIButton9) is reused across the password and
   * KMSI steps of the flow.
   */
  private static async clickM365Button(page: Page): Promise<void> {
    const selectors = ['#idSIButton9', 'input[type="submit"]', 'button[type="submit"]'];
    for (const selector of selectors) {
      try {
        const btn = page.locator(selector).first();
        if (await btn.isVisible({ timeout: 5000 })) {
          await btn.click();
          return;
        }
      } catch {
        continue;
      }
    }
    console.log('[Teams] WARNING: could not find a Next/Sign in button on the Microsoft password step');
  }

  /**
   * Join a Teams meeting.
   *
   * Flow (based on ScreenApp's production implementation):
   * 1. Navigate to meeting URL (original URL, no /_#/ rewriting)
   * 2. Wait for launcher page, click "Continue on this browser" (force:true)
   * 3. Wait for pre-join screen (name input field, up to 120s)
   * 4. Fill in bot name — or, if BOT_EMAIL/BOT_PASSWORD are configured (WI
   *    #7101), sign in with M365 instead, right at the point the name input
   *    is confirmed visible; falls back to filling the name on any sign-in
   *    failure. A signed-in account uses its M365 profile name and has no
   *    display-name field to fill.
   * 5. Turn off camera/mic
   * 6. Click "Join now"
   * 7. Wait for meeting entry (look for "Leave" button)
   * 8. Handle waiting room if needed
   */
  static async join(page: Page, botName: string, originalUrl?: string): Promise<void> {
    console.log('[Teams] Starting join flow...');
    console.log('[Teams] Current URL:', page.url());

    // Instantiate S3Service early for all debug screenshots
    const meetingId = process.env.MEETING_ID || '0';
    const s3 = new S3Service(
      process.env.AWS_REGION || 'us-east-1',
      process.env.S3_BUCKET || 'firm-recordings-dev'
    );

    await this.screenshot(page, '01-initial-page', s3, meetingId);

    // Step 1: Handle the launcher page
    // Wait for page to stabilize
    await page.waitForTimeout(3000);

    const pageText = await page.evaluate(() => document.body?.innerText?.substring(0, 1000) || 'NO BODY TEXT');
    console.log('[Teams] Initial page text:', pageText.substring(0, 200));

    // Check if we're on the launcher page
    const isLauncherPage = pageText.includes('Join your Teams meeting') || 
                           pageText.includes('Continue on this browser') ||
                           pageText.includes('Join on the web') ||
                           page.url().includes('/dl/launcher/') ||
                           page.url().includes('launcher.html');

    if (isLauncherPage) {
      console.log('[Teams] On launcher page, clicking through...');
      const clicked = await this.clickLauncherButton(page);
      
      if (clicked) {
        console.log('[Teams] Launcher button clicked, waiting for navigation...');
        // Wait for navigation or page change after clicking
        await page.waitForTimeout(5000);
      } else {
        console.log('[Teams] WARNING: Could not find launcher button');
        // Log page state for debugging
        const html = await page.evaluate(() => document.body?.innerHTML?.substring(0, 2000) || '');
        console.log('[Teams] Page HTML snippet:', html.substring(0, 500));
      }

      await this.screenshot(page, '01b-after-launcher-click', s3, meetingId);
    }

    // Step 2: Wait for pre-join screen
    const preJoinReached = await this.waitForPreJoinScreen(page, 120000);

    if (!preJoinReached) {
      console.log('[Teams] WARNING: Pre-join screen not reached after 120s');
      console.log('[Teams] Current URL:', page.url());
      const currentText = await page.evaluate(() => document.body?.innerText?.substring(0, 500) || '');
      console.log('[Teams] Current page text:', currentText);
      await this.screenshot(page, '01c-pre-join-not-reached', s3, meetingId);

      // If we're still on the launcher, try one more time with a fresh navigation
      if (page.url().includes('/dl/launcher/') || page.url().includes('launcher.html')) {
        console.log('[Teams] Still on launcher — trying fresh navigation with original URL...');
        if (originalUrl) {
          await page.goto(originalUrl, { waitUntil: 'networkidle', timeout: 30000 });
          await page.waitForTimeout(3000);
          await this.clickLauncherButton(page);
          await page.waitForTimeout(5000);
          // Try waiting for pre-join one more time
          const retryResult = await this.waitForPreJoinScreen(page, 60000);
          if (!retryResult) {
            console.log('[Teams] WARNING: Still cannot reach pre-join screen after retry');
            await this.screenshot(page, '01d-retry-failed', s3, meetingId);
          }
        }
      }
    }

    await this.screenshot(page, '02-pre-join-screen', s3, meetingId);

    // Step 3: Enter name in the name field — or, if BOT_EMAIL/BOT_PASSWORD are
    // configured (WI #7101), sign in with M365 instead. Each environment's ECS
    // task definition injects the correct values; absent either one, or if
    // sign-in fails for any reason, fall back to the anonymous join path.
    // The sign-in attempt happens exactly where the anonymous flow would call
    // nameInput.fill() — once the name input is confirmed visible, i.e. once
    // we know we're truly on the pre-join screen.
    const botEmail = process.env.BOT_EMAIL;
    const botPassword = process.env.BOT_PASSWORD;

    const nameSelectors = [
      'input[data-tid="prejoin-display-name-input"]',
      'input[placeholder*="Enter your name" i]',
      'input[placeholder*="Type your name" i]',
      'input[placeholder*="name" i]',
      'input[aria-label*="name" i]',
      '#username',
      'input[type="text"]',
    ];

    let enteredName = false;
    let authenticated = false;
    for (const selector of nameSelectors) {
      try {
        const nameInput = page.locator(selector).first();
        if (await nameInput.isVisible({ timeout: 3000 })) {
          if (botEmail && botPassword) {
            authenticated = await this.signInWithM365(page, botEmail, botPassword, s3, meetingId);
            if (authenticated) {
              console.log('[Teams] M365 sign-in succeeded — page is already on authenticated pre-join screen');
              console.log('[Teams] Post-auth URL:', page.url());
              await page.waitForTimeout(1000); // Give Teams time to fully render after auth
              await this.screenshot(page, '02b-post-auth-prejoin', s3, meetingId);
              enteredName = true;
              break;
            }
            console.log('[Teams] M365 sign-in failed — falling back to anonymous name entry');
          }
          await nameInput.clear();
          await nameInput.fill(botName);
          console.log(`[Teams] Entered name "${botName}" via: ${selector}`);
          enteredName = true;
          break;
        }
      } catch {
        continue;
      }
    }

    if (!enteredName) {
      console.log('[Teams] WARNING: Could not find name input field');
      const inputs = await page.evaluate(() => {
        return Array.from(document.querySelectorAll('input')).map(i => ({
          type: i.type,
          placeholder: i.placeholder,
          ariaLabel: i.getAttribute('aria-label'),
          id: i.id,
          dataTid: i.getAttribute('data-tid'),
          visible: i.offsetParent !== null,
        }));
      });
      console.log('[Teams] All inputs on page:', JSON.stringify(inputs));
    }

    // Step 4: Turn off camera and microphone
    await this.turnOffDevices(page);

    await page.waitForTimeout(1000);
    await this.screenshot(page, '03-before-join-click', s3, meetingId);

    // Step 5: Click Join now button
    const joinButtonTexts = ['Join now', 'Join', 'Ask to join', 'Join meeting'];

    let clickedJoin = false;
    
    // First try data-tid selector (most reliable)
    try {
      const tidButton = page.locator('[data-tid="prejoin-join-button"]').first();
      if (await tidButton.isVisible({ timeout: 3000 })) {
        await tidButton.click();
        console.log('[Teams] Clicked join via data-tid="prejoin-join-button"');
        clickedJoin = true;
      }
    } catch {
      // continue to text-based selectors
    }

    if (!clickedJoin) {
      for (const text of joinButtonTexts) {
        try {
          const button = page.getByRole('button', { name: new RegExp(text, 'i') });
          if (await button.isVisible({ timeout: 3000 })) {
            const buttonText = await button.textContent();
            // Skip buttons that would open the desktop app
            if (buttonText && (buttonText.includes('Teams app') || buttonText.includes('Download'))) {
              continue;
            }
            await button.click();
            console.log(`[Teams] Clicked join button: "${text}" (actual text: "${buttonText}")`);
            clickedJoin = true;
            break;
          }
        } catch {
          continue;
        }
      }
    }
    
    if (!clickedJoin) {
      console.log('[Teams] WARNING: Could not find join button');
      const buttons = await page.evaluate(() => {
        return Array.from(document.querySelectorAll('button')).map(b => ({
          text: b.textContent?.trim()?.substring(0, 50),
          ariaLabel: b.getAttribute('aria-label'),
          dataTid: b.getAttribute('data-tid'),
          visible: b.offsetParent !== null,
        }));
      });
      console.log('[Teams] All buttons on page:', JSON.stringify(buttons));
      await this.screenshot(page, '03b-no-join-button', s3, meetingId);
    }

    // Step 6: Wait for meeting to load
    console.log('[Teams] Waiting for meeting to load...');

    // Look for the Leave button as confirmation we're in the meeting
    try {
      const leaveButton = page.getByRole('button', { name: /Leave/i });
      await leaveButton.waitFor({ timeout: 60000 });
      console.log('[Teams] ✅ Successfully joined meeting (Leave button visible)');
      await this.screenshot(page, '04-in-meeting', s3, meetingId);
      await this.postAdmissionChatNotification(page);
      return;
    } catch {
      console.log('[Teams] Leave button not found within 60s, checking other states...');
    }

    await this.screenshot(page, '04-after-join-attempt', s3, meetingId);

    // Step 7: Check if we're in a waiting room
    const bodyText = await page.evaluate(() => document.body?.innerText || '');
    const waitingRoomPhrases = [
      'waiting to be admitted',
      'someone will let you in',
      'someone will admit you',
      'lobby',
      'waiting for the host',
      'the host will let you in',
      'waiting in the lobby',
    ];
    const inWaitingRoom = waitingRoomPhrases.some(t => bodyText.toLowerCase().includes(t.toLowerCase()));
    
    if (inWaitingRoom) {
      console.log('[Teams] In waiting room / lobby, waiting to be admitted (max 3 min)...');
      await this.screenshot(page, '04b-waiting-room', s3, meetingId);
      let admitted = false;
      // Poll 18×10s = 3 minutes
      for (let i = 0; i < 18; i++) {
        await page.waitForTimeout(10000);

        // Check for Leave button (means we were admitted)
        try {
          const leaveButton = page.getByRole('button', { name: /Leave/i });
          if (await leaveButton.isVisible({ timeout: 1000 })) {
            console.log('[Teams] ✅ Admitted from waiting room, now in meeting');
            await this.screenshot(page, '05-admitted-in-meeting', s3, meetingId);
            admitted = true;
            await this.postAdmissionChatNotification(page);
            return; // admitted — normal path
          }
        } catch {
          // not admitted yet
        }

        // Check if still in waiting room
        const currentText = await page.evaluate(() => document.body?.innerText?.toLowerCase() || '');
        const stillWaiting = waitingRoomPhrases.some(t => currentText.includes(t.toLowerCase()));
        if (!stillWaiting) {
          console.log('[Teams] No longer in waiting room — uncertain state');
          break;
        }
        console.log(`[Teams] Still in waiting room... (${(i + 1) * 10}s elapsed)`);
      }

      if (!admitted) {
        // Check one final time
        try {
          const leaveButton = page.getByRole('button', { name: /Leave/i });
          if (await leaveButton.isVisible({ timeout: 2000 })) {
            console.log('[Teams] ✅ Admitted just before timeout — now in meeting');
            await this.screenshot(page, '05-admitted-last-second', s3, meetingId);
            await this.postAdmissionChatNotification(page);
            return;
          }
        } catch {
          // not admitted
        }
        console.log('[Teams] ❌ Not admitted to lobby within 3 minutes — throwing LobbyTimeoutError');
        await this.screenshot(page, '05-lobby-timeout', s3, meetingId);
        throw new LobbyTimeoutError();
      }
    }

    // Final state check
    const joinedCheck = await page.evaluate(() => {
      const text = document.body?.innerText || '';
      const hasLeave = text.includes('Leave');
      const hasHangup = document.querySelector('[data-tid="hangup-button"]') !== null;
      const hasMeetingUI = document.querySelector('[data-tid="calling-screen"]') !== null;
      const hasRoster = document.querySelector('[data-tid="roster-button"]') !== null;
      // Check for error states
      const hasError = text.includes('error') || text.includes('Error') || text.includes('no longer available');
      const hasEOA = window.location.href.includes('/error/eoa');
      return { 
        hasLeave, hasHangup, hasMeetingUI, hasRoster, hasError, hasEOA,
        url: window.location.href, 
        textSnippet: text.substring(0, 300) 
      };
    });

    console.log('[Teams] Final join check:', JSON.stringify(joinedCheck));

    if (joinedCheck.hasEOA) {
      console.log('[Teams] ❌ ERROR: Hit Classic Teams EOA page! Treating as lobby timeout.');
      await this.screenshot(page, '05-eoa-error', s3, meetingId);
      throw new LobbyTimeoutError();
    } else if (joinedCheck.hasLeave || joinedCheck.hasHangup || joinedCheck.hasMeetingUI || joinedCheck.hasRoster) {
      console.log('[Teams] ✅ Successfully joined meeting');
      await this.screenshot(page, '05-in-meeting', s3, meetingId);
      await this.postAdmissionChatNotification(page);
    } else {
      console.log('[Teams] ⚠️ Meeting join status uncertain — hasMeetingUI=false, hasLeave=false. Treating as lobby timeout.');
      await this.screenshot(page, '05-uncertain-state', s3, meetingId);
      throw new LobbyTimeoutError();
    }
  }

  /**
   * Post a join notification to the meeting chat (WI #7034), identifying the
   * bot and attributing the recording to the FIRM user(s) who requested it.
   *
   * Only ever called after confirmed admission — never from the lobby.
   * `BOT_NAMES_CSV` is passed by firm-web at ECS task launch: a single name
   * for a single recorder, or a comma-separated list when multiple FIRM
   * users share the meeting.
   *
   * Non-fatal by design — a failed chat post must never fail the recording.
   */
  private static async postAdmissionChatNotification(page: Page): Promise<void> {
    try {
      const namesCsv = process.env.BOT_NAMES_CSV || '';
      const names = namesCsv.split(',').map(n => n.trim()).filter(Boolean);
      if (names.length === 0) {
        console.log('[Teams] BOT_NAMES_CSV not set — skipping chat notification');
        return;
      }

      const message =
        `Fortress Notetaker has joined to record this meeting on behalf of ${names.join(', ')}.\n` +
        `This session is being recorded. Participants who continue acknowledge they consent to recording.`;

      console.log('[Teams] Posting join notification to meeting chat...');

      // Step 1: open the chat panel
      const chatButtonSelectors = [
        '[data-tid="chat-button"]',
        'button[aria-label="Chat"]',
        'button[aria-label*="Show conversation" i]',
        'button[aria-label*="chat" i]',
      ];
      let openedChat = false;
      for (const selector of chatButtonSelectors) {
        try {
          const btn = page.locator(selector).first();
          if (await btn.isVisible({ timeout: 3000 })) {
            await btn.click();
            console.log(`[Teams] Opened chat panel via: ${selector}`);
            openedChat = true;
            break;
          }
        } catch {
          continue;
        }
      }
      if (!openedChat) {
        console.log('[Teams] WARNING: could not find chat panel toggle — skipping chat notification');
        return;
      }

      // Step 2: wait for the chat input to be ready
      const inputSelectors = [
        'div[aria-label="Type a message"]',
        'div[data-tid="ckeditor"]',
        '[contenteditable="true"][aria-label*="message" i]',
        '[contenteditable="true"][role="textbox"]',
      ];
      let chatInput: Locator | null = null;
      for (const selector of inputSelectors) {
        try {
          const el = page.locator(selector).first();
          if (await el.isVisible({ timeout: 5000 })) {
            chatInput = el;
            console.log(`[Teams] Found chat input via: ${selector}`);
            break;
          }
        } catch {
          continue;
        }
      }
      if (!chatInput) {
        console.log('[Teams] WARNING: could not find chat input field — skipping chat notification');
        return;
      }

      // Step 3: type the message — Shift+Enter for the internal line break,
      // plain Enter (or the Send button) submits at the end.
      await chatInput.click();
      const lines = message.split('\n');
      for (let i = 0; i < lines.length; i++) {
        await chatInput.type(lines[i]);
        if (i < lines.length - 1) {
          await page.keyboard.down('Shift');
          await page.keyboard.press('Enter');
          await page.keyboard.up('Shift');
        }
      }

      // Step 4: submit
      const sendButtonSelectors = [
        'button[data-tid="sendMessageCommand"]',
        'button[aria-label="Send"]',
        'button[aria-label*="send" i]',
      ];
      let sent = false;
      for (const selector of sendButtonSelectors) {
        try {
          const btn = page.locator(selector).first();
          if (await btn.isVisible({ timeout: 3000 })) {
            await btn.click();
            sent = true;
            break;
          }
        } catch {
          continue;
        }
      }
      if (!sent) {
        await page.keyboard.press('Enter');
      }

      console.log('[Teams] ✅ Chat notification posted');
    } catch (err) {
      console.log('[Teams] WARNING: failed to post chat notification (non-fatal):', err);
    }
  }

  /**
   * Turn off camera and microphone on the pre-join screen.
   *
   * New Teams uses toggle inputs (data-tid="toggle-video" / "toggle-mute")
   * and button elements. We try both patterns.
   */
  private static async turnOffDevices(page: Page): Promise<void> {
    try {
      console.log('[Teams] Toggling camera and microphone off...');
      await page.waitForTimeout(2000);

      // Turn off camera — try toggle inputs first (new Teams), then buttons
      const cameraSelectors = [
        // New Teams toggle inputs (checked = camera ON, need to click to turn off)
        'input[data-tid="toggle-video"][checked]',
        'input[type="checkbox"][title*="Turn camera off" i]',
        'input[role="switch"][data-tid="toggle-video"]',
        // Button-based (older or alternative UI)
        'button[aria-label*="Turn camera off" i]',
        'button[aria-label*="Camera off" i]',
        '[data-tid="prejoin-camera-button"]',
        'button[aria-label*="camera" i]',
      ];

      for (const selector of cameraSelectors) {
        try {
          const el = page.locator(selector).first();
          if (await el.isVisible({ timeout: 2000 })) {
            await el.click();
            console.log(`[Teams] Turned off camera via: ${selector}`);
            await page.waitForTimeout(500);
            break;
          }
        } catch {
          continue;
        }
      }

      // Mute microphone
      const micSelectors = [
        // New Teams toggle inputs
        'input[data-tid="toggle-mute"]:not([checked])',
        'input[type="checkbox"][title*="Mute mic" i]',
        'input[role="switch"][data-tid="toggle-mute"]',
        // Button-based
        'button[aria-label*="Mute microphone" i]',
        'button[aria-label*="Mute mic" i]',
        '[data-tid="prejoin-mic-button"]',
        'button[aria-label*="microphone" i]',
      ];

      for (const selector of micSelectors) {
        try {
          const el = page.locator(selector).first();
          if (await el.isVisible({ timeout: 2000 })) {
            await el.click();
            console.log(`[Teams] Muted microphone via: ${selector}`);
            await page.waitForTimeout(500);
            break;
          }
        } catch {
          continue;
        }
      }

      console.log('[Teams] Finished toggling devices');
    } catch (error) {
      console.log('[Teams] Could not toggle devices, continuing...', error);
    }
  }
}
