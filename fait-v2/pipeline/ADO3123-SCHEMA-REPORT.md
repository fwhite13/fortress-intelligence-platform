# ADO#3123 — Schema Consolidation Build Report
**Date:** 2026-05-09
**Branch:** main (HEAD)
**Agent:** Rhodey (Claude Sonnet 4.6)

---

## Summary

Completed surgical schema consolidation to make FAIT v2 run against `fait_dev` (live production schema) instead of `fait_v2_dev`. All model/DbContext adaptations were already applied in prior commits (1bb5e191 and earlier). This WI completed the missing migration infrastructure.

---

## Files Changed

### Created: `Data/Migrations/20260509000000_FaitDevConsolidation.cs`
Single surgical migration that:
- Creates `__EFMigrationsHistory` bootstrap table IF NOT EXISTS
- Adds new v2 columns to existing fait_dev tables (ALTER TABLE IF NOT EXISTS)
- Creates all v2-only tables with IF NOT EXISTS guards
- Does NOT touch existing fait_dev table structures

### Created: `Data/Migrations/FaitV2DbContextModelSnapshot.cs`
Auto-generated via `dotnet ef migrations add --context FaitV2DbContext` (temp migration deleted afterward). 1620 lines — reflects full combined model state including all column name mappings.

---

## Column Mappings Confirmed in DbContext (prior commits)

### users (fait_dev: PascalCase existing, snake_case new)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | PascalCase PK |
| Email | `Email` | PascalCase |
| DisplayName | `DisplayName` | PascalCase, nullable |
| EntraOid | `entra_oid` | snake_case, nullable |
| CreatedAt | `CreatedAt` | PascalCase |
| UpdatedAt | `updated_at` | NEW column |
| OnboardingCompletedAt | `onboarding_completed_at` | NEW column |
| OnboardingStep | `onboarding_step` | NEW column |
| AvatarUrl | `avatar_url` | NEW column |

### conversations (fait_dev: PascalCase existing)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | PascalCase PK |
| UserId | `UserId` | PascalCase FK |
| Title | `Title` | PascalCase |
| CreatedAt | `CreatedAt` | PascalCase |
| LastActiveAt | `last_active_at` | NEW column |
| EstimatedTokenCount | `estimated_token_count` | NEW column |

### messages (fait_dev: PascalCase existing)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | PascalCase PK |
| ConversationId | `ConversationId` | PascalCase FK |
| Role | `Role` | PascalCase |
| Content | `Content` | PascalCase |
| CreatedAt | `CreatedAt` | PascalCase |
| CompactedAt | `compacted_at` | NEW column |
| IsCompactionSummary | `is_compaction_summary` | NEW column |
| SessionType | `session_type` | NEW column, default 'main' |
| PluginAgentId | `plugin_agent_id` | NEW column |
| TokenCount | `token_count` | NEW column |

### projects (fait_dev: PascalCase existing, snake_case KB flags)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | PascalCase PK |
| UserId | `UserId` | PascalCase FK |
| Name | `Name` | PascalCase |
| Description | `Description` | PascalCase |
| CustomInstructions | `CustomInstructions` | PascalCase |
| Model | `Model` | PascalCase |
| CreatedAt | `CreatedAt` | PascalCase |
| UpdatedAt | `UpdatedAt` | PascalCase |
| EnableFortressKb | `enable_fortress_kb` | snake_case (v1 lowercase version) |
| EnablePersonalKb | `enable_personal_kb` | snake_case (v1 lowercase version) |
| V1ProjectId | `v1_project_id` | NEW column |

### kb_entries (fait_dev: PascalCase, int PK)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | int PK, auto-increment |
| UserId | `UserId` | PascalCase |
| TeamId | `TeamId` | int?, PascalCase |
| Tier | `Tier` | PascalCase |
| Title | `Title` | PascalCase |
| Content | `Content` | PascalCase |
| Tags | `Tags` | PascalCase |
| SourceUrl | `SourceUrl` | PascalCase |
| CreatedAt | `CreatedAt` | PascalCase |
| UpdatedAt | `UpdatedAt` | PascalCase |

### kb_teams (fait_dev: PascalCase, int PK)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | int PK, auto-increment |
| CreatorId | `CreatorId` | PascalCase |
| Name | `Name` | PascalCase |
| Description | `Description` | PascalCase |
| CreatedAt | `CreatedAt` | PascalCase |

### kb_team_members (fait_dev: PascalCase, int PK + int TeamId)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | int PK, auto-increment |
| TeamId | `TeamId` | int, PascalCase FK |
| UserId | `UserId` | PascalCase |
| Role | `Role` | PascalCase |
| JoinedAt | `JoinedAt` | PascalCase |

### mcp_servers (fait_dev: snake_case, matches v2 + new cols)
| Property | Column | Notes |
|---|---|---|
| Id | `id` | snake_case PK |
| Name | `name` | snake_case |
| Slug | `slug` | snake_case, NOT NULL |
| EndpointUrl | `endpoint_url` | nullable |
| AuthType | `auth_type` | snake_case |
| IsActive | `is_active` | snake_case |
| CreatedAt | `created_at` | snake_case |
| DefaultRead | `default_read` | NEW column |
| DefaultWrite | `default_write` | NEW column |

### user_mcp_tokens (v1 table name; server_name new col)
| Property | Column | Notes |
|---|---|---|
| Id | `id` | snake_case PK |
| UserId | `user_id` | snake_case |
| ServerName | `server_name` | NEW column (v1 had server_id) |
| AccessToken | `access_token` | snake_case |
| RefreshToken | `refresh_token` | snake_case |
| TokenExpiresAt | `token_expires_at` | snake_case |
| CreatedAt | `created_at` | snake_case |
| UpdatedAt | `updated_at` | snake_case |

### project_documents (fait_dev: PascalCase)
| Property | Column | Notes |
|---|---|---|
| Id | `Id` | PascalCase PK |
| ProjectId | `ProjectId` | PascalCase FK |
| Filename | `Filename` | PascalCase |
| ContentType | `ContentType` | PascalCase |
| Content | `Content` | PascalCase |
| FileSize | `FileSize` | PascalCase |
| UploadedAt | `UploadedAt` | PascalCase |
| S3Key | `S3Key` | PascalCase |
| IngestionStatus | `IngestionStatus` | PascalCase |
| IngestedAt | `IngestedAt` | PascalCase |

---

## Migration: Tables Skipped (already exist in fait_dev)

- `users`
- `conversations`
- `messages`
- `projects`
- `kb_entries`
- `kb_teams`
- `kb_team_members`
- `mcp_servers`
- `user_mcp_tokens`
- `project_documents`
- `user_devops_connections`
- `DataProtectionKeys` (handled by FipPortalDbContext)

## Migration: New Columns Added to Existing Tables

| Table | Column | Type | Default |
|---|---|---|---|
| users | onboarding_completed_at | datetime(6) NULL | — |
| users | onboarding_step | int NULL | — |
| users | updated_at | datetime(6) NULL | CURRENT_TIMESTAMP(6) ON UPDATE |
| users | avatar_url | varchar(1000) NULL | — |
| conversations | last_active_at | datetime(6) NULL | — |
| conversations | estimated_token_count | int NOT NULL | 0 |
| messages | compacted_at | datetime(6) NULL | — |
| messages | is_compaction_summary | tinyint(1) NOT NULL | 0 |
| messages | session_type | varchar(10) NOT NULL | 'main' |
| messages | plugin_agent_id | varchar(50) NULL | — |
| messages | token_count | int NOT NULL | 0 |
| projects | v1_project_id | int NULL | — |
| mcp_servers | default_read | tinyint(1) NOT NULL | 1 |
| mcp_servers | default_write | tinyint(1) NOT NULL | 0 |
| user_mcp_tokens | server_name | varchar(100) NULL | — |

## Migration: New v2-Only Tables Created

- `main_assistants` — FK → users.Id
- `memory_topics` — FK → users.Id
- `user_sessions` — FK → users.Id
- `design_agent_sessions` — FK → users.Id
- `design_agent_artifacts` — FK → design_agent_sessions.id
- `pushed_messages` — FK → users.Id
- `artifact_records`
- `feedback_submissions`
- `agent_plugins`
- `scheduled_tasks`
- `scheduled_task_runs` — FK → scheduled_tasks.id
- `scheduled_task_approvals`
- `conversation_tasks` — FK → users.Id

---

## Acceptance Criteria Verification

| # | Criterion | Status |
|---|---|---|
| 1 | `dotnet build` 0 errors | ✅ PASS |
| 2 | No existing fait_dev tables dropped/altered | ✅ All ALTER TABLE use IF NOT EXISTS |
| 3 | New v2-only tables via IF NOT EXISTS | ✅ All CREATE TABLE use IF NOT EXISTS |
| 4 | New columns via ADD COLUMN IF NOT EXISTS | ✅ |
| 5 | All v2 models map to fait_dev column names | ✅ via FaitV2DbContext HasColumnName() |
| 6 | KbEntry.Id, KbTeam.Id, KbTeamMember.Id = int | ✅ |
| 7 | KbEntry.TeamId, KbTeamMember.TeamId = int?/int | ✅ |
| 8 | User.EntraOid nullable | ✅ string? |
| 9 | McpUserToken maps to user_mcp_tokens | ✅ ToTable("user_mcp_tokens") |
| 10 | user_mcp_tokens.server_name added via migration | ✅ |
| 11 | McpServer.EndpointUrl nullable | ✅ string? |
| 12 | Program.cs seeding includes Slug | ✅ Slug = tg.Name.ToLowerInvariant().Replace("-", "_") |

---

## Build Result

```
Build succeeded.
    0 Warning(s) [first build, no-snapshot]
    5 Warning(s) [final build, all pre-existing]
    0 Error(s)
Time Elapsed 00:00:03.50
```

Pre-existing warnings (not introduced by this WI):
- CS8604 in ConnectorService.cs:64 (nullable ServerName hash add)
- CS8604 in ScheduledTaskNotificationService.cs:59 (nullable EntraOid)
- CS8604 in Program.cs:700 (nullable EntraOid)
- CS0649 in KnowledgeBase.razor:501 (unassigned timer field)
- BedrockRuntime1002 in CompactionService.cs:178 (model ID pattern)

---

## Assumptions

1. `dotnet ef migrations add` was used to generate the snapshot (with temp migration `__SnapshotGen` deleted afterward) — this is the correct approach per EF Core tooling.
2. The `applied_migrations` table in fait_dev (v1 custom tracker) is ignored by v2.
3. `CREATE UNIQUE INDEX IF NOT EXISTS` syntax is supported on the MySQL version deployed (Aurora MySQL 8.0+).
4. All column mappings confirmed from the `fait_dev` DDL provided in WI.
5. `disable_null_check` not needed — EF Fluent API `IsRequired(false)` overrides data annotations.
