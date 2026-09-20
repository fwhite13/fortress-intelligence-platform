/**
 * Zoom specific join logic
 */

import { Page, FrameLocator, Locator } from 'playwright';
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

    // The outer page is the Zoom PWA shell (app.zoom.us React app). The
    // actual pre-join form — name input, Join button, checkboxes — renders
    // inside an <iframe> loaded by the PWA. page.locator() doesn't cross
    // iframe boundaries; use page.frameLocator('iframe') for all form
    // interactions. page.evaluate() / waitForTimeout still use the page.
    await page.waitForTimeout(1000);
    try {
      await page.waitForSelector('iframe', { timeout: 15000 });
      console.log('[Zoom] Web client iframe appeared');
    } catch {
      console.log('[Zoom] Warning: iframe not found within 15s');
    }
    const wcFrame = page.frameLocator('iframe');
    await this.screenshot(page, '02-pre-join-screen');

    // Check for bot-detection block (page-level redirect — main frame)
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

    // Wait for the pre-join form to render inside the iframe.
    // The iframe starts as a #wc-loading spinner; wait for an input to appear.
    try {
      await wcFrame.locator('input').first().waitFor({ timeout: 15000 });
      console.log('[Zoom] Pre-join form ready in iframe');
    } catch {
      console.log('[Zoom] Warning: pre-join form input not found in iframe within 15s');
      await this.screenshot(page, '02c-iframe-form-timeout');
    }

    // Enter name (inside iframe)
    try {
      const nameSelectors = [
        '#inputname',
        'input[placeholder*="name" i]',
        'input[aria-label*="name" i]',
        'input[type="text"]',
      ];
      let nameEntered = false;
      for (const selector of nameSelectors) {
        try {
          const nameInput = wcFrame.locator(selector).first();
          if (await nameInput.isVisible({ timeout: 3000 })) {
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
        console.log('[Zoom] Warning: could not find name input field in iframe');
        await this.screenshot(page, '02d-no-name-input');
      }
    } catch (error) {
      console.log('[Zoom] Could not enter name in iframe');
    }

    // Handle "Remember my name" / "I agree" checkbox if present (inside iframe)
    try {
      const agreeCheckbox = wcFrame.locator('input[type="checkbox"]').first();
      if (await agreeCheckbox.isVisible({ timeout: 1500 })) {
        await agreeCheckbox.check();
        console.log('[Zoom] Checked agreement checkbox');
      }
    } catch {
      // Not present
    }

    // Turn off audio/video before joining (inside iframe)
    await this.turnOffDevices(wcFrame);

    // Click Join button (inside iframe)
    await this.screenshot(page, '03-before-join-click');
    let joinClicked = false;
    let clickedJoinLocator: Locator | null = null;
    try {
      const joinLocators = [
        wcFrame.getByRole('button', { name: 'Join', exact: true }),
        wcFrame.getByRole('button', { name: 'Join Meeting', exact: true }),
        wcFrame.locator('#joinBtn'),
        wcFrame.locator('button[type="submit"]'),
        wcFrame.locator('button:has-text("Join")'),
      ];
      for (const joinButton of joinLocators) {
        try {
          const button = joinButton.first();
          if (await button.isVisible({ timeout: 2000 })) {
            await button.click();
            console.log('[Zoom] Clicked join button');
            joinClicked = true;
            clickedJoinLocator = button;
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

    // Verify the click actually took effect. A click can be logged as fired
    // (Playwright reports the event dispatched) while the page still doesn't
    // navigate — event interception, a stale handler, etc. The real signal
    // is whether the pre-join name input is still present: if it is, we
    // never left the pre-join form no matter what the click log says.
    const nameInputSelectors = ['#inputname', 'input[placeholder*="name" i]', 'input[type="text"]'];
    const isNameInputVisible = async (): Promise<boolean> => {
      for (const selector of nameInputSelectors) {
        try {
          if (await wcFrame.locator(selector).first().isVisible()) {
            return true;
          }
        } catch {
          continue;
        }
      }
      return false;
    };
    const pollUntilPreJoinGone = async (timeoutMs: number): Promise<boolean> => {
      const start = Date.now();
      while (Date.now() - start < timeoutMs) {
        if (!(await isNameInputVisible())) return true;
        await page.waitForTimeout(500);
      }
      return !(await isNameInputVisible());
    };

    console.log('[Zoom] Verifying join click took effect — waiting up to 10s for pre-join name input to disappear...');
    let leftPreJoin = await pollUntilPreJoinGone(10000);

    if (!leftPreJoin) {
      console.log('[Zoom] Retry 1: name input still visible after 10s — join click had no effect. Retrying with force click...');
      if (clickedJoinLocator) {
        try {
          await clickedJoinLocator.click({ force: true });
          console.log('[Zoom] Retry 1: force-clicked join button');
        } catch (e) {
          console.log(`[Zoom] Retry 1: force click threw: ${e}`);
        }
      } else {
        console.log('[Zoom] Retry 1: no join button locator retained from first pass — cannot force-click');
      }
      leftPreJoin = await pollUntilPreJoinGone(5000);
    }

    if (!leftPreJoin) {
      console.log('[Zoom] Retry 2: name input still visible after force click — dispatching click event directly via evaluate()...');
      try {
        if (clickedJoinLocator) {
          await clickedJoinLocator.evaluate((el) => {
            el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
          });
          console.log('[Zoom] Retry 2: dispatched click event via evaluate() on join button element');
        } else {
          console.log('[Zoom] Retry 2: no join button locator retained from first pass — cannot evaluate() click');
        }
      } catch (e) {
        console.log(`[Zoom] Retry 2: evaluate() click dispatch threw: ${e}`);
      }
      leftPreJoin = await pollUntilPreJoinGone(5000);
    }

    if (!leftPreJoin) {
      await this.screenshot(page, '03c-join-click-no-effect');
      throw new Error('[Zoom] Join button click had no effect after retries — pre-join form still visible');
    }

    console.log('[Zoom] Confirmed: pre-join name input is gone — join click took effect');

    // Wait for meeting to start
    await page.waitForTimeout(5000);
    await this.screenshot(page, '04-post-join-attempt');

    // Check for bot-detection block after clicking join (main frame)
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

    // Handle waiting room — check iframe content
    const wcFrameObj = page.frames().find(f => f !== page.mainFrame());
    const waitingRoomText = wcFrameObj
      ? await wcFrameObj.evaluate(() => document.body.innerText).catch(() => '')
      : '';
    const inWaitingRoom = waitingRoomText.includes('waiting room') ||
                          waitingRoomText.includes('Please wait') ||
                          waitingRoomText.includes('host will let you in');
    if (inWaitingRoom) {
      await this.screenshot(page, '04c-waiting-room');
      console.log('[Zoom] In waiting room, waiting to be admitted...');
    }

    // Check for successful join — meeting controls render inside the iframe
    try {
      await wcFrame.locator('.meeting-app, .meeting-client, [class*="meeting"]').first().waitFor({ timeout: 60000 });
      await this.screenshot(page, '05-in-meeting');
      console.log('[Zoom] Successfully joined meeting');
      // Send chat announcement if configured (WI #7258)
      await this.sendChatAnnouncement(page, wcFrame);
    } catch {
      const inMeetingText = wcFrameObj
        ? await wcFrameObj.evaluate(() => document.body.innerText).catch(() => '')
        : await page.evaluate(() => document.body.innerText);

      // Diagnostics up front — enough to root-cause future failures from
      // CloudWatch alone, without needing screenshots.
      const stillOnPreJoin = await isNameInputVisible();
      const currentUrl = page.url();
      console.log(
        `[Zoom] Join result diagnostics — name input still visible: ${stillOnPreJoin}, ` +
        `iframe text (first 200 chars): ${JSON.stringify(inMeetingText.slice(0, 200))}, ` +
        `page URL: ${currentUrl}`
      );

      // "Mute" is also a button label on the pre-join page itself (the
      // muted-mic toggle), so it is NOT a valid in-meeting indicator on its
      // own — checking it caused false positives whenever the join click
      // failed silently and the bot never left the pre-join screen. The only
      // reliable signal is whether the pre-join name input is actually gone.
      if (stillOnPreJoin) {
        await this.screenshot(page, '05-still-on-prejoin');
        console.log('[Zoom] FAILED: Still on pre-join page after join click — name input still visible');
        throw new Error('[Zoom] FAILED: Still on pre-join page after join click — name input still visible');
      }

      const hasLeaveControl = /\bLeave\b/.test(inMeetingText);
      const hasParticipants = inMeetingText.includes('Participants');
      const hasMeetingControlArea = inMeetingText.includes('Meeting Controls') || inMeetingText.includes('meeting control');

      if (hasLeaveControl || hasParticipants || hasMeetingControlArea) {
        await this.screenshot(page, '05-in-meeting-alt-check');
        console.log('[Zoom] Successfully joined meeting (alternative check)');
        // Send chat announcement if configured (WI #7258)
        await this.sendChatAnnouncement(page, wcFrame);
      } else {
        // Pre-join form is confirmed gone (checked above) but no in-meeting
        // indicator matched either — genuinely uncertain, not a pre-join
        // false positive. Safe to continue past this point.
        await this.screenshot(page, '05-uncertain-state');
        console.log('[Zoom] Meeting join status uncertain, continuing...');
      }
    }
  }

  /**
   * Turn off audio and video before joining (operates inside iframe context)
   */
  private static async turnOffDevices(ctx: Page | FrameLocator): Promise<void> {
    try {
      const muteAudio = ctx.locator('button:has-text("Mute"), button[aria-label*="audio" i]').first();
      if (await muteAudio.isVisible({ timeout: 1000 })) {
        await muteAudio.click();
        console.log('[Zoom] Muted audio');
      }
      const stopVideo = ctx.locator('button:has-text("Stop Video"), button[aria-label*="video" i]').first();
      if (await stopVideo.isVisible({ timeout: 1000 })) {
        await stopVideo.click();
        console.log('[Zoom] Stopped video');
      }
    } catch {
      console.log('[Zoom] Could not toggle devices');
    }
  }

  /**
   * Send chat announcement to meeting participants (WI #7258).
   * Non-fatal — a failed chat post must never fail the recording.
   */
  private static async sendChatAnnouncement(page: Page, wcFrame: FrameLocator): Promise<void> {
    try {
      const announceName = process.env.BOT_CHAT_ANNOUNCE_NAME || '';
      if (!announceName) {
        console.log('[Zoom] BOT_CHAT_ANNOUNCE_NAME not set — skipping chat announcement');
        return;
      }

      const message = `I'm here to take notes for ${announceName}. I'll send a summary when the meeting ends.`;
      console.log('[Zoom] Sending chat announcement...');

      // Wait a bit for the meeting to stabilize
      await page.waitForTimeout(2000);

      // Step 1: Open chat panel — button is in the main Zoom PWA frame, not the iframe
      const chatButtonSelectors = [
        'button[aria-label="Chat"]',
        'button[aria-label*="chat" i]',
        'button:has-text("Chat")',
        '[data-tooltip*="Chat" i]',
      ];
      let openedChat = false;
      for (const selector of chatButtonSelectors) {
        try {
          const btn = page.locator(selector).first();
          if (await btn.isVisible({ timeout: 3000 })) {
            await btn.click();
            console.log(`[Zoom] Opened chat panel via: ${selector}`);
            openedChat = true;
            await page.waitForTimeout(1000);
            break;
          }
        } catch {
          continue;
        }
      }
      if (!openedChat) {
        console.log('[Zoom] WARNING: could not find chat button — skipping chat announcement (non-fatal)');
        return;
      }

      // Step 2: Find chat input — chat UI renders inside the iframe
      const chatInputSelectors = [
        'textarea[placeholder*="Type message" i]',
        'textarea[aria-label*="Type message" i]',
        'textarea[placeholder*="chat" i]',
        'div[contenteditable="true"][aria-label*="message" i]',
        'textarea',
      ];
      let chatInput: Locator | null = null;
      for (const selector of chatInputSelectors) {
        try {
          const el = wcFrame.locator(selector).first();
          if (await el.isVisible({ timeout: 5000 })) {
            chatInput = el;
            console.log(`[Zoom] Found chat input via: ${selector}`);
            break;
          }
        } catch {
          continue;
        }
      }
      if (!chatInput) {
        console.log('[Zoom] WARNING: could not find chat input — skipping chat announcement (non-fatal)');
        return;
      }

      // Step 3: Type and send message
      await chatInput.click();
      await chatInput.fill(message);
      await page.waitForTimeout(500);

      // Try to find and click send button
      const sendButtonSelectors = [
        'button[aria-label="Send"]',
        'button[aria-label*="send" i]',
        'button:has-text("Send")',
      ];
      let sentViaButton = false;
      for (const selector of sendButtonSelectors) {
        try {
          const btn = wcFrame.locator(selector).first();
          if (await btn.isVisible({ timeout: 2000 })) {
            await btn.click();
            console.log(`[Zoom] Chat announcement sent via button: ${message}`);
            sentViaButton = true;
            break;
          }
        } catch {
          continue;
        }
      }

      // Fallback: press Enter
      if (!sentViaButton) {
        await chatInput.press('Enter');
        console.log(`[Zoom] Chat announcement sent via Enter: ${message}`);
      }
    } catch (err) {
      console.log(`[Zoom] Chat announcement failed (non-fatal): ${err}`);
    }
  }
}
