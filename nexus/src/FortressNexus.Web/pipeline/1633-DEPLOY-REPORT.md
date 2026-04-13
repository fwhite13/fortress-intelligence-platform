# Deploy Report — ADO #1633 — NEXUS DiscoveryKnowledgeBaseId Update

**Date:** 2026-04-13  
**Deployer:** War Machine (devops subagent)  
**Type:** Config-only update (no code change)  
**Service:** `nexus-web` on `fortress-tools-cluster`

---

## Summary

Updated `Nexus__DiscoveryKnowledgeBaseId` env var from the Corp KB (`WYSKBKWHPL`) to the correct dedicated NEXUS Discovery KB (`WHB6WU9CVW`).

---

## Change

| Field | Old | New |
|-------|-----|-----|
| `Nexus__DiscoveryKnowledgeBaseId` | `WYSKBKWHPL` (Corp KB) | `WHB6WU9CVW` (NEXUS-Discovery-KB) |
| Task Definition | `nexus-web:26` | `nexus-web:27` |
| Image | `nexus-web:60beea17...` (unchanged) | `nexus-web:60beea17...` (unchanged) |

---

## Deployment Steps

1. ✅ Pulled `nexus-web:26` task definition
2. ✅ Updated `Nexus__DiscoveryKnowledgeBaseId` → `WHB6WU9CVW`
3. ✅ Registered `nexus-web:27` — ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:27`
4. ✅ `update-service` with `--force-new-deployment` — deployment PRIMARY
5. ✅ Reached ECS steady state (1/1 running)
6. ✅ Health check: `https://nexus.fortressam.ai/` → HTTP 403 (Cloudflare challenge — app live)
7. ✅ CloudWatch logs: clean startup, EF migrations ran, no exceptions
8. ✅ Running task confirmed: `nexus-web:27`, `healthStatus: HEALTHY`

---

## Verification

- **Task ARN:** `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/6b501fec69cf4689af7bb8a5a18716ac`
- **Task Def ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:27`
- **Health Status:** `HEALTHY`
- **Running Count:** 1/1
- `Nexus__DiscoveryKnowledgeBaseId` = `WHB6WU9CVW` ✅ confirmed in task def

---

## Rollback

If needed: `aws ecs update-service --cluster fortress-tools-cluster --service nexus-web --task-definition nexus-web:26 --force-new-deployment --profile fortress-tools-deployer --region us-east-1`

---

## CloudWatch Logs (Startup)

```
[13:42:59 INF] [NEXUS] Running EF Core migrations on startup...
[13:43:00 INF] [NEXUS] EF Core migrations complete.
[13:43:00 WRN] Overriding HTTP_PORTS '8080' and HTTPS_PORTS ''. Binding to values defined by URLS instead 'http://+:8080'.
```

Clean startup. No errors or exceptions.
