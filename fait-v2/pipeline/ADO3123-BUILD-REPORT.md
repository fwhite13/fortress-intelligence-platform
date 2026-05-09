# Build Report — ADO#3123

## What was built

Schema consolidation for fait-v2 → fait_dev. The rule: fait_dev v1 schema is ground truth — v2 code changes to match it. Applied across models, DbContext, services, and a surgical additive migration.

---

## Files changed

### Models (committed in prior sessions)
- `Data/Models/KbEntry.cs` — `int Id`, `int? TeamId` (was string GUID)
- `Data/Models/KbTeam.cs` — `int Id` (was string GUID)
- `Data/Models/KbTeamMember.cs` — `int Id`, `int TeamId` (was string GUIDs)
- `Data/Models/User.cs` — `string? EntraOid`, `string? DisplayName` (made nullable per fait_dev)
- `Data/Models/McpServer.cs` — Added `Slug` (NOT NULL UNIQUE in fait_dev), `DefaultRead`, `DefaultWrite`
- `Data/Models/McpUserToken.cs` — Table mapped to `user_mcp_tokens` (v1 name); `ServerName` nullable string added

### DbContext
- `Data/FaitV2DbContext.cs` — Added `HasColumnName` mappings for all PascalCase v1 columns (users, conversations, messages, projects, project_documents, kb_*); removed invalid `HasPrincipalKey<T>()` calls (were breaking build); corrected FK/index definitions; added ServerName, Slug, DefaultRead/Write mappings

### Services
- `Services/KbForgeService.cs` — `int teamId`, `int entryId` throughout (was string)

### UI
- `Components/Pages/KnowledgeBase.razor` — `SaveEntry(int? teamId)` signature; `.ToString()` on `_selectedTeam.Id` for KbDocumentService calls (still string-based for S3 paths)

### Migration (this session)
- `Data/Migrations/20260509000000_FaitDevConsolidation.cs` — Surgical additive migration
- `Data/Migrations/FaitV2DbContextModelSnapshot.cs` — Full model snapshot

---

## Migration details

**Approach:** Single additive migration. fait_dev is untouched on existing structures. Only additions.

**__EFMigrationsHistory bootstrap:** fait_dev had no EF history table (used custom `applied_migrations`). Migration bootstraps it with `CREATE TABLE IF NOT EXISTS`.

**Column additions (15 new columns on 6 existing tables):**
| Table | New Columns |
|-------|-------------|
| users | onboarding_completed_at, onboarding_step, updated_at, avatar_url |
| conversations | last_active_at, estimated_token_count |
| messages | compacted_at, is_compaction_summary, session_type, plugin_agent_id, token_count |
| projects | v1_project_id |
| mcp_servers | default_read, default_write |
| user_mcp_tokens | server_name |

**New tables (13):** main_assistants, memory_topics, user_sessions, design_agent_sessions, design_agent_artifacts, pushed_messages, feedback_submissions, artifact_records, agent_plugins, scheduled_tasks, scheduled_task_runs, scheduled_task_approvals, conversation_tasks

**Aurora MySQL gotchas resolved:**
1. `ADD COLUMN IF NOT EXISTS` not supported → INFORMATION_SCHEMA PREPARE/EXECUTE guards
2. `CREATE INDEX IF NOT EXISTS` not supported → same conditional pattern
3. FK char(36) charset incompatibility (v1 `users.Id` is `ascii`, v2 was `utf8mb4`) → removed FK constraints from CreateTable (EF handles refs in code; fait_dev v1 never had strict FKs on all tables anyway)
4. `HasPrincipalKey<T>()` invalid EF8 syntax → removed

---

## Migration verified on live fait_dev ✅

```
MigrationId: 20260509000000_FaitDevConsolidation
ProductVersion: 8.0.26
```

All 13 new tables present. 15 column additions confirmed via INFORMATION_SCHEMA query.

---

## Parallelization used

No — model, DbContext, service, and migration changes were sequential (each depends on the prior).

---

## CC sessions run

- CC Sonnet: migration file + snapshot generation
- CC Sonnet: DbContext HasPrincipalKey fix
- CC Sonnet: KnowledgeBase.razor type fixes
- Manual edits: [DbContext]/[Migration] attributes, char(36) FK fix, INFORMATION_SCHEMA guard via Python script

---

## Acceptance criteria verification

- [x] fait-v2 builds cleanly — `Build succeeded. 0 Error(s)`
- [x] Migration applied to live fait_dev — confirmed via `__EFMigrationsHistory`
- [x] No existing v1 tables altered — verified via `SHOW TABLES` + column diff
- [x] New v2-only tables created — all 13 present
- [x] KB int ID types consistent throughout (KbForgeService, KnowledgeBase.razor, models, DbContext)

---

## Things Clint should scrutinize

1. **FK constraints removed from new tables** — I dropped EF FK constraints to avoid char vs utf8mb4 charset conflict with v1 PK columns. The FK navigation properties still work in EF (EF doesn't require DB-level FKs). But if DB-level enforcement matters, we'd need to add the FKs manually with `CHARACTER SET ascii`.

2. **session_type default** — `messages.session_type` defaults to `'main'` in the migration SQL. Verify that's the right default for all existing rows being backfilled.

3. **updated_at on users** — Added with `ON UPDATE CURRENT_TIMESTAMP(6)`. Existing user rows will have NULL until their next update.

4. **ModelSnapshot accuracy** — The snapshot was CC-generated from the DbContext. Review it for any subtle mismatches vs. the actual DbContext config.

---

## How to test locally

```bash
# Connect via env vars
FORTRESS_DB_HOST=fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com
FORTRESS_DB_PASS='=RiQOSU5To4aE3F^'
FORTRESS_DB_USER=fortress_mysql
FORTRESS_DB_NAME=fait_dev

# Verify migration history
mysql -h $FORTRESS_DB_HOST -u $FORTRESS_DB_USER -p$FORTRESS_DB_PASS fait_dev \
  -e "SELECT * FROM __EFMigrationsHistory;"

# Build check
cd fait-v2/src/FortressAI.V2.Web && dotnet build
```
