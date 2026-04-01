# Pipeline State: ADO#1485

## Current Stage: DEPLOYED ✅
## Risk Level: low (config-only fix)
## Pipeline Path: shortcut (infra config change — no code)
## Review Cycles: N/A

### Root Cause
Bot callbacks blocked by Cloudflare Turnstile. Firm__ApiUrl pointed to public URL
(https://firm.dev.fortressam.ai) instead of internal VPC endpoint (http://firm.fip.internal:8080).
Bot is VPC-internal; calling the public URL routes through Cloudflare which blocks non-browser traffic.

### Fix
Update firm-web ECS task def env: Firm__ApiUrl = http://firm.fip.internal:8080
Also kill stuck bot task 369243ef1fad44e597743ee3f25c90d8

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Maria | 08:15 | 08:25 | Root cause from CW logs — no code needed |
| DEPLOY | ✅ DONE | Rhodey | 08:25 | 08:34 | firm-web:74. Firm__ApiUrl=http://firm.fip.internal:8080. TG=1 |
| VERIFY | ✅ PASS | Natasha | 08:34 | 08:36 | PASS 4/4. firm.fip.internal resolves healthy |
| CONFIRM | ✅ DONE | Maria | 08:36 | 08:36 | Pipeline complete |
