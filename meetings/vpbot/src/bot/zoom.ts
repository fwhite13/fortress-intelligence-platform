/**
 * Zoom specific join logic
 */

import { Page } from 'playwright';

export class ZoomHandler {
  /**
   * Join a Zoom meeting via web browser
   */
  static async join(page: Page, botName: string): Promise<void> {
    console.log('[Zoom] Starting join flow...');

    // Wait for page to load
    await page.waitForLoadState('networkidle').catch(() => {});

    // Zoom often redirects to app download - look for "Join from Your Browser"
    try {
      const joinFromBrowser = page.locator('text="Join from Your Browser"');
      await joinFromBrowser.waitFor({ timeout: 10000 });
      await joinFromBrowser.click();
      console.log('[Zoom] Clicked "Join from Your Browser"');
    } catch {
      console.log('[Zoom] "Join from Your Browser" not found, trying alternatives...');
      
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
            break;
          }
        } catch {
          continue;
        }
      }
    }

    // Wait for web client to load
    await page.waitForTimeout(3000);

    // Enter name
    try {
      const nameSelectors = [
        '#inputname',
        'input[placeholder*="name"]',
        'input[aria-label*="name"]',
        'input[type="text"]',
      ];

      for (const selector of nameSelectors) {
        try {
          const nameInput = page.locator(selector).first();
          if (await nameInput.isVisible({ timeout: 2000 })) {
            await nameInput.clear();
            await nameInput.fill(botName);
            console.log(`[Zoom] Entered name: ${botName}`);
            break;
          }
        } catch {
          continue;
        }
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
            break;
          }
        } catch {
          continue;
        }
      }
    } catch (error) {
      console.log('[Zoom] Could not find join button');
      throw new Error('Failed to find Zoom join button');
    }

    // Wait for meeting to start
    await page.waitForTimeout(5000);

    // Handle waiting room
    const inWaitingRoom = await page.evaluate(() => {
      return document.body.innerText.includes('waiting room') ||
             document.body.innerText.includes('Please wait') ||
             document.body.innerText.includes('host will let you in');
    });

    if (inWaitingRoom) {
      console.log('[Zoom] In waiting room, waiting to be admitted...');
    }

    // Check for successful join
    try {
      // Look for meeting controls
      await page.waitForSelector('.meeting-app, .meeting-client, [class*="meeting"]', { timeout: 60000 });
      console.log('[Zoom] Successfully joined meeting');
    } catch {
      const inMeeting = await page.evaluate(() => {
        return document.body.innerText.includes('Mute') ||
               document.body.innerText.includes('Leave') ||
               document.body.innerText.includes('Participants');
      });

      if (inMeeting) {
        console.log('[Zoom] Successfully joined meeting (alternative check)');
      } else {
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
