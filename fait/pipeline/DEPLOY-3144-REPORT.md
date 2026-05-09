# Deploy Report — ADO#3144: ChatView → SendTurnAsync Wiring

**Date:** 2026-05-09  
**Engineer:** Rhodey (War Machine)  
**Commit:** `a890d5c1`  
**ADO:** #3144 — Story 1.5-A: Wire ChatView → SendTurnAsync

---

## Summary

Deployed FAIT with ChatView.HandleSend now routing through `IUserAgentRuntime.SendTurnAsync` instead of direct Bedrock calls. `effectiveSystemPrompt` (KB context, project context) is forwarded via the `TurnRequest` constructor call.

---

## What Was Deployed

- **Commit:** `a890d5c1` — `fix(fait#3144): pass effectiveSystemPrompt to TurnRequest — KB and project context now forwarded to harness`
- **Previous commit:** `db33dcc4` (feat: full 4-step setup wizard)
- **Change:** `ChatView.razor` line ~765 — SystemPrompt wired into TurnRequest constructor; HandleSend routes through SendTurnAsync harness SSE streaming

---

## Resources

| Resource | Value |
|---|---|
| ECR Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:a890d5c1` |
| ECR Digest | `sha256:72f52d5e3ecec5142f1bcc21d11075920f96378d1f4b42a9b226ffce775973fa` |
| Task Definition | `fred-dev:135` |
| Task Def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:135` |
| Previous Task Def | `fred-dev:133` (image `fred-chat:db33dcc4`) |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |

---

## Build

- **Method:** `docker build --no-cache -f fait/Dockerfile -t fred-chat:a890d5c1 .` from monorepo root `/home/fredw/projects/fip`
- **Pre-flight:** ✅ Passed
- **Build result:** ✅ Success
- **Push:** ✅ Tagged and pushed to ECR

---

## Deployment Steps

1. ✅ Docker build (no-cache) from monorepo root
2. ✅ ECR push — `fred-chat:a890d5c1`
3. ✅ Task def `fred-dev:135` registered (preserved all env vars, Fargate__* vars, taskRoleArn)
4. ✅ ECS service updated with `--force-new-deployment`
5. ✅ Stabilized: PRIMARY `fred-dev:135` running=1, pending=0

---

## Verification

### ECS Status
```
PRIMARY: running=1, pending=0, desired=1 — fred-dev:135
```

### CloudWatch Logs (`/ecs/fred-dev`)
- ✅ DB schema migrations: all idempotent (columns already exist — expected)
- ✅ MCP servers seeded: Brave Search, Azure DevOps, Microsoft 365
- ✅ Ghost conversation cleanup completed
- ✅ `Database initialization complete`
- ✅ `Now listening on: http://[::]:8080`
- ✅ `Application started. Press Ctrl+C to shut down.`
- ✅ Hosting environment: Development
- No fatal errors

---

## ADO Update

- State set to **Resolved**
- Comment posted with image digest, task def revision, and change summary

---

## Rollback

If needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:133 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Cost Impact

No change — same Fargate resources (1024 CPU / 2048 MB), same service sizing.

---

## Notes

- Task def jumped to `:135` (not `:134`) because `:134` was registered by a prior concurrent operation. `:135` is the active revision with the correct image.
- All `fail: Microsoft.EntityFrameworkCore.Database.Command` entries in logs are expected — idempotent schema migrations that gracefully handle already-applied columns.
