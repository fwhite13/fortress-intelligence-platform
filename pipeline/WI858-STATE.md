# Pipeline State: WI858

## Current Stage: IN-REVIEW
## Risk Level: high
## Pipeline Path: full
## Review Cycles: 1

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Reed Richards | — | 2026-03-17 | Spec: FFE-ENTRA-AUTH-SPEC.md (906 lines) |
| BUILD | ✅ DONE | Tony Stark | 20:35 | 21:44 | commits 9d33305 (ffe) + 83011c0 (fip); TS clean; all gate checks pass |
| REVIEW | ↩️ NEEDS-CHANGES | Hawkeye | 21:45 | 21:50 | C1: auth-dialog path mismatch (404); I1: whoami missing authScheme; I2: storage duplication; I3: hardcoded URL |
| SECURITY | ⏳ PENDING | CodeSec | — | — | High risk: new auth scheme, JWT validation, token storage |
| APPROVE | ✅ DONE | Fred | — | 20:33 | Standing approval |
| DEPLOY | ⏳ PENDING | Rhodey | — | — | INFRA FIRST: (1) Expose FfE.Access scope on FIP app reg; (2) Add redirect URIs for authDialog.html; then CodeBuild + ECS |
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
