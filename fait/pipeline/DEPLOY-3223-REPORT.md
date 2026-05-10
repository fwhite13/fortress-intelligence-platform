# Deploy Report — ADO#3223
## Harness BLAZOR_BASE_URL → FAIT_BASE_URL Fix

**Date:** 2026-05-10  
**Agent:** War Machine (James Rhodes) — DevOps  
**ADO:** [#3223](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3223) → **Resolved**

---

## What Was Deployed

Harness-only fix. The three memory tool handlers (`search_memory`, `read_memory`, `write_memory`) in `harness-server.js` already had the correct code (`blazorBase = FAIT_BASE_URL`) — the fix was already committed. This deploy ships that commit to production.

**File changed:** `/home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js`  
**Commit SHA:** `42be3a3f`

---

## Resources

| Resource | Before | After |
|---|---|---|
| Harness task def | `fait-v2-agent-harness:14` | `fait-v2-agent-harness:15` |
| Blazor task def | `fred-dev:173` | `fred-dev:174` |
| Fargate__TaskDefinition env var | `fait-v2-agent-harness:14` | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:15` |

---

## ECR Image

- **Tag:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:42be3a3f`
- **Digest:** `sha256:a125c10f2e63b6a283cb39bd87811b322d61fb9c84f30cfd4b5501462182c428`

---

## Task Definitions

- **`fait-v2-agent-harness:15`** — `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:15`
- **`fred-dev:174`** — `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:174`

---

## ECS Service

- **Service:** `fred-dev` on `fortress-tools-cluster`
- **Final state:** `fred-dev:174` RUNNING, HEALTHY
- **Harness:** On-demand Fargate tasks — new sessions automatically pick up `fait-v2-agent-harness:15`

---

## Rollbacks

- Harness: `fait-v2-agent-harness:14`
- Blazor: `fred-dev:173` (update service back to `:173`)

---

## Notes

- No Blazor code changes
- No DB migrations
- No env var changes needed (FAIT_BASE_URL was already set on the Fargate task definition)
- The fix was already in the codebase at commit `42be3a3f`; this deploy was purely a build + push + task def registration
