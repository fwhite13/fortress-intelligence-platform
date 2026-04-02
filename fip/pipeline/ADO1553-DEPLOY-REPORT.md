# ADO#1553 — Deploy Report
**fip-web:4 — Apps__NexusUrl env var addition**
**Date:** 2026-04-02
**Deployed by:** War Machine (devops subagent)

---

## Summary

Registered `fip-web:4` task definition from `:3` baseline with `Apps__NexusUrl=https://nexus.fortressam.ai` added to the container environment. Updated ECS service to use the new revision. No CodeBuild, no image rebuild — config-only change.

---

## What Changed

| Field | Before | After |
|-------|--------|-------|
| Task Definition | `fip-web:3` | `fip-web:4` |
| `Apps__NexusUrl` | *(not present)* | `https://nexus.fortressam.ai` |

### Environment vars in `fip-web:4` (Apps__ namespace)
```
Apps__FaitUrl=https://fait.fortressam.ai
Apps__FirmUrl=https://firm.fortressam.ai
Apps__FormsUrl=https://forms.fortressam.ai
Apps__NexusUrl=https://nexus.fortressam.ai   ← NEW
```

---

## Execution Log

| Step | Result |
|------|--------|
| Credentials | `arn:aws:iam::742932328420:user/fortress-tools-deployer` ✅ |
| ADO comment (start) | Posted — comment ID 736878 ✅ |
| Export `fip-web:3` task def | `/tmp/fip-td-v3.json` ✅ |
| Build `fip-td-v4.json` | `Apps__NexusUrl` added, `taskRoleArn` (null) stripped ✅ |
| Register `fip-web:4` | `arn:aws:ecs:us-east-1:742932328420:task-definition/fip-web:4` ✅ |
| ECS service update | `fip-web` → `fip-web:4`, `force-new-deployment` ✅ |
| Rollout poll | COMPLETED after ~7 intervals (~3.5 min), running=1 ✅ |
| Health check | Cloudflare bot challenge (expected for curl) — app live behind CF ✅ |
| ADO comment (complete) | Posted — comment ID 736883 ✅ |
| ADO WI #1553 state | → **Closed** ✅ |

---

## Rollback

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fip-web \
  --task-definition fip-web:3 \
  --force-new-deployment \
  --region us-east-1
```

---

## Notes

- `taskRoleArn` is `null` in `:3` and was omitted from the `:4` registration payload (AWS CLI rejects `null` for that field).
- Health check returns Cloudflare managed challenge (403 with `cf-mitigated: challenge`) from headless curl — this is standard CF bot protection behavior, not an app error. ECS deployment state is `COMPLETED`, `runningCount=1`.
- No image changes. No CodeBuild triggered. Pure task definition config update.

---

_War Machine out._
