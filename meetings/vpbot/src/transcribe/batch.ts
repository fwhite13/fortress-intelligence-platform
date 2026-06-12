import { BatchClient, SubmitJobCommand } from "@aws-sdk/client-batch";

const JOB_QUEUE = "firm-transcription-queue";
const JOB_DEFINITION = "firm-transcription-job";
const AWS_REGION = process.env.AWS_REGION || "us-east-1";

export class BatchTranscriptionService {
  private client: BatchClient;

  constructor() {
    this.client = new BatchClient({ region: AWS_REGION });
  }

  async submitTranscriptionJob(meetingId: number, audioS3Key: string): Promise<string> {
    const jobName = `transcribe-meeting-${meetingId}-${Date.now()}`;
    const command = new SubmitJobCommand({
      jobName,
      jobQueue: JOB_QUEUE,
      jobDefinition: JOB_DEFINITION,
      containerOverrides: {
        environment: [
          { name: "MEETING_ID", value: String(meetingId) },
          { name: "AUDIO_S3_KEY", value: audioS3Key },
        ],
      },
    });
    const response = await this.client.send(command);
    console.log(`[Batch] Submitted job ${response.jobId} for meeting ${meetingId}`);
    return response.jobId!;
  }
}
