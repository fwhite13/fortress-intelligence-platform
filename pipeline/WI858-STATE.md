# Pipeline State: WI858

## Current Stage: BUILD (active)
## Risk Level: high
## Pipeline Path: full
## Review Cycles: 0

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Reed Richards | — | 2026-03-17 | Spec: FFE-ENTRA-AUTH-SPEC.md (906 lines) |
| BUILD | 🔄 ACTIVE | Tony Stark | 20:35 | — | 4 new + 12 modified (taskpane) + 1 new + 3 modified (backend) |
| REVIEW | ⏳ PENDING | Hawkeye | — | — | Top: authDialog in AppDomains, getAuthHeader() replaces apiKey param, OID→userId mapping, AppKey fallback intact |
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
