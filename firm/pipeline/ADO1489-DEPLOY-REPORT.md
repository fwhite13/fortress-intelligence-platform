# ADO#1489 — Deploy Report: vpbot Whisper medium rebuild

**Date:** 2026-04-01  
**Engineer:** War Machine (DevOps)  
**Commit:** `449dc600d0fde4fa058cc1c318172ef147916409`  
**Branch:** fix(ADO#1489): pre-bake Whisper medium instead of large-v3

---

## Pre-Deploy Snapshot (firm-vpbot:4)

| Field | Value |
|-------|-------|
| Tags | `latest`, `4a9b7807704183a2bbaf2cdf87e4640ca583d2a3` |
| Size | 3,794,011,812 bytes (~3.79 GB) |
| Digest | `sha256:a4e1361630afc0de6522cf1192d2ffba419e71bec15da1cb646af196b5e2561d` |

---

## New Image (firm-vpbot:5)

| Field | Value |
|-------|-------|
| Tags | `latest`, `449dc600d0fde4fa058cc1c318172ef147916409` |
| Size | 2,358,256,350 bytes (~2.20 GB) |
| Digest | `sha256:ea0df3253eed286640b6c6046d80ef50839521e244644e25351e2ca982f8b611` |
| ECR Repo | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot` |

---

## Size Delta

| Metric | Value |
|--------|-------|
| Before (large-v3) | 3,794,011,812 bytes (3.79 GB) |
| After (medium) | 2,358,256,350 bytes (2.20 GB) |
| **Delta** | **-1,435,755,462 bytes (-1.44 GB, -38%)** |

Within target range of ~1.8–2.2 GB. ✅

---

## Task Definition

| Field | Value |
|-------|-------|
| Previous ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-vpbot:4` |
| **New ARN** | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-vpbot:5` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:449dc600d0fde4fa058cc1c318172ef147916409` |

---

## What Changed

- Dockerfile pre-bake switched from `large-v3` → `medium`
- Whisper medium model: ~750 MB (vs ~1.5 GB for large-v3)
- "Whisper medium pre-baked successfully" confirmed in build log
- Build time faster than ADO#1488 build (less model data to download)

---

## Rollback

firm-vpbot task def `:4` still available. To rollback:
- Use `firm-vpbot:4` task definition in RunTask
- Or re-tag ECR image `4a9b7807...` as `latest` and register new task def pointing to it

---

## Build Log

`/tmp/vpbot-1489-build.log`
