import { S3Client, ListObjectsV2Command } from '@aws-sdk/client-s3';
import { getEntitlements } from './list_kbs.js';
import { KB_INVENTORY, KB_TYPE } from '../config/kb-inventory.js';

const s3Client = new S3Client({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

const KB_BUCKET = process.env.KB_BUCKET ?? 'fortress-tools';

/**
 * Resolve the S3 prefix for a given KB type.
 * Personal: kb-docs/personal/{user.user_id}/
 * Team:     kb-docs/teams/{team_id}/
 * Corp:     kb-docs/fortress/
 */
function getS3Prefix(kbType, user, args) {
  switch (kbType) {
    case KB_TYPE.PERSONAL:
      return `kb-docs/personal/${user.user_id}/`;
    case KB_TYPE.TEAM:
      if (!args.team_id) throw { code: 'TEAM_ID_REQUIRED', status: 400, message: 'team_id is required for Team KB' };
      return `kb-docs/teams/${args.team_id}/`;
    case KB_TYPE.CORP:
      return 'kb-docs/fortress/';
    default:
      throw { code: 'UNSUPPORTED_KB_TYPE', status: 400, message: `File listing not supported for KB type: ${kbType}` };
  }
}

export async function listKbFiles(args, user) {
  const { kb_id, team_id } = args;
  if (!kb_id) throw { code: 'KB_ID_REQUIRED', status: 400, message: 'kb_id is required' };

  const kb = KB_INVENTORY[kb_id];
  if (!kb) throw { code: 'UNKNOWN_KB', status: 400, message: `Unknown KB: ${kb_id}` };

  // Check read entitlement
  const entitlements = await getEntitlements(user);
  const entitled = entitlements.find(e => e.kb_id === kb_id && e.read);
  if (!entitled) throw { code: 'NOT_ENTITLED', status: 403, message: `Not entitled to read KB: ${kb_id}` };

  const prefix = getS3Prefix(kb.kb_type, user, { team_id });

  const files = [];
  let continuationToken;

  do {
    const resp = await s3Client.send(new ListObjectsV2Command({
      Bucket: KB_BUCKET,
      Prefix: prefix,
      ContinuationToken: continuationToken,
    }));

    for (const obj of (resp.Contents ?? [])) {
      const filename = obj.Key.split('/').pop();
      // Skip empty keys, companion metadata files, and BDA sidecar text files
      if (!filename) continue;
      if (filename.endsWith('.metadata.json')) continue;
      if (filename.endsWith('-bda-text.txt')) continue;

      files.push({
        filename,
        size_bytes: obj.Size,
        last_modified: obj.LastModified?.toISOString() ?? null,
        s3_key: obj.Key,
      });
    }

    continuationToken = resp.NextContinuationToken;
  } while (continuationToken);

  return {
    kb_id,
    kb_type: kb.kb_type,
    prefix,
    file_count: files.length,
    files,
  };
}
