# ADO#1553 — Deploy Report: NEXUS Tile Image Rebuild

**Date:** 2026-04-02  
**Engineer:** War Machine (devops subagent)  
**Status:** ✅ COMPLETE

---

## What Was Deployed

Rebuilt `fip-portal` Docker image from commit `7656ffd` to include the NEXUS tile added in `fd904fc`. The previous `fip-portal:prod` image predated that commit and did not contain the NEXUS tile code in `Home.razor`. The task definition `fip-web:4` already had the correct `Apps__NexusUrl` env var — only the image needed rebuilding.

---

## Changes

| Item | Before | After |
|------|--------|-------|
| `fip-portal:prod` image | Pre-`fd904fc` (no NEXUS tile) | Commit `7656ffd` (NEXUS tile included) |
| Task definition | `fip-web:4` | `fip-web:4` (unchanged — already correct) |
| `Apps__NexusUrl` | `https://nexus.fortressam.ai` | `https://nexus.fortressam.ai` (kept) |
| `FIP__ComingSoonApps` | `forms,firm` | `forms,firm` (kept — intentional) |

---

## ECR Push

- **Repo:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-portal`
- **Tags pushed:**
  - `prod` → `sha256:8c21649fee6924a9010ddcf24f51992f3d9892deee9cfe49a2c844cbfe0ac8d9`
  - `7656ffd` → same digest
- **Build flags:** `--no-cache`

---

## ECS Deployment

- **Cluster:** `fortress-tools-cluster`
- **Service:** `fip-web`
- **Task definition:** `fip-web:4` (force-new-deployment)
- **Rollout state:** `COMPLETED`
- **Running count:** `1`

---

## Health Check

```
curl -sk -o /dev/null -w "%{http_code}" https://fip.fortressam.ai/
→ 403 (Cloudflare proxy — expected/healthy)
```

---

## Notes

- `fip-web:5` was briefly registered mid-deploy with a `FIP__ComingSoonApps` change (`forms,firm` → `forms`) but that change was **reverted per Fred's instruction** — FIRM showing Coming Soon is intentional.
- `fip-web:5` registration was abandoned; final deploy used `fip-web:4` as-is.

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
