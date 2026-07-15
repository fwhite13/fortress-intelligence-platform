/**
 * Google Meet specific join logic
 */
import * as fs from 'fs';
import * as path from 'path';
const SCREENSHOTS_DIR = process.env.SCREENSHOTS_DIR || '/tmp/screenshots';
if (!fs.existsSync(SCREENSHOTS_DIR)) {
    fs.mkdirSync(SCREENSHOTS_DIR, { recursive: true });
}
export class GoogleMeetHandler {
    /**
     * Save a debug screenshot.
     */
    static async screenshot(page, label) {
        try {
            const filename = `meet-${label}-${Date.now()}.png`;
            const filepath = path.join(SCREENSHOTS_DIR, filename);
            await page.screenshot({ path: filepath, fullPage: true });
            console.log(`[Google Meet] Screenshot saved: ${filename}`);
        }
        catch (e) {
            console.log(`[Google Meet] Screenshot failed: ${e}`);
        }
    }
    /**
     * Check the page for known Google Meet block/rejection messages.
     * Meet doesn't publicly document a bot-detection wall the way Zoom does,
     * but it does reject anonymous/unusual callers with explicit text —
     * capture that here so we can tell "blocked" apart from "still waiting
     * for host to admit".
     */
    static async checkForBlock(page) {
        return await page.evaluate(() => {
            const text = document.body.innerText;
            const blockPhrases = [
                "You can't join this video call",
                'This video call cannot be joined',
                "couldn't join",
                'denied entry',
                'Access denied',
            ];
            const match = blockPhrases.find((p) => text.includes(p));
            return match ?? null;
        });
    }
    /**
     * Join a Google Meet meeting
     */
    static async join(page, botName) {
        console.log('[Google Meet] Starting join flow...');
        // Wait for page to load
        await page.waitForLoadState('networkidle').catch(() => { });
        await page.waitForTimeout(2000);
        await this.screenshot(page, '01-initial-page');
        const initialBlock = await this.checkForBlock(page);
        if (initialBlock) {
            await this.screenshot(page, '01b-blocked');
            throw new Error(`[Google Meet] Blocked on initial page load: "${initialBlock}"`);
        }
        // Google Meet might show different screens based on login state
        // For guest access, we need to enter a name
        // Check if we need to enter a name (guest mode)
        try {
            const nameInput = page.locator('input[placeholder="Your name"]');
            if (await nameInput.isVisible({ timeout: 5000 })) {
                await nameInput.clear();
                await nameInput.fill(botName);
                console.log(`[Google Meet] Entered name: ${botName}`);
            }
        }
        catch {
            console.log('[Google Meet] Name input not found, might be logged in');
        }
        await this.screenshot(page, '02-pre-join-screen');
        // Turn off camera and microphone before joining
        await this.turnOffDevices(page);
        // Look for "Ask to join" or "Join now" button
        await this.screenshot(page, '03-before-join-click');
        try {
            const joinSelectors = [
                'button:has-text("Ask to join")',
                'button:has-text("Join now")',
                'button:has-text("Join")',
                '[data-mdc-dialog-action="join"]',
                'button[jsname="Qx7uuf"]',
            ];
            let joined = false;
            for (const selector of joinSelectors) {
                try {
                    const joinButton = page.locator(selector).first();
                    if (await joinButton.isVisible({ timeout: 2000 })) {
                        await joinButton.click();
                        console.log('[Google Meet] Clicked join button');
                        joined = true;
                        break;
                    }
                }
                catch {
                    continue;
                }
            }
            if (!joined) {
                throw new Error('Could not find join button');
            }
        }
        catch (error) {
            await this.screenshot(page, '03b-no-join-button');
            console.log('[Google Meet] Could not find join button');
            throw new Error('Failed to find Google Meet join button');
        }
        // Wait for meeting to start
        await page.waitForTimeout(5000);
        await this.screenshot(page, '04-post-join-attempt');
        const postJoinBlock = await this.checkForBlock(page);
        if (postJoinBlock) {
            await this.screenshot(page, '04b-blocked-post-join');
            throw new Error(`[Google Meet] Blocked after clicking join: "${postJoinBlock}"`);
        }
        // Handle waiting room / admission request
        const waitingForAdmission = await page.evaluate(() => {
            return document.body.innerText.includes('Asking to be let in') ||
                document.body.innerText.includes('waiting') ||
                document.body.innerText.includes('Someone will let you in soon');
        });
        if (waitingForAdmission) {
            await this.screenshot(page, '04c-waiting-for-admission');
            console.log('[Google Meet] Waiting to be admitted...');
        }
        // Check for successful join
        try {
            // Look for meeting controls
            await page.waitForSelector('[data-call-id], [data-meeting-id], [data-is-call-active]', { timeout: 60000 });
            await this.screenshot(page, '05-in-meeting');
            console.log('[Google Meet] Successfully joined meeting');
        }
        catch {
            // Alternative check
            const inMeeting = await page.evaluate(() => {
                return document.body.innerText.includes('Present now') ||
                    document.body.innerText.includes('Turn on microphone') ||
                    document.body.innerText.includes('Leave call') ||
                    document.querySelector('[aria-label="Leave call"]') !== null;
            });
            if (inMeeting) {
                await this.screenshot(page, '05-in-meeting-alt-check');
                console.log('[Google Meet] Successfully joined meeting (alternative check)');
            }
            else {
                await this.screenshot(page, '05-uncertain-state');
                console.log('[Google Meet] Meeting join status uncertain, continuing...');
            }
        }
    }
    /**
     * Turn off camera and microphone
     */
    static async turnOffDevices(page) {
        try {
            // Google Meet uses specific buttons with aria labels
            // Camera toggle
            const cameraSelectors = [
                'button[aria-label*="camera"]',
                'button[aria-label*="Camera"]',
                'button[data-is-muted="false"][aria-label*="video"]',
                '[aria-label="Turn off camera"]',
            ];
            for (const selector of cameraSelectors) {
                try {
                    const cameraButton = page.locator(selector).first();
                    if (await cameraButton.isVisible({ timeout: 1000 })) {
                        const ariaLabel = await cameraButton.getAttribute('aria-label');
                        if (ariaLabel?.toLowerCase().includes('turn off') ||
                            ariaLabel?.toLowerCase().includes('camera is on')) {
                            await cameraButton.click();
                            console.log('[Google Meet] Turned off camera');
                            break;
                        }
                    }
                }
                catch {
                    continue;
                }
            }
            // Microphone toggle
            const micSelectors = [
                'button[aria-label*="microphone"]',
                'button[aria-label*="Microphone"]',
                '[aria-label="Turn off microphone"]',
            ];
            for (const selector of micSelectors) {
                try {
                    const micButton = page.locator(selector).first();
                    if (await micButton.isVisible({ timeout: 1000 })) {
                        const ariaLabel = await micButton.getAttribute('aria-label');
                        if (ariaLabel?.toLowerCase().includes('turn off') ||
                            ariaLabel?.toLowerCase().includes('microphone is on')) {
                            await micButton.click();
                            console.log('[Google Meet] Turned off microphone');
                            break;
                        }
                    }
                }
                catch {
                    continue;
                }
            }
        }
        catch (error) {
            console.log('[Google Meet] Could not toggle devices');
        }
    }
}
//# sourceMappingURL=google-meet.js.map