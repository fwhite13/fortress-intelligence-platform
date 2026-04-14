# NEXUS Deploy Report — ADO #1821
**Date:** 2026-04-14 00:47–00:53 EDT  
**Deployer:** War Machine (Rhodey / devops)  
**Build:** fip-nexus-build #36  

---

## Summary

Discovery + SpecGen large file handling deployed successfully to `nexus-web`.

---

## Commits Deployed

| Commit | Description |
|--------|-------------|
| `545622a` | ADO #1821 (commit 1) |
| `22dbbe4` | ADO #1821 (commit 2) |

Both commits confirmed in HEAD on `main`.

---

## Build

| Field | Value |
|-------|-------|
| Project | `fip-nexus-build` |
| Build # | 36 |
| Build ID | `fip-nexus-build:fbd6f4b7-a1f3-4dd7-a1b1-3eb6be1847d2` |
| Source | `main` |
| Status | **SUCCEEDED** |
| Duration | ~1m 32s |

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `nexus-web` |
| Previous task def | `nexus-web:33` |
| New task def | `nexus-web:34` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:34` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| ECS Health | **1/1 running** ✅ |
| Stabilized | 00:53 EDT |

---

## Rollback

```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:33 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## ADO Activity

- **00:47** — Start comment posted on #1821
- **00:53** — Complete comment posted on #1821

---

## Notes

- Image tag was already `:latest`; task def registered cleanly as `nexus-web:34`
- Blue/green swap completed cleanly — old `:33` task drained without errors
- No issues observed during deployment
