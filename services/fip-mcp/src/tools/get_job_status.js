import {
  BedrockAgentClient,
  GetIngestionJobCommand,
} from '@aws-sdk/client-bedrock-agent';

const mgmtClient = new BedrockAgentClient({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

// In-memory job map — Phase 0, single container, restarts are rare
// Maps job_id → { type, kb_id, bedrock_job_id, data_source_id, initiated_by, created_at }
export const jobMap = new Map();

const STATUS_MAP = {
  STARTING: 'running',
  IN_PROGRESS: 'running',
  STOPPING: 'running',
  STOPPED: 'failed',
  FAILED: 'failed',
  COMPLETE: 'complete',
};

export async function getJobStatus(args, user) {
  const { job_id } = args;
  if (!job_id) throw { code: 'JOB_ID_REQUIRED', status: 400, message: 'job_id is required' };

  const jobMeta = jobMap.get(job_id);
  if (!jobMeta) {
    // Could be a job from a previous server instance (pre-restart)
    return {
      status: 'unknown',
      error: 'Job not found — may have been lost on server restart (Phase 0 in-memory tracking)',
    };
  }

  if (jobMeta.type === 'kb-ingest') {
    if (!jobMeta.bedrock_job_id) {
      return { status: 'queued', percent_complete: 0 };
    }

    const command = new GetIngestionJobCommand({
      knowledgeBaseId: jobMeta.kb_id,
      dataSourceId: jobMeta.data_source_id,
      ingestionJobId: jobMeta.bedrock_job_id,
    });

    const response = await mgmtClient.send(command);
    const job = response.ingestionJob;
    const status = STATUS_MAP[job?.status] ?? 'unknown';

    const result = {
      status,
      percent_complete: status === 'complete' ? 100 : status === 'running' ? 50 : 0,
    };

    if (status === 'complete') {
      result.result = {
        statistics: job.statistics,
        started_at: job.startedAt,
        updated_at: job.updatedAt,
      };
    }

    if (status === 'failed') {
      result.error = job.failureReasons?.join('; ') ?? 'Unknown failure';
    }

    return result;
  }

  // Future job types routed here
  return { status: 'unknown', error: `Unknown job type: ${jobMeta.type}` };
}
