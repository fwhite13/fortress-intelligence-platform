# FAIT Processing Chip Fix + Dictation Stutter Fix — Deploy Report

**Date:** 2026-03-10  
**Time:** 01:02–01:08 EDT  
**Deployer:** devops subagent (Maria Hill request)  
**Commit:** `e33c3d4` (main)

---

## Summary

✅ Deploy **SUCCEEDED** — `fred-dev:57` HEALTHY, digest match confirmed, health endpoint green.

⚠️ Migration `kb-documents-nullable-projectid-v1` ran but **failed non-fatally** due to a FK constraint incompatibility. See details below.

---

## Step 1: CodeBuild

| Field | Value |
|-------|-------|
| **Build ID** | `fip-fait-build:8abbdb64-0c45-4411-9136-c53860ea65cc` |
| **Result** | `SUCCEEDED` |
| **Duration** | ~90 seconds |
| **Started** | 01:03:03 EDT |
| **Completed** | 01:04:34 EDT |

---

## Step 2: Task Definition

| Field | Value |
|-------|-------|
| **Cloned from** | `fred-dev:56` |
| **New revision** | `fred-dev:57` |
| **Image** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest` |

---

## Step 3–4: ECS Deployment

| Field | Value |
|-------|-------|
| **Service** | `fred-dev` on `fortress-tools-cluster` |
| **Task def deployed** | `fred-dev:57` |
| **Task ARN** | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/54ff0f4819e64eb38c9803f7db86f097` |
| **Activating** | 01:06:41 EDT |
| **HEALTHY** | 01:07:24 EDT |
| **Rollback target** | `fred-dev:56` (untouched, still registered) |

---

## Step 5: Digest & Health Verification

| Check | Result |
|-------|--------|
| **Running digest** | `sha256:205423189fa66aca181cc4f81401b8647eefd3ec8edaff4e6a20e637fe751e22` |
| **ECR kb-latest digest** | `sha256:205423189fa66aca181cc4f81401b8647eefd3ec8edaff4e6a20e637fe751e22` |
| **Digest match** | ✅ MATCH |
| **Health endpoint** | `https://fait.dev.fortressam.ai/health` → `{"status":"healthy","service":"fred","timestamp":"2026-03-10T05:07:32.4632824Z"}` ✅ |

---

## Migration Log: `kb-documents-nullable-projectid-v1`

**Found in CloudWatch** log stream `ecs/fred/54ff0f4819e64eb38c9803f7db86f097`.

**Status: ⚠️ FAILED (non-fatal) — migration was NOT applied**

```
fail: Microsoft.EntityFrameworkCore.Database.Command[20102]
      Failed executing DbCommand (1ms)
      ALTER TABLE project_documents MODIFY COLUMN ProjectId char(36) NULL

warn: FortressAI.Web.Services.DatabaseInitializationService[0]
      kb-documents-nullable-projectid-v1 migration failed (non-fatal)
      MySqlConnector.MySqlException (0x80004005): Referencing column 'ProjectId'
      and referenced column 'Id' in foreign key constraint
      'FK_project_documents_projects_ProjectId' are incompatible.
```

**Root cause:** The `ALTER TABLE project_documents MODIFY COLUMN ProjectId char(36) NULL` failed because MySQL/Aurora cannot modify a column that is part of a foreign key constraint (`FK_project_documents_projects_ProjectId`) in this way. The referenced column type/nullability must be compatible with the FK.

**Impact:** The `ProjectId` column on `project_documents` remains NOT NULL. The processing chip fix that depends on nullable `ProjectId` may not function correctly for uploads without a project context. The app started successfully and is HEALTHY — the migration failure was caught and logged as non-fatal.

**Action required:** The migration SQL needs to be updated to either:
1. Drop the FK constraint, alter the column to NULL, then recreate the FK (with nullable support), OR
2. Modify the migration to handle the FK constraint before altering the column.

This should be escalated to the software engineer (commit `e33c3d4`) for a fix.

---

## Other Migrations (same startup)

All prior migrations ran as expected (idempotent — already applied):
- `ALTER TABLE mcp_servers ADD COLUMN oauth_client_secret` — already applied ✅
- `ALTER TABLE mcp_servers ADD COLUMN rate_limit_per_minute` — already applied ✅
- `ALTER TABLE conversations DROP COLUMN EnableTeamKbId` — already applied ✅
- `ALTER TABLE users ADD COLUMN is_active` — already applied ✅
- `ALTER TABLE users ADD COLUMN is_entra_user` — already applied ✅
- `mcp_tool_call_log MODIFY COLUMN input_json LONGTEXT` — **newly applied** ✅
- `mcp_tool_call_log MODIFY COLUMN output_json LONGTEXT` — **newly applied** ✅
- `Project clean slate migration` — already applied ✅
- `KB team rename migration` — already applied ✅

---

## Rollback Procedure (if needed)

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:56 --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## Verdict

| Item | Status |
|------|--------|
| Build | ✅ SUCCEEDED |
| Image digest match | ✅ MATCH |
| ECS service health | ✅ HEALTHY |
| Health endpoint | ✅ 200 OK |
| Migration `kb-documents-nullable-projectid-v1` | ⚠️ FAILED (non-fatal, FK constraint issue) |

**Deploy is live.** Dictation stutter fix is deployed. Processing chip fix code is deployed but the DB migration did not apply — the `ProjectId` column is still NOT NULL. Needs a follow-up fix from the engineering team.
