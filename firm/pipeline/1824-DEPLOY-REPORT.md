# Deploy Report — ADO #1824
**FIRM RetranscribeAsync HttpClient Timeout Fix**

| Field | Value |
|---|---|
| ADO Work Item | #1824 |
| Commit | `b5beaf2` |
| Service | `firm-web` on `fortress-tools-cluster` |
| CodeBuild | `fip-firm-build` build #58 |
| CodeBuild ID | `fip-firm-build:118639f3-977b-4158-a842-33cd76334800` |
| Build Result | ✅ SUCCEEDED |
| Previous Task Def | `firm-web:88` |
| New Task Def | `firm-web:89` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest` |
| ECS Health | ✅ 1/1 |
| Rollback Target | `firm-web:88` |
| Deployed At | 2026-04-14 00:43 EDT |
| Deployed By | War Machine (devops subagent) |

## Env Var Verification
- `Firm__VpBotUrl`: ✅ Preserved (`http://172.31.48.117:3500`)
- All other env vars from `:88` carried forward

## ADO Comments
- Start comment: ✅ Posted (#743896)
- Complete comment: ✅ Posted

## Rollback
```bash
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition firm-web:88 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```
