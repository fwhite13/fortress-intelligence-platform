import {
  BedrockAgentClient,
  StartIngestionJobCommand,
} from '@aws-sdk/client-bedrock-agent';
import { S3Client, PutObjectCommand } from '@aws-sdk/client-s3';
import { v4 as uuidv4 } from 'uuid';
import { getKb } from '../config/kb-inventory.js';
import { getEntitlements } from './list_kbs.js';
import { jobMap } from './get_job_status.js';

const mgmtClient = new BedrockAgentClient({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

const s3Client = new S3Client({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

/**
 * Determine the scoping ID for S3 path construction.
 * - Personal: user.user_id (Entra OID)
 * - Team: caller-supplied metadata.team_id
 * - Project: caller-supplied metadata.project_id
 * - Corp/NEXUS: empty string (no sub-path scoping)
 */
function getScopingId(kb, user, metadata) {
  switch (kb.kb_type) {
    case 'personal': return user.user_id;
    case 'team': return metadata.team_id ?? '';
    case 'project': return metadata.project_id ?? '';
    default: return '';
  }
}

/**
 * Derive file extension from content_type metadata field, defaulting to .txt.
 */
function getExtension(contentType) {
  if (!contentType) return 'txt';
  if (contentType.includes('markdown') || contentType.includes('md')) return 'md';
  if (contentType.includes('html')) return 'html';
  if (contentType.includes('json')) return 'json';
  if (contentType.includes('pdf')) return 'pdf';
  return 'txt';
}

export async function addToKb(args, user) {
  const { kb_id, content, metadata } = args;

  if (!kb_id) throw { code: 'KB_ID_REQUIRED', status: 400, message: 'kb_id is required' };
  if (!content) throw { code: 'CONTENT_REQUIRED', status: 400, message: 'content is required' };
  if (!metadata?.source) throw { code: 'METADATA_REQUIRED', status: 400, message: 'metadata.source is required' };
  if (!metadata?.created_by) throw { code: 'METADATA_REQUIRED', status: 400, message: 'metadata.created_by is required' };

  const kb = getKb(kb_id);
  if (!kb) {
    throw { code: 'UNKNOWN_KB', status: 400, message: `Unknown KB: ${kb_id}` };
  }

  // Project KB requires project_id in metadata
  if (kb.kb_type === 'project' && !metadata.project_id) {
    throw { code: 'PROJECT_ID_REQUIRED', status: 400, message: 'metadata.project_id is required for Project KB' };
  }

  // Team KB requires team_id in metadata
  if (kb.kb_type === 'team' && !metadata.team_id) {
    throw { code: 'TEAM_ID_REQUIRED', status: 400, message: 'metadata.team_id is required for Team KB' };
  }

  // Corp KB and NEXUS KB require forge-kb-admin role for write
  if ((kb.kb_type === 'corp' || kb.kb_type === 'nexus') && !user.roles.includes('forge-kb-admin')) {
    throw { code: 'WRITE_NOT_ENTITLED', status: 403, message: `Writing to ${kb.kb_type} KB requires forge-kb-admin role` };
  }

  // Check write entitlement
  const entitlements = await getEntitlements(user);
  const entitled = entitlements.find(e => e.kb_id === kb_id && e.write);
  if (!entitled) {
    throw { code: 'WRITE_NOT_ENTITLED', status: 403, message: `Not entitled to write to KB: ${kb_id}` };
  }

  if (!kb.data_source_id) {
    throw { code: 'DATA_SOURCE_UNAVAILABLE', status: 503, message: `KB ${kb_id} data source ID not configured` };
  }

  // Write content to S3 before triggering ingestion
  const scopingId = getScopingId(kb, user, metadata);
  const ext = getExtension(metadata.content_type);
  const timestamp = Date.now();
  const safeSource = (metadata.source ?? 'document').replace(/[^a-zA-Z0-9_-]/g, '_');
  const filename = `${safeSource}-${timestamp}.${ext}`;
  const s3Key = scopingId
    ? `${kb.s3_prefix}/${scopingId}/${filename}`
    : `${kb.s3_prefix}/${filename}`;

  // Write main content file
  await s3Client.send(new PutObjectCommand({
    Bucket: kb.s3_bucket,
    Key: s3Key,
    Body: content,
    ContentType: metadata.content_type ?? 'text/plain',
  }));

  // Write metadata sidecar for all KB types except Corp and NEXUS
  if (kb.metadata_key) {
    const scopingValue = scopingId;
    const sidecar = JSON.stringify({
      metadataAttributes: {
        [kb.metadata_key]: scopingValue,
      },
    });
    await s3Client.send(new PutObjectCommand({
      Bucket: kb.s3_bucket,
      Key: `${s3Key}.metadata.json`,
      Body: sidecar,
      ContentType: 'application/json',
    }));
  }

  const clientToken = uuidv4();

  const command = new StartIngestionJobCommand({
    knowledgeBaseId: kb_id,
    dataSourceId: kb.data_source_id,
    clientToken,
    description: `Ingestion triggered by ${user.user_id} via fip-mcp at ${new Date().toISOString()}`,
  });

  const response = await mgmtClient.send(command);
  const bedrockJobId = response.ingestionJob?.ingestionJobId;
  const job_id = `kb-ingest-${clientToken}`;

  // Track job in memory
  jobMap.set(job_id, {
    type: 'kb-ingest',
    kb_id,
    bedrock_job_id: bedrockJobId,
    data_source_id: kb.data_source_id,
    initiated_by: user.user_id,
    created_at: new Date().toISOString(),
  });

  return {
    status: 'queued',
    job_id,
    kb_id,
  };
}
