# WI864 Code Review Brief — CC Memory MCP Server ECS Adaptation

You are Hawkeye (Clint Barton), code reviewer. Analyze the following code for the specified issues. Be precise. Quote exact line content when flagging issues.

## Files to Analyze

### mcp-memory/src/db.ts
```typescript
import { Pool } from 'pg';
import * as fs from 'fs';
import * as path from 'path';
import dotenv from 'dotenv';
dotenv.config();

let pool: Pool | null = null;

async function getDbCredentials(): Promise<{
  host: string; port: number; database: string; user: string; password: string;
}> {
  // Local dev: use env vars directly (no Secrets Manager)
  if (process.env.PGHOST) {
    return {
      host:     process.env.PGHOST,
      port:     parseInt(process.env.PGPORT ?? '5432', 10),
      database: process.env.PGDATABASE ?? 'mcp_memory',
      user:     process.env.PGUSER ?? 'mcp_memory',
      password: process.env.PGPASSWORD ?? '',
    };
  }

  // AWS ECS: fetch from Secrets Manager
  const { SecretsManagerClient, GetSecretValueCommand } = await import('@aws-sdk/client-secrets-manager');
  const sm = new SecretsManagerClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
  const secretId = process.env.DB_SECRET_ARN ?? 'mcp-memory/db-credentials';
  const resp = await sm.send(new GetSecretValueCommand({ SecretId: secretId }));
  return JSON.parse(resp.SecretString!) as {
    host: string; port: number; database: string; user: string; password: string;
  };
}

export async function initDb(): Promise<void> {
  if (pool) return; // already initialized

  const creds = await getDbCredentials();
  pool = new Pool({
    host:     creds.host,
    port:     creds.port,
    database: creds.database,
    user:     creds.user,
    password: creds.password,
    ssl:      process.env.NODE_ENV === 'production' ? { rejectUnauthorized: true } : false,
    max:      5,
    idleTimeoutMillis: 30_000,
  });

  const sql = fs.readFileSync(path.join(__dirname, '../migrations/001_init.sql'), 'utf8');
  await pool.query(sql);

  // Idempotent column migration: ensure vector(1024) not vector(1536)
  const dimCheck = await pool.query<{ atttypmod: number }>(
    `SELECT a.atttypmod FROM pg_attribute a
     JOIN pg_class c ON c.oid = a.attrelid
     WHERE c.relname = 'cc_memory_entries' AND a.attname = 'embedding'`,
  );
  if (dimCheck.rows.length > 0 && dimCheck.rows[0].atttypmod === 1540) {
    await pool.query('ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024)');
    console.log('[db] Migrated embedding column from vector(1536) to vector(1024)');
  }

  console.log('[db] Migrations applied');
}

export function getPool(): Pool {
  if (!pool) throw new Error('DB not initialized — call initDb() first');
  return pool;
}
```

### mcp-memory/src/server.ts (key startup section)
```typescript
const PORT = parseInt(process.env.PORT ?? '8080', 10);

async function main(): Promise<void> {
  await initDb();
  app.listen(PORT, () => {
    console.log(`[mcp-memory] listening on port ${PORT}`);
  });
}

main().catch(console.error);
```

### mcp-memory/buildspec.yml
```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - aws ecr get-login-password --region us-east-1 | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com
      - IMAGE_TAG=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c1-7)
      - ECR_URI=$AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/mcp-memory
  build:
    commands:
      - cd mcp-memory
      - docker build -t $ECR_URI:$IMAGE_TAG -t $ECR_URI:latest .
  post_build:
    commands:
      - docker push $ECR_URI:$IMAGE_TAG
      - docker push $ECR_URI:latest
      - aws ecs update-service --cluster fortress-tools-cluster --service mcp-memory --force-new-deployment --region us-east-1
      - printf '[{"name":"mcp-memory","imageUri":"%s"}]' $ECR_URI:$IMAGE_TAG > imagedefinitions.json

artifacts:
  files:
    - imagedefinitions.json
```

### Reference: famos/buildspec.yml (for comparison)
```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
  build:
    commands:
      - echo Build started on `date`
      - docker build -f famos/Dockerfile -t famos-web:$IMAGE_TAG .
      - docker tag famos-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
  post_build:
    commands:
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
      - docker tag famos-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:latest
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:latest
      - aws ecs update-service --cluster fortress-tools-cluster --service famos-dev --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

### mcp-memory/src/embed.ts
```typescript
import { BedrockRuntimeClient, InvokeModelCommand } from '@aws-sdk/client-bedrock-runtime';

const client = new BedrockRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });
const MODEL_ID = 'amazon.titan-embed-text-v2:0';

export async function embedText(text: string): Promise<number[]> {
  const body = JSON.stringify({ inputText: text.slice(0, 8000) });
  const cmd = new InvokeModelCommand({
    modelId: MODEL_ID,
    contentType: 'application/json',
    accept: 'application/json',
    body: Buffer.from(body),
  });
  const response = await client.send(cmd);
  const result = JSON.parse(Buffer.from(response.body).toString('utf8')) as { embedding: number[] };
  return result.embedding;
}
```

### mcp-memory/src/tools/add.ts (spot-check for getPool() pattern)
```typescript
import { getPool } from '../db';
// ...
const result = await getPool().query<{ id: string; created_at: Date }>(
  `INSERT INTO cc_memory_entries ...`,
  [...]
);
```

---

## Review Checklist — Answer Each Question Precisely

### P1 — CRITICAL

**Q1: Secrets Manager key name mismatch**
The AWS Secrets Manager secret format is: `{"host":..., "username":..., "password":...}`
Note the key is `username` (not `user`).
The `getDbCredentials()` function casts the parsed JSON as `{ host, port, database, user, password }`.
It then passes `creds.user` to `new Pool({ user: creds.user, ... })`.
QUESTION: If the secret JSON has key `username` but the TypeScript type says `user`, what is `creds.user` at runtime? Will this cause a silent auth failure (undefined password/user) or a clear error? Is this a BUG?

**Q2: getPool() before initDb() guard**
Look at `getPool()`:
```typescript
export function getPool(): Pool {
  if (!pool) throw new Error('DB not initialized — call initDb() first');
  return pool;
}
```
QUESTION: Is the guard adequate? If a request arrives before `initDb()` completes (race condition at startup), will the error be thrown clearly? Note `auth.ts` calls `getPool()` inside `getActiveUsers()` which is called on every request. Is there any risk of the pool being null when a request hits the /mcp endpoint?

**Q3: buildspec.yml — cd mcp-memory before docker build**
Look at the build phase:
```yaml
build:
  commands:
    - cd mcp-memory
    - docker build -t $ECR_URI:$IMAGE_TAG -t $ECR_URI:latest .
```
QUESTION: Does `cd mcp-memory` happen before `docker build .`? Is the build context correct? The Dockerfile is at mcp-memory/Dockerfile. Will this work correctly?

**Q4: SSL / RDS CA bundle**
The pool config uses: `ssl: { rejectUnauthorized: true }` in production.
QUESTION: For AWS RDS PostgreSQL, does Node.js's built-in CA trust store (Mozilla/OpenSSL) cover AWS RDS certificates? Or does this require an explicit `ca` option pointing to the RDS CA bundle (rds-ca-2019.pem or rds-ca-rsa2048-g1.pem)? Is `rejectUnauthorized: true` without a `ca` option safe for RDS connections, or will it reject the RDS cert?

### P2 — IMPORTANT

**Q5: initDb() before app.listen()**
Look at server.ts main():
```typescript
async function main(): Promise<void> {
  await initDb();
  app.listen(PORT, () => { ... });
}
```
QUESTION: Is `await initDb()` called before `app.listen()`? Will the pool be ready before any request can be served?

**Q6: AWS_ACCOUNT_ID in CodeBuild**
The mcp-memory buildspec uses `$AWS_ACCOUNT_ID` as a variable but does NOT define it in an `env.variables` block.
The famos buildspec DOES define it: `AWS_ACCOUNT_ID: 742932328420`.
QUESTION: In AWS CodeBuild, is `AWS_ACCOUNT_ID` available as a built-in environment variable, or must it be explicitly set? If it's not a CodeBuild built-in, will the ECR login and image tag commands silently fail (expanding to empty string)?

**Q7: Titan embed dimension and vector(1024) migration**
- embed.ts uses model `amazon.titan-embed-text-v2:0` — this model outputs 1024-dimensional embeddings. ✓
- db.ts checks `atttypmod === 1540` to detect vector(1536) columns.
QUESTION: For pgvector, what is the `atttypmod` value for `vector(1024)` vs `vector(1536)`? Is `1540` the correct value to check for vector(1536)? (pgvector stores atttypmod as dimensions + 4 for overhead). Calculate: 1536 + 4 = 1540 ✓, 1024 + 4 = 1028. Confirm this math is correct and the migration check is right.

### P3 — CONSISTENCY

**Q8: getPool() call site pattern**
The add.ts tool uses `getPool().query(...)` directly (no assignment to local variable).
QUESTION: Is this pattern consistent? Any risk of calling `getPool()` twice per operation (once for query, once for something else)? Is there any double-init risk?

---

## Output Format
For each question, respond:
- **Finding**: PASS / BUG / WARNING / NOTE
- **Explanation**: 1-3 sentences with specific evidence from the code
- **Fix required** (if BUG/WARNING): exact change needed

Then give an overall verdict: PASS / NEEDS-CHANGES / FAIL
