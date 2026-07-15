/**
 * Core types for the Meeting Assistant
 */
export type MeetingPlatform = 'teams' | 'zoom' | 'google-meet' | 'unknown';
export type MeetingStatus = 'pending' | 'joining' | 'recording' | 'processing' | 'transcribing' | 'summarizing' | 'completed' | 'error';
export interface Meeting {
    id: string;
    url: string;
    platform: MeetingPlatform;
    botName: string;
    status: MeetingStatus;
    createdAt: Date;
    startedAt?: Date;
    endedAt?: Date;
    audioPath?: string;
    s3AudioKey?: string;
    transcribeJobName?: string;
    transcript?: Transcript;
    summary?: MeetingSummary;
    error?: string;
}
export interface TranscriptSegment {
    speakerLabel: string;
    startTime: number;
    endTime: number;
    content: string;
    confidence: number;
}
export interface Transcript {
    segments: TranscriptSegment[];
    fullText: string;
    speakers: string[];
    duration: number;
}
export interface ActionItem {
    description: string;
    owner?: string;
    deadline?: string;
}
export interface MeetingSummary {
    overview: string;
    summaryText: string;
    keyDecisions: string[];
    actionItems: ActionItem[];
    openQuestions: string[];
    followUps: string[];
    participants: string[];
    generatedAt: Date;
}
export interface JoinMeetingRequest {
    url: string;
    name?: string;
}
export interface JoinMeetingResponse {
    meetingId: string;
    status: MeetingStatus;
    message: string;
}
export interface MeetingStatusResponse {
    meeting: Meeting;
}
export interface MeetingListResponse {
    meetings: Meeting[];
}
export interface StopMeetingResponse {
    meetingId: string;
    status: MeetingStatus;
    message: string;
}
export interface PlatformConfig {
    name: MeetingPlatform;
    urlPatterns: RegExp[];
    joinFlow: (page: any, botName: string) => Promise<void>;
}
export interface AudioConfig {
    sampleRate: number;
    channels: number;
    format: 'webm' | 'wav' | 'mp3';
}
export interface AWSConfig {
    region: string;
    accessKeyId: string;
    secretAccessKey: string;
    s3Bucket: string;
    bedrockModelId: string;
}
//# sourceMappingURL=types.d.ts.map