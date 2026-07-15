/**
 * S3 operations for audio file storage
 */
export declare class S3Service {
    private client;
    private bucket;
    constructor(region: string, bucket: string);
    /**
     * Upload an audio file to S3
     */
    uploadAudio(filePath: string, meetingId: string): Promise<string>;
    /**
     * Upload a WAV file (for Transcribe)
     */
    uploadWav(filePath: string, meetingId: string): Promise<string>;
    /**
     * Upload a file with an explicit S3 key (for FIRM key conventions)
     */
    uploadWithKey(filePath: string, key: string): Promise<string>;
    /**
     * Save JSON string to S3 with an explicit key
     */
    saveJsonWithKey(jsonStr: string, key: string): Promise<string>;
    /**
     * Get the S3 URI for a file
     */
    getS3Uri(key: string): string;
    /**
     * Download a file from S3
     */
    download(key: string): Promise<string>;
    /**
     * Download a binary file from S3 to a local temp path
     */
    downloadToFile(key: string, localPath: string): Promise<void>;
    /**
     * Save transcript JSON from Transcribe
     */
    saveTranscript(meetingId: string, transcriptJson: string): Promise<string>;
    /**
     * Save summary to S3
     */
    saveSummary(meetingId: string, summary: string): Promise<string>;
    /**
     * Find the first object key matching a prefix
     */
    findFirstKey(prefix: string, extension?: string): Promise<string | null>;
    /**
     * Delete a file from S3
     */
    delete(key: string): Promise<void>;
    /**
     * Get content type based on file extension
     */
    private getContentType;
}
//# sourceMappingURL=s3.d.ts.map