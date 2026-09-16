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
import * as os from 'os';
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
   * Save a debug screenshot with sequential numbering
   */
  private static async screenshot(page: Page, label: string): Promise<void> {
    try {
      const filename = `debug-${label}-${Date.now()}.png`;
      const filepath = path.join(SCREENSHOTS_DIR, filename);
      await page.screenshot({ path: filepath, fullPage: true });
      console.log(`[Teams] Screenshot saved: ${filename}`);
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
   * Process a Teams meeting URL for browser join.
   * 
   * NEW TEAMS (v2): We navigate to the original URL directly. 
   * The launcher page is handled by clicking "Continue on this browser".
   * 
   * We do NOT rewrite to /_#/ URLs — those route to Classic Teams which
   * was retired July 1, 2025 and returns /error/eoa.
   * 
   * We DO add query params that hint the browser to suppress app launch prompts,
   * but the core flow relies on clicking through the launcher page.
   */
  static async processTeamsMeetingUrl(meetingUrl: string): Promise<string> {
    console.log('[Teams] Processing meeting URL:', meetingUrl);

    // IMPORTANT: Do NOT add extra query parameters (anon, launchAgent, type).
    // Teams' server-side redirect intermittently mangles URLs when extra params
    // are present — it can drop the ?p= passcode parameter, causing the coords
    // base64 blob to have empty meetingCode and missing passcode, resulting in
    // "We couldn't find a meeting matching this ID and passcode" errors.
    //
    // The launcher page is handled by clicking "Continue on this browser" anyway,
    // so the extra params are unnecessary. Pass the URL through as-is.

    try {
      // Validate it's a proper URL
      new URL(meetingUrl);
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

    // Check for authenticated pre-join first (no name input, but join button
    // present) — after a successful M365 sign-in, Teams reloads straight into
    // this state with no display-name field to wait for (WI #7101).
    const joinBtn = page.locator('[data-tid="prejoin-join-button"]').first();
    if (await joinBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
      const nameInput = page.locator('input[data-tid="prejoin-display-name-input"]').first();
      if (!(await nameInput.isVisible({ timeout: 1000 }).catch(() => false))) {
        console.log('[Teams] Authenticated pre-join screen detected (join button present, no name input)');
        return true;
      }
    }

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
   * Sign in with Microsoft 365 from the Teams pre-join screen (WI #7101).
   *
   * Contract: `page` must already be on the Teams light-meetings pre-join
   * screen (anonymous state) when this is called — NOT
   * login.microsoftonline.com. Teams web auth starts from a "Sign in" link
   * on the pre-join screen itself; navigating to Microsoft's login domain
   * directly (the old approach) does not carry the meeting context and
   * doesn't reflect how the Teams web client actually authenticates.
   *
   * Flow: click "Sign in" -> in-page Teams email modal -> enter email ->
   * Next -> Microsoft password step (rendered in an iframe OR a popup
   * window, detected at runtime) -> enter password -> Sign in -> KMSI
   * ("Stay signed in?") -> pre-join screen reloads authenticated (name
   * input gone, Join button + account identity present).
   *
   * Never throws — sign-in problems (MFA challenge, conditional access,
   * changed page layout, etc.) are logged and reported as `false` so the
   * caller can fall back to the anonymous join path rather than losing the
   * meeting entirely.
   *
   * @param meetingId  numeric FIRM meeting id, used only to namespace debug
   *                   screenshots uploaded to S3 (`debug/auth/<meetingId>/...`).
   *                   Defaults to 0, which skips screenshot capture entirely
   *                   (unit tests / callers that don't have a meeting yet).
   */
  static async signInWithM365(page: Page, email: string, password: string, meetingId: number = 0): Promise<boolean> {
    console.log('[Teams] Attempting M365 sign-in from pre-join screen...');
    const s3 = meetingId > 0
      ? new S3Service(process.env.AWS_REGION || 'us-east-1', process.env.S3_BUCKET || 'firm-recordings-dev')
      : null;

    try {
      // Step 1: click the "Sign in" link/button on the pre-join screen
      const signInSelectors = [
        'a:has-text("Sign in")',
        'button:has-text("Sign in")',
        '[data-tid*="sign-in"]',
      ];
      let clickedSignIn = false;
      for (const selector of signInSelectors) {
        try {
          const el = page.locator(selector).first();
          if (await el.isVisible({ timeout: 5000 })) {
            await el.click();
            console.log(`[Teams] Clicked "Sign in" via: ${selector}`);
            clickedSignIn = true;
            break;
          }
        } catch {
          continue;
        }
      }
      if (!clickedSignIn) {
        console.log('[Teams] No "Sign in" link found on pre-join screen — cannot authenticate');
        return false;
      }
      await this.captureAuthDebugScreenshot(page, meetingId, 'after-signin-click', s3);

      // Step 2: in-page Teams email entry modal
      const emailInput = page.locator('input[type="email"], input[placeholder*="email" i], input[placeholder*="phone" i]').first();
      await emailInput.waitFor({ state: 'visible', timeout: 20000 });
      await emailInput.fill(email);
      await this.clickTeamsAuthNext(page);
      await this.captureAuthDebugScreenshot(page, meetingId, 'after-email', s3);

      // Step 3: password step — start listening for a popup *before* the
      // click that could open one, then try the iframe path; whichever
      // surface actually shows the password field wins.
      const popupPromise = page.context().waitForEvent('page', { timeout: 8000 }).catch(() => null);
      const authCtx = await this.resolvePasswordContext(page, popupPromise);
      if (!authCtx) {
        console.log('[Teams] Could not locate password field (iframe or popup) — falling back to anonymous join');
        await this.captureAuthDebugScreenshot(page, meetingId, 'no-password-field', s3);
        return false;
      }

      const passwordInput = authCtx.locator('input[type="password"], input[name="passwd"]').first();
      await passwordInput.waitFor({ state: 'visible', timeout: 20000 });
      await passwordInput.fill(password);
      await this.clickM365Button(authCtx.locator);
      await this.captureAuthDebugScreenshot(page, meetingId, 'after-password', s3);

      // Step 4: KMSI and other known Microsoft interrupt pages, in whichever
      // surface (iframe or popup) the auth flow is actually running.
      await this.handleM365InterruptsInContext(authCtx, email);
      await this.captureAuthDebugScreenshot(page, meetingId, 'after-kmsi', s3);

      // Step 5: wait for the pre-join screen to reload in authenticated
      // state — name input gone, Join button present.
      await page.waitForFunction(() => {
        const nameInput = document.querySelector('input[data-tid="prejoin-display-name-input"]');
        const joinBtn = document.querySelector('button[data-tid="prejoin-join-button"], [data-tid="prejoin-join-button"]');
        return !nameInput && !!joinBtn;
      }, { timeout: 30000 }).catch(async () => {
        // Fall back to an identity-display indicator in case the join
        // button selector changed but the account identity view is there.
        await page.waitForSelector('[data-tid*="identity"], [class*="identity"], a:has-text("Change")', { timeout: 10000 });
      });

      console.log('[Teams] M365 sign-in complete — authenticated pre-join screen detected');
      return true;
    } catch (err) {
      console.log('[Teams] M365 sign-in failed, falling back to anonymous join:', err);
      await this.captureAuthDebugScreenshot(page, meetingId, 'signin-failed', s3);
      return false;
    }
  }

  /**
   * Click the "Next" button on the in-page Teams email entry modal (the
   * Teams-styled overlay shown after clicking "Sign in" on the pre-join
   * screen — distinct from the Microsoft-branded password step).
   */
  private static async clickTeamsAuthNext(page: Page): Promise<void> {
    const selectors = ['button:has-text("Next")', 'input[type="submit"]'];
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
   * Locate the Microsoft password step after the Teams email modal's "Next"
   * click. It can render two different ways depending on the tenant/session:
   * an iframe embedded in the Teams pre-join page, or a separate popup
   * window. Try the iframe first (checked in-place, no extra wait needed
   * beyond its own timeout); if that comes up empty, fall back to whatever
   * popup (if any) the caller's `popupPromise` already started listening
   * for before this step began.
   *
   * Returns a small context object abstracting over the two surfaces so the
   * rest of the sign-in flow can locate elements without caring which one
   * it's in.
   */
  private static async resolvePasswordContext(
    page: Page,
    popupPromise: Promise<Page | null>
  ): Promise<{ locator: (selector: string) => Locator; kind: 'iframe' | 'popup' } | null> {
    console.log('[Teams] Looking for Microsoft password step (iframe or popup)...');

    const iframeSelectors = [
      'iframe[src*="login.microsoftonline.com"]',
      'iframe[src*="login.microsoft.com"]',
    ];
    for (const selector of iframeSelectors) {
      try {
        const frame = page.frameLocator(selector);
        const pwInput = frame.locator('input[type="password"], input[name="passwd"]').first();
        if (await pwInput.isVisible({ timeout: 8000 }).catch(() => false)) {
          console.log(`[Teams] Password step found in iframe: ${selector}`);
          return { locator: (sel: string) => frame.locator(sel), kind: 'iframe' };
        }
      } catch {
        continue;
      }
    }

    console.log('[Teams] No iframe password field found — checking for popup window...');
    const popup = await popupPromise;
    if (popup) {
      await popup.waitForLoadState('domcontentloaded', { timeout: 10000 }).catch(() => {});
      console.log('[Teams] Popup window detected for auth:', popup.url());
      return { locator: (sel: string) => popup.locator(sel), kind: 'popup' };
    }

    console.log('[Teams] No popup window appeared either');
    return null;
  }

  /**
   * Save a debug screenshot of the current page state to
   * `debug/auth/<meetingId>/<step>.png` in S3, for diagnosing M365 sign-in
   * failures after the fact (WI #7086).
   *
   * No-op when meetingId <= 0 (unit tests / callers with no real meeting).
   * Never throws — a failed screenshot must never break the sign-in flow.
   */
  private static async captureAuthDebugScreenshot(
    page: Page,
    meetingId: number,
    step: string,
    s3: S3Service | null
  ): Promise<void> {
    if (meetingId <= 0 || !s3) return;

    let tmpFile: string | null = null;
    try {
      tmpFile = path.join(os.tmpdir(), `auth-debug-${meetingId}-${step}-${Date.now()}.png`);
      await page.screenshot({ path: tmpFile });
      const key = `debug/auth/${meetingId}/${step}.png`;
      await s3.uploadWithKey(tmpFile, key);
      console.log(`[Teams] Uploaded auth debug screenshot: ${key}`);
    } catch (err) {
      console.log(`[Teams] WARNING: failed to capture/upload auth debug screenshot (${step}):`, err);
    } finally {
      if (tmpFile) {
        try {
          fs.unlinkSync(tmpFile);
        } catch {
          // Best-effort cleanup — nothing to do if it fails.
        }
      }
    }
  }

  /**
   * Handle known Microsoft sign-in interrupt pages that can appear after the
   * password step (KMSI, security info nag, account picker, consent), in
   * whichever surface (iframe or popup) the Teams-initiated auth flow is
   * actually running (WI #7101 — generalized from the old page-only version
   * that assumed login.microsoftonline.com was the top-level page). Loops up
   * to 3 times since dismissing one interrupt (e.g. KMSI) can reveal another.
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
  private static async handleM365InterruptsInContext(
    authCtx: { locator: (selector: string) => Locator; kind: 'iframe' | 'popup' },
    email: string
  ): Promise<void> {
    const locate = authCtx.locator;

    for (let attempt = 1; attempt <= 3; attempt++) {
      console.log(`[Teams] Checking for M365 interrupt pages in ${authCtx.kind} (attempt ${attempt}/3)...`);
      let handled = false;

      // a. KMSI "Stay signed in?"
      try {
        const staySignedIn = locate('#idSIButton9');
        const kmsiCheckbox = locate('#KmsiCheckboxField');
        const onKmsi =
          (await staySignedIn.isVisible({ timeout: 4000 }).catch(() => false)) ||
          (await kmsiCheckbox.isVisible({ timeout: 2000 }).catch(() => false));
        if (onKmsi) {
          await staySignedIn.click({ timeout: 4000 }).catch((err) => {
            console.log('[Teams] Could not click KMSI Yes button (non-fatal):', err);
          });
          console.log(`[Teams] Handled KMSI prompt in ${authCtx.kind}`);
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
            console.log(`[Teams] Handled "Don't lose access" security prompt in ${authCtx.kind}`);
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
            console.log(`[Teams] Handled account picker in ${authCtx.kind}`);
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
            console.log(`[Teams] Handled consent/permissions screen in ${authCtx.kind}`);
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
        break;
      }
      await new Promise((resolve) => setTimeout(resolve, 1500));
    }
  }

  /**
   * Click the Next/Sign in submit button on the Microsoft password step.
   * The same button id (#idSIButton9) is reused across the password and
   * KMSI steps of the flow. `locate` abstracts over whether the step is
   * rendered in an iframe or a popup window (WI #7101).
   */
  private static async clickM365Button(locate: (selector: string) => Locator): Promise<void> {
    const selectors = ['#idSIButton9', 'input[type="submit"]', 'button[type="submit"]'];
    for (const selector of selectors) {
      try {
        const btn = locate(selector).first();
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
   * 4. Attempt M365 sign-in from the pre-join screen if BOT_EMAIL/BOT_PASSWORD
   *    are configured (WI #7101) — falls back to anonymous on any failure
   * 5. Fill in bot name — anonymous guest join only; a signed-in account
   *    uses its M365 profile name and has no display-name field to fill
   * 6. Turn off camera/mic
   * 7. Click "Join now"
   * 8. Wait for meeting entry (look for "Leave" button)
   * 9. Handle waiting room if needed
   */
  static async join(page: Page, botName: string, originalUrl?: string): Promise<void> {
    console.log('[Teams] Starting join flow...');
    console.log('[Teams] Current URL:', page.url());

    await this.screenshot(page, '01-initial-page');

    // Step 1: Handle the launcher page
    // Teams v2 lands on /v2/?meetingjoin=true#/... and immediately begins client-side
    // hash routing (SPA navigation). A naive waitForTimeout(3000) here races that
    // navigation — the execution context can be destroyed mid-wait, which then blows up
    // the page.evaluate() below with "Execution context was destroyed, most likely
    // because of a navigation". Wait for the SPA to settle first, then a short buffer.
    try {
      await page.waitForLoadState('domcontentloaded', { timeout: 15000 });
    } catch (err) {
      console.log('[Teams] WARNING: waitForLoadState(domcontentloaded) timed out, continuing anyway:', err);
    }
    await page.waitForTimeout(1000);

    const pageText = await this.evaluateWithNavRetry(
      page,
      () => document.body?.innerText?.substring(0, 1000) || 'NO BODY TEXT'
    );
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
        console.log('[Teams] Launcher button clicked, waiting for navigation to light-meetings...');
        try {
          await page.waitForURL(
            (url) => !url.toString().includes('launcher.html') && !url.toString().includes('/dl/launcher/'),
            { timeout: 15000 }
          );
          console.log('[Teams] Launcher navigation complete:', page.url());
        } catch {
          // Natural navigation didn't complete in 15s — a browser camera/mic
          // permission popup can add enough delay to blow past a flat sleep.
          // Force it by re-navigating directly to the original meeting URL.
          // We do NOT rewrite to the classic /_#/l/meetup-join/ format here:
          // that route was retired July 1, 2025 and now returns /error/eoa
          // (see module docstring) — re-navigating the original URL re-enters
          // the same launcher/light-meetings flow instead.
          if (originalUrl) {
            console.log('[Teams] Launcher navigation timed out — re-navigating directly to original meeting URL:', originalUrl);
            await page.goto(originalUrl, { waitUntil: 'domcontentloaded', timeout: 30000 }).catch((err) => {
              console.log('[Teams] WARNING: direct re-navigation failed:', err);
            });
          } else {
            console.log('[Teams] WARNING: Launcher navigation timed out and no originalUrl available for fallback');
          }
        }
      } else {
        console.log('[Teams] WARNING: Could not find launcher button');
        // Log page state for debugging
        const html = await page.evaluate(() => document.body?.innerHTML?.substring(0, 2000) || '');
        console.log('[Teams] Page HTML snippet:', html.substring(0, 500));
      }

      await this.screenshot(page, '01b-after-launcher-click');
    }

    // Step 2: Wait for pre-join screen
    const preJoinReached = await this.waitForPreJoinScreen(page, 120000);

    if (!preJoinReached) {
      console.log('[Teams] WARNING: Pre-join screen not reached after 120s');
      console.log('[Teams] Current URL:', page.url());
      const currentText = await page.evaluate(() => document.body?.innerText?.substring(0, 500) || '');
      console.log('[Teams] Current page text:', currentText);
      await this.screenshot(page, '01c-pre-join-not-reached');

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
            await this.screenshot(page, '01d-retry-failed');
          }
        }
      }
    }

    await this.screenshot(page, '02-pre-join-screen');

    // Step 3: Attempt M365 sign-in from the pre-join screen if credentials
    // are configured (WI #7101). BOT_EMAIL / BOT_PASSWORD are generic env
    // vars — each environment's ECS task definition injects the correct
    // values. Absent either one, or if sign-in fails for any reason, fall
    // back to the existing anonymous join path.
    let authenticated = false;
    const botEmail = process.env.BOT_EMAIL;
    const botPassword = process.env.BOT_PASSWORD;
    if (botEmail && botPassword) {
      // Numeric meeting id for namespacing auth debug screenshots in S3
      // (WI #7086) — same MEETING_ID-env-first pattern reportStatus() uses.
      const numericMeetingId = parseInt(process.env.MEETING_ID || '0', 10) || 0;
      authenticated = await this.signInWithM365(page, botEmail, botPassword, numericMeetingId);
      console.log(authenticated
        ? '[Teams] M365 authentication succeeded — will skip anonymous name entry'
        : '[Teams] M365 authentication did not complete — continuing as anonymous guest');
    } else {
      console.log('[Teams] BOT_EMAIL/BOT_PASSWORD not set — joining Teams as anonymous guest');
    }

    // Step 4: Enter name in the name field
    // Anonymous guest join only — a signed-in M365 account has no display-name
    // field on the pre-join screen; Teams uses the account's profile name (WI #7032).
    if (!authenticated) {
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
      for (const selector of nameSelectors) {
        try {
          const nameInput = page.locator(selector).first();
          if (await nameInput.isVisible({ timeout: 3000 })) {
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
    } else {
      console.log('[Teams] Authenticated session — skipping anonymous display-name pre-fill');
    }

    // Step 5: Turn off camera and microphone
    await this.turnOffDevices(page);

    await page.waitForTimeout(1000);
    await this.screenshot(page, '03-before-join-click');

    // Step 6: Click Join now button
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
      await this.screenshot(page, '03b-no-join-button');
    }

    // Step 7: Wait for meeting to load
    console.log('[Teams] Waiting for meeting to load...');
    
    // Look for the Leave button as confirmation we're in the meeting
    try {
      const leaveButton = page.getByRole('button', { name: /Leave/i });
      await leaveButton.waitFor({ timeout: 60000 });
      console.log('[Teams] ✅ Successfully joined meeting (Leave button visible)');
      await this.screenshot(page, '04-in-meeting');
      await this.postAdmissionChatNotification(page);
      return;
    } catch {
      console.log('[Teams] Leave button not found within 60s, checking other states...');
    }

    await this.screenshot(page, '04-after-join-attempt');

    // Step 8: Check if we're in a waiting room
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
      await this.screenshot(page, '04b-waiting-room');
      let admitted = false;
      // Poll 18×10s = 3 minutes
      for (let i = 0; i < 18; i++) {
        await page.waitForTimeout(10000);

        // Check for Leave button (means we were admitted)
        try {
          const leaveButton = page.getByRole('button', { name: /Leave/i });
          if (await leaveButton.isVisible({ timeout: 1000 })) {
            console.log('[Teams] ✅ Admitted from waiting room, now in meeting');
            await this.screenshot(page, '05-admitted-in-meeting');
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
            await this.screenshot(page, '05-admitted-last-second');
            await this.postAdmissionChatNotification(page);
            return;
          }
        } catch {
          // not admitted
        }
        console.log('[Teams] ❌ Not admitted to lobby within 3 minutes — throwing LobbyTimeoutError');
        await this.screenshot(page, '05-lobby-timeout');
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
      await this.screenshot(page, '05-eoa-error');
      throw new LobbyTimeoutError();
    } else if (joinedCheck.hasLeave || joinedCheck.hasHangup || joinedCheck.hasMeetingUI || joinedCheck.hasRoster) {
      console.log('[Teams] ✅ Successfully joined meeting');
      await this.screenshot(page, '05-in-meeting');
      await this.postAdmissionChatNotification(page);
    } else {
      console.log('[Teams] ⚠️ Meeting join status uncertain — hasMeetingUI=false, hasLeave=false. Treating as lobby timeout.');
      await this.screenshot(page, '05-uncertain-state');
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
