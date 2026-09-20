/**
 * ZoomSDKBot — manages one Zoom SDK meeting recording session.
 *
 * Replaces the browser-based ZoomHandler/MeetingBot path for Zoom on
 * one-shot ECS tasks. No Playwright, no browser — joins directly via the
 * Zoom Meeting SDK for Linux (zoom_sdk/zoom_join.py) and reads mixed PCM
 * audio out of a named FIFO with ffmpeg.
 *
 * NOTE: the Zoom Meeting SDK's raw audio subscription only supports
 * 32kHz or 48kHz sampling (see zoom_join.py) — 16kHz, used by the
 * Teams/Meet browser path, is not an option here. ffmpeg is started
 * with `-ar` matching ZOOM_SDK_SAMPLE_RATE below.
 */

import { EventEmitter } from 'events';
import { ChildProcess, spawn, execFileSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as readline from 'readline';
import { Meeting } from '../types.js';

const ZOOM_SDK_SAMPLE_RATE = 32000;
const ZOOM_JOIN_SCRIPT = process.env.ZOOM_JOIN_SCRIPT || '/app/zoom_sdk/zoom_join.py';

async function reportStatus(
  meetingId: string,
  status: string,
  extra?: Record<string, unknown>
): Promise<void> {
  const firmApiUrl = process.env.FIRM_API_URL;
  const botSecret = process.env.BOT_CALLBACK_SECRET || '';
  const numericId = parseInt(process.env.MEETING_ID || meetingId, 10);
  if (!firmApiUrl) {
    console.log(`[ZoomSDKBot] FIRM_API_URL not set — skipping callback (status: ${status})`);
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
      console.error(`[ZoomSDKBot] Callback failed: ${res.status} ${await res.text()}`);
    } else {
      console.log(`[ZoomSDKBot] Callback sent: ${status}`);
    }
  } catch (err) {
    console.error(`[ZoomSDKBot] Callback error: ${err}`);
  }
}

/**
 * Extract the numeric Zoom meeting number and password from a Zoom join
 * URL, e.g. https://zoom.us/j/1234567890?pwd=abcXYZ
 */
function parseZoomUrl(url: string): { meetingNumber: string; password: string } {
  const parsed = new URL(url);
  const match = parsed.pathname.match(/\/(?:j|wc)\/(\d+)/);
  const meetingNumber = match?.[1];
  const password = parsed.searchParams.get('pwd') || '';
  if (!meetingNumber) {
    throw new Error(`[ZoomSDKBot] Could not extract meeting number from URL: ${url}`);
  }
  return { meetingNumber, password };
}

export interface ZoomSDKBotEvents {
  'joined': () => void;
  'recording-started': () => void;
  'recording-stopped': (audioPath: string) => void;
  'error': (error: Error) => void;
}

export class ZoomSDKBot extends EventEmitter {
  private meeting: Meeting;
  private recordingsDir: string;
  private fifoPath: string;
  private _audioPath: string;
  private ffmpegProcess: ChildProcess | null = null;
  private pythonProcess: ChildProcess | null = null;
  private _isInMeeting = false;
  private _isRecording = false;
  private _stopping = false;
  private _joinPromise: Promise<void> | null = null;
  private _resolveJoin: (() => void) | null = null;
  private _rejectJoin: ((err: Error) => void) | null = null;

  constructor(meeting: Meeting, recordingsDir: string) {
    super();
    this.meeting = meeting;
    this.recordingsDir = recordingsDir;
    this.fifoPath = `/tmp/zoom_pcm_${meeting.id}_${Date.now()}.fifo`;
    this._audioPath = path.join(recordingsDir, `${meeting.id}.wav`);
  }

  get audioPath(): string {
    return this._audioPath;
  }

  get isInMeeting(): boolean {
    return this._isInMeeting;
  }

  get isRecording(): boolean {
    return this._isRecording;
  }

  async join(): Promise<void> {
    const { meetingNumber, password } = parseZoomUrl(this.meeting.url);

    const sdkKey = process.env.FIRM_ZOOM_SDK_KEY || '';
    const sdkSecret = process.env.FIRM_ZOOM_SDK_SECRET || '';
    if (!sdkKey || !sdkSecret) {
      const err = new Error('[ZoomSDKBot] FIRM_ZOOM_SDK_KEY / FIRM_ZOOM_SDK_SECRET not set');
      this.emit('error', err);
      throw err;
    }

    execFileSync('mkfifo', [this.fifoPath]);
    console.log(`[ZoomSDKBot] Created FIFO: ${this.fifoPath}`);

    this._joinPromise = new Promise<void>((resolve, reject) => {
      this._resolveJoin = resolve;
      this._rejectJoin = reject;
    });

    this.startFfmpeg();
    this.startPython(meetingNumber, password, sdkKey, sdkSecret);

    return this._joinPromise;
  }

  private startFfmpeg(): void {
    // Same fast-exit-detection / stdio pattern as MeetingBot.startRecording().
    // The first -ar (input option, before -i) declares the FIFO's actual raw
    // PCM rate — the Zoom SDK only emits 32kHz or 48kHz, never 16kHz. The
    // second -ar (output option, after -i) resamples down to 16kHz so the
    // WAV output matches the Teams/Meet path's format for downstream
    // transcription.
    this.ffmpegProcess = spawn('ffmpeg', [
      '-f', 's16le',
      '-ar', String(ZOOM_SDK_SAMPLE_RATE),
      '-ac', '1',
      '-i', this.fifoPath,
      '-ar', '16000',
      '-af', 'silencedetect=noise=-40dB:d=60',
      '-y',
      this._audioPath,
    ], {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: { ...process.env },
    });

    this.ffmpegProcess.on('error', (err) => {
      console.error(`[ZoomSDKBot] FFmpeg failed to start: ${err.message}`);
      this.emit('error', new Error(`FFmpeg failed: ${err.message}`));
      this._rejectJoin?.(err);
    });

    this.ffmpegProcess.stderr?.on('data', (data: Buffer) => {
      const trimmed = data.toString().trim();
      if (trimmed && !trimmed.startsWith('size=') && !trimmed.startsWith('frame=')) {
        console.log(`[ZoomSDKBot][FFmpeg] ${trimmed}`);
      }
    });

    const ffmpegStartTime = Date.now();
    this.ffmpegProcess.on('exit', (code) => {
      console.log(`[ZoomSDKBot] FFmpeg exited with code ${code}`);
      const elapsed = Date.now() - ffmpegStartTime;
      if (elapsed < 5000 && !this._stopping) {
        const errorMsg = `FFmpeg exited after ${elapsed}ms (code ${code})`;
        console.error(`[ZoomSDKBot] Fast-exit detected: ${errorMsg}`);
        this.emit('error', new Error(errorMsg));
        this._rejectJoin?.(new Error(errorMsg));
      }
    });

    console.log(`[ZoomSDKBot] FFmpeg recording started → ${this._audioPath} (PID: ${this.ffmpegProcess.pid})`);
  }

  private startPython(meetingNumber: string, password: string, sdkKey: string, sdkSecret: string): void {
    this.pythonProcess = spawn('python3', [
      ZOOM_JOIN_SCRIPT,
      '--meeting-id', meetingNumber,
      '--password', password,
      '--display-name', this.meeting.botName,
      '--sdk-key', sdkKey,
      '--sdk-secret', sdkSecret,
      '--fifo-path', this.fifoPath,
      '--audio-sample-rate', String(ZOOM_SDK_SAMPLE_RATE),
    ], {
      stdio: ['ignore', 'ignore', 'pipe'],
      env: { ...process.env },
    });

    this.pythonProcess.on('error', (err) => {
      console.error(`[ZoomSDKBot] Python process failed to start: ${err.message}`);
      this.emit('error', new Error(`Zoom SDK process failed: ${err.message}`));
      this._rejectJoin?.(err);
    });

    const rl = readline.createInterface({ input: this.pythonProcess.stderr! });
    rl.on('line', (line: string) => {
      console.log(`[ZoomSDK] ${line}`);

      if (line.includes('[ZoomSDK] In meeting')) {
        this._isInMeeting = true;
        this.emit('joined');
        this._resolveJoin?.();
      } else if (line.includes('[ZoomSDK] Audio started')) {
        this._isRecording = true;
        this.emit('recording-started');
        reportStatus(this.meeting.id, 'recording', { participants: [] }).catch(console.error);
      }
    });

    this.pythonProcess.on('exit', (code) => {
      console.log(`[ZoomSDKBot] Python process exited with code ${code}`);
      const hadRecorded = this._isRecording;
      this._isRecording = false;
      this._isInMeeting = false;

      if (!hadRecorded && code !== 0) {
        const err = new Error(`Zoom SDK process exited with code ${code} before recording started`);
        this.emit('error', err);
        this._rejectJoin?.(err);
        return;
      }

      this.finishRecording();
    });
  }

  private finishRecording(): void {
    if (this.ffmpegProcess && !this.ffmpegProcess.killed) {
      // SIGINT = graceful ffmpeg quit, writes proper WAV headers.
      this.ffmpegProcess.kill('SIGINT');
    }

    const ffmpeg = this.ffmpegProcess;
    const emitStopped = () => {
      if (fs.existsSync(this.fifoPath)) {
        try { fs.unlinkSync(this.fifoPath); } catch { /* best effort */ }
      }
      this.emit('recording-stopped', this._audioPath);
    };

    if (ffmpeg && !ffmpeg.killed) {
      const timeout = setTimeout(() => {
        ffmpeg.kill('SIGKILL');
      }, 5000);
      ffmpeg.on('exit', () => {
        clearTimeout(timeout);
        emitStopped();
      });
    } else {
      emitStopped();
    }
  }

  async stop(): Promise<void> {
    this._stopping = true;
    if (this.pythonProcess && !this.pythonProcess.killed) {
      this.pythonProcess.kill('SIGTERM');
      await new Promise<void>((resolve) => {
        const timeout = setTimeout(() => {
          this.pythonProcess?.kill('SIGKILL');
          resolve();
        }, 10000);
        this.pythonProcess!.on('exit', () => {
          clearTimeout(timeout);
          resolve();
        });
      });
    }
  }
}
