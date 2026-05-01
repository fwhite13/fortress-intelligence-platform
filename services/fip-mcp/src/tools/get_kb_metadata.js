import {
  BedrockAgentClient,
  GetKnowledgeBaseCommand,
  ListIngestionJobsCommand,
} from '@aws-sdk/client-bedrock-agent';
import { getKb } from '../config/kb-inventory.js';
import { getEntitlements } from './list_kbs.js';

const mgmtClient = new BedrockAgentClient({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

export async function getKbMetadata(args, user) {
  const { kb_id } = args;
  if (!kb_id) throw { code: 'KB_ID_REQUIRED', status: 400, message: 'kb_id is required' };

  const kb = getKb(kb_id);
  if (!kb) {
    throw { code: 'UNKNOWN_KB', status: 400, message: `Unknown KB: ${kb_id}` };
  }

  // Check read entitlement
  const entitlements = await getEntitlements(user);
  const entitled = entitlements.find(e => e.kb_id === kb_id && e.read);
  if (!entitled) {
    throw { code: 'NOT_ENTITLED', status: 403, message: `Not entitled to read KB: ${kb_id}` };
  }

  // GetKnowledgeBase — management plane API
  await mgmtClient.send(new GetKnowledgeBaseCommand({
    knowledgeBaseId: kb_id,
  }));

  // ListIngestionJobs — get most recent completed job for last_updated
  let last_updated = null;
  let document_count = 0;

  if (kb.data_source_id) {
    try {
      const jobsResponse = await mgmtClient.send(new ListIngestionJobsCommand({
        knowledgeBaseId: kb_id,
        dataSourceId: kb.data_source_id,
        maxResults: 10,
        sortBy: { attribute: 'STARTED_AT', order: 'DESCENDING' },
      }));

      const completedJobs = (jobsResponse.ingestionJobSummaries ?? [])
        .filter(j => j.status === 'COMPLETE');

      if (completedJobs.length > 0) {
        last_updated = completedJobs[0].updatedAt?.toISOString() ?? null;
        // Sum statistics for document count approximation
        document_count = completedJobs.reduce((acc, j) => acc + (j.statistics?.numberOfDocumentsIndexed ?? 0), 0);
      }
    } catch (e) {
      // Non-fatal — metadata still returns, just without job stats
      console.warn(`[fip-mcp] Could not list ingestion jobs for ${kb_id}: ${e.message}`);
    }
  }

  return {
    kb_id,
    kb_type: kb.kb_type,
    document_count,
    last_updated,
    data_source_id: kb.data_source_id,
  };
}
