# Pipeline State: NEXUS P0 Sprint (WI#1515, #1516, #1517, #1521)

## Current Stage: DEPLOYING (fip-nexus-build created by Fred — building fbc0b0d)
## Risk Level: medium (config + Program.cs changes, no schema changes)
## Pipeline Path: full
## Review Cycles: 0

### WIs in scope
- #1515 — Entra SSO + security headers + /health verification
- #1516 — Cookie domain .fortressam.ai
- #1517 — Key Vault wiring + DB name fix (nexus_db → nexus) + remove hardcoded secrets
- #1521 — 10-section SpecGenSystem prompt + ArtifactGenSystem placeholder

### Stage History
| Stage | Status | Agent | Started | Completed | Notes |
|-------|--------|-------|---------|-----------|-------|
| PLAN | ✅ DONE | Maria | 21:12 | 21:15 | Pre-read: task def :3 has Entra creds, DB is nexus, Cognito leftovers to clean |
| BUILD | ✅ DONE | Tony | 21:15 | 21:19 | Commit fbc0b0d. 0 errors. All 4 WIs. |
| REVIEW | ✅ PASS | Clint | 21:19 | 21:22 | PASS 23/23. 2 nitpicks, non-blocking. |
| DEPLOY | ❌ BLOCKED (x2) | Rhodey | 21:49 | — | CodeBuild IAM gap: deployer lacks CreateProject + StartBuild on nexus-* projects. fip-nexus-build must be created by admin. ADO#1517 commented with resolution options. |
| DEPLOY | ❌ BLOCKED (x3) | Rhodey | 22:14 | 22:17 | fip-nexus-build service role missing ecr:GetAuthorizationToken. PRE_BUILD failure. nexus-web:1 untouched. |
| DEPLOY | ❌ FAILED | Rhodey | 22:48 | 23:09 | Build SUCCEEDED. App crashes SIGSEGV on startup — AddAzureKeyVault with placeholder URI causes DefaultAzureCredential to exhaust probes in Fargate. Rolled back to :1. |
| BUILD (cycle 2) | ✅ DONE | Tony | 23:09 | 23:11 | Commit 16acb3f. KV guard tightened — 4-condition check, placeholder excluded. |
| REVIEW (cycle 2) | ✅ PASS | Clint | 23:11 | 23:13 | PASS. Guard correct — all 3 test cases verified. |
| DEPLOY (cycle 2) | ✅ DONE | Rhodey | 23:13 | 23:25 | nexus-web:5 live (prior failures registered :2/:3/:4). Health 200. |
| VERIFY | ❌ FAIL | Natasha | 23:25 | 23:27 | FAIL 4/8 — HTTP 500. AzureAd env vars missing from :5 (cloned from :1, not :3). IDX20803 placeholder TenantId. |
| DEPLOY (cycle 3) | ✅ DONE | Rhodey | 23:27 | 23:32 | nexus-web:6 live. :3 baseline, same image. Health 200. |
| VERIFY (cycle 2) | ✅ PASS | Natasha | 23:32 | 23:34 | PASS 8/8. IDX20803 fixed. All gates clean. |
| CONFIRM | ✅ DONE | Maria | 23:34 | 23:34 | Pipeline complete. |
