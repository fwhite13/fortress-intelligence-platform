/**
 * firm-vpbot API Server
 *
 * Provides REST API for meeting bot control, transcription, and summarization.
 * When MEETING_URL + MEETING_ID env vars are set, runs in one-shot Fargate mode
 * (joins one meeting, processes it, then exits). Otherwise starts the Express
 * API server as normal.
 */
import express from 'express';
import cors from 'cors';
import * as path from 'path';
import * as fs from 'fs';
import { v4 as uuidv4 } from 'uuid';
import { config } from 'dotenv';
import { fileURLToPath } from 'url';
import { MeetingBot } from './bot/meeting-bot.js';
import { S3Service } from './transcribe/s3.js';
import { BatchTranscriptionService } from './transcribe/batch.js';
import { convertToWav } from './utils/audio.js';
// Load environment variables
config();
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
const PORT = process.env.PORT || 3500;
const AWS_REGION = process.env.AWS_REGION || 'us-east-1';
const S3_BUCKET = process.env.S3_BUCKET || 'firm-recordings-dev';
const BOT_NAME = process.env.BOT_NAME || 'Fortress Notetaker';
const RECORDINGS_DIR = process.env.RECORDINGS_DIR || path.join(__dirname, '..', 'recordings');
// FIRM-specific env vars
const FIRM_API_URL = process.env.FIRM_API_URL || '';
const MEETING_ID = process.env.MEETING_ID || '';
const BOT_CALLBACK_SECRET = process.env.BOT_CALLBACK_SECRET || '';
const MEETING_PLATFORM = process.env.MEETING_PLATFORM || ''; // teams|zoom|meet|google-meet
const FIRM_MAX_MEETING_HOURS = parseFloat(process.env.FIRM_MAX_MEETING_HOURS || '4');
// Ensure recordings directory exists
if (!fs.existsSync(RECORDINGS_DIR)) {
    fs.mkdirSync(RECORDINGS_DIR, { recursive: true });
}
// ---------------------------------------------------------------------------
// Initialize services (module-level — shared by API server and one-shot mode)
// ---------------------------------------------------------------------------
const s3Service = new S3Service(AWS_REGION, S3_BUCKET);
// In-memory meeting storage (in production, use a database)
const meetings = new Map();
const bots = new Map();
// Express app
const app = express();
app.use(cors());
app.use(express.json());
app.use(express.static(path.join(__dirname, '..', 'public')));
// ---------------------------------------------------------------------------
// Module-level FIRM callback (used in one-shot mode)
// ---------------------------------------------------------------------------
async function postCallback(status, extra) {
    if (!FIRM_API_URL || !MEETING_ID)
        return;
    try {
        const response = await fetch(`${FIRM_API_URL}/api/vp/callback`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Bot-Secret': BOT_CALLBACK_SECRET },
            body: JSON.stringify({ meetingId: parseInt(MEETING_ID, 10), status, ...extra }),
        });
        if (response.ok) {
            console.log(`[Pipeline] FIRM callback sent: ${status} — HTTP ${response.status}`);
        }
        else {
            const body = await response.text().catch(() => '(unreadable)');
            console.error(`[Pipeline] FIRM callback FAILED: ${status} — HTTP ${response.status} — ${body}`);
        }
    }
    catch (err) {
        console.error('[Pipeline] FIRM callback error:', err);
    }
}
// ---------------------------------------------------------------------------
// One-shot Fargate mode — triggered by MEETING_URL + MEETING_ID env vars
// ---------------------------------------------------------------------------
const ONE_SHOT_MEETING_URL = process.env.MEETING_URL;
const ONE_SHOT_MEETING_ID = process.env.MEETING_ID;
const ONE_SHOT_BOT_NAME = process.env.BOT_DISPLAY_NAME || process.env.BOT_NAME || 'Fortress Notetaker';
if (ONE_SHOT_MEETING_URL && ONE_SHOT_MEETING_ID) {
    console.log('[OneShot] Starting one-shot meeting mode:', ONE_SHOT_MEETING_ID);
    // Safety net: force exit after FIRM_MAX_MEETING_HOURS + 30 min regardless of stuck state
    const maxMs = (parseFloat(process.env.FIRM_MAX_MEETING_HOURS || '4') * 60 + 30) * 60 * 1000;
    const safetyTimer = setTimeout(() => {
        console.error('[OneShot] Safety net timeout — forcing process.exit(1)');
        process.exit(1);
    }, maxMs);
    safetyTimer.unref(); // Don't prevent normal exit
    runOneShotMeeting(ONE_SHOT_MEETING_URL, ONE_SHOT_MEETING_ID, ONE_SHOT_BOT_NAME)
        .then(() => { console.log('[OneShot] Complete — exiting.'); process.exit(0); })
        .catch(async (err) => {
        console.error('[OneShot] Fatal:', err);
        await uploadDebugScreenshots(ONE_SHOT_MEETING_ID).catch(() => { });
        await postCallback('failed', { error: String(err?.message ?? err) }).catch(() => { });
        process.exit(1);
    });
}
else {
    startApiServer();
}
// ---------------------------------------------------------------------------
// One-shot meeting runner
// ---------------------------------------------------------------------------
async function runOneShotMeeting(meetingUrl, meetingId, botName) {
    // Pre-recording bind-mount write check
    const testPath = path.join('/tmp/recordings', '.write-test');
    try {
        fs.mkdirSync('/tmp/recordings', { recursive: true });
        fs.writeFileSync(testPath, 'ok');
        fs.unlinkSync(testPath);
        console.log('[Bot] Recordings dir write check passed');
    }
    catch (err) {
        console.error('[Bot] Recordings dir not writable — aborting:', err);
        await postCallback('failed', { error: 'Recordings directory not writable' });
        process.exit(1);
    }
    // Set max meeting duration for MeetingBot (which reads MAX_RECORDING_MINUTES)
    process.env.MAX_RECORDING_MINUTES = String(Math.round(FIRM_MAX_MEETING_HOURS * 60));
    const meeting = {
        id: meetingId,
        url: meetingUrl,
        platform: MeetingBot.detectPlatform(meetingUrl),
        status: 'joining',
        botName,
        createdAt: new Date(),
    };
    // Override platform detection with explicit env var if provided
    if (MEETING_PLATFORM) {
        const platformMap = {
            'teams': 'teams',
            'zoom': 'zoom',
            'meet': 'google-meet',
            'google-meet': 'google-meet',
        };
        meeting.platform = platformMap[MEETING_PLATFORM.toLowerCase()] || meeting.platform;
        console.log(`[OneShot] Platform override: ${meeting.platform}`);
    }
    // MeetingBot constructor: (meeting: Meeting, recordingsDir: string)
    const bot = new MeetingBot(meeting, '/tmp/recordings');
    // Register SIGTERM handler for graceful stop (e.g. from ECS task stop or Stop Recording button)
    process.once('SIGTERM', () => {
        console.log('[Bot] SIGTERM received — stopping recording gracefully');
        if (bot && bot.isCurrentlyRecording()) {
            bot.stop('sigterm-graceful-stop').catch((err) => {
                console.error('[Bot] Error stopping on SIGTERM:', err);
                process.exit(1);
            });
        }
        else {
            console.log('[Bot] SIGTERM received but not recording — exiting');
            process.exit(0);
        }
    });
    // Listen for status changes to emit recording callback
    bot.on('status-change', (status) => {
        meeting.status = status;
        if (status === 'recording') {
            meeting.startedAt = new Date();
            postCallback('recording', { participants: [] }).catch(console.error);
        }
    });
    await new Promise((resolve, reject) => {
        bot.on('recording-stopped', async (wavPath) => {
            try {
                meeting.audioPath = wavPath;
                meeting.endedAt = new Date();
                // Upload debug screenshots unconditionally, not just on hard failure —
                // "join status uncertain, continuing" is not an exception (bot proceeds
                // to record regardless), so this is the only place that reliably runs
                // after every join attempt, successful or not.
                await uploadDebugScreenshots(meetingId).catch(() => { });
                await processRecording(meeting);
                resolve();
            }
            catch (err) {
                reject(err);
            }
        });
        bot.on('error', (err) => reject(err));
        bot.on('ffmpeg-fast-exit', (code, elapsed) => {
            console.error(`[Bot] ffmpeg exited within 5s — treating as failed start (elapsed: ${elapsed}ms, code: ${code})`);
            postCallback('failed', { error: `ffmpeg exited after ${elapsed}ms (code ${code})` }).catch(() => { });
            process.exit(1);
        });
        // MeetingBot uses .join() not .start()
        bot.join().catch(reject);
    });
}
// ---------------------------------------------------------------------------
// API Server (wrapped for one-shot / dev-mode branching)
// ---------------------------------------------------------------------------
function startApiServer() {
    /**
     * POST /api/meetings/join
     * Join a meeting and start recording
     */
    app.post('/api/meetings/join', async (req, res) => {
        try {
            const { url, name } = req.body;
            if (!url) {
                return res.status(400).json({ error: 'Meeting URL is required' });
            }
            // Detect platform
            const platform = MeetingBot.detectPlatform(url);
            if (platform === 'unknown') {
                return res.status(400).json({
                    error: 'Unsupported meeting platform. Supported: Teams, Zoom, Google Meet',
                });
            }
            // Create meeting record
            const meeting = {
                id: uuidv4(),
                url,
                platform,
                botName: name || BOT_NAME,
                status: 'pending',
                createdAt: new Date(),
            };
            meetings.set(meeting.id, meeting);
            // MeetingBot constructor: (meeting: Meeting, recordingsDir: string)
            const bot = new MeetingBot(meeting, RECORDINGS_DIR);
            bots.set(meeting.id, bot);
            // FIRM callback helper for this meeting
            const firmMeetingId = parseInt(process.env.MEETING_ID || '0', 10);
            const firmApiUrl = process.env.FIRM_API_URL || '';
            const botSecret = process.env.BOT_CALLBACK_SECRET || '';
            async function postFirmCallback(status, extra) {
                if (!firmApiUrl || !firmMeetingId)
                    return;
                try {
                    const response = await fetch(`${firmApiUrl}/api/vp/callback`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json', 'X-Bot-Secret': botSecret },
                        body: JSON.stringify({ meetingId: firmMeetingId, status, ...extra }),
                    });
                    if (response.ok) {
                        console.log(`[Pipeline] FIRM callback sent: ${status} — HTTP ${response.status}`);
                    }
                    else {
                        const body = await response.text().catch(() => '(unreadable)');
                        console.error(`[Pipeline] FIRM callback FAILED: ${status} — HTTP ${response.status} — ${body}`);
                    }
                }
                catch (err) {
                    console.error('[Pipeline] FIRM callback error:', err);
                }
            }
            // Set up event handlers
            bot.on('status-change', (status) => {
                meeting.status = status;
                if (status === 'recording') {
                    meeting.startedAt = new Date();
                    // Notify FIRM that recording has started
                    postFirmCallback('recording', { participants: [] }).catch(console.error);
                }
            });
            bot.on('recording-stopped', async (audioPath) => {
                meeting.audioPath = audioPath;
                meeting.endedAt = new Date();
                // Start transcription pipeline
                processRecording(meeting).catch(err => {
                    console.error(`[Pipeline] Error processing meeting ${meeting.id}:`, err);
                    meeting.status = 'error';
                    meeting.error = err.message;
                    postFirmCallback('failed', { error: err.message }).catch(console.error);
                });
            });
            bot.on('error', (error) => {
                console.error(`[Bot] Error for meeting ${meeting.id}:`, error);
                meeting.status = 'error';
                meeting.error = error.message;
                postFirmCallback('failed', { error: error.message }).catch(console.error);
            });
            bot.on('meeting-ended', () => {
                console.log(`[Bot] Meeting ${meeting.id} ended`);
            });
            // Start joining (don't await - runs in background)
            bot.join().catch(err => {
                console.error(`[Bot] Failed to join meeting ${meeting.id}:`, err);
                meeting.status = 'error';
                meeting.error = err.message;
            });
            const response = {
                meetingId: meeting.id,
                status: 'joining',
                message: `Bot is joining ${platform} meeting...`,
            };
            res.status(202).json(response);
        }
        catch (error) {
            console.error('[API] Error joining meeting:', error);
            res.status(500).json({ error: 'Failed to join meeting' });
        }
    });
    /**
     * GET /api/meetings/:id
     * Get meeting status and results
     */
    app.get('/api/meetings/:id', (req, res) => {
        const meeting = meetings.get(req.params.id);
        if (!meeting) {
            return res.status(404).json({ error: 'Meeting not found' });
        }
        const response = { meeting };
        res.json(response);
    });
    /**
     * POST /api/meetings/:id/stop
     * Stop recording and leave meeting
     */
    app.post('/api/meetings/:id/stop', async (req, res) => {
        const meeting = meetings.get(req.params.id);
        const bot = bots.get(req.params.id);
        if (!meeting || !bot) {
            return res.status(404).json({ error: 'Meeting not found' });
        }
        if (meeting.status !== 'recording') {
            return res.status(400).json({ error: 'Meeting is not currently recording' });
        }
        try {
            await bot.stop();
            const response = {
                meetingId: meeting.id,
                status: meeting.status,
                message: 'Recording stopped, processing audio...',
            };
            res.json(response);
        }
        catch (error) {
            console.error('[API] Error stopping meeting:', error);
            res.status(500).json({ error: 'Failed to stop meeting' });
        }
    });
    /**
     * POST /api/meetings/retranscribe
     * @deprecated Use firm-web's direct Batch submission instead (ADO#1844).
     * Kept for backward compat with older clients. Submits a Batch job and returns immediately.
     * Body: { firmMeetingId: number, audioS3Key?: string }
     */
    app.post('/api/meetings/retranscribe', async (req, res) => {
        try {
            const { firmMeetingId, audioS3Key } = req.body;
            if (!firmMeetingId) {
                return res.status(400).json({ error: 'firmMeetingId is required' });
            }
            const s3Key = audioS3Key ?? `firm-audio/${firmMeetingId}/recording.wav`;
            // Deprecated: Submit Batch job directly (firm-web now does this itself)
            const batchSvc = new BatchTranscriptionService();
            const batchJobId = await batchSvc.submitTranscriptionJob(firmMeetingId, s3Key);
            console.log(`[Retranscribe][Deprecated] Batch job ${batchJobId} submitted for meeting ${firmMeetingId}`);
            res.json({ status: 'retranscribe_started', firmMeetingId, s3Key, batchJobId, deprecated: true });
        }
        catch (err) {
            console.error('[Retranscribe] Request error:', err);
            res.status(500).json({ error: err.message });
        }
    });
    /**
     * GET /api/meetings
     * List recent meetings
     */
    app.get('/api/meetings', (req, res) => {
        const limit = parseInt(req.query.limit) || 20;
        const meetingList = Array.from(meetings.values())
            .sort((a, b) => b.createdAt.getTime() - a.createdAt.getTime())
            .slice(0, limit);
        const response = { meetings: meetingList };
        res.json(response);
    });
    /**
     * GET /api/meetings/:id/transcript
     * Download transcript
     */
    app.get('/api/meetings/:id/transcript', (req, res) => {
        const meeting = meetings.get(req.params.id);
        if (!meeting) {
            return res.status(404).json({ error: 'Meeting not found' });
        }
        if (!meeting.transcript) {
            return res.status(404).json({ error: 'Transcript not available yet' });
        }
        res.json({ transcript: meeting.transcript });
    });
    /**
     * GET /api/meetings/:id/summary
     * Download summary
     */
    app.get('/api/meetings/:id/summary', (req, res) => {
        const meeting = meetings.get(req.params.id);
        if (!meeting) {
            return res.status(404).json({ error: 'Meeting not found' });
        }
        if (!meeting.summary) {
            return res.status(404).json({ error: 'Summary not available yet' });
        }
        res.json({ summary: meeting.summary });
    });
    /**
     * Health check endpoint
     */
    app.get('/health', (req, res) => {
        res.json({
            status: 'ok',
            version: '1.0.0',
            activeMeetings: bots.size,
        });
    });
    /**
     * Error handler
     */
    app.use((err, req, res, next) => {
        console.error('[Server] Unhandled error:', err);
        res.status(500).json({ error: 'Internal server error' });
    });
    // Start server
    app.listen(PORT, () => {
        console.log(`
╔═══════════════════════════════════════════════════════════╗
║              Fortress Notetaker v1.0.0                    ║
╠═══════════════════════════════════════════════════════════╣
║  Server running on http://localhost:${PORT}                  ║
║                                                           ║
║  Endpoints:                                               ║
║    POST /api/meetings/join    - Join a meeting            ║
║    GET  /api/meetings/:id     - Get meeting status        ║
║    POST /api/meetings/:id/stop - Stop recording           ║
║    GET  /api/meetings         - List meetings             ║
║                                                           ║
║  Supported platforms: Teams, Zoom, Google Meet            ║
╚═══════════════════════════════════════════════════════════╝
    `);
    });
}
// ---------------------------------------------------------------------------
// processRecording — S3 upload + Batch submission (ADO#1841)
// uploadDebugScreenshots — on fatal join failure, push /tmp/screenshots/* to S3 for post-mortem
async function uploadDebugScreenshots(meetingId) {
    const screenshotsDir = process.env.SCREENSHOTS_DIR || '/tmp/screenshots';
    if (!fs.existsSync(screenshotsDir))
        return;
    const files = fs.readdirSync(screenshotsDir).filter(f => f.endsWith('.png'));
    if (files.length === 0) {
        console.log('[Debug] No screenshots to upload');
        return;
    }
    console.log(`[Debug] Uploading ${files.length} debug screenshot(s) to S3...`);
    for (const file of files) {
        const localPath = path.join(screenshotsDir, file);
        const s3Key = `debug-screenshots/${meetingId}/${file}`;
        try {
            await s3Service.uploadWithKey(localPath, s3Key);
            console.log(`[Debug] Uploaded: ${s3Key}`);
        }
        catch (e) {
            console.log(`[Debug] Failed to upload ${file}: ${e}`);
        }
    }
}
// ---------------------------------------------------------------------------
async function processRecording(meeting) {
    if (!meeting.audioPath) {
        throw new Error('No audio file available');
    }
    console.log(`[Pipeline] Processing recording for meeting ${meeting.id}`);
    // Step 1: Convert audio to WAV
    meeting.status = 'processing';
    const wavPath = await convertToWav(meeting.audioPath);
    // Step 2: Upload audio to S3 with FIRM key convention
    console.log('[Pipeline] Uploading audio to S3...');
    const audioKey = `firm-audio/${meeting.id}/recording.wav`;
    await s3Service.uploadWithKey(wavPath, audioKey);
    meeting.s3AudioKey = audioKey;
    console.log(`[Pipeline] Audio uploaded to S3: ${audioKey}`);
    // Step 3: Post recording_complete callback — firm-web will submit Batch job (ADO#2179)
    await postCallback('recording_complete', { audioS3Key: audioKey });
    console.log(`[Pipeline] Meeting ${meeting.id} recording_complete posted — Batch submission delegated to firm-web`);
}
export { app };
//# sourceMappingURL=index.js.map