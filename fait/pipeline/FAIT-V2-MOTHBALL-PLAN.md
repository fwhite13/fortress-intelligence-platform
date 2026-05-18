# FAIT v2 Mothball Plan

**Created:** 2026-05-17  
**Status:** Ready to execute — do when Fred is at his desk  
**Prerequisite:** ADO#3437 (Epic 7 regression investigation) resolved first

---

## Background

`fip/fait-v2/` is dead code. All FAIT evolution work now lives in `fip/fait/`. The problem is `fait-v2/agent-harness/` contains the live harness (`harness-server.js`) that every Epic has been built on top of. Tony keeps accidentally working in `fait-v2/` because it exists and has history. This plan kills the confusion permanently.

---

## What Gets Killed

| Thing | Status | Action |
|-------|--------|--------|
| `fip/fait-v2/src/FortressAI.V2.Web/` | Dead Blazor app | Delete |
| `fip/fait-v2/Dockerfile.debian` | Builds dead Blazor app | Delete |
| `fip/fait-v2/pipeline/` | 100+ stale pipeline artifacts | Delete |
| `fip/fait-v2/*.md` brief files | Old sprint artifacts | Delete |
| `fip/fait-v2/agent-harness/` | **Live harness — MOVE, don't delete** | Move first, then delete |
| ECS service `fait-v2` | Running on task def `fait-v2:47` | Scale to 0, then delete |
| ALB rule for `fait-v2.dev.fortressam.ai` | Priority 10 → `fait-v2-dev-tg` | Delete rule |
| Target group `fait-v2-dev-tg` | `b81255eae56c643c` | Delete after rule removed |
| ECR repo `fait-v2` | Old Blazor images | Keep one tagged `archived-20260517`, then stop pushing; optionally clean old images |

---

## What Must Be Preserved / Moved

**`fip/fait-v2/agent-harness/`** contains:
- `harness-server.js` — the live harness, all Epic 7 work is here
- `package.json` / `package-lock.json`
- `Dockerfile` — builds `fait-v2-agent-harness` ECR image

These move to **`fip/fait/agent-harness/`**.

---

## Order of Operations

### Step 1 — Move harness (WI to Tony)
- Copy `harness-server.js`, `package.json`, `package-lock.json`, `Dockerfile` from `fip/fait-v2/agent-harness/` → `fip/fait/agent-harness/`
- No code changes — straight file move
- Commit to main

### Step 2 — Update harness build process
The harness currently has no `buildspec.yml` — Rhodey builds it manually with `docker build` + ECR push. Two options:
- **Option A (simpler):** Add `fip/fait/agent-harness/buildspec.yml` — separate CodeBuild project for harness builds
- **Option B (consolidated):** Extend `fip/fait/buildspec.yml` to also build + push the harness image in the same pipeline run

Recommend **Option A** — keeps Blazor and harness deploys independent (harness-only changes don't trigger a full Blazor rebuild).

### Step 3 — Update all references
- `AGENTS.md` dead code warning: update canonical harness path to `fip/fait/agent-harness/`
- Pipeline brief templates in `memory/ops/` or wherever Tony brief templates live: update harness path
- Any WI that references `fip/fait-v2/agent-harness/` in open/future briefs

### Step 4 — Verify harness builds from new location
- Rhodey does a test build from `fip/fait/agent-harness/`
- Pushes to ECR as `fait-v2-agent-harness:<next-rev>`
- Confirms harness deploys and connects correctly

### Step 5 — Kill fait-v2 ECS infrastructure
```bash
# Scale service to 0
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 \
  --desired-count 0 --profile fortress-tools-deployer --region us-east-1

# Delete ECS service (after tasks drain)
aws ecs delete-service --cluster fortress-tools-cluster --service fait-v2 \
  --profile fortress-tools-deployer --region us-east-1

# Delete ALB rule (Priority 10: fait-v2.dev.fortressam.ai)
# Rule ARN needs to be looked up first — Rhodey handles this

# Delete target group fait-v2-dev-tg
aws elbv2 delete-target-group \
  --target-group-arn arn:aws:elasticloadbalancing:us-east-1:742932328420:targetgroup/fait-v2-dev-tg/b81255eae56c643c \
  --profile fortress-tools-deployer --region us-east-1
```

### Step 6 — Archive ECR
```bash
# Tag current latest as archived before cleanup
aws ecr batch-get-image --repository-name fait-v2 \
  --image-ids imageTag=c4660a65 --profile fortress-tools-deployer --region us-east-1
# Retag as archived-20260517, then optionally delete old images
```

### Step 7 — Delete fip/fait-v2/ contents
- Delete everything except leave a `DEPRECATED.md` at the root:

```markdown
# DEPRECATED

This directory is dead code as of 2026-05-17.

All FAIT evolution work is in `fip/fait/`.
The agent harness is in `fip/fait/agent-harness/`.

Do not work here. Do not reference code here in WIs.
```

- Commit to main

---

## Notes / Risks

- **Harness move is the only risky step** — everything else is cleanup. Do Step 1–4 first and confirm the harness is working from the new location before touching ECS infrastructure.
- **ECR repo name `fait-v2-agent-harness`** — the ECR repo name doesn't need to change even after the source moves. The image tag family stays the same. Only the build source path changes.
- **`Fargate__TaskDefinition`** env var in Blazor task def still points to `fait-v2-agent-harness` family — that's fine, ECR repo name is separate from source directory.
- **Tony briefs going forward** must say `fip/fait/agent-harness/harness-server.js` not `fip/fait-v2/agent-harness/harness-server.js`. Update AGENTS.md dead code warning on Step 3.
