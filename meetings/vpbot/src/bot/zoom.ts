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

    // Zoom often redirects to app download - look for "Join from Your Browser"
    let clickedBrowserJoin = false;
    try {
      const joinFromBrowser = page.locator('text="Join from Your Browser"');
      await joinFromBrowser.waitFor({ timeout: 10000 });
      await joinFromBrowser.click();
      console.log('[Zoom] Clicked "Join from Your Browser"');
      clickedBrowserJoin = true;
    } catch {
      console.log('[Zoom] "Join from Your Browser" not found, trying alternatives...');
      await this.screenshot(page, '01c-no-browser-join-link');

      // Alternative text variations
      const alternatives = [
        'text="join from your browser"',
        'a:has-text("browser")',
        'text="Join from browser"',
      ];

      for (const alt of alternatives) {
        try {
          const button = page.locator(alt).first();
          if (await button.isVisible({ timeout: 2000 })) {
            await button.click();
            console.log('[Zoom] Clicked browser join alternative');
            clickedBrowserJoin = true;
            break;
          }
        } catch {
          continue;
        }
      }
    }

    if (!clickedBrowserJoin) {
      await this.screenshot(page, '01d-no-browser-join-found');
      console.log('[Zoom] Warning: could not find any browser-join link');
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
      const joinSelectors = [
        'button:has-text("Join")',
        '#joinBtn',
        'button[type="submit"]',
        'button:has-text("Join Meeting")',
      ];

      for (const selector of joinSelectors) {
        try {
          const joinButton = page.locator(selector).first();
          if (await joinButton.isVisible({ timeout: 2000 })) {
            await joinButton.click();
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
