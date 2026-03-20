# Deploy Report: WI902 — CSS Hotfix (Button Variables)

**Agent:** War Machine (James Rhodes)
**Date:** 2026-03-19
**Commit:** `4b38ff2`
**Deploy Type:** ECS force-new-deployment (CodeBuild → ECR → ECS Fargate)

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Task Definition | `famos-dev:3` |
| Running Image Digest | `sha256:d9c17f4c4efb21906dc6c059322661b49e66c0ed6d800261ffa246876616935d` |
| Running Task | `701fc7c59ae1410b8662c825b0521efb` (started 17:48:55) |
| Health Baseline | 200 OK |

---

## Changes Deployed

Two lines changed in `famos.css`:

1. `.famos-btn-primary` — `background-color: var(--navy)` → `background-color: #002050` (hardcoded to fix CSS variable resolution failure)
2. `.famos-btn-outline-sm` — added `border-style: solid !important;` (border was invisible without explicit style)

---

## Deploy Steps

| Step | Time | Status | Notes |
|------|------|--------|-------|
| ADO comment — DEPLOY RETRY | 17:51:54 | ✅ DONE | Comment ID 726413 |
| CodeBuild `fip-famos-build` start | 17:51:59 | ✅ DONE | Build ID `fip-famos-build:86ff03a7-8aed-4e92-bbf4-71938a170107` |
| CodeBuild SUCCEEDED | 17:54:01 | ✅ DONE | ~2 min build |
| ECR push (`famos-web:latest`) | 17:53:45 | ✅ DONE | Digest `sha256:42555948b657a4f3c49a606b7580722355eb73a5c41eeb4af7c54a7bcdcaf66c` |
| ECS force-new-deployment | 17:54:33 | ✅ DONE | Triggered on `famos-dev` service |
| New Fargate task RUNNING | 18:02:15 | ✅ DONE | Task `bbadbe24aa68421d84249b2ad2ed7925` |
| Health check | 18:02:30 | ✅ PASS | HTTP 200 |
| CSS spot-check | 18:02:30 | ✅ PASS | `#002050` confirmed ×5; `border-style: solid` confirmed |

---

## Post-Deploy State

| Item | Value |
|------|-------|
| Task Definition | `famos-dev:3` |
| New Image Digest | `sha256:42555948b657a4f3c49a606b7580722355eb73a5c41eeb4af7c54a7bcdcaf66c` |
| Running Task | `bbadbe24aa68421d84249b2ad2ed7925` (started 18:02:15) |
| Health | HTTP 200 |
| CSS `#002050` | ✅ Confirmed (5 occurrences) |
| CSS `border-style: solid` | ✅ Confirmed in `.famos-btn-outline-sm` |

---

## Verification Results

```
Health: 200
CSS check: #002050 #002050 #002050 #002050 #002050
.famos-btn-outline-sm {
    border-style: solid !important;
}
```

---

## Rollback Plan

If post-deploy QA fails, execute:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service famos-dev \
  --task-definition famos-dev:3 --force-new-deployment --region us-east-1
```

> Note: Both pre- and post-deploy tasks use `famos-dev:3`. Rollback means re-pulling the old image from ECR.
> The pre-deploy image digest is: `sha256:d9c17f4c4efb21906dc6c059322661b49e66c0ed6d800261ffa246876616935d`
> To fully rollback to pre-deploy image, retag ECR or re-push the previous image before running the above.

---

## Verdict: ✅ DEPLOYED

CSS hotfix live. `#002050` hardcoded, `border-style: solid` added. Natasha to verify buttons visually.
