/**
 * Audio conversion utilities using ffmpeg
 */
/**
 * Convert audio file to WAV format suitable for Amazon Transcribe
 *
 * Amazon Transcribe requirements:
 * - Sample rate: 8000-48000 Hz (16000 recommended for phone calls)
 * - Channels: 1 or 2
 * - Bit depth: 16-bit PCM
 */
export declare function convertToWav(inputPath: string): Promise<string>;
/**
 * Get audio file duration in seconds
 */
export declare function getAudioDuration(filePath: string): Promise<number>;
/**
 * Extract audio from video file
 */
export declare function extractAudio(videoPath: string): Promise<string>;
/**
 * Split audio file into chunks
 * Useful for very long recordings that exceed Transcribe limits
 */
export declare function splitAudio(inputPath: string, chunkDurationSeconds?: number): Promise<string[]>;
/**
 * Check if ffmpeg is available
 */
export declare function checkFfmpegAvailable(): Promise<boolean>;
//# sourceMappingURL=audio.d.ts.map