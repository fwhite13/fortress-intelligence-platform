# Deploy Report — WIs #1673 + #1674
**Date:** 2026-04-08  
**Deployed by:** War Machine (Rhodey / devops)  
**App:** nexus-web  
**Cluster:** fortress-tools-cluster  

---

## Summary

Combined deploy for two WIs:
- **#1673** — Bedrock Discovery model ID fix (`appsettings.json` → `us.anthropic.claude-sonnet-4-6`)
- **#1674** — Narrative persisted via `UpdateNarrativeAsync` before step advance in resume mode (`NewSpecWizard.razor`)

---

## Build

| Field | Value |
|---|---|
| CodeBuild Project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:426a6db2-968c-4c58-a438-bea65177fb1e` |
| Build Number | 24 |
| Build Status | **SUCCEEDED** |
| Git SHA | `60beea17a9f528a832c75a0975ed780e1753c711` |
| Image Digest | `sha256:b69beacd96e77400747fdc1bd5b6382085eff669ff5ea991baf57ad299e6d668` |
| Image URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:60beea17a9f528a832c75a0975ed780e1753c711` |
| Build Time | ~1.5 min (16:20:27 → 16:21:47 EDT) |

---

## Task Definition

| Field | Value |
|---|---|
| Base | `nexus-web:25` (contained `Nexus__DiscoveryKnowledgeBaseId=WYSKBKWHPL`) |
| Registered | `nexus-web:26` |
| ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:26` |
| `Nexus__DiscoveryKnowledgeBaseId` | `WYSKBKWHPL` ✅ confirmed |

---

## Deployment

| Field | Value |
|---|---|
| Previous task def | `nexus-web:24` |
| New task def | `nexus-web:26` |
| Force new deployment | Yes |
| Steady state | **Reached** (running 1/1) |

---

## Health Check

| Check | Result |
|---|---|
| `curl https://nexus.fortressam.ai/` | **403** ✅ (expected — auth-protected) |
| CloudWatch startup logs | **Clean** — migrations complete, no errors or exceptions |
| Log group | `/ecs/nexus-web` stream `ecs/nexus-web/0243941aa0e54a07b940c362d45d2fe2` |

### Startup log
```
[20:24:20 INF] [NEXUS] Running EF Core migrations on startup...
[20:24:22 INF] [NEXUS] EF Core migrations complete.
[20:24:22 WRN] Overriding HTTP_PORTS '8080' — binding to URLS 'http://+:8080' (benign, pre-existing)
```

---

## Rollback

If rollback needed: deploy `nexus-web:24` (pre-KB-ID, pre-fixes).

```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:24 --force-new-deployment --profile fortress-tools-deployer
```

---

## Schema Changes
None — no migrations required.
