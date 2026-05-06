# Deploy Report — nexus-web Sprint 2821/2822/2806/2811

**Date:** 2026-05-06  
**Time:** 09:57–10:02 EDT  
**Agent:** War Machine (Rhodey) — DEPLOY  
**Repo:** `/home/fredw/projects/fip/nexus/`  
**HEAD Commit:** `7867087`

---

## Work Items Deployed

| ADO # | Title | Commit |
|-------|-------|--------|
| #2821 | Auth fix: NexusReviewer to VerifySubmissionAccessAsync bypass; use IsNexusEditorAsync in view guard | `ca777b2` |
| #2822 | ADO post action wired to nexus repo | `eaf36b7` |
| #2806 | v7 ArtifactGenSystem prompt in appsettings.Production.json | `b6dee8f` |
| #2811 | NexusAdmin cross-user visibility | `7867087` |

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Task Definition | `nexus-web:46` |
| Running Count | 1 |
| Desired Count | 1 |
| Status | ACTIVE |

**Rollback Command:**
```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:46 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild Project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:c7dd1b0e-99d1-43d0-a5cc-5baa0105131d` |
| Build Status | `SUCCEEDED` |
| Build Start | ~09:57:56 EDT |
| Build End | 09:59:09 EDT |
| Image Tag | `nexus-web:latest` + `nexus-web:7867087bbfb2ad000e4c33e22e5d31f2f259561d` |
| Image Digest | `sha256:5de5a2c1ed0bfb0ddbbc8238eeabf2ada5f219593b8805673774b2b13e9a8db8` |

---

## Deployment

| Field | Value |
|-------|-------|
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `nexus-web` |
| Deploy Method | `force-new-deployment` on task def `nexus-web:46` |
| Task Def Revision | `:46` (force-new-deployment — same task def, new image via `:latest` tag) |
| Task Started | 09:59:57 EDT |
| Stabilized | 10:01:22 EDT |
| Final State | 1/1 running, 0 pending, 1 deployment |

---

## EF Core Migration

| Field | Value |
|-------|-------|
| Migration | `20260506000001_AddAcceptanceCriteriaToWorkItemRecord` |
| Log Stream | `ecs/nexus-web/d136c856a0434f8aac48dbd030a9accd` |
| Startup Log | `[NEXUS] Running EF Core migrations on startup...` |
| Completion Log | `[NEXUS] EF Core migrations complete.` |
| Errors | None |

---

## Health Check

| Field | Value |
|-------|-------|
| ECS Health | `HEALTHY` |
| Container Status | `RUNNING` |
| CloudWatch Errors | None (PdfExporter font/arrow ERR — pre-existing noise, excluded) |
| Result | **PASS** ✅ |

---

## ADO Comments Posted

| ADO # | Comment ID | Status |
|-------|-----------|--------|
| #2821 | 780720 | ✅ Posted |
| #2822 | 780721 | ✅ Posted |
| #2806 | 780722 | ✅ Posted |
| #2811 | 780723 | ✅ Posted |

---

## Summary

Clean deploy. Build completed in ~1m13s. ECS stabilized in ~1m25s from build completion. EF migration `20260506000001_AddAcceptanceCriteriaToWorkItemRecord` applied cleanly on startup. No errors in CloudWatch. Service HEALTHY.
