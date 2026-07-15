/**
 * S3 operations for audio file storage
 */
import { S3Client, PutObjectCommand, GetObjectCommand, DeleteObjectCommand, ListObjectsV2Command, } from '@aws-sdk/client-s3';
import * as fs from 'fs';
import * as path from 'path';
export class S3Service {
    client;
    bucket;
    constructor(region, bucket) {
        this.client = new S3Client({ region });
        this.bucket = bucket;
    }
    /**
     * Upload an audio file to S3
     */
    async uploadAudio(filePath, meetingId) {
        const fileContent = fs.readFileSync(filePath);
        const extension = path.extname(filePath);
        const key = `meetings/${meetingId}/audio${extension}`;
        const contentType = this.getContentType(extension);
        const command = new PutObjectCommand({
            Bucket: this.bucket,
            Key: key,
            Body: fileContent,
            ContentType: contentType,
        });
        await this.client.send(command);
        console.log(`[S3] Uploaded audio to s3://${this.bucket}/${key}`);
        return key;
    }
    /**
     * Upload a WAV file (for Transcribe)
     */
    async uploadWav(filePath, meetingId) {
        const fileContent = fs.readFileSync(filePath);
        const key = `meetings/${meetingId}/audio.wav`;
        const command = new PutObjectCommand({
            Bucket: this.bucket,
            Key: key,
            Body: fileContent,
            ContentType: 'audio/wav',
        });
        await this.client.send(command);
        console.log(`[S3] Uploaded WAV to s3://${this.bucket}/${key}`);
        return key;
    }
    /**
     * Upload a file with an explicit S3 key (for FIRM key conventions)
     */
    async uploadWithKey(filePath, key) {
        const fileContent = fs.readFileSync(filePath);
        const extension = path.extname(filePath);
        const contentType = this.getContentType(extension);
        const command = new PutObjectCommand({
            Bucket: this.bucket,
            Key: key,
            Body: fileContent,
            ContentType: contentType,
        });
        await this.client.send(command);
        console.log(`[S3] Uploaded to s3://${this.bucket}/${key}`);
        return key;
    }
    /**
     * Save JSON string to S3 with an explicit key
     */
    async saveJsonWithKey(jsonStr, key) {
        const command = new PutObjectCommand({
            Bucket: this.bucket,
            Key: key,
            Body: jsonStr,
            ContentType: 'application/json',
        });
        await this.client.send(command);
        console.log(`[S3] Saved JSON to s3://${this.bucket}/${key}`);
        return key;
    }
    /**
     * Get the S3 URI for a file
     */
    getS3Uri(key) {
        return `s3://${this.bucket}/${key}`;
    }
    /**
     * Download a file from S3
     */
    async download(key) {
        const command = new GetObjectCommand({
            Bucket: this.bucket,
            Key: key,
        });
        const response = await this.client.send(command);
        const bodyContents = await response.Body?.transformToString();
        return bodyContents || '';
    }
    /**
     * Download a binary file from S3 to a local temp path
     */
    async downloadToFile(key, localPath) {
        const command = new GetObjectCommand({
            Bucket: this.bucket,
            Key: key,
        });
        const response = await this.client.send(command);
        if (!response.Body)
            throw new Error(`S3 key not found: ${key}`);
        const { pipeline } = await import('stream/promises');
        const { createWriteStream } = await import('fs');
        const readable = response.Body;
        await pipeline(readable, createWriteStream(localPath));
    }
    /**
     * Save transcript JSON from Transcribe
     */
    async saveTranscript(meetingId, transcriptJson) {
        const key = `transcripts/${meetingId}.json`;
        const command = new PutObjectCommand({
            Bucket: this.bucket,
            Key: key,
            Body: transcriptJson,
            ContentType: 'application/json',
        });
        await this.client.send(command);
        console.log(`[S3] Saved transcript to s3://${this.bucket}/${key}`);
        return key;
    }
    /**
     * Save summary to S3
     */
    async saveSummary(meetingId, summary) {
        const key = `summaries/${meetingId}.md`;
        const command = new PutObjectCommand({
            Bucket: this.bucket,
            Key: key,
            Body: summary,
            ContentType: 'text/markdown',
        });
        await this.client.send(command);
        console.log(`[S3] Saved summary to s3://${this.bucket}/${key}`);
        return key;
    }
    /**
     * Find the first object key matching a prefix
     */
    async findFirstKey(prefix, extension) {
        const command = new ListObjectsV2Command({
            Bucket: this.bucket,
            Prefix: prefix,
            MaxKeys: 10,
        });
        const response = await this.client.send(command);
        const contents = response.Contents;
        if (contents && contents.length > 0) {
            if (extension) {
                const match = contents.find(obj => obj.Key?.endsWith(extension));
                return match?.Key || null;
            }
            return contents[0].Key || null;
        }
        return null;
    }
    /**
     * Delete a file from S3
     */
    async delete(key) {
        const command = new DeleteObjectCommand({
            Bucket: this.bucket,
            Key: key,
        });
        await this.client.send(command);
        console.log(`[S3] Deleted s3://${this.bucket}/${key}`);
    }
    /**
     * Get content type based on file extension
     */
    getContentType(extension) {
        const types = {
            '.webm': 'audio/webm',
            '.wav': 'audio/wav',
            '.mp3': 'audio/mpeg',
            '.flac': 'audio/flac',
            '.mp4': 'audio/mp4',
            '.ogg': 'audio/ogg',
        };
        return types[extension.toLowerCase()] || 'application/octet-stream';
    }
}
//# sourceMappingURL=s3.js.map