# Deploy Report — ADO#3159 `/assistant-settings` Page

**Date:** 2026-05-09  
**Deployed by:** Rhodey (devops subagent)  
**Status:** ✅ SUCCESS

---

## Summary

Deployed ADO#3159 — `/assistant-settings` page with sidebar nav entry.

---

## Build

| Field | Value |
|-------|-------|
| Repo | `/home/fredw/projects/fip/fait` |
| Tip commit | `7d1688f2` |
| Image tag | `fred-chat:7d1688f2` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:7d1688f2` |
| Image digest | `sha256:25d68cfd81e82946b52cc2af271a9dc47ad00d31d38481adba928d3161157c67` |
| Build flags | Standard (no `--no-cache` — not UI-facing service rebuild) |

---

## Task Definition

| Field | Value |
|-------|-------|
| Previous | `fred-dev:149` (image `fred-chat:09a2e08b`) |
| New | `fred-dev:150` (image `fred-chat:7d1688f2`) |
| taskRoleArn | `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅ preserved |
| `Fargate__ContainerName` | `fait-v2-agent-harness` ✅ preserved |

---

## Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |
| Deployment status | PRIMARY: RUNNING=1, PENDING=0 |
| Old deployment | DRAINING (0 tasks) |

---

## Verification

- ✅ Service stable: fred-dev:150 PRIMARY, RUNNING=1, PENDING=0
- ✅ CloudWatch logs: clean startup, no errors
- ✅ MCP transports (brave, m365) responding 200
- ✅ `Fargate__ContainerName = fait-v2-agent-harness` preserved
- ✅ ADO#3159 → Resolved

---

## Commits in this deploy

```
7d1688f2 fix(fait#3159): normalize CommunicationStyle/ResponseFormat to lowercase on load; add structured comm style option
fd3b6f3a feat(fait#3159): add /assistant-settings page with nav link
```
