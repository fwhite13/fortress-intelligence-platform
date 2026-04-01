# QA Report: ADO#1487 — FIRM_API_URL + BOT_CALLBACK_SECRET on vpbot task def

### Verdict: ✅ PASS (6/6)

### Environment
- **Target:** AWS ECS — `firm-vpbot:3` (us-east-1)
- **AWS Profile:** `fortress-tools-deployer`
- **Test Start:** 2026-04-01 13:29 EDT
- **Test Duration:** ~2 minutes
- **Risk Level:** Low (config-only, no image change)

---

### Test Results

| TC | Description | Result | Details |
|----|-------------|--------|---------|
| TC1 | `FIRM_API_URL` present in firm-vpbot:3 | ✅ PASS | `http://firm.fip.internal:8080` ✓ |
| TC2 | `BOT_CALLBACK_SECRET` present in firm-vpbot:3 | ✅ PASS | `bd9b7660300968f7a201384cbba697a23bfa6211b0d64854ef6c44b96060405a` ✓ |
| TC3 | Existing env vars retained | ✅ PASS | `S3_BUCKET`, `AWS_REGION`, `FIRM_MAX_MEETING_HOURS` all present ✓ |
| TC4 | firm-vpbot:3 is latest active revision | ✅ PASS | `describe-task-definition firm-vpbot` → revision `3` ✓ |
| TC5 | VpBotService references family name (no `:N` suffix) | ✅ PASS | ECS env `Firm__VpBotTaskDefinition=firm-vpbot` — auto-picks revision 3 ✓ |
| TC6 | Docker image unchanged | ✅ PASS | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:latest` ✓ |

---

### TC1–TC3 Detail — Full Environment on firm-vpbot:3

```json
[
    { "name": "S3_BUCKET",               "value": "firm-recordings-dev" },
    { "name": "BOT_CALLBACK_SECRET",     "value": "bd9b7660300968f7a201384cbba697a23bfa6211b0d64854ef6c44b96060405a" },
    { "name": "AWS_REGION",              "value": "us-east-1" },
    { "name": "FIRM_MAX_MEETING_HOURS",  "value": "4" },
    { "name": "FIRM_API_URL",            "value": "http://firm.fip.internal:8080" }
]
```

All 5 env vars confirmed. No vars dropped. 2 new vars added as expected.

---

### TC5 Detail — VpBotService family name resolution

`VpBotService.cs` reads `_config["Firm:VpBotTaskDefinition"]` and passes it as `TaskDefinition` in the `RunTaskRequest`. The FIRM web ECS task definition has:

```
Firm__VpBotTaskDefinition = "firm-vpbot"
```

Family name only — no `:N` suffix. AWS ECS will auto-select the latest **ACTIVE** revision, which is revision 3. Confirmed correct.

Additionally, `VpBotService.cs` passes `FIRM_API_URL` and `BOT_CALLBACK_SECRET` as **container overrides** at RunTask time (lines 63–64), pulling from `Firm:ApiUrl` and `Firm:BotCallbackSecret` app config. The task def values serve as defaults; runtime overrides take precedence. Architecture is sound.

---

### TC6 Detail — Image unchanged

```
742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-vpbot:latest
```

Same image as prior revisions. No Docker build was part of this change. Confirmed.

---

### Issues Found
None.

---

### Test Summary
- Total tests: 6
- Passed: 6
- Failed: 0
- Warnings: 0

### Recommendations
- Ready for live meeting test (end-to-end: trigger bot via FIRM web, verify callback received with correct secret)
- No rollback risk — config-only change is fully reversible by deploying a new revision if needed

---

_QA sign-off: Natasha Romanoff — Trust nothing. Verify everything._
_Report generated: 2026-04-01 13:30 EDT_
