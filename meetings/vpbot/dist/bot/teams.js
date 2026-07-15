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
import * as path from 'path';
export class LobbyTimeoutError extends Error {
    constructor() {
        super('Bot was not admitted to the Teams meeting lobby within 2 minutes');
        this.name = 'LobbyTimeoutError';
    }
}
const SCREENSHOTS_DIR = process.env.RECORDINGS_DIR || '/app/recordings';
export class TeamsHandler {
    /**
     * Save a debug screenshot with sequential numbering
     */
    static async screenshot(page, label) {
        try {
            const filename = `debug-${label}-${Date.now()}.png`;
            const filepath = path.join(SCREENSHOTS_DIR, filename);
            await page.screenshot({ path: filepath, fullPage: true });
            console.log(`[Teams] Screenshot saved: ${filename}`);
        }
        catch (e) {
            console.log(`[Teams] Screenshot failed: ${e}`);
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
    static async processTeamsMeetingUrl(meetingUrl) {
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
        }
        catch (error) {
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
    static async clickLauncherButton(page) {
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
            }
            catch {
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
                }
                catch {
                    continue;
                }
            }
        }
        catch {
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
    static async waitForPreJoinScreen(page, timeoutMs = 120000) {
        console.log(`[Teams] Waiting for pre-join screen (timeout: ${timeoutMs / 1000}s)...`);
        // Primary indicator: the name input field
        try {
            const nameInput = page.locator('input[data-tid="prejoin-display-name-input"]');
            await nameInput.waitFor({ state: 'visible', timeout: timeoutMs });
            console.log('[Teams] Pre-join screen detected (found name input field)');
            return true;
        }
        catch {
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
            }
            catch {
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
        }
        catch {
            // ignore
        }
        console.log('[Teams] Pre-join screen NOT detected');
        return false;
    }
    /**
     * Join a Teams meeting as an anonymous guest.
     *
     * Flow (based on ScreenApp's production implementation):
     * 1. Navigate to meeting URL (original URL, no /_#/ rewriting)
     * 2. Wait for launcher page, click "Continue on this browser" (force:true)
     * 3. Wait for pre-join screen (name input field, up to 120s)
     * 4. Fill in bot name
     * 5. Turn off camera/mic
     * 6. Click "Join now"
     * 7. Wait for meeting entry (look for "Leave" button)
     * 8. Handle waiting room if needed
     */
    static async join(page, botName, originalUrl) {
        console.log('[Teams] Starting join flow...');
        console.log('[Teams] Current URL:', page.url());
        await this.screenshot(page, '01-initial-page');
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
            }
            else {
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
        // Step 3: Enter name in the name field
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
            }
            catch {
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
        await this.screenshot(page, '03-before-join-click');
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
        }
        catch {
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
                }
                catch {
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
        // Step 6: Wait for meeting to load
        console.log('[Teams] Waiting for meeting to load...');
        // Look for the Leave button as confirmation we're in the meeting
        try {
            const leaveButton = page.getByRole('button', { name: /Leave/i });
            await leaveButton.waitFor({ timeout: 60000 });
            console.log('[Teams] ✅ Successfully joined meeting (Leave button visible)');
            await this.screenshot(page, '04-in-meeting');
            return;
        }
        catch {
            console.log('[Teams] Leave button not found within 60s, checking other states...');
        }
        await this.screenshot(page, '04-after-join-attempt');
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
            console.log('[Teams] In waiting room / lobby, waiting to be admitted (max 2 min)...');
            await this.screenshot(page, '04b-waiting-room');
            let admitted = false;
            // Poll 12×10s = 2 minutes
            for (let i = 0; i < 12; i++) {
                await page.waitForTimeout(10000);
                // Check for Leave button (means we were admitted)
                try {
                    const leaveButton = page.getByRole('button', { name: /Leave/i });
                    if (await leaveButton.isVisible({ timeout: 1000 })) {
                        console.log('[Teams] ✅ Admitted from waiting room, now in meeting');
                        await this.screenshot(page, '05-admitted-in-meeting');
                        admitted = true;
                        return; // admitted — normal path
                    }
                }
                catch {
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
                        return;
                    }
                }
                catch {
                    // not admitted
                }
                console.log('[Teams] ❌ Not admitted to lobby within 2 minutes — throwing LobbyTimeoutError');
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
        }
        else if (joinedCheck.hasLeave || joinedCheck.hasHangup || joinedCheck.hasMeetingUI || joinedCheck.hasRoster) {
            console.log('[Teams] ✅ Successfully joined meeting');
            await this.screenshot(page, '05-in-meeting');
        }
        else {
            console.log('[Teams] ⚠️ Meeting join status uncertain — hasMeetingUI=false, hasLeave=false. Treating as lobby timeout.');
            await this.screenshot(page, '05-uncertain-state');
            throw new LobbyTimeoutError();
        }
    }
    /**
     * Turn off camera and microphone on the pre-join screen.
     *
     * New Teams uses toggle inputs (data-tid="toggle-video" / "toggle-mute")
     * and button elements. We try both patterns.
     */
    static async turnOffDevices(page) {
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
                }
                catch {
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
                }
                catch {
                    continue;
                }
            }
            console.log('[Teams] Finished toggling devices');
        }
        catch (error) {
            console.log('[Teams] Could not toggle devices, continuing...', error);
        }
    }
}
//# sourceMappingURL=teams.js.map