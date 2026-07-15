/**
 * Core Meeting Bot - Playwright browser automation for meeting capture
 *
 * Audio capture: Uses ffmpeg to record from PulseAudio virtual sink.
 * The browser routes all WebRTC audio through PulseAudio, and ffmpeg
 * captures from the monitor source. This works for Teams, Zoom, and Meet.
 */
import { EventEmitter } from 'events';
import { Meeting, MeetingPlatform, MeetingStatus } from '../types.js';
export interface MeetingBotEvents {
    'status-change': (status: MeetingStatus) => void;
    'recording-started': () => void;
    'recording-stopped': (audioPath: string) => void;
    'error': (error: Error) => void;
    'meeting-ended': () => void;
}
export declare class MeetingBot extends EventEmitter {
    private browser;
    private context;
    private page;
    private meeting;
    private recordingsDir;
    private isRecording;
    private ffmpegProcess;
    private audioPath;
    private _hardTimeout;
    private _endPollInterval;
    private _monitorInterval;
    private _recordingStartTime;
    private _silenceStartTime;
    constructor(meeting: Meeting, recordingsDir: string);
    /**
     * Detect the meeting platform from the URL
     */
    static detectPlatform(url: string): MeetingPlatform;
    /**
     * Launch browser and join the meeting
     */
    join(): Promise<void>;
    /**
     * Start capturing audio from the meeting using ffmpeg + PulseAudio.
     *
     * Instead of trying to capture from DOM audio/video elements (which don't
     * carry WebRTC audio), we record from PulseAudio's monitor source.
     * The browser outputs all meeting audio through PulseAudio, and ffmpeg
     * captures everything from the default monitor source.
     */
    private startRecording;
    /**
     * Monitor if the meeting has ended
     */
    private monitorMeetingStatus;
    /**
     * Stop recording and save audio.
     * Sends SIGINT (graceful quit) to ffmpeg so it writes the WAV header properly.
     *
     * @param reason  Optional stop reason for logging (e.g. 'meeting-ended-detected',
     *                'max-recording-timeout', 'participant-count-timeout', 'api-request')
     */
    stop(reason?: string): Promise<string>;
    /**
     * Force leave the meeting and clean up
     */
    cleanup(): Promise<void>;
    /**
     * Get current recording status
     */
    isCurrentlyRecording(): boolean;
}
//# sourceMappingURL=meeting-bot.d.ts.map