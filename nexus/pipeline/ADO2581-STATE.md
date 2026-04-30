# Pipeline State: ADO#2581

## Current Stage: DONE
## Risk Level: low
## Pipeline Path: shortcut (no deploy — validation only)
## Review Cycles: 0

### WI
- **Title:** Validate NEXUS decomp prompt v6 via standalone Bedrock call
- **ADO ID:** 2581
- **ADO URL:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2581
- **v6 Prompt:** `/home/fredw/.openclaw/workspace/memory/projects/nexus-prompt-v6-candidate.md`
- **Checklist:** §G (13 items) — in v6 candidate file
- **Test input:** `/home/fredw/.openclaw/workspace/memory/projects/forge-kb-mcp-server-spec-2026-04-27.md`

### Pipeline Notes
- No code changes, no deploy, no Clint, no Rhodey.
- Checklist is §G (13 items) from v6 candidate file
- Model: `us.anthropic.claude-sonnet-4-20250514-v1:0`, max_tokens=32768, beta=output-128k-2025-02-19
- Output: ADO2580-BUILD-REPORT.md + ADO2580-BEDROCK-OUTPUT.json (per Jarvis naming)
- Run history: v1=7/10, v2=3/10, v3=6/10, v4=8/10, v5=10/13 (ADO#2577)
- v6 key fixes: two-pass TC compliance scan; conditional migration language fix

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN  | ✅ DONE | Jarvis/Maria | 22:31 | 22:32 | WI #2581 activated (Jarvis pre-created), #2582 closed (duplicate) |
| BUILD | ✅ DONE | Tony | 22:32 | 23:05 | v6 Bedrock call complete — 11/13 §G. G10 FIXED, G6+G7 still failing. See ADO2581-BUILD-REPORT.md |
