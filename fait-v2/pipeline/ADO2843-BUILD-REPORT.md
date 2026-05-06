# Build Report — ADO#2843

**WI:** FAIT v2: Aurora MySQL schema — users, main_assistants, projects, memory_topics, user_sessions
**Engineer:** Tony Stark (subagent)
**Date:** 2026-05-06
**Status:** ✅ COMPLETE

---

## What was built

EF Core data models + `FaitV2DbContext` + `InitialSchema` migration for the FAIT v2 Aurora MySQL database. All 5 tables specified in the WI are created with correct column types, foreign keys, and indexes.

---

## Commit

`6bf0d14` — `feat(fait-v2#2843): EF Core models, FaitV2DbContext, InitialSchema migration`

---

## Files Created/Modified

| File | Action | Notes |
|------|--------|-------|
| `FortressAI.V2.Web.csproj` | Modified | Added `Microsoft.EntityFrameworkCore.Design 8.0.*` (required for `dotnet ef` tooling) |
| `Data/Models/User.cs` | Created | `users` table model — id, entra_oid, email, display_name, onboarding_completed_at, timestamps |
| `Data/Models/MainAssistant.cs` | Created | `main_assistants` table model — id, user_id (UNIQUE FK), soul/memory blob paths, workspace prefix, fargate fields |
| `Data/Models/Project.cs` | Created | `projects` table model — id, user_id, name, description, v1_project_id |
| `Data/Models/MemoryTopic.cs` | Created | `memory_topics` table model — id, user_id, topic_name, topic_slug (unique per user), blob_path |
| `Data/Models/UserSession.cs` | Created | `user_sessions` table model — id, user_id, started_at, last_active_at, ended_at, ip_address, user_agent |
| `Data/FaitV2DbContext.cs` | Created | Full `OnModelCreating` with explicit column names, types, indexes, and FK relationships |
| `Data/FaitV2DbContextDesignTimeFactory.cs` | Created | Design-time factory for `dotnet ef` tooling (follows Nexus pattern) |
| `Program.cs` | Modified | Registered `FaitV2DbContext` with Pomelo MySQL provider |
| `Data/Migrations/20260506224542_InitialSchema.cs` | Created | EF-generated migration |
| `Data/Migrations/20260506224542_InitialSchema.Designer.cs` | Created | EF scaffolding |
| `Data/Migrations/FaitV2DbContextModelSnapshot.cs` | Created | EF model snapshot |

---

## Migration Name

`InitialSchema` — file: `20260506224542_InitialSchema.cs`

---

## Migration SQL Verification

All 5 tables confirmed in `Up()` method:

| Table | Columns | FKs | Indexes |
|-------|---------|-----|---------|
| `users` | id, entra_oid, email, display_name, onboarding_completed_at, created_at, updated_at | — | UNIQUE on entra_oid, UNIQUE on email |
| `main_assistants` | id, user_id, soul_blob_path, memory_blob_path, workspace_s3_prefix, fargate_session_id, fargate_task_arn, created_at, updated_at | → users.id (CASCADE) | UNIQUE on user_id |
| `projects` | id, user_id, name, description (TEXT), v1_project_id (INT NULL), created_at, updated_at | → users.id (CASCADE) | on user_id |
| `memory_topics` | id, user_id, topic_name, topic_slug, blob_path, last_updated_at, created_at | → users.id (CASCADE) | on user_id; UNIQUE composite (user_id, topic_slug) |
| `user_sessions` | id, user_id, started_at, last_active_at, ended_at (NULL), ip_address (NULL), user_agent (NULL) | → users.id (CASCADE) | on user_id; on started_at |

All PKs: CHAR(36) GUIDs. All timestamps: datetime(6). All FKs: ON DELETE CASCADE.

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.56
```

---

## Notable Deviation from WI

**`ServerVersion.AutoDetect` → `new MySqlServerVersion(new Version(8, 0, 28))`** in `Program.cs` registration.

`ServerVersion.AutoDetect` requires a live MySQL connection at startup — this fails at `dotnet ef migrations add` time (no live DB in CI or migration tool context). Fixed by using a pinned `MySqlServerVersion(8, 0, 28)`. This is identical to the Nexus pattern and correct for Aurora MySQL 8. No behavior difference at runtime.

---

## GuidFormat=None Compliance

✅ Connection string in `appsettings.json` already contains `GuidFormat=None;` (from #2842 scaffold).
✅ Design-time factory also uses `GuidFormat=None;` in its hardcoded connection string.
✅ No MySqlConnector connections created without GuidFormat=None.

---

## Things for Clint to Scrutinize

1. **`workspace_s3_prefix` column name** — The column is named `workspace_s3_prefix` per the WI spec, but the spec also states "S3 is not used anywhere in FAIT v2" (spec §3.3). This is a naming inconsistency in the WI itself — the column exists and stores whatever blob prefix path is needed. The name is a minor wart; safe to rename to `workspace_blob_prefix` in a follow-up if Fred prefers. Not blocking.

2. **Cascade delete behavior** — All FKs use `ON DELETE CASCADE`. This means deleting a user cascades to main_assistant, projects, memory_topics, and user_sessions. This is the right default for now (hard delete cleans up completely), but worth confirming with Fred before production: does user deletion actually cascade, or should rows be soft-deleted/archived?

3. **`description` column on `projects`** — Spec says `TEXT NULL`. Migration generates `TEXT` (correct) but nullable is implicit in EF for reference types. Verified correct in migration output.

---

## How to Test Locally

```bash
# Ensure local MySQL has the fait_v2_dev database
mysql -u root -pdev -e "CREATE DATABASE IF NOT EXISTS fait_v2_dev;"

# Apply migration
cd ~/projects/fip/fait-v2/src/FortressAI.V2.Web
dotnet ef database update

# Verify tables
mysql -u root -pdev fait_v2_dev -e "SHOW TABLES;"
```

Expected output: `main_assistants`, `memory_topics`, `projects`, `user_sessions`, `users` (plus EF's `__EFMigrationsHistory`).
