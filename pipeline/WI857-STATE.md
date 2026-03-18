# Pipeline State: WI857

## Current Stage: QUEUED (after WI#856 Done)
## Risk Level: high
## Pipeline Path: full
## Review Cycles: 0

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Reed Richards | — | 2026-03-17 | Spec: ~/projects/fip/firm/FIRM-V2-SPEC.md (1297 lines) |
| BUILD | ⏳ PENDING | Tony Stark | — | — | 8 new FIRM + 9 modified FIRM + 2 modified FAIT |
| REVIEW | ⏳ PENDING | Hawkeye | — | — | Top: Graph scope array (re-consent), admin consent gate, app reg rename in deploy checklist |
| SECURITY | ⏳ PENDING | CodeSec | — | — | High risk: new Graph scopes, OAuth re-consent, Teams message send |
| APPROVE | ✅ DONE | Fred | — | 20:23 | Standing approval |
| DEPLOY | ⏳ PENDING | Rhodey | — | — | BLOCKING PREREQS: (1) admin consent OnlineMeetingTranscripts.Read.All + ChannelMessage.Send; (2) app reg rename FAIT→Fortress Intelligence Platform; DEPLOY COMPLETE comment MUST include re-consent warning |
| VERIFY | ⏳ PENDING | Natasha | — | — | Sprint QA — FIRM v2 features; FipShared 200 mandatory |
| CONFIRM | ⏳ PENDING | Maria | — | — | |

### Key Context
- Repo: FIRM ~/projects/fip/firm/src/FortressIntelligenceRM.Web/ + FAIT ~/projects/fip/fait/src/FortressAI.Web/
- 8 new FIRM files + 9 modified FIRM + 2 modified FAIT
- Features: Teams-native transcription, calendar integration, send-to-Teams channel
- Deploy = monorepo build for FIRM (firm-web service) + FAIT rebuild (fait-prod)
- Admin consent MUST be granted before deploy (OnlineMeetingTranscripts.Read.All + ChannelMessage.Send)
- Entra app registration must be renamed FAIT→Fortress Intelligence Platform BEFORE deploy
- Re-consent warning MANDATORY in Rhodey's DEPLOY COMPLETE ADO comment

### Hard Deploy Blockers (Rhodey must verify both before proceeding)
1. Admin consent granted in Entra for OnlineMeetingTranscripts.Read.All + ChannelMessage.Send
2. App registration renamed to "Fortress Intelligence Platform" in Azure portal

### Blocked Until
WI#856 Done.
