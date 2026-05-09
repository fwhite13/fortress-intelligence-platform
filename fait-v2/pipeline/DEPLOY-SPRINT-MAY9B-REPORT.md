# Deploy Report — FAIT v2 Sprint May 9B

**Deployed by:** War Machine (Rhodey)  
**Date:** 2026-05-09  
**Completed at:** ~04:35 EDT  

---

## Outcome: ✅ DEPLOYED

---

## Image

| Property | Value |
|----------|-------|
| Tag | `fait-v2:3524bcc7` |
| Digest | `sha256:c54d23ecdb14e04888df3fab2dc02da7da24b2afd5c3ab8a6e96a66d4bd77635` |
| ECR URI | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:3524bcc7` |
| Build context | `/home/fredw/projects/fip` (monorepo root) |
| Dockerfile | `fait-v2/Dockerfile.debian` |

---

## Task Definition

| Property | Value |
|----------|-------|
| Previous revision | `fait-v2:42` |
| New revision | `fait-v2:43` |
| taskRoleArn | `arn:aws:iam::742932328420:role/fait-v2-task-role` ✅ preserved |
| New env vars added | `Bedrock__AvatarModerationModelId`, `AWS__AvatarBaseUrl` |

---

## ECS Deployment

| Property | Value |
|----------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `fait-v2` |
| Final state | running: 1, desired: 1, pending: 0 ✅ |
| Task definition | `fait-v2:43` |
| Rollout state | COMPLETED |

### Deployment Issue & Resolution

**Issue:** First task launch failed (exit code 139) due to missing EF migrations. The app startup seeding code queried `agent_plugins.allow_kb_write` before migrations could run, causing an unhandled exception crash.

**Root cause:** Three migrations were not in the database when the new image was deployed:
- `20260509075646_AddScheduledTaskApprovals` — `scheduled_task_approvals` table
- `20260509090000_AddAllowKbWriteToAgentPlugin` — `allow_kb_write` column on `agent_plugins`
- `20260509100000_AddAvatarUrlToUser` — `avatar_url` column on `users`

**Resolution:** Manually applied all three migrations directly to the RDS MySQL database (`fait_v2_dev`) via SQL and registered them in `__EFMigrationsHistory`. Forced a new deployment; the second launch succeeded cleanly.

**Traffic impact:** Zero — `fait-v2:42` continued serving all traffic throughout. ECS never cut over until `:43` was healthy.

---

## EF Migrations Applied

| Migration | Status |
|-----------|--------|
| `20260509075646_AddScheduledTaskApprovals` | ✅ Applied manually |
| `20260509090000_AddAllowKbWriteToAgentPlugin` | ✅ Applied manually |
| `20260509100000_AddAvatarUrlToUser` | ✅ Applied manually |

---

## Post-Deploy Verification

| Check | Result |
|-------|--------|
| ECS: running 1/1 on fait-v2:43 | ✅ PASS |
| App live: `GET /blazor.web.js` → 302 to auth | ✅ PASS |
| CloudWatch: no migration errors in new task logs | ✅ PASS |
| `ScheduledTaskBackgroundService started` in logs | ✅ PASS |
| Spot check: `POST /api/scheduled-tasks/approval/request` → 403 | ✅ PASS |

---

## WIs Shipped (14 total — all Closed)

| ADO# | Title | State |
|------|-------|-------|
| #3089 | Session resumption message (harness cold-start) | ✅ Closed |
| #3090 | Agent plugin admin panel + seed Marketing/Finance/Legal | ✅ Closed |
| #3092 | Avatar URL + NSFW moderation | ✅ Closed |
| #3093 | Runtime preference detection (harness + endpoint) | ✅ Closed |
| #3094 | File upload destination selector | ✅ Closed |
| #3096 | Scheduled task email notifications (MS Graph) | ✅ Closed |
| #3100 | Mobile responsive layout | ✅ Closed |
| #3101 | Per-connector read/write permission enforcement | ✅ Closed |
| #3105 | Credential/token scrubbing on CC stdout relay + harness logging | ✅ Closed |
| #3106 | G3: KB write intent enforcement | ✅ Closed |
| #3107 | G7: Scheduled task approval gate | ✅ Closed |
| #3108 | UserEmail injection into CC context envelope (Entra UPN) | ✅ Closed |
| #3109 | G4: MCP tool allowlist enforcement | ✅ Closed |
| #3115 | FIRM integration SharedSecret wiring | ✅ Closed |

---

## Rollback Plan

If rollback needed:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:42 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

**Note:** Rollback would require reversing the 3 applied migrations (drop `scheduled_task_approvals`, drop `allow_kb_write`, drop `avatar_url`) since `:42` doesn't know about those columns. The columns/table are additive and backward-compatible, so `:42` should tolerate them being present (it just won't use them).

---

## Credentials Used

- `fortress-tools-deployer` — all AWS operations ✅
- `openclaw-bedrock` — NOT used ✅
