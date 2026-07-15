/**
 * AI Summary generation using Claude via Bedrock
 */
import { Transcript, MeetingSummary } from '../types.js';
export interface SummaryConfig {
    region: string;
    modelId: string;
}
export declare class SummaryService {
    private client;
    private modelId;
    constructor(config: SummaryConfig);
    /**
     * Generate a meeting summary from a transcript
     */
    generateSummary(transcript: Transcript, orgContextNames?: string[]): Promise<MeetingSummary>;
    /**
     * Build the prompt for Claude
     */
    private buildPrompt;
    /**
     * Invoke the Bedrock model
     */
    private invokeModel;
    /**
     * Parse Claude's response into MeetingSummary
     */
    private parseResponse;
    /**
     * Format summary as markdown
     */
    formatAsMarkdown(summary: MeetingSummary): string;
    /**
     * Format summary as plain text
     */
    formatAsText(summary: MeetingSummary): string;
}
//# sourceMappingURL=summarize.d.ts.map