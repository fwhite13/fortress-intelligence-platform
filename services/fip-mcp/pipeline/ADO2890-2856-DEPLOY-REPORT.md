# Deploy Report — ADO#2890 + ADO#2856 (fip-mcp batch deploy)

**Date:** 2026-05-07  
**Agent:** Rhodey (War Machine)  
**Service:** `fip-mcp` on `fortress-tools-cluster`

---

## Summary

Batch deploy of two WIs plus incidental cleanup:

| WI | Feature |
|----|---------|
| **ADO#2890** | ADO MCP connector — 7 Azure DevOps REST API tools (`src/tools/ado/`) |
| **ADO#2856** | Web Search tool via Brave Search API (`src/tools/search/`) |
| **Cleanup** | Remove FIRM resolver dependency from `list_kb_files.js` |

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Previous task def | `fip-mcp:7` |
| Previous commit | `5457c22` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp:5457c22` |
| Service state | `ACTIVE`, 1/1 running |

---

## Build

| Field | Value |
|-------|-------|
| Commit deployed | `4c74494` |
| Branch | `origin/main` |
| Build method | Local Docker (`--no-cache`) from `services/fip-mcp/` |
| Image digest | `sha256:e68f11e06c21b528d624c1ed3eeb647b8627329ad9354c01cad55dfcf329ec9b` |
| ECR tags pushed | `4c74494`, `latest` |
| ECR repo | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-mcp` |

---

## Task Definition

| Field | Value |
|-------|-------|
| New revision | `fip-mcp:8` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fip-mcp:8` |
| Task role | `arn:aws:iam::742932328420:role/fip-mcp-task-role` (inherited) |
| Registration method | `scripts/ecs-register-task-def.sh` + python3 env-var patch |

### New Env Vars Added

| Var | Value | Source |
|-----|-------|--------|
| `AZDO_ORG` | `FortressAffinityGroup` | Hardcoded (per spec) |
| `AZDO_PAT` | *(empty string placeholder)* | ⚠️ Not found in `fait-prod` or `fip-dev` task defs — ADO tools will return graceful not-configured error until this is set |
| `BRAVE_API_KEY` | `BSADg2BAnh7dgKo-IG1Lj-SY7Ya3vrU` | Hardcoded (per spec) |

---

## Deployment

| Field | Value |
|-------|-------|
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fip-mcp` |
| Task def deployed | `fip-mcp:8` |
| Force new deployment | Yes |
| Stabilization | ✅ SUCCEEDED (`aws ecs wait services-stable`) |

---

## Health Check

```
running: 1
desired: 1
taskDef: arn:aws:ecs:us-east-1:742932328420:task-definition/fip-mcp:8
status: ACTIVE
```

**Logs (startup):**
```
[fip-mcp] FORGE KB MCP Server v1.0.0 listening on port 3000
[fip-mcp] Entra tenant: 7152ea12-c930-44b0-bb52-069152161c5b
[fip-mcp] Entra client: eda4d502-8c93-422e-b7fb-bb922a2a472e
[fip-mcp] Bedrock region: us-east-1
[fip-mcp] Entitlements config: /app/src/config/entitlements.json
```

No errors. Clean startup. ✅

---

## ADO Comments

| WI | Comment ID | Status |
|----|-----------|--------|
| #2890 | 782315 | ✅ Posted |
| #2856 | 782316 | ✅ Posted |

---

## Rollback Plan

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fip-mcp \
  --task-definition fip-mcp:7 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Action Items

- ⚠️ **`AZDO_PAT` is empty** — ADO tools (#2890) will not function until the PAT is set. The PAT was not found in `fait-prod` or `fip-dev` task definitions under any of: `DevOps__PersonalAccessToken`, `DevOpsConnection__PAT`, `AzureDevOps__PAT`, `DevOps__PAT`. Fred needs to supply the PAT value and a new task def revision registered with `fip-mcp:8` as the base.

---

_Deployed by Rhodey (devops subagent) — 2026-05-07_
