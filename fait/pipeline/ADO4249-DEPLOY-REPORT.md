# Deploy Report: ADO#4249 — Ephemeral Chips Contextual Detail

**Deploy Date:** 2026-05-27  
**Deployed By:** Rhodey (devops subagent)  
**Commit:** `efa0a41c`  
**Risk:** Medium — Two-image deploy (harness + fred-chat)

---

## Pre-Deploy Snapshot

| Resource | Pre-Deploy Value |
|---|---|
| fred-dev task def | `fred-dev:289` |
| fred-chat image | `fred-chat:5534de9c` |
| Harness task def | `fait-v2-agent-harness:77` |
| FAIT_HARNESS_VERSION | `77` |

---

## What Was Deployed

**Commit `efa0a41c`** — Ephemeral tool chips with contextual detail strings:
- `fait/agent-harness/harness-server.js` — `chipTrunc` helper, `resolveProgressLabel` rewrite, `getBuiltinSummary` rewrite (default `'Working...'`), folder context chip, ADO/web_search conditional labels, `/import-memory` GUID guard + content cap + pgvector non-fatal
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — `TruncChip()` helper, `GetToolLabel` simplification

---

## Stage 1 — Harness Image

| Field | Value |
|---|---|
| Image tag | `fait-v2-agent-harness:efa0a41c` |
| ECR digest | `sha256:d611437aad4e155b216a80bb0a90f83efdb62f03dd10cd5334ec090240b94440` |
| New task def | `fait-v2-agent-harness:78` |
| Build exit | `0` |
| Push exit | `0` |

---

## Stage 2 — fred-chat Image

| Field | Value |
|---|---|
| Image tag | `fred-chat:efa0a41c` |
| ECR digest | `sha256:9b636546e4c9c1dd61be232d0fa1694f2f5e034d9c300fc6660e0dd4c58c4239` |
| Dockerfile | `fait/Dockerfile.debian` |
| Build exit | `0` |
| Push exit | `0` |

---

## Stage 3 — fred-dev Deployment

| Field | Value |
|---|---|
| New fred-dev task def | `fred-dev:290` |
| fred-chat image | `fred-chat:efa0a41c` |
| FAIT_HARNESS_VERSION | `78` |
| Fargate__TaskDefinition | `fait-v2-agent-harness:78` |
| ECS rollout state | `COMPLETED` |
| Desired / Running | `1 / 1` |
| Service status | `ACTIVE` |

---

## Steps Completed

1. ✅ Pre-deploy snapshot captured (fred-dev:289, harness:77)
2. ✅ Harness image built — `fait-v2-agent-harness:efa0a41c`
3. ✅ Harness image pushed to ECR
4. ✅ Harness task def registered — `fait-v2-agent-harness:78`
5. ✅ fred-chat image built — `fred-chat:efa0a41c` (Dockerfile.debian)
6. ✅ fred-chat image pushed to ECR
7. ✅ fred-dev task def registered — `fred-dev:290` (image + FAIT_HARNESS_VERSION=78 + Fargate__TaskDefinition=fait-v2-agent-harness:78)
8. ✅ ECS service updated — force-new-deployment
9. ✅ ECS stable — running=1/1, rollout COMPLETED

---

## Rollback Plan

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:289 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Wait for stable
aws ecs wait services-stable \
  --cluster fortress-tools-cluster --services fred-dev \
  --region us-east-1 --profile fortress-tools-deployer
```

Rollback restores: `fred-dev:289` (fred-chat:5534de9c, harness:77, FAIT_HARNESS_VERSION=77)

---

## Deployment Time

- **Started:** ~13:25 EDT
- **Completed:** ~13:56 EDT
- **Duration:** ~31 minutes (includes two full Docker builds)
