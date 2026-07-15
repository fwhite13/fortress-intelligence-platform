/**
 * Google Meet specific join logic
 */
import { Page } from 'playwright';
export declare class GoogleMeetHandler {
    /**
     * Save a debug screenshot.
     */
    private static screenshot;
    /**
     * Check the page for known Google Meet block/rejection messages.
     * Meet doesn't publicly document a bot-detection wall the way Zoom does,
     * but it does reject anonymous/unusual callers with explicit text —
     * capture that here so we can tell "blocked" apart from "still waiting
     * for host to admit".
     */
    private static checkForBlock;
    /**
     * Join a Google Meet meeting
     */
    static join(page: Page, botName: string): Promise<void>;
    /**
     * Turn off camera and microphone
     */
    private static turnOffDevices;
}
//# sourceMappingURL=google-meet.d.ts.map