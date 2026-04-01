# Pipeline State: ADO#1487

## Current Stage: DEPLOYED ✅
## Risk Level: low (config-only — env vars on task def)
## Pipeline Path: shortcut (infra config, no code change)
## Review Cycles: N/A

### Root Cause
`FIRM_API_URL` and `BOT_CALLBACK_SECRET` missing from vpbot ECS task def.
vpbot source code already reads `process.env.FIRM_API_URL` correctly — silently skips callbacks when unset.

### Fix
Register new vpbot task def revision with:
- FIRM_API_URL=http://firm.fip.internal:8080
- BOT_CALLBACK_SECRET=bd9b7660300968f7a201384cbba697a23bfa6211b0d64854ef6c44b96060405a

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Maria | 13:26 | 13:28 | Config-only fix, no code change needed |
| DEPLOY | ✅ DONE | Rhodey | 13:28 | 13:31 | firm-vpbot:3. FIRM_API_URL + BOT_CALLBACK_SECRET added. |
| VERIFY | ✅ PASS | Natasha | 13:31 | 13:33 | PASS 6/6. All env vars confirmed. |
| CONFIRM | ✅ DONE | Maria | 13:33 | 13:33 | Pipeline complete. |
