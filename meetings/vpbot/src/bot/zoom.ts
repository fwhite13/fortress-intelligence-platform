/**
 * Zoom specific join logic
 */

import { Page } from 'playwright';
import * as fs from 'fs';
import * as path from 'path';

const SCREENSHOTS_DIR = process.env.SCREENSHOTS_DIR || '/tmp/screenshots';
if (!fs.existsSync(SCREENSHOTS_DIR)) {
  fs.mkdirSync(SCREENSHOTS_DIR, { recursive: true });
}

export class ZoomHandler {
  /**
   * Save a debug screenshot.
   */
  private static async screenshot(page: Page, label: string): Promise<void> {
    try {
      const filename = `zoom-${label}-${Date.now()}.png`;
      const filepath = path.join(SCREENSHOTS_DIR, filename);
      await page.screenshot({ path: filepath, fullPage: true });
      console.log(`[Zoom] Screenshot saved: ${filename}`);
    } catch (e) {
      console.log(`[Zoom] Screenshot failed: ${e}`);
    }
  }

  /**
   * Join a Zoom meeting via web browser
   */
  static async join(page: Page, botName: string): Promise<void> {
    console.log('[Zoom] Starting join flow...');

    // Wait for page to load
    await page.waitForLoadState('networkidle').catch(() => {});
    await this.screenshot(page, '01-initial-page');

    // Check for bot-detection block before attempting to join
    const isBlocked = await page.evaluate(() => {
      const text = document.body.innerText;
      return text.includes('Automated bots') ||
             text.includes('bots aren\'t allowed') ||
             text.includes('sign in to join');
    });
    if (isBlocked) {
      await this.screenshot(page, '01b-bot-blocked');
      throw new Error('[Zoom] Bot detection wall encountered on initial page load — join aborted');
    }

    // The initial page load is just an app-launch spinner ("Don't have the Zoom
    // Workplace app installed?") for a few seconds before the real app-chooser
    // UI ("Join from Zoom Workplace app" / "Join from browser" buttons) paints.
    // Searching for the browser-join button before the chooser renders was a
    // false negative, not a real absence — it made the whole click loop below
    // silently no-op and fall through to a later step that mis-clicked the
    // wrong button. Wait for the chooser text to actually appear first.
    try {
      await page.waitForFunction(() => {
        const text = document.body.innerText;
        return text.includes('Join from browser') || text.includes('Join from Zoom Workplace app');
      }, { timeout: 15000 });
    } catch {
      console.log('[Zoom] App-chooser page did not render within 15s — proceeding anyway');
    }

    // Dismiss the cookie consent banner early — Zoom's consent manager uses
    // event capture at the document level that can intercept clicks on other
    // elements (including the browser-join button) until dismissed. Try several
    // selector patterns since the banner comes from a third-party CMP and the
    // exact markup varies.
    try {
      const cookieDismissSelectors = [
        // Generic close/accept patterns
        'button[id*="accept"]', 'button[class*="accept"]',
        'button[id*="close"]',  'button[class*="close"]',
        'button[aria-label="Close"]', 'button[aria-label="close"]',
        // OneTrust
        '#onetrust-accept-btn-handler',
        // TrustArc / Truste
        '.truste_popclose', '.truste_overlay',
        // Zoom-specific cookie banner close (✕ button, bottom-left banner)
        '.coi-banner__close',
        // Fallback: any visible button whose text is Accept/Close/×
        'button:has-text("Accept")', 'button:has-text("Accept All")',
      ];
      for (const sel of cookieDismissSelectors) {
        try {
          const btn = page.locator(sel).first();
          if (await btn.isVisible({ timeout: 500 })) {
            await btn.click({ force: true });
            console.log(`[Zoom] Dismissed cookie/consent banner via: ${sel}`);
            await page.waitForTimeout(300);
            break;
          }
        } catch { continue; }
      }
    } catch {
      // No cookie banner present, or dismissal failed — continue
    }

    // Dismiss the "Did not open Zoom Workplace app?" tooltip if present — it can
    // overlay/intercept clicks on the real buttons underneath it.
    try {
      const dismissTooltip = page.locator('[aria-label="Close"], button:has-text("×")').first();
      if (await dismissTooltip.isVisible({ timeout: 1500 })) {
        await dismissTooltip.click();
        console.log('[Zoom] Dismissed app-launch tooltip');
        await page.waitForTimeout(500);
      }
    } catch {
      // No tooltip present, fine
    }

    // Primary strategy: extract the browser-join href from the link/button and
    // navigate directly via page.goto(). This completely bypasses any event
    // interception issues (cookie consent capture, JS click handlers that don't
    // fire on synthetic events, etc). Zoom's "Join from browser" element is
    // typically an <a> with an href pointing straight at the web client URL
    // (https://app.zoom.us/wc/{meetingId}/join?...).
    let clickedBrowserJoin = false;

    const browserJoinHref: string | null = await page.evaluate(() => {
      const candidates = [
        ...Array.from(document.querySelectorAll('a')),
        ...Array.from(document.querySelectorAll('button')),
      ] as (HTMLAnchorElement | HTMLButtonElement)[];
      for (const el of candidates) {
        const text = (el.textContent || '').trim();
        if (text === 'Join from browser' || text === 'Join from Your Browser') {
          return (el as HTMLAnchorElement).href || null;
        }
      }
      return null;
    });

    if (browserJoinHref) {
      console.log(`[Zoom] Navigating directly to browser-join URL (bypass click): ${browserJoinHref}`);
      await page.goto(browserJoinHref, { waitUntil: 'domcontentloaded', timeout: 30000 });
      clickedBrowserJoin = true;
    } else {
      // Fallback: click-based approach with precise selectors.
      // IMPORTANT: use precise button/link selectors, not generic "contains 'browser'"
      // text matches — the page can contain other elements with that word (e.g. a
      // "supported browsers" footer link) that a loose selector will match instead
      // of the real join button, silently clicking the wrong thing.
      const browserJoinSelectors = [
        'button:has-text("Join from browser")',
        'a:has-text("Join from browser")',
        'button:has-text("Join from Your Browser")',
        'a:has-text("Join from Your Browser")',
        'text="Join from Your Browser"',
        'text="Join from browser"',
      ];

      // Try up to 3 rounds spaced out — the chooser buttons can still be mid-render
      // (fading in / attaching handlers) right after the text check above passes.
      for (let attempt = 0; attempt < 3 && !clickedBrowserJoin; attempt++) {
        if (attempt > 0) {
          await page.waitForTimeout(1500);
        }
        for (const sel of browserJoinSelectors) {
          try {
            const button = page.locator(sel).first();
            if (await button.isVisible({ timeout: 3000 })) {
              await button.click({ force: true });
              console.log(`[Zoom] Clicked browser-join button via selector: ${sel} (attempt ${attempt + 1})`);
              clickedBrowserJoin = true;
              break;
            }
          } catch {
            continue;
          }
        }
      }
    }

    if (!clickedBrowserJoin) {
      await this.screenshot(page, '01c-no-browser-join-link');
      console.log('[Zoom] No precise browser-join selector matched after retries');
      throw new Error('[Zoom] Never found/clicked a browser-join button — refusing to proceed past app-chooser screen');
    }

    // Verify we actually left the app-chooser screen.
    await page.waitForTimeout(1500);
    const stillOnChooser = await page.evaluate(() => {
      const text = document.body.innerText;
      return text.includes('Join from Zoom Workplace app') && text.includes('Join from browser');
    });
    if (stillOnChooser) {
      console.log('[Zoom] Warning: still on app-chooser screen after navigation/click');
      await this.screenshot(page, '01d-chooser-still-visible-after-click');

      // Last-ditch: try a JS-level evaluate click on the element to bypass any
      // remaining event interception.
      try {
        const navigated = await page.evaluate(() => {
          const candidates = [
            ...Array.from(document.querySelectorAll('a')),
            ...Array.from(document.querySelectorAll('button')),
          ] as (HTMLAnchorElement | HTMLButtonElement)[];
          for (const el of candidates) {
            const text = (el.textContent || '').trim();
            if (text === 'Join from browser' || text === 'Join from Your Browser') {
              const href = (el as HTMLAnchorElement).href;
              if (href) { window.location.href = href; return true; }
              el.click();
              return true;
            }
          }
          return false;
        });
        if (navigated) {
          console.log('[Zoom] Retried via JS evaluate click/navigate');
          await page.waitForTimeout(2000);
        }
      } catch {
        console.log('[Zoom] JS evaluate retry failed');
      }

      const stillStuckAfterRetry = await page.evaluate(() => {
        const text = document.body.innerText;
        return text.includes('Join from Zoom Workplace app') && text.includes('Join from browser');
      });
      if (stillStuckAfterRetry) {
        await this.screenshot(page, '01e-stuck-after-retry');
        throw new Error('[Zoom] Stuck on app-chooser screen after all retry strategies — browser-join click never advanced the page');
      }
    }

    // Wait for web client to load
    await page.waitForTimeout(3000);
    await this.screenshot(page, '02-pre-join-screen');

    // Check for bot-detection block after clicking browser join
    const isBlockedAfterBrowserJoin = await page.evaluate(() => {
      const text = document.body.innerText;
      return text.includes('Automated bots') ||
             text.includes('bots aren\'t allowed') ||
             text.includes('sign in to join');
    });
    if (isBlockedAfterBrowserJoin) {
      await this.screenshot(page, '02b-bot-blocked-post-join');
      throw new Error('[Zoom] Bot detection wall encountered after browser-join click — join aborted');
    }

    // Enter name
    try {
      const nameSelectors = [
        '#inputname',
        'input[placeholder*="name"]',
        'input[aria-label*="name"]',
        'input[type="text"]',
      ];

      let nameEntered = false;
      for (const selector of nameSelectors) {
        try {
          const nameInput = page.locator(selector).first();
          if (await nameInput.isVisible({ timeout: 2000 })) {
            await nameInput.clear();
            await nameInput.fill(botName);
            console.log(`[Zoom] Entered name: ${botName}`);
            nameEntered = true;
            break;
          }
        } catch {
          continue;
        }
      }
      if (!nameEntered) {
        console.log('[Zoom] Warning: could not find name input field');
        await this.screenshot(page, '02c-no-name-input');
      }
    } catch (error) {
      console.log('[Zoom] Could not find name input');
    }

    // Handle "I agree" checkbox if present
    try {
      const agreeCheckbox = page.locator('input[type="checkbox"]').first();
      if (await agreeCheckbox.isVisible({ timeout: 2000 })) {
        await agreeCheckbox.check();
        console.log('[Zoom] Checked agreement checkbox');
      }
    } catch {
      // Checkbox not present
    }

    // Turn off audio/video before joining
    await this.turnOffDevices(page);

    // Click Join button
    await this.screenshot(page, '03-before-join-click');
    let joinClicked = false;
    try {
      // Exact-text/role matches first, then a hasText fallback that explicitly
      // excludes "Join from Zoom Workplace app" — that button can still be present
      // in the DOM (even if we believe we've left the chooser screen) and a bare
      // "has-text('Join')" match grabs it first since it's earlier in the DOM,
      // silently deep-linking to the native app instead of joining in-browser.
      const joinLocators = [
        page.getByRole('button', { name: 'Join', exact: true }),
        page.getByRole('button', { name: 'Join Meeting', exact: true }),
        page.locator('#joinBtn'),
        page.locator('button[type="submit"]'),
        page.locator('button:has-text("Join")').filter({ hasNotText: 'Workplace app' }),
      ];

      for (const joinButton of joinLocators) {
        try {
          const button = joinButton.first();
          if (await button.isVisible({ timeout: 2000 })) {
            await button.click();
            console.log('[Zoom] Clicked join button');
            joinClicked = true;
            break;
          }
        } catch {
          continue;
        }
      }
    } catch (error) {
      await this.screenshot(page, '03b-no-join-button');
      console.log('[Zoom] Could not find join button');
      throw new Error('Failed to find Zoom join button');
    }

    if (!joinClicked) {
      await this.screenshot(page, '03b-no-join-button');
      throw new Error('[Zoom] No join button found — cannot proceed');
    }

    // Wait for meeting to start
    await page.waitForTimeout(5000);
    await this.screenshot(page, '04-post-join-attempt');

    // Check for bot-detection block after clicking join (the key failure point)
    const isBlockedAfterJoin = await page.evaluate(() => {
      const text = document.body.innerText;
      return text.includes('Automated bots') ||
             text.includes('bots aren\'t allowed') ||
             text.includes('sign in to join');
    });
    if (isBlockedAfterJoin) {
      await this.screenshot(page, '04b-bot-blocked-post-join-click');
      throw new Error('[Zoom] Bot detection wall encountered after clicking Join — join aborted');
    }

    // Handle waiting room
    const inWaitingRoom = await page.evaluate(() => {
      return document.body.innerText.includes('waiting room') ||
             document.body.innerText.includes('Please wait') ||
             document.body.innerText.includes('host will let you in');
    });

    if (inWaitingRoom) {
      await this.screenshot(page, '04c-waiting-room');
      console.log('[Zoom] In waiting room, waiting to be admitted...');
    }

    // Check for successful join
    try {
      // Look for meeting controls
      await page.waitForSelector('.meeting-app, .meeting-client, [class*="meeting"]', { timeout: 60000 });
      await this.screenshot(page, '05-in-meeting');
      console.log('[Zoom] Successfully joined meeting');
    } catch {
      const inMeeting = await page.evaluate(() => {
        return document.body.innerText.includes('Mute') ||
               document.body.innerText.includes('Leave') ||
               document.body.innerText.includes('Participants');
      });

      if (inMeeting) {
        await this.screenshot(page, '05-in-meeting-alt-check');
        console.log('[Zoom] Successfully joined meeting (alternative check)');
      } else {
        await this.screenshot(page, '05-uncertain-state');
        console.log('[Zoom] Meeting join status uncertain, continuing...');
      }
    }
  }

  /**
   * Turn off audio and video
   */
  private static async turnOffDevices(page: Page): Promise<void> {
    try {
      // Look for audio/video toggles on pre-join screen
      const muteAudio = page.locator('button:has-text("Mute"), button[aria-label*="audio"]').first();
      if (await muteAudio.isVisible({ timeout: 1000 })) {
        await muteAudio.click();
        console.log('[Zoom] Muted audio');
      }

      const stopVideo = page.locator('button:has-text("Stop Video"), button[aria-label*="video"]').first();
      if (await stopVideo.isVisible({ timeout: 1000 })) {
        await stopVideo.click();
        console.log('[Zoom] Stopped video');
      }
    } catch (error) {
      console.log('[Zoom] Could not toggle devices');
    }
  }
}
