# Deploy Report: ADO#3201
**Date:** 2026-05-10  
**Agent:** War Machine (Rhodey) — DevOps  
**WI:** ADO#3201 — 5.2-A: Harness create_document tool + artifact SSE emission

---

## What Was Deployed

### Blazor — fred-dev
| Field | Value |
|-------|-------|
| Commit | `1a648a4a` |
| Image | `fred-chat:1a648a4a` |
| ECR Digest | `sha256:bfd59be75d5996b1a0b1e9e0c1dcc1ff3a47a6689bda5e7c431d26f4d0446b5e` |
| Task Def | `fred-dev:167` |
| ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:167` |
| Service | `fred-dev` on `fortress-tools-cluster` |
| Status | ✅ RUNNING / HEALTHY (1/1) |

### Harness — fait-v2-agent-harness
| Field | Value |
|-------|-------|
| Commit | `1a648a4a` |
| Image | `fait-v2-agent-harness:1a648a4a` |
| ECR Digest | `sha256:70ce69f371de878c421280a01e5232dea10fe4c8e0ec7c1de97728c5cb1482ec` |
| Task Def | `fait-v2-agent-harness:13` |
| ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:13` |
| ECS Service | None (on-demand Fargate tasks pick up new revision automatically) |
| Status | ✅ Registered — active for next Fargate task launch |

---

## Rollback
- Blazor: `fred-dev:166`
- Harness: `fait-v2-agent-harness:12`

---

## Notes
- `Fargate__TaskDefinition` env var in `fred-dev:167` updated to `fait-v2-agent-harness:13`
- No DB migration in this WI
- Both images built with `--no-cache` using `Dockerfile.debian` (Blazor) and standard Dockerfile (harness)
- Pre-flight docker-build check passed; credentials confirmed as `fortress-tools-deployer`

---

## Deploy Timeline
| Step | Time |
|------|------|
| Build start | 14:30:16 EDT |
| Both images built | ~14:35 EDT |
| ECR push complete | ~14:40 EDT |
| fred-dev:167 registered | ~14:42 EDT |
| fait-v2-agent-harness:13 registered | ~14:42 EDT |
| ECS update-service | ~14:42 EDT |
| fred-dev HEALTHY | 14:34:30 EDT (task started at) |
