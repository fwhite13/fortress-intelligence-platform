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
import { Page } from 'playwright';
export declare class LobbyTimeoutError extends Error {
    constructor();
}
export declare class TeamsHandler {
    /**
     * Save a debug screenshot with sequential numbering
     */
    private static screenshot;
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
    static processTeamsMeetingUrl(meetingUrl: string): Promise<string>;
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
    private static clickLauncherButton;
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
    private static waitForPreJoinScreen;
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
    static join(page: Page, botName: string, originalUrl?: string): Promise<void>;
    /**
     * Turn off camera and microphone on the pre-join screen.
     *
     * New Teams uses toggle inputs (data-tid="toggle-video" / "toggle-mute")
     * and button elements. We try both patterns.
     */
    private static turnOffDevices;
}
//# sourceMappingURL=teams.d.ts.map