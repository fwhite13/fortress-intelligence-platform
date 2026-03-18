# QA Report: WI836
## Verdict: PASS
## QA Tier: Sprint QA

## Test Results

| Test | Result | Evidence |
|------|--------|----------|
| vendorply-triage.service active (running) | ✅ | `Active: active (running) since Tue 2026-03-17 16:26:55 EDT` — PID 782224, node dist/index.js |
| Deployed commit = 97605da | ✅ | `97605da fix(WI836): use /messages not /me/messages (client_credentials); clean analyzeMailboxConcentration dead code` |
| No crash loop in logs | ✅ | Clean startup: DB tunnel → MySQL pool → Graph API auth → folder cache (25 folders) → poller running. No restarts, no exceptions. Polling clean every 30s. |
| searchMailbox fix in compiled dist/ (not /me/messages) | ✅ | `dist/services/graph-mail.js:380` has `searchMailbox()` method; line 418 uses `'/messages'` (not `/me/messages`) — correct for client_credentials flow |
| Service enabled status (document actual value) | ℹ️ | `disabled` — will NOT auto-start on reboot |

## Notes
- Service is **disabled** for auto-start on reboot
- Fred action needed: `sudo systemctl enable vendorply-triage.service` if persistent on reboot desired
- Service is running in **DRY-RUN MODE** — no emails will be moved until dry-run flag is removed
- Functional test (mailbox override firing on real emails — ≥3 concentration emails triggering the override) requires live email traffic — manual by Fred
- Startup sequence confirmed: SSH tunnel to Vendorply RDS (us-east-1) → MySQL pool → LLM fallback → AttachmentAnalyzer (Layer 3.5) → Graph API → 25-folder cache — all layers ENABLED

## Verdict

**PASS** — vendorply-triage.service is active and running on the correct commit (`97605da`). Startup is clean with no crash loops. The `searchMailbox` fix is present in compiled dist output using `/messages` (not `/me/messages`). Service is disabled for reboot persistence — Fred should enable if desired. Functional mailbox-override testing requires live email traffic and is out of scope for Sprint QA.
