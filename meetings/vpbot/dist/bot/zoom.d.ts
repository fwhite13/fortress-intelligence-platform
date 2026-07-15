/**
 * Zoom specific join logic
 */
import { Page } from 'playwright';
export declare class ZoomHandler {
    /**
     * Save a debug screenshot.
     */
    private static screenshot;
    /**
     * Join a Zoom meeting via web browser
     */
    static join(page: Page, botName: string): Promise<void>;
    /**
     * Turn off audio and video
     */
    private static turnOffDevices;
}
//# sourceMappingURL=zoom.d.ts.map