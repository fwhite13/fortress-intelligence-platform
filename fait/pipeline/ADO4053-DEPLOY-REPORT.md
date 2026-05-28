# Deploy Report: ADO#4053 — Memory Import from Claude/ChatGPT Export

**Deploy Date:** 2026-05-27  
**Deployed By:** Rhodey (devops subagent)  
**Commit:** `efa0a41c`  
**Risk:** Medium — Two-image deploy (harness + fred-chat)

---

## Pre-Deploy Snapshot

| Resource | Pre-Deploy Value |
|---|---|
| fred-dev task def | `fred-dev:290` |
| fred-chat image | `fred-chat:efa0a41c` |
| Harness task def | `fait-v2-agent-harness:78` |
| FAIT_HARNESS_VERSION | `78` |

---

## Harness Decision: REUSED `:78` — No Rebuild Required

**Reasoning:** The current harness image `fait-v2-agent-harness:78` was built by ADO#4249's deploy from commit `efa0a41c`. The `/import-memory` endpoint (ADO#4053's harness change) was introduced in parent commit `632d07f6` and is present in `efa0a41c`. Git verification:

```
git show efa0a41c --name-only  →  only .razor and .cs files (no harness-server.js)
grep "import-memory" harness-server.js  →  line 1273-1329 present in working tree
git log harness-server.js  →  12378215 (ADO#4249) built from efa0a41c which includes 632d07f6
```

**Harness `:78` already contains `/import-memory` endpoint.** Harness rebuild skipped.

---

## fred-chat Decision: ALREADY DEPLOYED — No Rebuild Required

**Reasoning:** ADO#4249's deploy (earlier today) pushed `fred-chat:efa0a41c` to ECR and registered it in `fred-dev:290`. That same commit `efa0a41c` is HEAD and contains ALL ADO#4053 changes:

- `fait/agent-harness/harness-server.js` — via `632d07f6` (parent, included in built image)
- `fait/src/FortressAI.Web/Components/Pages/Memory.razor` — ✅ in `efa0a41c`
- `fait/src/FortressAI.Web/Services/IMemoryFileService.cs` — via `632d07f6` (parent)
- `fait/src/FortressAI.Web/Services/MemoryFileService.cs` — ✅ in `efa0a41c`

**The current `fred-dev:290` already serves all ADO#4053 features.**

---

## What Is Deployed

**fred-chat image:** `fred-chat:efa0a41c`  
**ECR digest:** `sha256:9b636546e4c9c1dd61be232d0fa1694f2f5e034d9c300fc6660e0dd4c58c4239`  
**Harness:** `fait-v2-agent-harness:78`  
**Task Definition:** `fred-dev:290`

### ADO#4053 Features Live
- `/import-memory` endpoint in harness — GUID validation, 50K content cap, S3 write + pgvector upsert (non-fatal)
- Import Memory button on `/memory` page
- Two-step MudDialog for file selection and confirmation
- `ImportMemoryAsync` / `ImportMemoryResult` service pattern with `HarnessClient`
- Clipboard try-guard on Memory page

---

## ECS Service Status

| Field | Value |
|---|---|
| Service | `fred-dev` |
| Status | `ACTIVE` |
| Running / Desired | `1 / 1` |
| Task Def | `fred-dev:290` |
| Deployment | PRIMARY, stable |

---

## Rollback Plan

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:290 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

> Note: `fred-dev:290` IS the current deployment. Rollback to prior state (`fred-dev:289`, harness `:77`) if needed:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:289 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
aws ecs wait services-stable --cluster fortress-tools-cluster --services fred-dev \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## Cost Impact

No additional resources created. Same task def revision (`fred-dev:290`) already in use.

---

## Lessons Learned

- When two WIs share the same HEAD commit, the second deploy may be a no-op if the first deploy covered both features. Always check the currently running image tag against HEAD before rebuilding.
- ADO#4249 and ADO#4053 shared commit `efa0a41c`; ADO#4249's deploy covered both feature sets.
