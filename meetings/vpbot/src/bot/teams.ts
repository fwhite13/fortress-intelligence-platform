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

import { Page, Locator, BrowserContext } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';
import { S3Service } from '../transcribe/s3.js';
import { refreshTeamsSession } from './teams-auth.js';
import { ActiveSpeakerEntry } from '../types.js';

export class LobbyTimeoutError extends Error {
  constructor() {
    super('Bot was not admitted to the Teams meeting lobby within 3 minutes');
    this.name = 'LobbyTimeoutError';
  }
}

export interface RosterEntry {
  name: string;
  joinedAtMs: number;
  leftAtMs?: number;
  possiblyMultiVoice: boolean; // conference room heuristic
}

const SCREENSHOTS_DIR = process.env.RECORDINGS_DIR || '/app/recordings';

/**
 * Wait up to `timeout` ms for a locator to become visible; true if it did.
 *
 * WI #8032: use this instead of `locator.isVisible({ timeout })` — Playwright
 * ignores that timeout (deprecated option) and returns immediately, so every
 * "wait for X" check was really a single instant probe.
 */
async function waitVisible(locator: Locator, timeout: number): Promise<boolean> {
  try {
    await locator.waitFor({ state: 'visible', timeout });
    return true;
  } catch {
    return false;
  }
}

/**
 * In-page active speaker hook (WI #8031), installed via addInitScript so it
 * runs before Teams creates its RTCPeerConnections. Serialized with
 * toString(), so it must be self-contained (no closures, no async helpers).
 *
 * Teams' media server sends mixed audio; each RTP packet lists the
 * participants mixed into it as CSRCs. We capture every incoming audio
 * RTCRtpReceiver, poll getContributingSources(), and map recent CSRCs to
 * participants with Teams' own call model (participant.hasAudioSource) —
 * camera-agnostic, unlike the DOM tile selectors. Same technique as the
 * Attendee bot (teams_chromedriver_payload.js).
 *
 * Closed intervals are buffered on window.__firmSpeech as
 * { name, startMs, endMs } relative to window.__firmSpeechStartMs (set by
 * Node when recording starts); Node drains the buffer every few seconds.
 */
function firmSpeechHook(): void {
  const w = window as any;
  if (w.__firmSpeechHookInstalled) return;
  w.__firmSpeechHookInstalled = true;
  w.__firmReceivers = [];
  w.__firmSpeech = [];
  w.__firmSpeechDiag = { pcCount: 0, callFound: false, participantCount: 0, sampleCsrcs: [] };

  const POLL_MS = 150;
  const RECENT_MS = 100;    // CSRC seen within this window = speaking now
  const START_MS = 300;     // consecutive speech before an interval opens
  const STOP_MS = 500;      // silence before an open interval closes
  const MAX_BUFFER = 5000;  // cap if Node never drains

  const Orig = w.RTCPeerConnection;
  if (typeof Orig !== 'function') return;
  const Wrapped = class extends Orig {
    constructor(...args: any[]) {
      super(...args);
      w.__firmSpeechDiag.pcCount++;
      this.addEventListener('track', (ev: any) => {
        if (ev.track?.kind === 'audio' && ev.receiver && !w.__firmReceivers.includes(ev.receiver)) {
          w.__firmReceivers.push(ev.receiver);
        }
      });
    }
  };
  w.RTCPeerConnection = Wrapped;
  if (w.webkitRTCPeerConnection) w.webkitRTCPeerConnection = Wrapped;

  // key -> { name, first, last, open } in Date.now() ms
  const speakers = new Map<string, { name: string; first: number; last: number; open: boolean }>();

  const emit = (s: { name: string; first: number; last: number }) => {
    const base = w.__firmSpeechStartMs;
    if (typeof base !== 'number' || s.last < base) return; // before recording started
    if (w.__firmSpeech.length >= MAX_BUFFER) w.__firmSpeech.shift();
    w.__firmSpeech.push({ name: s.name, startMs: Math.max(0, s.first - base), endMs: s.last - base });
  };

  // Close every open interval now (called by Node on final drain).
  w.__firmSpeechFlush = () => {
    for (const s of speakers.values()) if (s.open) emit(s);
    speakers.clear();
  };

  setInterval(() => {
    try {
      const now = Date.now();
      w.__firmReceivers = w.__firmReceivers.filter((r: any) => r.track?.readyState !== 'ended');

      const recent: any[] = [];
      for (const r of w.__firmReceivers) {
        for (const c of r.getContributingSources?.() || []) {
          // Chrome reports epoch ms; fall back to performance.now() if a build uses a monotonic clock
          const ref = c.timestamp > 1e12 ? now : performance.now();
          if (ref - c.timestamp <= RECENT_MS) recent.push(c);
        }
      }
      if (recent.length > 0) {
        w.__firmSpeechDiag.sampleCsrcs = recent.slice(0, 5).map((c: any) => ({ source: c.source, audioLevel: c.audioLevel }));
      }

      let call: any = null;
      try { call = w.msteamscalling?.deref?.()?.callingService?.getActiveCall?.() || null; } catch { /* ignore */ }
      if (!call) call = w.callingDebug?.observableCall || null;
      w.__firmSpeechDiag.callFound = !!call;

      const speakingNow = new Map<string, string>();
      if (call?.participants && recent.length > 0) {
        const participants = Array.from(call.participants as Iterable<any>);
        w.__firmSpeechDiag.participantCount = participants.length;
        for (const p of participants) {
          if (typeof p?.hasAudioSource !== 'function') continue;
          if (!recent.some(c => { try { return p.hasAudioSource(c.source); } catch { return false; } })) continue;
          const name = (p.displayName || '').trim();
          if (name) speakingNow.set(p.id || name, name);
        }
      }

      for (const [key, name] of speakingNow) {
        const s = speakers.get(key);
        if (!s) {
          speakers.set(key, { name, first: now, last: now, open: false });
        } else {
          s.last = now;
          if (!s.open && now - s.first >= START_MS) s.open = true;
        }
      }
      for (const [key, s] of speakers) {
        if (speakingNow.has(key)) continue;
        if (s.open && now - s.last >= STOP_MS) {
          emit(s);
          speakers.delete(key);
        } else if (!s.open && now - s.last > 2 * POLL_MS) {
          speakers.delete(key); // speech wasn't consecutive long enough to count
        }
      }
    } catch { /* never break the page */ }
  }, POLL_MS);
}

export class TeamsHandler {
  private rosterEntries: RosterEntry[] = [];
  private rosterPollInterval?: NodeJS.Timeout;
  private rosterPanelOpenAttempted = false;
  private _activeSpeakerLog: ActiveSpeakerEntry[] = [];
  private _currentSpeaker: string | null = null;
  private _currentSpeakerEntry: ActiveSpeakerEntry | null = null;
  private _speakerPollInterval: NodeJS.Timeout | null = null;
  private _speechDrainInterval: NodeJS.Timeout | null = null;
  private _speechDrainCount = 0;
  private _csrcActive = false;
  private _speakerPage: Page | null = null;

  /**
   * Install the CSRC active speaker hook (WI #8031). Must be called on the
   * context before any Teams page loads — unconditionally, not only on the
   * sign-in path, since a session restored from storage state never signs in.
   */
  static async installSpeechHook(context: BrowserContext): Promise<void> {
    await context.addInitScript(firmSpeechHook);
    console.log('[Teams][ActiveSpeaker] CSRC speech hook registered on browser context');
  }
  private _recordingStartMs: number = 0;

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
   * Log every frame's URL and how many elements in it match `selector`
   * (WI #8032). Diagnoses chat compose-box misses — if the box lives in a
   * child frame, a page-level locator will never find it.
   */
  private static async logFrameDiagnostics(page: Page, selector: string): Promise<void> {
    try {
      const frames = page.frames();
      console.log(`[Teams] Frame diagnostics (${frames.length} frames):`);
      for (const frame of frames) {
        const matches = await frame.locator(selector).count().catch(() => -1);
        const label = frame === page.mainFrame() ? 'main' : 'child';
        console.log(`[Teams]   [${label}] ${frame.url()} — compose matches: ${matches}`);
      }
    } catch (e) {
      console.log(`[Teams] Frame diagnostics failed: ${e}`);
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
      'button[aria-label="Continue on this browser"]',
      'button[aria-label="Join on this browser"]',
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
        if (await waitVisible(element, 3000)) {
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
          if (await waitVisible(el, 2000)) {
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
   * For authenticated joins, the Join button appears without a name input.
   * For anonymous joins, the name input appears.
   *
   * Uses a long timeout (120s) because Teams can be slow to load,
   * especially for anonymous/guest joins.
   */
  private static async waitForPreJoinScreen(page: Page, timeoutMs: number = 120000): Promise<boolean> {
    console.log(`[Teams] Waiting for pre-join screen (timeout: ${timeoutMs / 1000}s)...`);

    // Primary indicator: either join button OR name input (authenticated vs anonymous)
    try {
      await page.waitForSelector(
        '[data-tid="prejoin-join-button"], input[data-tid="prejoin-display-name-input"]',
        { timeout: timeoutMs }
      );
      console.log('[Teams] Pre-join screen detected');
      return true;
    } catch {
      console.log('[Teams] Pre-join screen not detected within timeout');
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
      await TeamsHandler.screenshot(page, 'auth-00-before-signin-click', s3, meetingId);

      const signInBtnExists = await page.evaluate(() => {
        return !!document.querySelector('[data-tid="auth-sign-in-link"]');
      });
      console.log(`[Teams][AUTH] Sign in button found: ${signInBtnExists} | URL: ${page.url()}`);

      // Inject webdriver override so Teams routes us to authenticated pre-join after auth
      // Must be added before Sign in click so it applies to all subsequent navigations
      // (Microsoft login → KMSI → back to Teams)
      await page.context().addInitScript(() => {
        Object.defineProperty(navigator, 'webdriver', {
          get: () => undefined,
          configurable: true,
        });
      });
      console.log('[Teams][AUTH] navigator.webdriver override injected');

      await page.evaluate(() => {
        const btn = document.querySelector('[data-tid="auth-sign-in-link"]') as HTMLElement | null;
        if (btn) btn.click();
      });
      console.log('[Teams] Clicked Sign in button via evaluate');

      console.log(`[Teams][AUTH] Step: after-signin-click | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-01-after-signin-click', s3, meetingId);

      try {
        await page.waitForSelector('input[data-testid="emailInput"]', { state: 'visible', timeout: 10000 });
      } catch {
        console.log('[Teams] No "Sign in" dialog appeared on pre-join screen — cannot authenticate');
        console.log(`[Teams][AUTH] Step: no-signin-dialog | URL: ${page.url()}`);
        await TeamsHandler.screenshot(page, 'auth-no-signin-dialog', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      // Step 2: in-page Fluent UI email dialog. The email input has
      // data-testid="emailInput" and placeholder="Enter your email" — NOT
      // type="email" (confirmed via live test 2026-09-16).
      console.log(`[Teams][AUTH] Step: before-email | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-02-before-email', s3, meetingId);

      const emailInput = page.locator('input[data-testid="emailInput"], input[placeholder="Enter your email"]').first();
      try {
        await emailInput.waitFor({ state: 'visible', timeout: 10000 });
        const isVisible = await emailInput.isVisible();
        console.log(`[Teams][AUTH] Email input found: input[data-testid="emailInput"] | visible: ${isVisible} | URL: ${page.url()}`);
      } catch (err) {
        console.log(`[Teams][AUTH] Email input not found | URL: ${page.url()} | Error: ${err}`);
        await TeamsHandler.screenshot(page, 'auth-email-input-not-found', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      await emailInput.fill(email);
      console.log(`[Teams][AUTH] Step: after-email | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-03-after-email', s3, meetingId);

      console.log(`[Teams][AUTH] Step: before-email-submit | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-04-before-email-submit', s3, meetingId);
      await this.clickTeamsAuthNext(page);

      console.log(`[Teams][AUTH] Step: after-email-submit | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-05-after-email-submit', s3, meetingId);

      // Step 3: after Next, the entire page navigates to
      // login.microsoftonline.com — confirmed via live test 2026-09-16 to be
      // a full top-level navigation, not an iframe or popup as previously
      // assumed (WI #7101).
      console.log('[Teams] Email submitted — waiting for Microsoft login page...');
      try {
        await page.waitForURL('**/login.microsoftonline.com/**', { timeout: 30000 });
      } catch (err) {
        console.log(`[Teams] Did not navigate to login.microsoftonline.com — falling back to anonymous join | URL: ${page.url()} | Error: ${err}`);
        await TeamsHandler.screenshot(page, 'auth-no-msft-navigation', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }
      console.log('[Teams] On Microsoft login page:', page.url());
      console.log(`[Teams][AUTH] Step: after-msft-navigation | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-06-on-msft-login', s3, meetingId);

      // Step 4: password on login.microsoftonline.com
      console.log(`[Teams][AUTH] Step: before-password | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-07-before-password', s3, meetingId);

      const passwordInput = page.locator('input[type="password"], input[name="passwd"]').first();
      try {
        await passwordInput.waitFor({ state: 'visible', timeout: 20000 });
        const isVisible = await passwordInput.isVisible();
        console.log(`[Teams][AUTH] Password input found: input[type="password"] | visible: ${isVisible} | URL: ${page.url()}`);
      } catch (err) {
        console.log(`[Teams][AUTH] Password input not found | URL: ${page.url()} | Error: ${err}`);
        await TeamsHandler.screenshot(page, 'auth-password-input-not-found', s3, meetingId);
        console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
        return false;
      }

      await passwordInput.fill(password);
      console.log(`[Teams][AUTH] Step: after-password | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-08-after-password', s3, meetingId);

      console.log(`[Teams][AUTH] Step: before-password-submit | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-09-before-password-submit', s3, meetingId);
      await this.clickM365Button(page);

      console.log(`[Teams][AUTH] Step: after-password-submit | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-10-after-password-submit', s3, meetingId);

      // Step 5: KMSI and other known Microsoft interrupt pages on the
      // top-level page.
      console.log(`[Teams][AUTH] Step: before-interrupts | URL: ${page.url()}`);
      await this.handleM365Interrupts(page, email, s3, meetingId);
      console.log(`[Teams][AUTH] Step: after-interrupts | URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-13-after-kmsi', s3, meetingId);

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
        await TeamsHandler.screenshot(page, 'auth-at-warning', s3, meetingId);
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
      await TeamsHandler.screenshot(page, 'auth-14-final-state', s3, meetingId);
      console.log(`[Teams][AUTH] Navigation history: ${navLog.join(' -> ')}`);
      return true;
    } catch (err) {
      console.log('[Teams] M365 sign-in failed, falling back to anonymous join:', err);
      console.log(`[Teams][AUTH] Error at URL: ${page.url()}`);
      await TeamsHandler.screenshot(page, 'auth-signin-failed', s3, meetingId);
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
        if (await waitVisible(btn, 5000)) {
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
          (await waitVisible(staySignedIn, 4000)) ||
          (await waitVisible(kmsiCheckbox, 2000));
        if (onKmsi) {
          console.log(`[Teams][AUTH] KMSI page detected | URL: ${page.url()}`);
          await TeamsHandler.screenshot(page, 'auth-12-kmsi-page', s3, meetingId);
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
          if (await waitVisible(saotccTitle, 3000)) {
            const dismissSelectors = [
              '#idBtn_Back',
              'a:has-text("Not now")',
              'button:has-text("Not now")',
              'a:has-text("Skip")',
              'button:has-text("Skip")',
            ];
            for (const selector of dismissSelectors) {
              const el = locate(selector).first();
              if (await waitVisible(el, 3000)) {
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
          if (await waitVisible(tile, 3000)) {
            const matchingTile = locate('[data-test-id="tile"]').filter({ hasText: email }).first();
            if (await waitVisible(matchingTile, 3000)) {
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
          if (await waitVisible(acceptButton, 3000)) {
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
        await TeamsHandler.screenshot(page, `auth-11${String.fromCharCode(96 + attempt)}-interrupt-attempt-${attempt}`, s3, meetingId);
        break;
      }
      await TeamsHandler.screenshot(page, `auth-11${String.fromCharCode(96 + attempt)}-interrupt-attempt-${attempt}`, s3, meetingId);
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
        if (await waitVisible(btn, 5000)) {
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
  async join(page: Page, botName: string, originalUrl?: string): Promise<void> {
    console.log('[Teams] Starting join flow...');
    console.log('[Teams] Current URL:', page.url());

    // Instantiate S3Service early for all debug screenshots
    const meetingId = process.env.MEETING_ID || '0';
    const s3 = new S3Service(
      process.env.AWS_REGION || 'us-east-1',
      process.env.S3_BUCKET || 'firm-recordings-dev'
    );

    await TeamsHandler.screenshot(page, '01-initial-page', s3, meetingId);

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
      const clicked = await TeamsHandler.clickLauncherButton(page);
      
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

      await TeamsHandler.screenshot(page, '01b-after-launcher-click', s3, meetingId);
    }

    // Step 2: Wait for pre-join screen
    const preJoinReached = await TeamsHandler.waitForPreJoinScreen(page, 120000);

    if (!preJoinReached) {
      console.log('[Teams] WARNING: Pre-join screen not reached after 120s');
      console.log('[Teams] Current URL:', page.url());
      const currentText = await page.evaluate(() => document.body?.innerText?.substring(0, 500) || '');
      console.log('[Teams] Current page text:', currentText);
      await TeamsHandler.screenshot(page, '01c-pre-join-not-reached', s3, meetingId);

      // If we're still on the launcher, try one more time with a fresh navigation
      if (page.url().includes('/dl/launcher/') || page.url().includes('launcher.html')) {
        console.log('[Teams] Still on launcher — trying fresh navigation with original URL...');
        if (originalUrl) {
          await page.goto(originalUrl, { waitUntil: 'networkidle', timeout: 30000 });
          await page.waitForTimeout(3000);
          await TeamsHandler.clickLauncherButton(page);
          await page.waitForTimeout(5000);
          // Try waiting for pre-join one more time
          const retryResult = await TeamsHandler.waitForPreJoinScreen(page, 60000);
          if (!retryResult) {
            console.log('[Teams] WARNING: Still cannot reach pre-join screen after retry');
            await TeamsHandler.screenshot(page, '01d-retry-failed', s3, meetingId);
          }
        }
      }
    }

    await TeamsHandler.screenshot(page, '02-pre-join-screen', s3, meetingId);

    // Step 3: Detect pre-join state and handle auth retry if needed
    type PreJoinState = 'signed_in' | 'signed_out' | 'unknown';
    const detectPreJoinState = async (): Promise<PreJoinState> => {
      const hasJoinButton = await waitVisible(page.locator('[data-tid="prejoin-join-button"]'), 2000);
      const hasNameInput = await waitVisible(page.locator('input[data-tid="prejoin-display-name-input"]'), 2000);

      if (hasJoinButton && !hasNameInput) {
        return 'signed_in';
      } else if (hasNameInput) {
        return 'signed_out';
      } else {
        return 'unknown';
      }
    };

    const botEmail = process.env.BOT_EMAIL;
    const botPassword = process.env.BOT_PASSWORD;
    let preJoinState = await detectPreJoinState();
    console.log(`[Teams] Pre-join state: ${preJoinState}`);

    // If we're on signed_out but creds were configured, retry with refreshTeamsSession
    if (preJoinState === 'signed_out' && botEmail && botPassword) {
      console.log('[Teams] Pre-join shows signed_out despite auth attempt — refreshing session...');
      for (let retry = 0; retry < 2; retry++) {
        await TeamsHandler.screenshot(page, `02c-retry-${retry}-before-refresh`, s3, meetingId);
        const refreshed = await refreshTeamsSession(page.context());
        if (refreshed) {
          console.log('[Teams] Session refreshed — re-navigating to meeting...');
          await page.goto(originalUrl || page.url(), { waitUntil: 'domcontentloaded', timeout: 30000 });
          await page.waitForTimeout(2000);

          // Click launcher if present
          const launcherClicked = await TeamsHandler.clickLauncherButton(page);
          if (launcherClicked) {
            await page.waitForTimeout(3000);
          }

          // Wait for pre-join again
          await TeamsHandler.waitForPreJoinScreen(page, 60000);
          preJoinState = await detectPreJoinState();
          await TeamsHandler.screenshot(page, `02d-retry-${retry}-after-refresh`, s3, meetingId);
          console.log(`[Teams] Pre-join state after refresh: ${preJoinState}`);

          if (preJoinState === 'signed_in') {
            console.log('[Teams] Successfully reached authenticated pre-join after retry');
            break;
          }
        } else {
          console.log('[Teams] Session refresh failed — continuing with anonymous');
          break;
        }
      }
    }

    // Step 4: Enter name if on signed_out state
    if (preJoinState === 'signed_out') {
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
          if (await waitVisible(nameInput, 3000)) {
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
      console.log('[Teams] Pre-join state is signed_in — skipping name entry');
    }

    // Step 5: Turn off camera and microphone
    await TeamsHandler.turnOffDevices(page);

    // WI #7792: Wait at pre-join screen until scheduled start time (minus a small buffer)
    // so the bot joins at the meeting start rather than late due to cold-start overhead.
    // The wait is safe here because we haven't clicked Join yet — Teams doesn't know we exist.
    const scheduledStartEnv = process.env.SCHEDULED_START_TIME;
    if (scheduledStartEnv) {
      const scheduledStartMs = new Date(scheduledStartEnv).getTime();
      const waitUntilMs = scheduledStartMs - 20_000; // join 20s before start
      const nowMs = Date.now();
      const maxWaitMs = 10 * 60_000; // guard against a bad value parking the bot indefinitely
      if (Number.isNaN(scheduledStartMs)) {
        console.warn(`[Teams] Ignoring unparseable SCHEDULED_START_TIME: ${scheduledStartEnv}`);
      } else if (waitUntilMs > nowMs) {
        const waitMs = Math.min(waitUntilMs - nowMs, maxWaitMs);
        console.log(`[Teams] Waiting ${Math.round(waitMs / 1000)}s at pre-join screen until T-20s of scheduled start...`);
        await page.waitForTimeout(waitMs);
        console.log('[Teams] Pre-join wait complete — proceeding to join click');
      }
    }

    await page.waitForTimeout(1000);
    await TeamsHandler.screenshot(page, '03-before-join-click', s3, meetingId);

    // Step 6: Click Join now button
    const joinButtonTexts = ['Join now', 'Join', 'Ask to join', 'Join meeting'];

    let clickedJoin = false;
    
    // First try data-tid selector (most reliable)
    try {
      const tidButton = page.locator('[data-tid="prejoin-join-button"]').first();
      if (await waitVisible(tidButton, 3000)) {
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
          if (await waitVisible(button, 3000)) {
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
      await TeamsHandler.screenshot(page, '03b-no-join-button', s3, meetingId);
    }

    // Step 7: Wait for meeting to load
    console.log('[Teams] Waiting for meeting to load...');

    // Look for the Leave button as confirmation we're in the meeting
    try {
      const leaveButton = page.getByRole('button', { name: /Leave/i });
      await leaveButton.waitFor({ timeout: 60000 });
      console.log('[Teams] ✅ Successfully joined meeting (Leave button visible)');
      await TeamsHandler.screenshot(page, '04-in-meeting', s3, meetingId);
      return;
    } catch {
      console.log('[Teams] Leave button not found within 60s, checking other states...');
    }

    await TeamsHandler.screenshot(page, '04-after-join-attempt', s3, meetingId);

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
      await TeamsHandler.screenshot(page, '04b-waiting-room', s3, meetingId);
      let admitted = false;
      // Poll 18×10s = 3 minutes
      for (let i = 0; i < 18; i++) {
        await page.waitForTimeout(10000);

        // Check for Leave button (means we were admitted)
        try {
          const leaveButton = page.getByRole('button', { name: /Leave/i });
          if (await waitVisible(leaveButton, 1000)) {
            console.log('[Teams] ✅ Admitted from waiting room, now in meeting');
            await TeamsHandler.screenshot(page, '05-admitted-in-meeting', s3, meetingId);
            admitted = true;
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
          if (await waitVisible(leaveButton, 2000)) {
            console.log('[Teams] ✅ Admitted just before timeout — now in meeting');
            await TeamsHandler.screenshot(page, '05-admitted-last-second', s3, meetingId);
            return;
          }
        } catch {
          // not admitted
        }
        console.log('[Teams] ❌ Not admitted to lobby within 3 minutes — throwing LobbyTimeoutError');
        await TeamsHandler.screenshot(page, '05-lobby-timeout', s3, meetingId);
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
      await TeamsHandler.screenshot(page, '05-eoa-error', s3, meetingId);
      throw new LobbyTimeoutError();
    } else if (joinedCheck.hasLeave || joinedCheck.hasHangup || joinedCheck.hasMeetingUI || joinedCheck.hasRoster) {
      console.log('[Teams] ✅ Successfully joined meeting');
      await TeamsHandler.screenshot(page, '05-in-meeting', s3, meetingId);
    } else {
      console.log('[Teams] ⚠️ Meeting join status uncertain — hasMeetingUI=false, hasLeave=false. Treating as lobby timeout.');
      await TeamsHandler.screenshot(page, '05-uncertain-state', s3, meetingId);
      throw new LobbyTimeoutError();
    }
  }

  /**
   * Post a join notification to the meeting chat (WI #7034), identifying the
   * bot and attributing the recording to the FIRM user(s) who requested it.
   *
   * Only ever called after confirmed admission — never from the lobby.
   * Invoked by MeetingBot after FFmpeg recording has started (WI #7609).
   * `BOT_NAMES_CSV` is passed by firm-web at ECS task launch: a single name
   * for a single recorder, or a comma-separated list when multiple FIRM
   * users share the meeting.
   *
   * Non-fatal by design — a failed chat post must never fail the recording.
   */
  static async postAdmissionChatNotification(page: Page): Promise<void> {
    try {
      const namesCsv = process.env.BOT_NAMES_CSV || '';
      const names = namesCsv.split(',').map(n => n.trim()).filter(Boolean);
      if (names.length === 0) {
        console.log('[Teams] BOT_NAMES_CSV not set — skipping chat notification');
        return;
      }

      const botLabel = process.env.BOT_NAME || 'Fortress Notetaker';
      const message =
        `${botLabel} has joined to record this meeting on behalf of ${names.join(', ')}.\n` +
        `This session is being recorded. Participants who continue acknowledge they consent to recording.`;

      console.log('[Teams] Posting join notification to meeting chat...');

      // Step 1: open the chat panel. WI #8032: the in-meeting toggle is
      // button#chat-button (an id, not a data-tid — matches the Attendee bot).
      // The old broad aria-label "Chat" match hit the left app-bar Chat app in
      // the signed-in shell, which navigates away from the meeting stage.
      const composeSelector = [
        '[aria-label^="Type a message"]',
        '[placeholder^="Type a message"]',
        '[id^="new-message-"][contenteditable="true"]',
      ].join(', ');
      const composeBox = page.locator(composeSelector).first();

      const chatButtonSelectors = [
        '#chat-button',
        'button[data-tid="chat-button"]',
        'button[aria-label*="Show conversation" i]',
      ];
      let openedChat = await composeBox.isVisible();
      // WI #8064: the compose-box retry budget is measured from this moment.
      let chatOpenedAt = Date.now();
      if (openedChat) {
        console.log('[Teams] Chat panel already open — not toggling');
      }
      for (const selector of chatButtonSelectors) {
        if (openedChat) break;
        try {
          const btn = page.locator(selector).first();
          if (await waitVisible(btn, 3000)) {
            await btn.click();
            chatOpenedAt = Date.now();
            console.log(`[Teams] Opened chat panel via: ${selector}`);
            openedChat = true;
          }
        } catch {
          continue;
        }
      }
      if (!openedChat) {
        console.log('[Teams] WARNING: could not find chat panel toggle — skipping chat notification');
        await TeamsHandler.logFrameDiagnostics(page, composeSelector);
        return;
      }

      // Step 2: wait for the CKEditor compose box. It mounts lazily after the
      // panel shell, and the first open after joining can take well over the
      // old 2s+7s budget — allow 20s on cold start.
      console.log('[Teams] Chat panel opened, waiting up to 20s for compose box...');
      const composeReady = await waitVisible(composeBox, 20000);
      console.log(composeReady
        ? '[Teams] Compose box visible'
        : '[Teams] Compose box not visible within 20s — continuing to poll all selectors (up to 60s)');

      // Step 2a: Dismiss notifications/banners that may block chat input
      // Dismiss "You have been muted" notification if present
      try {
        const dismissBtn = page.locator('button[aria-label="Dismiss"]').first();
        if (await waitVisible(dismissBtn, 2000)) {
          await dismissBtn.click();
          console.log('[Teams] Dismissed "You have been muted" notification');
          await page.waitForTimeout(500);
        }
      } catch {
        // No notification present, continue
      }

      // Step 2b: Dismiss "Replying to external participants" banner if present
      // This banner appears above the compose box when external guests are in the meeting
      // and may interfere with input field detection
      try {
        const externalBannerSelectors = [
          'button[aria-label*="close" i]:has(~ *:has-text("Replying to external"))',
          'button[aria-label*="dismiss" i]:has(~ *:has-text("external"))',
          '[data-tid*="external-banner"] button[aria-label*="close" i]',
          '[data-tid*="external-banner"] button[aria-label*="dismiss" i]',
        ];
        for (const selector of externalBannerSelectors) {
          try {
            const btn = page.locator(selector).first();
            if (await waitVisible(btn, 1000)) {
              await btn.click();
              console.log('[Teams] Dismissed "Replying to external participants" banner');
              await page.waitForTimeout(500);
              break;
            }
          } catch {
            continue;
          }
        }
        // Also try finding the banner by text and clicking its X button
        const bannerText = page.locator('text=/replying to external/i').first();
        if (await waitVisible(bannerText, 1000)) {
          // Look for close button near the banner
          const closeBtn = page.locator('button:near(:text("Replying to external"))').filter({ hasText: /×|X|close/i }).first();
          if (await waitVisible(closeBtn, 1000)) {
            await closeBtn.click();
            console.log('[Teams] Dismissed external participants banner via nearby close button');
            await page.waitForTimeout(500);
          }
        }
      } catch {
        // No external banner present, continue
      }

      // Step 3: find the chat input. Primary selectors are the production-
      // confirmed Attendee ones above; the rest are older guesses kept as a
      // fallback. No shadow-DOM search — the compose box is light DOM
      // (CKEditor 5), and Playwright CSS locators pierce open shadow roots anyway.
      // WI #8064: when the bot joins at meeting start, the compose box can take
      // well over 20s to mount, so poll all selectors every 3s until 60s have
      // elapsed since the chat panel was opened.
      const COMPOSE_TOTAL_WAIT_MS = 60000;
      const COMPOSE_POLL_INTERVAL_MS = 3000;
      const COMPOSE_LOG_INTERVAL_MS = 10000;
      const fallbackInputSelectors = [
        'div[aria-placeholder="Type a message"]',
        'p[data-placeholder="Type a message"]',
        '[data-tid*="compose"][contenteditable="true"]',
        'div[data-tid="newMessageInput"]',
        'div[data-tid="ckeditor"]',
        'div[contenteditable="true"][aria-label*="message" i]',
        'div[contenteditable="true"][aria-label*="reply" i]',
        '[contenteditable="true"][role="textbox"]',
      ];
      let chatInput: Locator | null = null;
      let successfulSelector: string | null = null;

      let lastProgressLog = chatOpenedAt;
      while (true) {
        if (await composeBox.isVisible()) {
          chatInput = composeBox;
          successfulSelector = composeSelector;
          break;
        }
        for (const selector of fallbackInputSelectors) {
          const el = page.locator(selector).first();
          if (await el.isVisible()) {
            chatInput = el;
            successfulSelector = selector;
            break;
          }
        }
        if (chatInput) break;

        const now = Date.now();
        if (now - chatOpenedAt >= COMPOSE_TOTAL_WAIT_MS) {
          console.log(`[Teams] WARNING: compose box not visible within ${COMPOSE_TOTAL_WAIT_MS / 1000}s of opening chat`);
          break;
        }
        if (now - lastProgressLog >= COMPOSE_LOG_INTERVAL_MS) {
          console.log(`[Teams] Waiting for compose box... (${Math.round((now - chatOpenedAt) / 1000)}s elapsed)`);
          lastProgressLog = now;
        }
        await page.waitForTimeout(
          Math.min(COMPOSE_POLL_INTERVAL_MS, COMPOSE_TOTAL_WAIT_MS - (now - chatOpenedAt))
        );
      }
      if (successfulSelector) {
        console.log(`[Teams] ✅ Found chat input via: ${successfulSelector}`);
      }

      if (!chatInput || !successfulSelector) {
        console.log('[Teams] WARNING: could not find chat input field — skipping chat notification');
        await TeamsHandler.logFrameDiagnostics(page, composeSelector);
        const s3 = new S3Service(
          process.env.AWS_REGION || 'us-east-1',
          process.env.S3_BUCKET || 'firm-recordings-dev'
        );
        await this.screenshot(page, 'teams-chat-failed', s3, process.env.MEETING_ID || '0');
        return;
      }

      // Step 4: type the message — for contenteditable divs, use click + keyboard.type
      await chatInput.click();
      await page.waitForTimeout(500);
      const lines = message.split('\n');
      for (let i = 0; i < lines.length; i++) {
        await page.keyboard.type(lines[i]);
        if (i < lines.length - 1) {
          await page.keyboard.down('Shift');
          await page.keyboard.press('Enter');
          await page.keyboard.up('Shift');
        }
      }

      // Step 5: submit
      const sendButtonSelectors = [
        'button[data-tid="sendMessageCommand"]',
        'button[aria-label="Send"]',
        'button[aria-label*="send" i]',
      ];
      let sent = false;
      for (const selector of sendButtonSelectors) {
        try {
          const btn = page.locator(selector).first();
          if (await waitVisible(btn, 3000)) {
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

      // Step 6: capture screenshot and log success
      const s3 = new S3Service(
        process.env.AWS_REGION || 'us-east-1',
        process.env.S3_BUCKET || 'firm-recordings-dev'
      );
      await this.screenshot(page, 'teams-chat-sent', s3, process.env.MEETING_ID || '0');
      console.log(`[Teams] ✅ Chat notification posted (input selector: ${successfulSelector})`);
    } catch (err) {
      console.log('[Teams] WARNING: failed to post chat notification (non-fatal):', err);
    }
  }

  /**
   * Turn off camera and microphone on the pre-join screen.
   *
   * New Teams uses toggle inputs (data-tid="toggle-video" / "toggle-mute")
   * and button elements. We try both patterns.
   *
   * WI #7295: Fixed camera toggle to check actual button state using
   * aria-checked/aria-pressed instead of [checked] attribute.
   */
  private static async turnOffDevices(page: Page): Promise<void> {
    try {
      console.log('[Teams] Toggling camera and microphone off...');
      await page.waitForTimeout(2000);

      // Turn off camera — check actual state via aria-checked/aria-pressed
      // Pre-join defaults to camera ON, so if we find a toggle we click it once
      const cameraSelectors = [
        'button[data-tid="toggle-video"]',
        'input[data-tid="toggle-video"]',
        'button[aria-label*="camera" i]',
        '[data-tid="prejoin-camera-button"]',
      ];

      let toggledCamera = false;
      for (const selector of cameraSelectors) {
        try {
          const el = page.locator(selector).first();
          if (await waitVisible(el, 2000)) {
            // Check if camera is currently ON via aria-checked or aria-pressed
            const state = await page.evaluate((sel) => {
              const elem = document.querySelector(sel);
              if (!elem) return null;
              const ariaChecked = elem.getAttribute('aria-checked');
              const ariaPressed = elem.getAttribute('aria-pressed');
              const checked = (elem as HTMLInputElement).checked;
              return { ariaChecked, ariaPressed, checked };
            }, selector);

            console.log(`[Teams] Camera toggle state via ${selector}:`, state);

            // If camera is on (aria-checked="true" or aria-pressed="true"), click to turn off
            // Or if state is indeterminate, just click once (pre-join defaults to camera ON)
            const shouldClick =
              state?.ariaChecked === 'true' ||
              state?.ariaPressed === 'true' ||
              state?.checked === true ||
              state === null; // Fallback: always click if we can't determine state

            if (shouldClick) {
              // Use page.evaluate for reliable click (same pattern as Teams auth)
              await page.evaluate((sel) => {
                const elem = document.querySelector(sel) as HTMLElement | null;
                if (elem) elem.click();
              }, selector);
              console.log(`[Teams] Clicked camera toggle via: ${selector}`);
              await page.waitForTimeout(500);
              toggledCamera = true;
              break;
            } else {
              console.log(`[Teams] Camera already off via ${selector} — skipping click`);
              toggledCamera = true;
              break;
            }
          }
        } catch (err) {
          console.log(`[Teams] Failed to toggle camera via ${selector}:`, err);
          continue;
        }
      }

      if (!toggledCamera) {
        console.log('[Teams] WARNING: Could not find camera toggle — camera may still be on');
      }

      // Screenshot after camera toggle attempt
      const meetingId = process.env.MEETING_ID || '0';
      const s3 = new S3Service(
        process.env.AWS_REGION || 'us-east-1',
        process.env.S3_BUCKET || 'firm-recordings-dev'
      );
      await this.screenshot(page, 'pre-join-camera-toggled', s3, meetingId);

      // Mute microphone — check actual state via aria-checked/aria-pressed/checked
      // Pre-join defaults to mic ON, so if it's currently ON we click to mute
      const micSelectors = [
        'button[data-tid="toggle-mute"]',
        'input[data-tid="toggle-mute"]',
        'button[aria-label*="microphone" i]',
        '[data-tid="prejoin-mic-button"]',
      ];

      let toggledMic = false;
      for (const selector of micSelectors) {
        try {
          const el = page.locator(selector).first();
          if (await waitVisible(el, 2000)) {
            // Check if microphone is currently ON via aria-checked, aria-pressed, or checked
            const state = await page.evaluate((sel) => {
              const elem = document.querySelector(sel);
              if (!elem) return null;
              const ariaChecked = elem.getAttribute('aria-checked');
              const ariaPressed = elem.getAttribute('aria-pressed');
              const checked = (elem as HTMLInputElement).checked;
              return { ariaChecked, ariaPressed, checked };
            }, selector);

            console.log(`[Teams] Microphone toggle state via ${selector}:`, state);

            // If mic is on (aria-checked="true" or aria-pressed="true" or checked=true), click to mute
            // Or if state is indeterminate, just click once (pre-join defaults to mic ON)
            const shouldClick =
              state?.ariaChecked === 'true' ||
              state?.ariaPressed === 'true' ||
              state?.checked === true ||
              state === null; // Fallback: always click if we can't determine state

            if (shouldClick) {
              // Use page.evaluate for reliable click (same pattern as camera and Teams auth)
              await page.evaluate((sel) => {
                const elem = document.querySelector(sel) as HTMLElement | null;
                if (elem) elem.click();
              }, selector);
              console.log(`[Teams] Clicked microphone toggle via: ${selector}`);
              await page.waitForTimeout(500);
              toggledMic = true;
              break;
            } else {
              console.log(`[Teams] Microphone already muted via ${selector} — skipping click`);
              toggledMic = true;
              break;
            }
          }
        } catch (err) {
          console.log(`[Teams] Failed to toggle microphone via ${selector}:`, err);
          continue;
        }
      }

      if (!toggledMic) {
        console.log('[Teams] WARNING: Could not find microphone toggle — mic may still be on');
      }

      // Screenshot after microphone toggle attempt
      await this.screenshot(page, 'pre-join-mic-toggled', s3, meetingId);

      console.log('[Teams] Finished toggling devices');
    } catch (error) {
      console.log('[Teams] Could not toggle devices, continuing...', error);
    }
  }

  /**
   * Start roster polling every 30 seconds
   */
  startRosterPolling(page: Page): void {
    console.log('[Teams][Roster] Starting roster polling (30s interval)');
    this.rosterPollInterval = setInterval(async () => {
      try {
        await this.pollRoster(page);
      } catch (err) {
        console.log('[Teams][Roster] Polling error (non-fatal):', err);
      }
    }, 30000);
    // Also poll immediately on start
    this.pollRoster(page).catch(err => console.log('[Teams][Roster] Initial poll error (non-fatal):', err));
  }

  /**
   * Stop roster polling and mark all remaining entries as left
   */
  stopRosterPolling(): void {
    if (this.rosterPollInterval) {
      clearInterval(this.rosterPollInterval);
      this.rosterPollInterval = undefined;
      console.log('[Teams][Roster] Stopped roster polling');
    }
    // Mark all entries without leftAtMs
    const now = Date.now();
    for (const entry of this.rosterEntries) {
      if (!entry.leftAtMs) {
        entry.leftAtMs = now;
      }
    }
  }

  /**
   * Poll for current participants, regardless of camera state.
   *
   * The previous implementation read only Video-type stream tiles, which
   * only exist for participants with cameras on — camera-off meetings produced
   * an empty roster. Sources are now tried in order (see
   * references/roster-research.md):
   *   1. Teams' in-page call state (`callingService.getActiveCall().participants`)
   *      — camera-agnostic, includes participants outside the visible gallery,
   *      needs no UI interaction. Same technique as the open-source Attendee bot.
   *   2. The People/roster panel — opened once on first need and left open so
   *      we don't toggle UI every poll. Panel rows exist for every participant.
   *   3. Any stream tile (`[data-stream-type][data-tid]`, any stream type) —
   *      last resort; limited to whoever the gallery is currently rendering.
   */
  private async pollRoster(page: Page): Promise<void> {
    try {
      let source = 'call-state';
      let names = await this.readRosterFromCallState(page);

      // WI #7919: Supplement call-state with roster panel if count seems low
      // The organizer may be missing from call.participants but visible in the panel
      if (names !== null && names.length > 0) {
        const panelNames = await this.readRosterFromPanel(page);
        if (panelNames !== null && panelNames.length > names.length) {
          // Panel has more participants — merge any missing names
          const callStateSet = new Set(names.map(n => n.toLowerCase()));
          const supplemented: string[] = [];
          for (const panelName of panelNames) {
            if (!callStateSet.has(panelName.toLowerCase())) {
              supplemented.push(panelName);
            }
          }
          if (supplemented.length > 0) {
            console.log(`[Teams][Roster] Supplementing call-state with ${supplemented.length} names from panel: ${supplemented.join(', ')}`);
            names = [...names, ...supplemented];
            source = 'call-state+panel';
          }
        }
      }

      if (names === null) {
        source = 'roster-panel';
        names = await this.readRosterFromPanel(page);
      }
      if (names === null || names.length === 0) {
        const tileNames = await this.readRosterFromTiles(page);
        if (tileNames.length > 0 || names === null) {
          source = 'stream-tiles';
          names = tileNames;
        }
      }

      const selfNames = [process.env.BOT_NAME]
        .map(n => (n || '').trim().toLowerCase())
        .filter(n => n.length > 0);
      names = [...new Set(names
        .map(n => n.replace(/\s*\((?:you|guest|unverified|external)\)\s*$/i, '').trim())
        .filter(n => n.length > 0 && !n.includes('@') && !selfNames.includes(n.toLowerCase())))];

      // Only the call state is authoritative for "nobody else is here"; an empty
      // DOM read usually means the meeting UI is still loading.
      if (names.length === 0 && source !== 'call-state') {
        console.log('[Teams][Roster] No participants found via any source (meeting may still be loading)');
        return;
      }

      const now = Date.now();
      // Add new participants
      for (const name of names) {
        if (!this.rosterEntries.find(e => e.name === name && !e.leftAtMs)) {
          this.rosterEntries.push({
            name,
            joinedAtMs: now,
            possiblyMultiVoice: /conference.?room|conf.?room|board.?room|huddle/i.test(name)
          });
        }
      }
      // Mark left participants
      for (const entry of this.rosterEntries.filter(e => !e.leftAtMs)) {
        if (!names.includes(entry.name)) {
          entry.leftAtMs = now;
        }
      }

      console.log(`[Teams][Roster] ${names.length} participants via ${source}: ${names.join(', ')}`);
    } catch (e) {
      console.log(`[Teams][Roster] Poll error: ${e}`);
    }
  }

  /**
   * Read remote participants from the Teams web client's in-page call object.
   * Returns null if the call object isn't reachable (client internals changed).
   * Lobby participants (state 7) and nameless system participants are excluded.
   *
   * WI #7919: Also checks call.organizer, call.callerInfo, and other supplemental
   * properties to capture the meeting organizer who may not appear in participants.
   */
  private async readRosterFromCallState(page: Page): Promise<string[] | null> {
    try {
      const result = await page.evaluate(() => {
        const w = window as any;
        let call: any = null;
        try { call = w.msteamscalling?.deref?.()?.callingService?.getActiveCall?.() || null; } catch { /* ignore */ }
        if (!call) call = w.callingDebug?.observableCall || null;
        if (!call || !call.participants) return null;

        const LOBBY_STATE = 7;
        const names: string[] = [];
        const diagnostics = {
          rawParticipantCount: 0,
          filteredCount: 0,
          skippedStates: [] as number[],
          missingDisplayName: 0,
          organizerName: null as string | null,
          callerInfoName: null as string | null,
          localParticipantName: null as string | null,
        };

        // Extract displayName with fallbacks for nested structures
        const extractName = (p: any): string | null => {
          if (!p) return null;
          // Direct displayName
          if (typeof p.displayName === 'string' && p.displayName.trim()) {
            return p.displayName.trim();
          }
          // Nested identity.displayName (some Teams versions)
          if (p.identity?.displayName && typeof p.identity.displayName === 'string') {
            return p.identity.displayName.trim();
          }
          // Nested user.displayName
          if (p.user?.displayName && typeof p.user.displayName === 'string') {
            return p.user.displayName.trim();
          }
          // info.displayName pattern
          if (p.info?.displayName && typeof p.info.displayName === 'string') {
            return p.info.displayName.trim();
          }
          return null;
        };

        // Process main participants array
        const participantsArray = Array.from(call.participants as Iterable<any>);
        diagnostics.rawParticipantCount = participantsArray.length;

        for (const p of participantsArray) {
          const name = extractName(p);
          if (!name) {
            diagnostics.missingDisplayName++;
            continue;
          }
          if (p.state === LOBBY_STATE) {
            diagnostics.skippedStates.push(LOBBY_STATE);
            continue;
          }
          names.push(name);
        }
        diagnostics.filteredCount = names.length;

        // WI #7919: Check supplemental properties for organizer
        // The organizer may be in a separate property, not in participants array
        if (call.organizer) {
          const orgName = extractName(call.organizer);
          if (orgName) {
            diagnostics.organizerName = orgName;
            if (!names.includes(orgName)) {
              names.push(orgName);
            }
          }
        }

        // Check callerInfo (who initiated the call)
        if (call.callerInfo) {
          const callerName = extractName(call.callerInfo);
          if (callerName) {
            diagnostics.callerInfoName = callerName;
            if (!names.includes(callerName)) {
              names.push(callerName);
            }
          }
        }

        // Check localParticipant (might be the organizer if they're also local)
        if (call.localParticipant) {
          const localName = extractName(call.localParticipant);
          if (localName) {
            diagnostics.localParticipantName = localName;
            // Don't auto-add local participant (usually the bot itself)
          }
        }

        // Check for presenter/organizer role in participants
        for (const p of participantsArray) {
          const role = p.role || p.meetingRole || p.participantRole;
          if (role && /organizer|presenter/i.test(String(role))) {
            const name = extractName(p);
            if (name && !names.includes(name)) {
              names.push(name);
            }
          }
        }

        return { names, diagnostics };
      });

      if (!result) return null;

      // Log diagnostics if there's a mismatch (filtered < raw, suggesting possible missed participants)
      if (result.diagnostics.filteredCount < result.diagnostics.rawParticipantCount - 1) {
        console.log(`[Teams][Roster] Call-state diagnostic: raw=${result.diagnostics.rawParticipantCount}, ` +
          `filtered=${result.diagnostics.filteredCount}, missingDisplayName=${result.diagnostics.missingDisplayName}, ` +
          `skippedStates=${result.diagnostics.skippedStates.join(',') || 'none'}`);
      }
      if (result.diagnostics.organizerName) {
        console.log(`[Teams][Roster] Organizer found via call.organizer: ${result.diagnostics.organizerName}`);
      }
      if (result.diagnostics.callerInfoName) {
        console.log(`[Teams][Roster] Caller found via call.callerInfo: ${result.diagnostics.callerInfoName}`);
      }

      return result.names;
    } catch (e) {
      console.log(`[Teams][Roster] Call-state read failed (non-fatal): ${e}`);
      return null;
    }
  }

  /**
   * Read participant names from the People/roster panel, opening it once if it
   * isn't already open. Returns null if the panel can't be found or opened.
   */
  private async readRosterFromPanel(page: Page): Promise<string[] | null> {
    const panelSelector = [
      '[data-tid="roster"]',
      '[data-tid="people-pane"]',
      '[data-tid*="participant-list"]',
      '[role="tree"][aria-label*="articipant" i]',
      '[role="tree"][aria-label*="people" i]',
    ].join(', ');
    try {
      let panelOpen = await waitVisible(page.locator(panelSelector).first(), 1000);
      if (!panelOpen && !this.rosterPanelOpenAttempted) {
        this.rosterPanelOpenAttempted = true;
        const button = page.locator('[data-tid="roster-button"], #roster-button, button[aria-label^="People" i]').first();
        if (await waitVisible(button, 2000)) {
          await button.click({ timeout: 3000 });
          console.log('[Teams][Roster] Opened roster panel (left open for remaining polls)');
          panelOpen = await page.locator(panelSelector).first()
            .waitFor({ state: 'visible', timeout: 5000 }).then(() => true).catch(() => false);
        }
      }
      if (!panelOpen) return null;

      return await page.evaluate((sel) => {
        const sectionHeader = /^(in this meeting|in the meeting|others invited|waiting in lobby|lobby|suggestions|attendees|presenters|organizers?)\b/i;
        const names: string[] = [];
        for (const panel of document.querySelectorAll(sel)) {
          const rows = panel.querySelectorAll(
            '[data-tid*="roster-participant"], [data-tid^="participantsInCall-"], [role="treeitem"], [role="listitem"]'
          );
          for (const row of rows) {
            // Skip rows under a lobby / "others invited" section
            const group = row.closest('[role="group"], [role="treeitem"][aria-expanded]');
            const groupLabel = group && group !== row ? (group.getAttribute('aria-label') || '') : '';
            if (/lobby|invited|suggest/i.test(groupLabel)) continue;
            if (row.getAttribute('aria-expanded') !== null) continue; // section header

            const titled = row.querySelector('span[title], [data-tid*="display-name"], [data-tid*="participant-name"]');
            const raw = titled?.getAttribute('title')
              || titled?.textContent
              || (row.getAttribute('aria-label') || '').split(',')[0];
            const name = (raw || '').trim();
            if (name && name.length <= 128 && !sectionHeader.test(name)) names.push(name);
          }
        }
        return names;
      }, panelSelector);
    } catch (e) {
      console.log(`[Teams][Roster] Roster panel read failed (non-fatal): ${e}`);
      return null;
    }
  }

  /**
   * Read names from rendered stream tiles of any stream type (camera on or off).
   * Only covers participants the gallery is currently rendering.
   */
  private async readRosterFromTiles(page: Page): Promise<string[]> {
    try {
      return await page.evaluate(() =>
        [...document.querySelectorAll('[data-stream-type][data-tid]')]
          .map(el => (el.getAttribute('data-tid') || '').trim())
          .filter(name => name.length > 0)
      );
    } catch (e) {
      console.log(`[Teams][Roster] Tile read failed (non-fatal): ${e}`);
      return [];
    }
  }

  /**
   * Get the roster timeline for inclusion in recording_complete callback
   */
  getRosterTimeline(): RosterEntry[] {
    return this.rosterEntries;
  }

  /**
   * Build a timestamped active speaker log (ms relative to recordingStartMs)
   * that firm-transcriber matches against diarized segments.
   *
   * Primary source (WI #8031): the in-page CSRC hook (firmSpeechHook), drained
   * every 3s. Works with cameras off. Fallback: DOM polling every 1s (WI
   * #7298) — video-tile selectors only, so it is only recorded until the CSRC
   * hook produces its first interval.
   */
  private _speakerPollCount = 0;
  private _speakerZeroMatchWarned = false;

  startActiveSpeakerPolling(page: Page, recordingStartMs: number): void {
    this._recordingStartMs = recordingStartMs;
    this._speakerPollCount = 0;
    this._speakerZeroMatchWarned = false;
    this._speechDrainCount = 0;
    this._csrcActive = false;
    this._speakerPage = page;
    console.log('[Teams][ActiveSpeaker] Polling started');

    page.evaluate((t) => { (window as any).__firmSpeechStartMs = t; }, recordingStartMs)
      .catch(err => console.log('[Teams][ActiveSpeaker] Could not set speech start time:', err));
    this._speechDrainInterval = setInterval(() => {
      this.drainSpeech(page, false).catch(() => { /* logged inside */ });
    }, 3000);

    this._speakerPollInterval = setInterval(async () => {
      try {
        // DOM fallback only counts until the CSRC hook is producing data
        if (this._csrcActive) return;
        this._speakerPollCount++;
        const result = await page.evaluate(() => {
          // Teams v2 (Fluent 2 / New Teams) active speaker detection strategies:
          // 1. CSS class-based speaking ring (blue border) — most common in v2
          // 2. data-is-speaking attribute on tile
          // 3. aria-label containing ", speaking" (comma-separated format)
          // 4. Presence of speaking indicator element within the tile

          // Strategy 1: Look for CSS classes indicating speaking state
          const speakingClassSelectors = [
            '.fui-VideoTile--speaking',
            '[data-tid="video-tile"].speaking',
            '[data-tid="video-tile"][class*="speaking" i]',
            '[data-tid="video-tile"][class*="active-speaker" i]',
            '.video-tile--speaking',
            '[class*="VideoTile"][class*="speaking" i]',
          ];

          for (const sel of speakingClassSelectors) {
            const el = document.querySelector(sel);
            if (el) {
              const nameEl = el.querySelector('[data-tid="participant-display-name"]') ||
                             el.querySelector('[data-tid*="display-name"]') ||
                             el;
              const name = nameEl.textContent?.trim();
              if (name) return { name, strategy: 'css-class', selector: sel };
            }
          }

          // Strategy 2: Look for speaking indicator element within video tiles
          const indicatorSelectors = [
            '[data-tid="video-tile-speaking-indicator"]',
            '[data-tid*="speaking-indicator"]',
            '[data-tid="video-tile"] [data-tid*="speaking"]',
          ];

          for (const sel of indicatorSelectors) {
            const indicator = document.querySelector(sel);
            if (indicator) {
              // Navigate up to find the parent tile and extract name
              const tile = indicator.closest('[data-tid="video-tile"]') ||
                           indicator.closest('.fui-VideoTile') ||
                           indicator.closest('[data-tid*="video"]');
              if (tile) {
                const nameEl = tile.querySelector('[data-tid="participant-display-name"]') ||
                               tile.querySelector('[data-tid*="display-name"]');
                const name = nameEl?.textContent?.trim();
                if (name) return { name, strategy: 'indicator-element', selector: sel };
              }
            }
          }

          // Strategy 3: data-is-speaking attribute
          const dataAttrSelectors = [
            '[data-tid="video-tile"][data-is-speaking="true"]',
            '[data-tid="calling-roster-cell"][data-is-speaking="true"]',
            '.fui-VideoTile[data-is-speaking="true"]',
            '[data-is-speaking="true"]',
          ];

          for (const sel of dataAttrSelectors) {
            const el = document.querySelector(sel);
            if (el) {
              const nameEl = el.querySelector('[data-tid="participant-display-name"]') ||
                             el.querySelector('[data-tid*="display-name"]') ||
                             el;
              const name = nameEl.textContent?.trim();
              if (name) return { name, strategy: 'data-attr', selector: sel };
            }
          }

          // Strategy 4: aria-label containing ", speaking" (comma-separated format in Teams v2)
          // Format: "Julie Austin, speaking" or "Julie Austin, not speaking"
          const tiles = document.querySelectorAll('[data-tid="video-tile"], .fui-VideoTile');
          for (const tile of tiles) {
            const ariaLabel = tile.getAttribute('aria-label') || '';
            // Match ", speaking" but not ", not speaking"
            if (/,\s*speaking\s*$/i.test(ariaLabel) && !/not\s+speaking/i.test(ariaLabel)) {
              // Extract name: everything before ", speaking"
              const name = ariaLabel.replace(/,\s*speaking\s*$/i, '').trim();
              if (name) return { name, strategy: 'aria-label-comma', selector: 'aria-label' };
            }
          }

          // Strategy 5: Original selectors (legacy fallback)
          const legacySelectors = [
            '[data-tid="video-tile"][aria-label*="speaking"]:not([aria-label*="not speaking" i])',
          ];

          for (const sel of legacySelectors) {
            const el = document.querySelector(sel);
            if (el) {
              const nameEl = el.querySelector('[data-tid="participant-display-name"]') || el;
              const name = nameEl.textContent?.trim();
              // Also try extracting from aria-label if textContent is empty
              if (!name) {
                const ariaLabel = el.getAttribute('aria-label') || '';
                const match = ariaLabel.match(/^([^,]+)/);
                if (match) return { name: match[1].trim(), strategy: 'aria-label-legacy', selector: sel };
              }
              if (name) return { name, strategy: 'legacy', selector: sel };
            }
          }

          // No match — return diagnostic info for debugging
          const tileCount = document.querySelectorAll('[data-tid="video-tile"], .fui-VideoTile').length;
          return { name: null, strategy: 'none', tileCount };
        }).catch(() => ({ name: null, strategy: 'error' }));

        const nowMs = Date.now() - this._recordingStartMs;

        // Diagnostic logging for first few polls with zero matches
        if (!result.name && !this._speakerZeroMatchWarned && this._speakerPollCount <= 5) {
          if (this._speakerPollCount === 1) {
            // Dump DOM diagnostic to help identify correct selectors
            try {
              const domDiag = await page.evaluate(() => {
                const candidates = [
                  '[data-tid*="video"]',
                  '[data-tid*="tile"]',
                  '[class*="VideoTile"]',
                  '[class*="video-tile"]',
                  '[data-tid*="speaking"]',
                  '[data-is-speaking]',
                  '[aria-label*="speaking" i]',
                ];
                const results: Record<string, number> = {};
                for (const sel of candidates) {
                  results[sel] = document.querySelectorAll(sel).length;
                }
                // Sample first aria-label from any video-like element
                const sampleEl = document.querySelector('[data-tid*="video"], [class*="VideoTile"]');
                const sampleAriaLabel = sampleEl?.getAttribute('aria-label') || null;
                const sampleDataTid = sampleEl?.getAttribute('data-tid') || null;
                return { counts: results, sampleAriaLabel, sampleDataTid };
              });
              console.log('[Teams][ActiveSpeaker] DOM diagnostic:', JSON.stringify(domDiag));
            } catch (diagErr) {
              console.log('[Teams][ActiveSpeaker] DOM diagnostic failed:', diagErr);
            }
          }
          const tileCount = (result as any).tileCount;
          console.log(`[Teams][ActiveSpeaker] Poll #${this._speakerPollCount}: no match (strategy: ${result.strategy}, tiles: ${tileCount !== undefined ? tileCount : '?'})`);
          if (this._speakerPollCount === 5) {
            // WI #8031: no early exit on tileCount=0 — camera-off meetings have
            // no tiles, and the CSRC hook is the primary source anyway.
            console.log('[Teams][ActiveSpeaker] 5 DOM polls with zero matches — relying on CSRC hook (DOM fallback keeps polling)');
            this._speakerZeroMatchWarned = true;
          }
        }

        const speakerName = result.name;
        if (speakerName && speakerName !== this._currentSpeaker) {
          if (this._currentSpeakerEntry) this._currentSpeakerEntry.endMs = nowMs;
          this._currentSpeakerEntry = { name: speakerName, startMs: nowMs };
          this._activeSpeakerLog.push(this._currentSpeakerEntry);
          this._currentSpeaker = speakerName;
          console.log(`[Teams][ActiveSpeaker] ${speakerName} at ${nowMs}ms (via ${result.strategy})`);
        }
      } catch { /* ignore poll errors */ }
    }, 1000);
  }

  /**
   * Drain speech intervals buffered by the in-page CSRC hook into
   * _activeSpeakerLog. `final` closes any still-open interval first.
   */
  private async drainSpeech(page: Page, final: boolean): Promise<void> {
    try {
      const { speech, diag } = await page.evaluate(([t, flush]) => {
        const w = window as any;
        // Re-set if the page reloaded mid-meeting (init script reran with no start time)
        if (typeof w.__firmSpeechStartMs !== 'number') w.__firmSpeechStartMs = t;
        if (flush) w.__firmSpeechFlush?.();
        const s = w.__firmSpeech || [];
        w.__firmSpeech = [];
        return {
          speech: s as { name: string; startMs: number; endMs: number }[],
          diag: {
            hookInstalled: !!w.__firmSpeechHookInstalled,
            receiverCount: (w.__firmReceivers || []).length,
            ...(w.__firmSpeechDiag || {}),
          },
        };
      }, [this._recordingStartMs, final] as const);
      this._speechDrainCount++;

      if (speech.length > 0) {
        if (!this._csrcActive) {
          this._csrcActive = true;
          console.log(`[Teams][ActiveSpeaker] CSRC hook producing data — receivers: ${diag.receiverCount}, ` +
            `peerConnections: ${diag.pcCount}, participants: ${diag.participantCount}, ` +
            `sample CSRCs: ${JSON.stringify(diag.sampleCsrcs)}`);
          // Close any open DOM-sourced entry; CSRC takes over from here
          if (this._currentSpeakerEntry && this._currentSpeakerEntry.endMs === undefined) {
            this._currentSpeakerEntry.endMs = Date.now() - this._recordingStartMs;
          }
        }
        this._activeSpeakerLog.push(...speech);
        console.log(`[Teams][ActiveSpeaker] CSRC: ${speech.map(s => `${s.name} ${s.startMs}-${s.endMs}ms`).join(', ')}`);
      } else if (!this._csrcActive && (this._speechDrainCount === 1 || this._speechDrainCount % 20 === 0)) {
        // Hook status in CloudWatch while no data has arrived (first drain, then ~every 60s)
        console.log(`[Teams][ActiveSpeaker] CSRC drain #${this._speechDrainCount}: no intervals yet — ${JSON.stringify(diag)}`);
      }
    } catch (err) {
      console.log(`[Teams][ActiveSpeaker] CSRC drain failed (non-fatal): ${err}`);
    }
  }

  /**
   * Stop active speaker polling, drain the CSRC buffer one last time, and
   * close the open entry
   */
  async stopActiveSpeakerPolling(): Promise<ActiveSpeakerEntry[]> {
    if (this._speakerPollInterval) {
      clearInterval(this._speakerPollInterval);
      this._speakerPollInterval = null;
    }
    if (this._speechDrainInterval) {
      clearInterval(this._speechDrainInterval);
      this._speechDrainInterval = null;
      if (this._speakerPage && !this._speakerPage.isClosed()) {
        await this.drainSpeech(this._speakerPage, true);
      }
    }
    const nowMs = Date.now() - this._recordingStartMs;
    if (this._currentSpeakerEntry && this._currentSpeakerEntry.endMs === undefined) {
      this._currentSpeakerEntry.endMs = nowMs;
    }
    this._activeSpeakerLog.sort((a, b) => a.startMs - b.startMs);
    console.log(`[Teams][ActiveSpeaker] Polling stopped (${this._activeSpeakerLog.length} entries, source: ${this._csrcActive ? 'csrc' : 'dom'})`);
    return this._activeSpeakerLog;
  }

  /**
   * Get the active speaker log for inclusion in recording_complete callback
   */
  getActiveSpeakerLog(): ActiveSpeakerEntry[] {
    return this._activeSpeakerLog;
  }
}
