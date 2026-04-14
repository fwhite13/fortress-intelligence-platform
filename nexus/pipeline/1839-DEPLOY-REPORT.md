# Deploy Report — ADO #1839
**NEXUS — Discovery Answers Persistence Fix**

## Summary
- **Date:** 2026-04-14
- **Time:** ~14:33–14:40 EDT
- **Service:** `nexus-web` on `fortress-tools-cluster`
- **Commits:** `b01ba37` + `9fdee11`
- **Result:** ✅ SUCCEEDED

## CodeBuild
- **Project:** `fip-nexus-build`
- **Build #:** 37
- **Build ID:** `fip-nexus-build:bc2b17fb-3959-47be-a1eb-9fb762974239`
- **Status:** SUCCEEDED (~90 seconds)
- **Source:** `main` branch

## ECS Task Definition
- **Previous:** `nexus-web:34`
- **Registered:** `nexus-web:35`
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest`

## ECS Service
- **Cluster:** `fortress-tools-cluster`
- **Service:** `nexus-web`
- **Task Def:** `nexus-web:35`
- **Running/Desired:** 1/1 ✅
- **Deployment status:** PRIMARY (single deployment, stable)

## Timeline
| Time (EDT) | Event |
|---|---|
| 14:33:50 | CodeBuild #37 triggered |
| 14:33:50 | ADO #1839 start comment posted |
| 14:35:33 | CodeBuild SUCCEEDED |
| 14:35:xx | Task def nexus-web:35 registered |
| 14:35:xx | ECS service updated to nexus-web:35 |
| 14:40:13 | Single deployment, 1/1 running — HEALTHY |
| 14:40:xx | ADO #1839 complete comment posted |

## Rollback
```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:34 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

## ADO
- Work Item: [#1839](https://dev.azure.com/refugegroup/FAIT/_workitems/edit/1839)
- Start comment: posted ✅
- Complete comment: posted ✅
