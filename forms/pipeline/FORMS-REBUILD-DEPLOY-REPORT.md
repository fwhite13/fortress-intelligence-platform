# FORMS Rebuild Deploy Report

**Date:** 2026-02-28
**Time:** 21:31–21:33 EST
**Triggered by:** P0 — stale UI (cached Docker layer skipped FIP R2 changes)

## Pre-deploy
- Task def: formiq-dev:5
- Image digest before: sha256:97af9439987f34b970a2c2c76a9f80723db21e1213075f685f0404649ad05a03
- Last push (before rebuild): 2026-02-28T19:22:28 EST

## Rebuild
- Build method: docker build --no-cache ✅
- Git commit verified: 37bf3bf (feat(fip-r2): waffle URLs updated, hamburger padding, favicon, header correction, sidebar branding, data dictionary banner, modal spec) ✅
- Home.razor grep count: 5 matches (MudGrid/Generate JSON/View Submissions/question-sets) ✅
- DLLs verified: FortressFormTools.Web.dll, FortressFormTools.Data.dll ✅

## Push
- New digest: sha256:6727f3ae0d39e7b4db4c85097c25f190c1aa08aa5377cd982391a4e2f65eb8ed
- Pushed at: 2026-02-28T21:31:19 EST

## ECS Deploy
- Service stable: ✅
- Task status: RUNNING
- Health: HEALTHY
- Task started: 2026-02-28T21:32:32 EST

## FORMS health check: HTTP 302 ✅
(302 = redirect to auth — expected healthy response for forms.dev.fortressam.ai)

## FAIT/FIRM spot check
- FAIT image pushed: 2026-02-28T19:22:28 EST — **FRESH** (after 7 PM cutoff) ✅
  - Digest: sha256:8e2528225e96dd53631c4c8fc566da22d5a1f7ff6a92e5cdf876f940fcb6e248
  - Tag: kb-latest
- FIRM image pushed: 2026-02-28T19:22:28 EST — **FRESH** (after 7 PM cutoff) ✅
  - Digest: sha256:966b2e614310bbdafe2ecd4f7824e43d081cb51ac4111142c054648242afa13e
  - Tag: dev-latest
- Action taken: none — both images are fresh, no rebuild required

## Rollback plan
aws ecs update-service --cluster fortress-tools-cluster --service formiq-dev --task-definition formiq-dev:5 --region us-east-1
