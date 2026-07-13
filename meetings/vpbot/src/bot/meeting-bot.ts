/**
 * Core Meeting Bot - Playwright browser automation for meeting capture
 * 
 * Audio capture: Uses ffmpeg to record from PulseAudio virtual sink.
 * The browser routes all WebRTC audio through PulseAudio, and ffmpeg
 * captures from the monitor source. This works for Teams, Zoom, and Meet.
 */

import { chromium, Browser, BrowserContext, Page } from 'playwright';
import { EventEmitter } from 'events';
import { ChildProcess, spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { Meeting, MeetingPlatform, MeetingStatus } from '../types.js';
import { TeamsHandler, LobbyTimeoutError } from './teams.js';
import { ZoomHandler } from './zoom.js';
import { GoogleMeetHandler } from './google-meet.js';

// FIRM callback: POST status updates to FIRM_API_URL /api/vp/callback
// Env vars: FIRM_API_URL, BOT_CALLBACK_SECRET, MEETING_ID (numeric)
async function reportStatus(
  meetingId: string,
  status: string,
  extra?: Record<string, unknown>
): Promise<void> {
  const firmApiUrl = process.env.FIRM_API_URL;
  const botSecret = process.env.BOT_CALLBACK_SECRET || '';
  const numericId = parseInt(process.env.MEETING_ID || meetingId, 10);
  if (!firmApiUrl) {
    console.log(`[Bot] FIRM_API_URL not set — skipping callback (status: ${status})`);
    return;
  }
  const payload = { meetingId: numericId, status, ...extra };
  try {
    const res = await fetch(`${firmApiUrl}/api/vp/callback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Bot-Secret': botSecret,
      },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      console.error(`[Bot] Callback failed: ${res.status} ${await res.text()}`);
    } else {
      console.log(`[Bot] Callback sent: ${status}`);
    }
  } catch (err) {
    console.error(`[Bot] Callback error: ${err}`);
  }
}

export interface MeetingBotEvents {
  'status-change': (status: MeetingStatus) => void;
  'recording-started': () => void;
  'recording-stopped': (audioPath: string) => void;
  'error': (error: Error) => void;
  'meeting-ended': () => void;
}

const MIN_RECORDING_MINUTES = 3;

export class MeetingBot extends EventEmitter {
  private browser: Browser | null = null;
  private context: BrowserContext | null = null;
  private page: Page | null = null;
  private meeting: Meeting;
  private recordingsDir: string;
  private isRecording = false;
  private ffmpegProcess: ChildProcess | null = null;
  private audioPath: string = '';
  private _hardTimeout: ReturnType<typeof setTimeout> | null = null;
  private _endPollInterval: ReturnType<typeof setInterval> | null = null;
  private _monitorInterval: ReturnType<typeof setInterval> | null = null;
  private _recordingStartTime: number = 0;
  private _silenceStartTime: number | null = null;

  constructor(meeting: Meeting, recordingsDir: string) {
    super();
    this.meeting = meeting;
    this.recordingsDir = recordingsDir;
  }

  /**
   * Detect the meeting platform from the URL
   */
  static detectPlatform(url: string): MeetingPlatform {
    if (url.includes('teams.microsoft.com') || url.includes('teams.live.com')) {
      return 'teams';
    }
    if (url.includes('zoom.us')) {
      return 'zoom';
    }
    if (url.includes('meet.google.com')) {
      return 'google-meet';
    }
    return 'unknown';
  }

  /**
   * Launch browser and join the meeting
   */
  async join(): Promise<void> {
    this.emit('status-change', 'joining');

    try {
      // Platform-specific browser launch args
      // Teams needs fake media devices + kiosk mode (per ScreenApp's working implementation)
      const isTeams = this.meeting.platform === 'teams';
      
      const baseArgs = [
        '--no-sandbox',
        '--disable-setuid-sandbox',
        '--disable-web-security',
        '--autoplay-policy=no-user-gesture-required',
        '--enable-features=MediaRecorder',
        '--enable-audio-service-out-of-process',
      ];

      const teamsArgs = [
        '--use-fake-ui-for-media-stream',   // Auto-grant media permissions
        '--use-fake-device-for-media-stream', // Teams needs fake devices for pre-join toggles
        '--kiosk',                            // Prevents address bar in recording
        '--start-maximized',
      ];

      const otherArgs = [
        '--use-fake-ui-for-media-stream',
      ];

      this.browser = await chromium.launch({
        headless: false, // Must be headed — Teams requires it for audio/video
        args: [
          ...baseArgs,
          ...(isTeams ? teamsArgs : otherArgs),
        ],
      });

      // User agent: Linux X11 Chrome 135 — matches ScreenApp's production config
      // which is confirmed working with New Teams (v2) in 2026.
      // Linux UA avoids Windows-specific Teams desktop app detection.
      const userAgent = isTeams
        ? 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36'
        : 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0.0.0 Safari/537.36';

      this.context = await this.browser.newContext({
        permissions: ['microphone', 'camera'],
        userAgent,
        viewport: { width: 1280, height: 720 },
        ignoreHTTPSErrors: true,
      });

      this.page = await this.context.newPage();

      // Navigate to meeting URL
      // For Teams, process the URL (add query hints, keep original URL — no /_#/ rewriting)
      let navUrl = this.meeting.url;
      if (this.meeting.platform === 'zoom') {
        // Grant mic/camera for both Zoom origins (chooser page + web client)
        await this.context.grantPermissions(['microphone', 'camera'], {
          origin: 'https://zoom.us'
        });
        await this.context.grantPermissions(['microphone', 'camera'], {
          origin: 'https://app.zoom.us'
        });
      } else if (this.meeting.platform === 'teams') {
        // Grant permissions for the Teams origin specifically
        await this.context.grantPermissions(['microphone', 'camera'], { 
          origin: 'https://teams.microsoft.com' 
        });
        navUrl = await TeamsHandler.processTeamsMeetingUrl(this.meeting.url);
        console.log(`[Bot] Teams processed URL: ${navUrl}`);
      }
      // Teams: use networkidle (heavy JS app). Others: domcontentloaded is fine.
      const waitUntil = this.meeting.platform === 'teams' ? 'networkidle' as const : 'domcontentloaded' as const;
      await this.page.goto(navUrl, { waitUntil, timeout: 30000 });

      // Platform-specific join logic
      try {
        switch (this.meeting.platform) {
          case 'teams':
            await TeamsHandler.join(this.page, this.meeting.botName, this.meeting.url);
            break;
          case 'zoom':
            await ZoomHandler.join(this.page, this.meeting.botName);
            break;
          case 'google-meet':
            await GoogleMeetHandler.join(this.page, this.meeting.botName);
            break;
          default:
            throw new Error(`Unsupported platform: ${this.meeting.platform}`);
        }
      } catch (err) {
        if (err instanceof LobbyTimeoutError) {
          console.log('[Bot] Lobby timeout — sending failed callback with reason=lobby_timeout');
          await reportStatus(this.meeting.id, 'failed', { reason: 'lobby_timeout' });
          await this.cleanup();
          return; // Do NOT start FFmpeg, do NOT send recording callback
        }
        throw err; // re-throw other errors
      }

      // Start recording after joining (only reached if join succeeded)
      await this.startRecording();
      
    } catch (error) {
      this.emit('error', error as Error);
      throw error;
    }
  }

  /**
   * Start capturing audio from the meeting using ffmpeg + PulseAudio.
   * 
   * Instead of trying to capture from DOM audio/video elements (which don't
   * carry WebRTC audio), we record from PulseAudio's monitor source.
   * The browser outputs all meeting audio through PulseAudio, and ffmpeg
   * captures everything from the default monitor source.
   */
  private async startRecording(): Promise<void> {
    if (!this.page) throw new Error('Page not initialized');

    // Attach page-close listener for unexpected navigation/tab close
    this.page.on('close', () => {
      console.log('[Bot] Page close event fired — treating as meeting end');
      if (this.isRecording) {
        this.stop('page-close-event').catch((err) =>
          console.error('[Bot] Error stopping on page close event:', err)
        );
      }
    });

    // Fix A — Pre-recording health check: verify recordings directory is writable
    const testPath = path.join(this.recordingsDir, '.writetest');
    try {
      fs.writeFileSync(testPath, 'ok');
      fs.unlinkSync(testPath);
    } catch (err) {
      const errorMsg = 'Recordings directory is not writable. Restart the service.';
      console.error(`[Bot] Pre-recording health check failed: ${err}`);
      await reportStatus(this.meeting.id, 'recording_failed', { error: errorMsg });
      this.emit('error', new Error(errorMsg));
      return;
    }

    // Verify bot is actually in the meeting (not stuck in lobby) before starting FFmpeg
    if (this.meeting.platform === 'teams' && this.page && !this.page.isClosed()) {
      try {
        const leaveVisible = await this.page
          .getByRole('button', { name: /Leave/i })
          .isVisible({ timeout: 3000 })
          .catch(() => false);
        if (!leaveVisible) {
          console.error('[Bot] Pre-recording check: Leave button not visible — bot may still be in lobby. Aborting recording.');
          await reportStatus(this.meeting.id, 'failed', { error: 'Bot not admitted to meeting — lobby state detected before recording start' });
          return;
        }
        console.log('[Bot] Pre-recording check: Leave button visible — confirmed in meeting');
      } catch (err) {
        console.warn('[Bot] Pre-recording admission check failed (non-fatal):', err);
        // Continue — uncertain is better than silently recording lobby audio
      }
    }

    // Record when recording started — used for Leave button grace period
    this._recordingStartTime = Date.now();

    this.isRecording = true;
    this.emit('status-change', 'recording');
    this.emit('recording-started');

    // Output directly as WAV — no conversion needed for transcription
    this.audioPath = path.join(this.recordingsDir, `${this.meeting.id}.wav`);

    // Spawn ffmpeg to record from PulseAudio monitor source
    // -f pulse: PulseAudio input
    // -i virtual_out.monitor: monitor source of our named virtual sink
    // -ac 1: mono (sufficient for speech)
    // -ar 16000: 16kHz sample rate (optimal for speech-to-text)
    this.ffmpegProcess = spawn('ffmpeg', [
      '-f', 'pulse',
      '-i', 'virtual_out.monitor',
      '-ac', '1',
      '-ar', '16000',
      '-af', 'silencedetect=noise=-40dB:d=60',  // detect 60s+ silence blocks; metadata only, does NOT affect WAV output
      '-y',  // overwrite output
      this.audioPath,
    ], {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env, PULSE_SERVER: 'unix:/var/run/pulse/native' },
    });

    this.ffmpegProcess.on('error', (err) => {
      console.error(`[Bot] FFmpeg failed to start: ${err.message}`);
      this.emit('error', new Error(`FFmpeg failed: ${err.message}`));
    });

    this.ffmpegProcess.stderr?.on('data', (data: Buffer) => {
      const msg = data.toString();
      // Parse silencedetect filter output
      // FFmpeg emits: "[silencedetect @ 0x...] silence_start: 45.3"
      //               "[silencedetect @ 0x...] silence_end: 47.1 | silence_duration: 1.8"
      if (msg.includes('silence_start')) {
        this._silenceStartTime = Date.now();
        console.log('[Bot] FFmpeg silence_start detected');
      } else if (msg.includes('silence_end')) {
        const durMatch = msg.match(/silence_duration:\s*([\d.]+)/);
        this._silenceStartTime = null;
        console.log('[Bot] FFmpeg silence_end detected — silence cleared');
      }
      // Only log non-progress, non-silencedetect lines to avoid spam
      const trimmed = msg.trim();
      if (trimmed && !trimmed.startsWith('size=') && !trimmed.startsWith('frame=') && !trimmed.includes('silencedetect')) {
        console.log(`[FFmpeg] ${trimmed}`);
      }
    });

    // Fix C — FFmpeg fast-exit detection: if FFmpeg exits within 5s, report failure
    const ffmpegStartTime = Date.now();
    this.ffmpegProcess.on('exit', (code) => {
      console.log(`[Bot] FFmpeg exited with code ${code}`);
      const elapsed = Date.now() - ffmpegStartTime;
      if (elapsed < 5000) {
        const errorMsg = `FFmpeg exited after ${elapsed}ms (code ${code}). Possible stale bind mount.`;
        console.error(`[Bot] Fast-exit detected: ${errorMsg}`);
        if (code !== 0) {
          reportStatus(this.meeting.id, 'recording_failed', { error: errorMsg }).catch(e =>
            console.error('[Bot] Failed to report recording_failed status:', e)
          );
        }
        this.emit('ffmpeg-fast-exit', code, elapsed);
      }
    });

    console.log(`[Bot] FFmpeg recording started → ${this.audioPath} (PID: ${this.ffmpegProcess.pid})`);

    // Hard ceiling: MAX_RECORDING_MINUTES (default 180)
    const maxMinutes = parseInt(process.env.MAX_RECORDING_MINUTES || '180', 10);
    const maxMs = maxMinutes * 60 * 1000;
    this._hardTimeout = setTimeout(() => {
      console.log(`[Bot] MAX_RECORDING_MINUTES (${maxMinutes}) reached — leaving meeting`);
      this.stop('max-recording-timeout').catch((err) =>
        console.error('[Bot] Error stopping after MAX_RECORDING_MINUTES:', err)
      );
    }, maxMs);
    console.log(`[Bot] Hard timeout set: ${maxMinutes} minutes`);

    // DOM polling for Teams meeting end detection (15s interval)
    const END_POLL_INTERVAL_MS = 15_000;
    let participantOneCount = 0;

    this._endPollInterval = setInterval(async () => {
      try {
        const page = this.page;
        if (!page || !this.isRecording) return;

        // Signal 1: Explicit end-of-meeting overlays — fires immediately (no min-duration guard)
        const endTexts = [
          'This call has ended',
          'You left the meeting',
          'This meeting has ended',
          'Meeting ended',
          'The meeting has ended',
          'Left the meeting',
          "You've left",
          'Call ended',
        ];
        for (const text of endTexts) {
          const found = await page.getByText(text, { exact: false }).isVisible().catch(() => false);
          if (found) {
            console.log(`[Bot] Meeting end detected (explicit screen): "${text}"`);
            if (this._endPollInterval) clearInterval(this._endPollInterval);
            this._endPollInterval = null;
            this.stop('meeting-ended-detected').catch((err) =>
              console.error('[Bot] Error stopping after end detection:', err)
            );
            return;
          }
        }

        // Page-navigation/crash detection
        if (page.isClosed()) {
          console.log('[Bot] Page is closed — treating as meeting end');
          if (this._endPollInterval) clearInterval(this._endPollInterval);
          this._endPollInterval = null;
          this.stop('page-closed').catch((err) =>
            console.error('[Bot] Error stopping after page close:', err)
          );
          return;
        }

        // Detect navigation away from Teams meeting
        try {
          const currentUrl = page.url();
          const meetingUrl = this.meeting.url;
          if (currentUrl && meetingUrl) {
            const isTeams = this.meeting.platform === 'teams';
            if (isTeams && currentUrl !== meetingUrl) {
              // Check if we've navigated to a non-meeting page
              const isAboutBlank = currentUrl === 'about:blank' || currentUrl === '';
              const isTeamsHome = currentUrl.includes('teams.microsoft.com/_#/') && !currentUrl.includes('/meetup-join/');
              const isTeamsConversations = currentUrl.includes('/conversations') || currentUrl.includes('/calendar') || currentUrl.includes('/chat');
              if (isAboutBlank || isTeamsHome || isTeamsConversations) {
                console.log(`[Bot] Navigation away from meeting detected: ${currentUrl}`);
                if (this._endPollInterval) clearInterval(this._endPollInterval);
                this._endPollInterval = null;
                this.stop('navigation-away').catch((err) =>
                  console.error('[Bot] Error stopping after navigation away:', err)
                );
                return;
              }
            }
          }
        } catch (_urlErr) {
          // Non-fatal — page may be in transition
        }

        // Minimum duration guard: signals below require at least MIN_RECORDING_MINUTES
        const minDurationMs = MIN_RECORDING_MINUTES * 60 * 1000;
        const pastMinDuration = (Date.now() - this._recordingStartTime) >= minDurationMs;

        // Signal 2: Participant count drops to ≤1 for 5 consecutive polls (75s)
        // Requires: min-duration guard
        const countEl = await page.$('[data-tid="roster-button"]');
        if (countEl) {
          const countText = (await countEl.textContent()) || '';
          const count = parseInt(countText.match(/\d+/)?.[0] || '0', 10);
          if (count <= 1) {
            participantOneCount++;
            console.log(`[Bot] Participant count ≤1 (${participantOneCount}/5 consecutive polls)`);
            if (participantOneCount >= 5 && pastMinDuration) {
              // 5 consecutive 15s polls = alone for ≥75s
              console.log('[Bot] Alone in meeting for 75s — ending');
              if (this._endPollInterval) clearInterval(this._endPollInterval);
              this._endPollInterval = null;
              this.stop('participant-count-timeout').catch((err) =>
                console.error('[Bot] Error stopping after participant count timeout:', err)
              );
              return;
            }
          } else {
            participantOneCount = 0;
          }
        }

        // Signal 3: Sustained audio silence via FFmpeg silencedetect
        // Requires: min-duration guard + alone (participant count ≤1 or roster unknown)
        // Trigger: silence_start was detected and no silence_end has arrived for >90s
        if (this._silenceStartTime !== null && pastMinDuration) {
          const silenceDurationMs = Date.now() - this._silenceStartTime;
          const aloneOrUnknown = !countEl || participantOneCount >= 3; // 45s alone before silence can trigger
          if (silenceDurationMs > 90_000 && aloneOrUnknown) {
            console.log(`[Bot] Audio silence for ${(silenceDurationMs / 1000).toFixed(0)}s with no other participants — ending`);
            if (this._endPollInterval) clearInterval(this._endPollInterval);
            this._endPollInterval = null;
            this.stop('audio-silence-timeout').catch((err) =>
              console.error('[Bot] Error stopping after audio silence timeout:', err)
            );
            return;
          }
        }
      } catch (e) {
        // Non-fatal — polling continues
        console.log(`[Bot] End-poll error (non-fatal): ${e}`);
      }
    }, END_POLL_INTERVAL_MS);

    // Set up legacy periodic check for meeting end
    this.monitorMeetingStatus();
  }

  /**
   * Monitor if the meeting has ended
   */
  private monitorMeetingStatus(): void {
    this._monitorInterval = setInterval(async () => {
      if (!this.page || !this.isRecording) {
        clearInterval(this._monitorInterval!);
        this._monitorInterval = null;
        return;
      }

      try {
        // Check for meeting end indicators
        const meetingEnded = await this.page.evaluate(() => {
          // Teams end indicators
          const teamsEnd = document.querySelector('[data-tid="call-ended"]') ||
                          document.body.innerText.includes('This call has ended') ||
                          document.body.innerText.includes('You left the meeting') ||
                          document.body.innerText.includes('This meeting has ended') ||
                          document.body.innerText.includes('Meeting ended') ||
                          document.body.innerText.includes('The meeting has ended') ||
                          document.body.innerText.includes('Left the meeting') ||
                          document.body.innerText.includes("You've left") ||
                          document.body.innerText.includes('Call ended');
          
          // Zoom end indicators
          const zoomEnd = document.body.innerText.includes('This meeting has been ended') ||
                         document.body.innerText.includes('The host has ended this meeting');
          
          // Google Meet end indicators
          const meetEnd = document.body.innerText.includes('You left the meeting') ||
                         document.body.innerText.includes('The call ended');

          return !!(teamsEnd || zoomEnd || meetEnd);
        });

        if (meetingEnded) {
          clearInterval(this._monitorInterval!);
          this._monitorInterval = null;
          this.emit('meeting-ended');
          await this.stop();
        }
      } catch (error) {
        // Page might be closed
        clearInterval(this._monitorInterval!);
        this._monitorInterval = null;
      }
    }, 5000);
  }

  /**
   * Stop recording and save audio.
   * Sends SIGINT (graceful quit) to ffmpeg so it writes the WAV header properly.
   *
   * @param reason  Optional stop reason for logging (e.g. 'meeting-ended-detected',
   *                'max-recording-timeout', 'participant-count-timeout', 'api-request')
   */
  async stop(reason: string = 'api-request'): Promise<string> {
    if (!this.isRecording) {
      throw new Error('Not currently recording');
    }

    console.log(`[Bot] Stopping recording — reason: ${reason}`);

    // Clear end-detection timers
    if (this._hardTimeout) {
      clearTimeout(this._hardTimeout);
      this._hardTimeout = null;
    }
    if (this._endPollInterval) {
      clearInterval(this._endPollInterval);
      this._endPollInterval = null;
    }
    if (this._monitorInterval) {
      clearInterval(this._monitorInterval);
      this._monitorInterval = null;
    }
    this._silenceStartTime = null;

    this.isRecording = false;
    this.emit('status-change', 'processing');

    // Gracefully stop ffmpeg with SIGINT (writes proper WAV headers)
    if (this.ffmpegProcess && !this.ffmpegProcess.killed) {
      console.log(`[Bot] Stopping FFmpeg (PID: ${this.ffmpegProcess.pid})...`);
      
      await new Promise<void>((resolve) => {
        const timeout = setTimeout(() => {
          // Force kill if it doesn't stop gracefully
          console.log('[Bot] FFmpeg did not stop gracefully, sending SIGKILL');
          this.ffmpegProcess?.kill('SIGKILL');
          resolve();
        }, 5000);

        this.ffmpegProcess!.on('exit', () => {
          clearTimeout(timeout);
          resolve();
        });

        // SIGINT = 'q' quit for ffmpeg, writes headers
        this.ffmpegProcess!.kill('SIGINT');
      });
    }

    this.ffmpegProcess = null;

    // Verify the recording file exists and has data
    if (fs.existsSync(this.audioPath)) {
      const stats = fs.statSync(this.audioPath);
      console.log(`[Bot] Recording saved: ${this.audioPath} (${(stats.size / 1024).toFixed(1)} KB)`);
      
      if (stats.size < 100) {
        console.warn('[Bot] WARNING: Recording file is very small — audio may not have been captured');
      }
    } else {
      console.error(`[Bot] Recording file not found: ${this.audioPath}`);
      // Create empty file so downstream doesn't crash
      fs.writeFileSync(this.audioPath, Buffer.alloc(0));
    }

    this.emit('recording-stopped', this.audioPath);

    // Close browser
    await this.cleanup();

    return this.audioPath;
  }

  /**
   * Force leave the meeting and clean up
   */
  async cleanup(): Promise<void> {
    try {
      if (this.page) {
        await this.page.close().catch(() => {});
      }
      if (this.context) {
        await this.context.close().catch(() => {});
      }
      if (this.browser) {
        await this.browser.close().catch(() => {});
      }
    } catch (error) {
      // Ignore cleanup errors
    }
    
    this.page = null;
    this.context = null;
    this.browser = null;
  }

  /**
   * Get current recording status
   */
  isCurrentlyRecording(): boolean {
    return this.isRecording;
  }
}
