/**
 * firm-vpbot API Server
 *
 * Provides REST API for meeting bot control, transcription, and summarization.
 * When MEETING_URL + MEETING_ID env vars are set, runs in one-shot Fargate mode
 * (joins one meeting, processes it, then exits). Otherwise starts the Express
 * API server as normal.
 */
declare const app: import("express-serve-static-core").Express;
export { app };
//# sourceMappingURL=index.d.ts.map