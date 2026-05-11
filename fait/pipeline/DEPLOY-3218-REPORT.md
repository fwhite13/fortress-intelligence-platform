# Deploy Report — ADO#3218: MCP toolConfig Wiring (Blazor + Harness)

**Date:** 2026-05-10  
**Deployed by:** War Machine (rhodey-deploy-3218)  
**Commit:** `f1af77a8`  
**Status:** ✅ DEPLOYED — HEALTHY

---

## What Was Deployed

### Blazor (FAIT)
- `IUserAgentRuntime.cs` — `EnabledMcpSlugs` field added to `TurnRequest`
- `ChatView.razor` — slug extraction + `EnabledMcpSlugs` population
- `DatabaseInitializationService.cs` — DB seed tool names updated (M365 + DevOps)

### Harness (`fait-v2/agent-harness/harness-server.js`)
- `MCP_TOOL_SPECS` map with m365 + azdo/ado/devops aliases
- `enabledMcpSlugs` destructuring in /turn handler
- Dynamic toolConfig build
- Agentic loop dispatch for `graph_*`/`ado_*`
- `ado_wiql_query` spec added, `graph_list_calendar` stale entry removed

---

## Resources

### ECR Images
| Image | Tag | Digest |
|-------|-----|--------|
| `fred-chat` | `f1af77a8` | `sha256:1fb7d1ba63e6148b43d9051f844a77c0a9b7eaa9bd701f98852167d4b39a8871` |
| `fait-v2-agent-harness` | `f1af77a8` | `sha256:042c90b8c0ee5ce95297631f1c1c358221ba07b52dc47f3e253836d0e39e1df7` |

### Task Definitions
| Task Family | Old Rev | New Rev | ARN |
|------------|---------|---------|-----|
| `fait-v2-agent-harness` | `:17` | `:18` | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:18` |
| `fred-dev` | `:176` | `:177` | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:177` |

### ECS Service
| Cluster | Service | Task Def | Status | Running |
|---------|---------|----------|--------|---------|
| `fortress-tools-cluster` | `fred-dev` | `fred-dev:177` | ACTIVE | 1/1 HEALTHY |

---

## Rollbacks

| Component | Rollback To |
|-----------|-------------|
| ECS service | `fred-dev:176` |
| Harness task def | `fait-v2-agent-harness:17` |

```bash
# If rollback needed:
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev --task-definition fred-dev:176 --region us-east-1
```

---

## Deploy Timeline

| Step | Result |
|------|--------|
| Preflight check | ✅ Passed |
| Blazor build (`fred-chat:f1af77a8`) | ✅ Success |
| Blazor ECR push | ✅ `sha256:1fb7d1ba...` |
| Harness build (`fait-v2-agent-harness:f1af77a8`) | ✅ Success |
| Harness ECR push | ✅ `sha256:042c90b8...` |
| Harness task def `:18` registered | ✅ |
| fred-dev task def `:177` registered | ✅ |
| ECS service update | ✅ |
| ECS service HEALTHY | ✅ 1 running, 0 pending, 1 deployment |
