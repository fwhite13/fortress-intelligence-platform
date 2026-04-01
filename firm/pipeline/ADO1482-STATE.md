# Pipeline State: ADO#1482

## Current Stage: BUILDING
## Risk Level: medium
## Pipeline Path: full
## Review Cycles: 1

### Root Cause (confirmed from WI description)
Bot joins Teams pre-join screen successfully, clicks join, but Teams places it in lobby.
Bot detects uncertain state (hasMeetingUI=false, hasLeave=false) but starts FFmpeg recording silence anyway,
then fires "recording" callback. Bot never verifies it's actually IN the meeting before starting to record.

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Jarvis/Maria | 12:34 | 12:38 | Lobby detection + wait logic needed |
| BUILD | ✅ DONE | Tony | 12:38 | 12:41 | CC sonnet, 0 TS errors. Commit 8c60871 |
| REVIEW | ↩️ NEEDS-CHANGES | Clint | 12:41 | 12:44 | hasEOA branch falls through to startRecording — throw LobbyTimeoutError |
| BUILD (cycle 2) | 🔄 ACTIVE | Tony | 12:44 | — | |
