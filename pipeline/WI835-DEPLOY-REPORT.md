# WI835 Deploy Report
**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-03-17  
**Sprint:** FAIT Cowork Sprint 3 — FORGE injection + persistent instructions + task queue

---

## Summary

Deployed WI835 to ECS. Both containers updated to image tag `c4083da`. A TypeScript compile error was discovered in `runner.ts` during the first CodeBuild attempt and resolved with a targeted fix before the successful build.

---

## Build Fix Applied

**Issue:** First CodeBuild (commit `546e10a`) failed with TS2322:  
> `Type '{ name: string; description: string; input_schema: ...; execute(...): Promise<...>; }' is not assignable to type 'string'.`

**Root cause:** `buildSearchForgeTool()` returned a plain object assigned to `Options.tools`, which only accepts `string[] | { type: 'preset' }`. Custom tools must be registered via `mcpServers`.

**Fix (commit `c4083da`):**
- `forgeClient.ts`: Replaced `buildSearchForgeTool` with `buildSearchForgeMcpServer` using `createSdkMcpServer` (Zod `inputSchema` + `handler`)
- `runner.ts`: Changed `tools: [forgeTool]` → `mcpServers: { forge: forgeMcpServer }` with `allowedTools` updated to include `mcp__forge__SearchForge`
- Verified clean local build before re-triggering CodeBuild

---

## Image Tags

| Container | ECR Image | Digest |
|-----------|-----------|--------|
| cowork-web | `742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-web:c4083da` | `sha256:21ef17b09423d15a502537a4e2a4c6d842f54f8305956f7d4a02bb2f38158b30` |
| cowork-agent | `742932328420.dkr.ecr.us-east-1.amazonaws.com/cowork-agent:c4083da` | `sha256:dcad8abea46b04b6e138dad32542511bbdbf79ca6b799ee3574d228f175e02b4` |

---

## Task Definition Revisions

| Service | Previous | New | ARN |
|---------|----------|-----|-----|
| cowork-web | cowork-web:6 | **cowork-web:7** | `arn:aws:ecs:us-east-1:742932328420:task-definition/cowork-web:7` |
| cowork-agent | cowork-agent:6 | **cowork-agent:7** | `arn:aws:ecs:us-east-1:742932328420:task-definition/cowork-agent:7` |

---

## Service Status

Both services: `runningCount=1`, `desiredCount=1`, PRIMARY deployment active.

```json
[
  { "name": "cowork-web",   "running": 1, "desired": 1, "taskDef": "cowork-web:7" },
  { "name": "cowork-agent", "running": 1, "desired": 1, "taskDef": "cowork-agent:7" }
]
```

---

## Log Verification

**cowork-agent:** `CoworkAgent listening on :3000` ✅  
**cowork-web:** `Application started. Now listening on: http://[::]:8080` ✅  
(DataProtection KeyRing warn is pre-existing, non-fatal)

---

## FAIT Regression

| Environment | URL | Status |
|-------------|-----|--------|
| Dev | https://fait.dev.fortressam.ai/health | **200** ✅ |
| Prod | https://fait.fortressam.ai/health | **200** ✅ |

---

## Env Vars Carried Forward

**cowork-agent:7:**
- `REDIS_URL=rediss://master.cowork-redis.e3c7jk.use1.cache.amazonaws.com:6379`
- `S3_BUCKET=fip-cowork-workspaces`
- `AWS_REGION=us-east-1`
- `NODE_ENV=production`
- `COWORK_INTERNAL_SECRET=<unchanged>`

**cowork-web:7:** All env vars unchanged from :6.

---

## Rollback Plan

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service --cluster fortress-tools-cluster \
  --service cowork-web --task-definition cowork-web:6 \
  --force-new-deployment --region us-east-1

aws ecs update-service --cluster fortress-tools-cluster \
  --service cowork-agent --task-definition cowork-agent:6 \
  --force-new-deployment --region us-east-1
```

---

## Commits Deployed

| Commit | Description |
|--------|-------------|
| `546e10a` | WI835 original — taskQueue, forgeClient, runner, routes/users, taskStore, Razor pages |
| `c4083da` | fix(cowork-agent): wire SearchForge as SDK MCP server (TS2322) |

---

*Deploy complete. Natasha on deck for verification.*
