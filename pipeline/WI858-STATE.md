# Pipeline State: WI858

## Current Stage: DEPLOY (blocked — Entra prereqs)
## Risk Level: high
## Pipeline Path: full
## Review Cycles: 1

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Reed Richards | — | 2026-03-17 | Spec: FFE-ENTRA-AUTH-SPEC.md (906 lines) |
| BUILD | ✅ DONE | Tony Stark | 20:35 | 21:44 | commits 9d33305 (ffe) + 83011c0 (fip); TS clean; all gate checks pass |
| REVIEW | ✅ DONE | Hawkeye | 21:45 | 21:54 | PASS cycle 2 — all fixes confirmed |
| SECURITY | ✅ DONE | Maria (inline) | 21:56 | 21:57 | PASS — public client, no secrets, JWT validation correct, fails open |
| APPROVE | ✅ DONE | Fred | — | 20:33 | Standing approval |
| DEPLOY | ⛔ BLOCKED | Rhodey | 21:57 | — | Waiting: Fred must add FfE.Access scope + redirect URIs in Entra portal. wwwroot prepped (aacd5bc). |
| VERIFY | ⏳ PENDING | Natasha | — | — | Browser QA — sign-in flow in Excel Online, token storage, per-user KB scoping, whoami endpoint |
| CONFIRM | ⏳ PENDING | Maria | — | — | |

### Key Context
- Taskpane repo: ~/projects/fip/fait-for-excel/ (or ~/projects/fait-for-excel/)
- Backend repo: ~/projects/fip/fait/src/FortressAI.Web/
- New npm package: @azure/msal-browser
- No new AWS services — Entra admin portal ops only
- authDialog.html is a SECOND Vite entry point (vite.config.ts must add it)
- Both manifest.xml AND manifest.local.xml must get the AppDomains entry for auth dialog
- FAIT backend: Entra JWT validation in Program.cs + OID→userId mapping

### Deploy Blockers (Rhodey must verify before CodeBuild)
1. FfE.Access scope exposed on api://887206bc-fac1-436a-a8ed-2150418d76c0
2. Redirect URIs added: https://fait.dev.fortressam.ai/excel-addin/auth-dialog.html + localhost variant

### Blocked Until
WI#856 Done (per Jarvis queue order: 858 THEN 856 THEN 857). But WI#858 is running NOW per Fred's directive.
