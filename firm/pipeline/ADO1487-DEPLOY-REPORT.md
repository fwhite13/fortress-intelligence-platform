# ADO#1487 Deploy Report — vpbot Task Def: Add FIRM_API_URL + BOT_CALLBACK_SECRET

**Date:** 2026-04-01  
**Engineer:** War Machine (Rhodey — DevOps)  
**ADO WI:** FAIT#1487  
**Type:** Config-only task def update — no Docker rebuild  

---

## Root Cause

`firm-vpbot` ECS task definition was missing `FIRM_API_URL` and `BOT_CALLBACK_SECRET`. vpbot source reads `process.env.FIRM_API_URL` — when unset, logs "FIRM_API_URL not set — skipping callback" and silently drops all status callbacks (joining, recording, completed, failed).

---

## Pre-Deploy Snapshot

**Task def:** `firm-vpbot:2`  
**Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:latest`  
**Digest:** sha256:6d8dbc8e  

**Existing env vars (revision 2):**
```json
[
  { "name": "S3_BUCKET",               "value": "firm-recordings-dev" },
  { "name": "AWS_REGION",              "value": "us-east-1" },
  { "name": "FIRM_MAX_MEETING_HOURS",  "value": "4" }
]
```

---

## Deploy Action

Registered new task definition revision with two additional env vars added to the existing environment array. No image change — same `:latest` digest.

---

## New Task Definition ARN

```
arn:aws:ecs:us-east-1:742932328420:task-definition/firm-vpbot:3
```

---

## Post-Deploy Env Var Verification

**Task def:** `firm-vpbot:3`  

```json
[
  { "name": "S3_BUCKET",               "value": "firm-recordings-dev" },
  { "name": "BOT_CALLBACK_SECRET",     "value": "bd9b7660300968f7a201384cbba697a23bfa6211b0d64854ef6c44b96060405a" },
  { "name": "AWS_REGION",              "value": "us-east-1" },
  { "name": "FIRM_MAX_MEETING_HOURS",  "value": "4" },
  { "name": "FIRM_API_URL",            "value": "http://firm.fip.internal:8080" }
]
```

✅ `FIRM_API_URL` present  
✅ `BOT_CALLBACK_SECRET` present  

---

## VpBotService Task Family Reference

**Source:** `FortressIntelligenceRM.Web/Services/VpBotService.cs`  
```csharp
var taskDef = _config["Firm:VpBotTaskDefinition"];
// ...
TaskDefinition = taskDef,
```

**Config value (ECS env var on firm-web task def):**
```
Firm__VpBotTaskDefinition = "firm-vpbot"
```

**Result:** Family name (no revision pinned) → `firm-vpbot:3` will be **automatically used** on the next `RunTask` call. No firm-web code or config changes needed.

---

## Summary

| Item | Value |
|------|-------|
| Previous revision | firm-vpbot:2 |
| New revision | firm-vpbot:3 |
| New ARN | arn:aws:ecs:us-east-1:742932328420:task-definition/firm-vpbot:3 |
| Image changed | No — same :latest digest |
| Service update needed | No — RunTask uses family name |
| firm-web changes needed | No — Firm__VpBotTaskDefinition="firm-vpbot" (unpinned) |
| ADO comment posted | Yes — comment ID 736116 |

**Status:** ✅ Complete. Next meeting start will use firm-vpbot:3 with callbacks enabled.
