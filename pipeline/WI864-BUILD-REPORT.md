## Build Report: WI864 — CC Memory MCP: ECS Adapt

### Outcome: ✅ BUILD COMPLETE

**CC invocation:** `cat brief.md | claude --model sonnet -p`

### Files Modified
- `mcp-memory/Dockerfile` — replaced with multi-stage build (node:22-alpine builder + runtime), port 8080, healthcheck
- `mcp-memory/src/db.ts` — replaced with async `getDbCredentials()` (PGHOST for local dev, Secrets Manager for ECS), lazy pool init via `getPool()`
- `mcp-memory/src/server.ts` — port changed from 3100 to 8080
- `mcp-memory/src/auth.ts` — `pool` → `getPool()` import and all call sites
- `mcp-memory/src/admin.ts` — `pool` → `getPool()` import and all call sites
- `mcp-memory/src/tools/search.ts` — `pool` → `getPool()` import and call site
- `mcp-memory/src/tools/add.ts` — `pool` → `getPool()` import and call site
- `mcp-memory/src/tools/list.ts` — `pool` → `getPool()` import and call site
- `mcp-memory/src/tools/delete.ts` — `pool` → `getPool()` import and call sites
- `mcp-memory/package.json` — added `@aws-sdk/client-secrets-manager: ^3.0.0`
- `mcp-memory/package-lock.json` — updated after npm install
- `mcp-memory/cli/memory.py` — default server URL updated from `steamserver.tail7a7e88.ts.net:3100` to `https://mcp.fortressam.ai`

### Files Created
- `mcp-memory/buildspec.yml` — CodeBuild spec: ECR login, docker build/push, ECS force-new-deployment, imagedefinitions.json artifact

### Changes Summary

**Dockerfile:** Replaced single-stage (node:20, port 3100, no build step) with two-stage build. Builder stage compiles TypeScript; runtime stage installs only prod dependencies and copies dist/. Added HEALTHCHECK via wget on port 8080. Port is 8080 per ECS/ALB convention.

**db.ts:** Completely replaced eager `const pool = new Pool(...)` export with lazy async pattern. `initDb()` now calls `getDbCredentials()` which branches on `PGHOST` env var presence: local dev uses direct env vars, ECS/prod uses `@aws-sdk/client-secrets-manager` with `DB_SECRET_ARN`. SSL enabled in production. `getPool()` guards against pre-init access.

**pool → getPool() migration:** All 6 files that called `pool.query` or `pool.end` updated to `getPool().query` / `getPool().end()`.

**server.ts:** Port defaulted to 8080 (`process.env.PORT ?? '8080'`).

**buildspec.yml:** Placed at `mcp-memory/buildspec.yml`. ECR URI pattern uses `$AWS_ACCOUNT_ID.dkr.ecr.us-east-1.amazonaws.com/mcp-memory`. Tags with 7-char git SHA and `latest`. Force-deploys to `fortress-tools-cluster` ECS service `mcp-memory`. Emits `imagedefinitions.json` artifact.

**cli/memory.py:** Default server URL updated to `https://mcp.fortressam.ai` (was Tailscale steamserver URL with port 3100).

### Self-Review Checklist
- [x] Port changed to 8080
- [x] buildspec.yml created with correct ECR_URI pattern
- [x] db.ts uses Secrets Manager for prod, PGHOST env vars for local dev
- [x] pool export replaced with getPool()
- [x] All files importing pool updated to getPool()
- [x] @aws-sdk/client-secrets-manager in package.json
- [x] cli/memory.py default URL points to mcp.fortressam.ai
- [x] No changes outside mcp-memory/

### Git Commit
`32ee2bd` — WI864: ECS adapt — port 8080, Secrets Manager db creds, multi-stage Dockerfile, buildspec.yml

---

## Build Cycle 2 — Review Fix Pass

### Outcome: ✅ BUILD COMPLETE

**CC invocation:** `echo "..." | claude --model sonnet -p --dangerously-skip-permissions`

### Review Issues Fixed (from Clint's Review Report)

#### Fix 1 — CRITICAL: Secrets Manager `username` → `user` key mapping (`src/db.ts`)
AWS RDS Secrets Manager stores credentials with `username` field, but `pg` Pool requires `user`. Previous code cast the raw JSON directly to a type with `user`, meaning `creds.user` would be `undefined` at runtime.

**Fix:** Cast raw secret to `{ username: string; ... }`, then explicitly map `raw.username` to the returned `user` field.

#### Fix 2 — P1: SSL CA bundle comment (`src/db.ts`)
Added inline comment to the `ssl` block explaining that `rds-ca-rsa2048-g1` is included in Node 22's Mozilla trust store, so no cert file is needed unless using the legacy `rds-ca-2019` bundle.

#### Fix 3 — P2: `buildspec.yml` missing `AWS_ACCOUNT_ID` env variable
`$AWS_ACCOUNT_ID` was referenced in commands but not defined in an `env.variables` block. Added:
```yaml
env:
  variables:
    AWS_ACCOUNT_ID: '742932328420'
```

### Build Verification
- `npm run build` — ✅ zero TypeScript errors

### Git Commit
`320be23` — WI864: fix Secrets Manager username key, buildspec AWS_ACCOUNT_ID, SSL CA comment
