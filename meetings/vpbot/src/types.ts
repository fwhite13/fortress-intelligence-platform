/**
 * Core types for the Meeting Assistant
 */

export type MeetingPlatform = 'teams' | 'zoom' | 'google-meet' | 'unknown';

export type MeetingStatus = 
  | 'pending'      // Created but not started
  | 'joining'      // Bot is joining the meeting
  | 'recording'    // Active recording
  | 'processing'   // Meeting ended, processing audio
  | 'transcribing' // Transcription in progress
  | 'summarizing'  // AI summary in progress
  | 'completed'    // All done
  | 'error';       // Something went wrong

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
  overview: string;       // keep existing
  summaryText: string;    // full structured markdown
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

// Platform-specific selectors and logic
export interface PlatformConfig {
  name: MeetingPlatform;
  urlPatterns: RegExp[];
  joinFlow: (page: any, botName: string) => Promise<void>;
}

// Audio capture configuration
export interface AudioConfig {
  sampleRate: number;
  channels: number;
  format: 'webm' | 'wav' | 'mp3';
}

// AWS configuration
export interface AWSConfig {
  region: string;
  accessKeyId: string;
  secretAccessKey: string;
  s3Bucket: string;
  bedrockModelId: string;
}
