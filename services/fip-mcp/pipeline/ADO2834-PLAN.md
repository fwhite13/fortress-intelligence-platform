# BUILD Plan — ADO#2834
## KB File Enumeration Fix: S3-authoritative listing + preserve filenames with extensions

**WI:** ADO#2834
**Repos:** `/home/fredw/projects/fip/fait/` (1-line fix) + `/home/fredw/projects/fip/services/fip-mcp/` (new tool)
**Risk:** low (1-line FAIT fix + new additive tool in fip-mcp)

---

## Two Issues

### Issue 1 — FAIT strips file extensions in KB context headers
**File:** `fait/src/FortressAI.Web/Services/KnowledgeBaseService.cs` line 323

```csharp
// CURRENT (wrong — strips extension):
var sourceName = System.IO.Path.GetFileNameWithoutExtension(chunk.Source.Split('/').Last());

// FIX (correct — preserve full filename):
var sourceName = chunk.Source.Split('/').Last();
```

That's it. One-character change from `GetFileNameWithoutExtension(...)` wrapping to nothing. This ensures when Claude says "according to `report.pdf`" it shows the full filename not just `report`.

---

### Issue 2 — No tool exists for Claude to enumerate KB files
fip-mcp has `search_kb`, `list_kbs`, `get_kb_metadata` — but no way to list what files are actually in a user's KB. When a user asks "what documents are in my KB?", Claude can only search — it cannot enumerate. Need a new `list_kb_files` tool.

**New file:** `fip-mcp/src/tools/list_kb_files.js`

```javascript
import { S3Client, ListObjectsV2Command } from '@aws-sdk/client-s3';
import { getEntitlements } from './list_kbs.js';
import { KB_INVENTORY, KB_TYPE } from '../config/kb-inventory.js';

const s3Client = new S3Client({
  region: process.env.BEDROCK_REGION ?? 'us-east-1',
});

const KB_BUCKET = process.env.KB_BUCKET ?? 'fortress-tools';

// S3 prefix per KB type — matches KbDocumentService upload paths
function getS3Prefix(kbType, user, args) {
  switch (kbType) {
    case KB_TYPE.PERSONAL:
      // S3 path: kb-docs/personal/{userId}/
      // userId = user.oid (Entra OID) maps to FAIT userId guid
      // FAIT stores by userId (FAIT DB guid), not Entra OID directly
      // Use user.user_id which is the FAIT userId injected by auth middleware
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
      // Skip companion metadata files and BDA sidecar text files
      if (filename.endsWith('.metadata.json')) continue;
      if (filename.endsWith('-bda-text.txt')) continue;
      if (!filename) continue;

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
```

**Register in server.js:**
```javascript
import { listKbFiles } from './tools/list_kb_files.js';
```

And add tool registration in the `buildMcpServer` function alongside the other tools. Follow the exact same pattern as `searchKb`, `listKbs`, `getKbMetadata` — look at how those are registered and do the same for `listKbFiles`.

**Tool schema:**
```javascript
{
  name: 'list_kb_files',
  description: 'List files in a user\'s knowledge base. Returns filenames with extensions, sizes, and last modified dates. Use this when the user asks what documents are in their KB.',
  inputSchema: {
    type: 'object',
    properties: {
      kb_id: { type: 'string', description: 'KB ID from list_kbs' },
      team_id: { type: 'string', description: 'Team ID (required for Team KB)' },
    },
    required: ['kb_id'],
  },
}
```

**Note on `user.user_id`:** Check how `search_kb.js` accesses the user's ID for the personal KB filter — use the same field. It likely comes from the JWT auth middleware as `user.user_id` or `user.oid`. Verify in `auth.js` what fields are on the `user` object.

---

## ECS task def update needed

`KB_BUCKET` env var needs to be added to `fip-mcp` ECS task def. Value: `fortress-tools`. Tony should note this in the Build Report — Rhodey adds it at deploy time.

---

## Acceptance Criteria

- [ ] FAIT `KnowledgeBaseService.FormatKbContext`: `GetFileNameWithoutExtension` replaced with `chunk.Source.Split('/').Last()` — full filename preserved
- [ ] fip-mcp `list_kb_files.js` tool exists — lists S3 objects by prefix, excludes `.metadata.json` + `-bda-text.txt`, returns `filename` (with extension), `size_bytes`, `last_modified`
- [ ] Tool registered in `server.js` alongside other tools
- [ ] `@aws-sdk/client-s3` used (check if already in `package.json` — likely yes since S3 is already used)
- [ ] Both builds compile/pass with no errors

---

## Files to create/modify

**FAIT:**
- `fait/src/FortressAI.Web/Services/KnowledgeBaseService.cs` — 1-line fix

**fip-mcp:**
- `services/fip-mcp/src/tools/list_kb_files.js` — new
- `services/fip-mcp/src/server.js` — import + register new tool

---

## CC env vars
```bash
export CLAUDE_CODE_ENTRYPOINT=ado-pipeline
export CLAUDE_CODE_DISABLE_AUTO_MEMORY=1
export CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1
export CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30
```
