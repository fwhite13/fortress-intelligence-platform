# ADO#2604 — NBAIS WC Visual Polish Deploy Report

**Date:** 2026-04-30  
**Deployer:** War Machine (Rhodey)  
**Commit:** 1db791c  

---

## What Was Deployed

- `scripts/build-nbais-wc-template.py` — visual polish fixes (template already regenerated + S3 synced by Tony)
- `src/services/assembleTemplateData.js` — EL fee corrected (20→120), stackedLogoBase64 key alignment fix
- `templates/verticals/nbais-wc/master.docx` — already synced to S3 by Tony via `--sync` flag

---

## Pre-Deploy State

| Field | Value |
|-------|-------|
| Task Definition | `proposal-generator-dev:24` |
| Running / Desired | 1 / 1 |
| ECR Image | `fip-proposal-generator:a078f36` (prev) |
| S3 master.docx | last-modified 2026-04-30 17:25:45 UTC (Tony's sync) ✓ |

---

## Build

- Method: `docker build --no-cache` from monorepo root
- Base: `node:22-alpine` + LibreOffice 25.8.1.1
- Result: **SUCCESS**
- Image digest: `sha256:a426111491c4a2747a2594fb1984b4f9c0f5f70c037d509db548e29b4558bf6c`

---

## ECR Push

| Tag | Digest |
|-----|--------|
| `fip-proposal-generator:latest` | sha256:a426111... |
| `fip-proposal-generator:1db791c` | sha256:a426111... |

---

## Task Definition

- **New revision:** `proposal-generator-dev:25`
- **Image pinned to:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-proposal-generator:1db791c`
- **Rollback to:** `proposal-generator-dev:24`

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `proposal-generator-dev` |
| Force new deployment | Yes |
| Stable after | First poll (15s) |
| Running / Desired | 1 / 1 |
| Deployment status | PRIMARY |

---

## Health Check

All `/health` probes returning HTTP 200 within <1ms. No errors in CloudWatch logs for the last 5 minutes.

---

## Rollback Plan

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition proposal-generator-dev:24 \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Deploy Result

**✅ SUCCEEDED** — proposal-generator-dev:25 healthy 1/1
