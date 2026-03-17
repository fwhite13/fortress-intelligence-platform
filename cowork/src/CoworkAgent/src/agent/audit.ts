import { CloudWatchLogsClient, PutLogEventsCommand } from '@aws-sdk/client-cloudwatch-logs';

const client = new CloudWatchLogsClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
const LOG_GROUP = '/cowork/tasks';

interface AuditEntry {
  event: string;
  taskId?: string;
  userId?: string;
  userEmail?: string;
  prompt?: string;
  data?: any;
}

export async function auditLog(entry: AuditEntry): Promise<void> {
  const streamName = entry.taskId ?? 'system';
  const message = JSON.stringify({
    timestamp: new Date().toISOString(),
    ...entry,
    // Redact: never log file contents, API keys, or JWTs
    prompt: entry.prompt ? entry.prompt.slice(0, 200) : undefined,
  });

  try {
    await client.send(new PutLogEventsCommand({
      logGroupName:  LOG_GROUP,
      logStreamName: streamName,
      logEvents: [{
        timestamp: Date.now(),
        message,
      }],
    }));
  } catch (err: any) {
    // Non-fatal — audit failure must not break task execution
    console.error('Audit log failed:', err.message);
  }
}
