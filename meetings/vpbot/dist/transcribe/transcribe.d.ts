/**
 * Whisper transcription service using faster-whisper (Python subprocess)
 * Replaces AWS Transcribe for firm-vpbot
 */
import { Transcript } from '../types.js';
export interface WhisperConfig {
    region: string;
    s3Bucket: string;
    modelSize?: string;
    language?: string;
    device?: string;
    computeType?: string;
    initialPrompt?: string;
}
export interface WhisperSegment {
    speakerLabel: string;
    speakerName: string;
    text: string;
    startTimeMs: number;
    endTimeMs: number;
}
export declare class WhisperService {
    private modelSize;
    private language;
    private initialPrompt;
    constructor(config: WhisperConfig);
    /**
     * Transcribe an audio file using faster-whisper (Python subprocess)
     */
    transcribe(audioPath: string, meetingId: string, initialPrompt?: string): Promise<Transcript>;
    /**
     * Convert Whisper segments to our Transcript type
     */
    private convertToTranscript;
}
export { WhisperService as TranscribeService };
//# sourceMappingURL=transcribe.d.ts.map