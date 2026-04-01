# ADO#1482 Deploy Report — Teams Lobby Detection Fixes

**Date:** 2026-04-01  
**Time:** 12:48 EDT  
**Engineer:** War Machine (James Rhodes)  
**Status:** ✅ COMPLETE

---

## What Was Deployed

**vpbot image only** — lobby detection fixes from ADO#1482 + cycle 2 EOA fix.

### Files Changed
- `src/bot/teams.ts` — lobby detection + EOA page now throws `LobbyTimeoutError` instead of just logging
- `src/bot/meeting-bot.ts` — lobby uncertain-state throws `LobbyTimeoutError` + pre-recording admission check

---

## Pre-Deploy State

| Item | Value |
|------|-------|
| ECR `firm-vpbot:latest` | `0e59e38ae2a2946040d4c973e597b9d60dbbc59d` |
| Image digest (pre) | `sha256:8b0feee4791b08aed94f71e8de4987566ab362c17d1cdfc9dc500f17fcaba6ff` |
| Pushed (pre) | 2026-04-01T09:13:36 EDT |

---

## Post-Deploy State

| Item | Value |
|------|-------|
| ECR `firm-vpbot:latest` | `8c60871dc13a9c20d4e5315f2cd0496f55766ac7` |
| Commit tag | `8c60871dc13a9c20d4e5315f2cd0496f55766ac7` |
| Image digest | `sha256:6d8dbc8e4ae057f3a7a38c8312ffe8ea95f4c927647952a97a670880eb093afc` |
| Pushed | 2026-04-01T12:48:05 EDT |

---

## Commit Notes

- **HEAD commit:** `8c60871` — fix(ADO#1482): lobby uncertain-state throws LobbyTimeoutError + pre-recording admission check
- **Unstaged change included in build:** `teams.ts` cycle 2 EOA fix — EOA page now throws `LobbyTimeoutError` instead of logging only
  - This is the `eb3d689` content described in the deploy plan, present as an unstaged working-tree change (no remote configured for skunkworks repo)

---

## ECS

No task definition update required. `firm-vpbot:2` references `:latest` — next `RunTask` will automatically pull the new image.

**firm-web:75 — NOT TOUCHED** ✅

---

## Rollback Plan

Previous image still tagged in ECR:
```bash
# Re-tag old commit as latest
MANIFEST=$(aws ecr batch-get-image \
  --repository-name firm-vpbot \
  --region us-east-1 --profile fortress-tools-deployer \
  --image-ids imageTag=0e59e38ae2a2946040d4c973e597b9d60dbbc59d \
  --query 'images[0].imageManifest' --output text)

aws ecr put-image \
  --repository-name firm-vpbot \
  --region us-east-1 --profile fortress-tools-deployer \
  --image-tag latest \
  --image-manifest "$MANIFEST"
```
