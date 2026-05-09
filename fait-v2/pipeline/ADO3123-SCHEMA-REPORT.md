# ADO#3123 — Schema Consolidation Report

## Summary
- Total v2 tables: 24
- COMPATIBLE: 3
- CONFLICT: 7
- V2-ONLY: 14
- V1-ONLY: 17

---

## Full Table Classification

| Table | Classification | Notes |
|-------|---------------|-------|
| `users` | CONFLICT | v1: Guid PK (char(36)), extra cols (password_hash, role, is_active, is_entra_user). v2: varchar(36) PK, different col set (onboarding_*, avatar_url, updated_at). v2 drops auth-local cols; Entra-only. |
| `main_assistants` | V2-ONLY | New in v2; no v1 equivalent. |
| `projects` | COMPATIBLE | v2 is superset: adds custom_instructions, enable_fortress_kb, enable_personal_kb (already in v2 migration AddProjectDocumentsAndProjectFields). |
| `memory_topics` | V2-ONLY | New in v2. |
| `user_sessions` | V2-ONLY | New in v2. |
| `mcp_servers` | CONFLICT | v1: richer schema (slug, transport_type, auth_config JSON, tool_manifest JSON, requires_user_auth, system_api_key, oauth_client_secret, rate_limit_per_minute, icon_url, updated_at). v2: simplified (id, name, endpoint_url, auth_type, default_read, default_write, is_active, created_at). Incompatible. |
| `mcp_user_tokens` | V2-ONLY | v2 uses `mcp_user_tokens`; v1 used `user_mcp_tokens` (different table name — effectively a redesign). |
| `design_agent_sessions` | V2-ONLY | New in v2. |
| `design_agent_artifacts` | V2-ONLY | New in v2. |
| `pushed_messages` | V2-ONLY | New in v2. |
| `feedback_submissions` | V2-ONLY | New in v2. |
| `artifact_records` | V2-ONLY | New in v2. |
| `agent_plugins` | V2-ONLY | New in v2. |
| `scheduled_tasks` | V2-ONLY | New in v2. |
| `scheduled_task_runs` | V2-ONLY | New in v2. |
| `scheduled_task_approvals` | V2-ONLY | New in v2. |
| `conversation_tasks` | V2-ONLY | New in v2. |
| `project_documents` | COMPATIBLE | v2 is superset: adds ingestion_status (default "none"), ingested_at. |
| `user_devops_connections` | COMPATIBLE | Same structure; v2 uses TEXT vs v1 LONGTEXT for pat_encrypted (negligible — both store encrypted PAT). |
| `conversations` | CONFLICT | v1: has model (varchar), project_id FK; v2: has estimated_token_count, last_active_at; drops model, project_id. Different schema intent. |
| `messages` | CONFLICT | v1: has model, TokensIn, TokensOut columns; v2: has compacted_at, is_compaction_summary, session_type, plugin_agent_id, token_count. Different schema. |
| `kb_entries` | CONFLICT | v1: int PK (auto-increment), Guid UserId, int? TeamId. v2: varchar(36) PK, varchar(36) UserId/TeamId. PK type mismatch. |
| `kb_teams` | CONFLICT | v1: int PK, Guid CreatorId. v2: varchar(36) PK, varchar(36) CreatorId. PK type mismatch. |
| `kb_team_members` | CONFLICT | v1: int PK, int TeamId FK, Guid UserId. v2: varchar(36) PK, varchar(36) TeamId FK, varchar(36) UserId. PK and FK type mismatch. |

### V1-ONLY Tables (informational — no action needed in v2)

| Table | Notes |
|-------|-------|
| `DataProtectionKeys` | ASP.NET DataProtection; v2 does not use data protection keys in this DB |
| `user_assistant_config` | v1 assistant configuration; replaced by v2 MainAssistant model |
| `briefing_history` | v1 briefing feature; not in v2 |
| `user_briefing_schedule` | v1 briefing feature |
| `user_microsoft_tokens` | v1 Graph API tokens; not in v2 |
| `graph_subscriptions` | v1 Graph subscriptions |
| `email_alerts` | v1 email alert feature |
| `email_log` | v1 email logging |
| `task_cache` | v1 Planner task cache |
| `calendar_cache` | v1 calendar cache |
| `post_meeting_notes` | v1 meeting notes |
| `user_mcp_tokens` | v1 MCP token store (v2 uses `mcp_user_tokens`) |
| `conversation_mcp_servers` | v1 conversation-MCP links; not in v2 |
| `mcp_tool_call_log` | v1 MCP audit log |
| `conversation_team_kbs` | v1 team KB linking |
| `user_module_permissions` | v1 module permissions |
| `chat_attachments` | v1 chat attachments |

---

## Conflict Resolution Decisions

### `users`
**Decision**: v2 schema is authoritative. The `users` table in `fait_dev` must be replaced with v2 schema.
- v1 columns dropped: `password_hash`, `role`, `is_active`, `is_entra_user`
- v2 columns added: `onboarding_completed_at`, `onboarding_step`, `updated_at`, `avatar_url`
- Both use GUID PKs (varchar(36)/char(36)) — PK type compatible but different column name convention
- **Deployment strategy**: Fresh `fait_dev` — v2 InitialSchema migration creates `users` with v2 schema

### `mcp_servers`
**Decision**: v2 schema is authoritative (simplified design — v2 no longer supports per-server auth_config, tool_manifest at DB level).
- v2 mcp_servers is a lean registry; harness handles auth/tool discovery
- **Deployment strategy**: Fresh `fait_dev` — v2 AddMcpTables migration creates `mcp_servers` with v2 schema

### `conversations`
**Decision**: v2 schema is authoritative.
- v2 drops `model` (handled at Project level) and `project_id` FK (conversation scoping changed)
- v2 adds `estimated_token_count`, `last_active_at`
- **Deployment strategy**: Fresh `fait_dev` — v2 AddConversationsAndMessages migration creates `conversations` with v2 schema

### `messages`
**Decision**: v2 schema is authoritative.
- v2 drops `model`, `TokensIn`, `TokensOut`; adds `compacted_at`, `is_compaction_summary`, `session_type`, `plugin_agent_id`, `token_count`
- v2 messages support compaction and multi-agent sessions
- **Deployment strategy**: Fresh `fait_dev` — v2 AddConversationsAndMessages migration creates `messages` with v2 schema

### `kb_entries` / `kb_teams` / `kb_team_members` (PK type conflict)
**Decision**: v2 varchar(36) GUID PKs are authoritative.
- v1 used auto-increment int PKs; v2 uses client-generated varchar(36) GUIDs
- v2 migration `20260509050927_AddProjectDocumentsAndProjectFields` already creates all three KB tables with varchar(36) PKs
- **Deployment strategy**: Fresh `fait_dev` — v2 migration creates KB tables with correct GUID PKs
- **Data concern**: Any existing KB data in v1's `fait_dev` with int PKs will be lost. This is acceptable for dev (no production KB data in dev environment).

---

## V2-ONLY Tables — Already Covered by Existing Migrations

All V2-ONLY tables have existing EF migrations. No new migrations required.

| Table | Created By Migration |
|-------|---------------------|
| `main_assistants` | `20260506224542_InitialSchema` |
| `memory_topics` | `20260506224542_InitialSchema` |
| `user_sessions` | `20260506224542_InitialSchema` |
| `mcp_servers` (v2 schema) | `20260507125357_AddMcpTables` |
| `mcp_user_tokens` | `20260507125357_AddMcpTables` |
| `design_agent_sessions` | `20260507125357_AddMcpTables` |
| `design_agent_artifacts` | `20260507125357_AddMcpTables` |
| `pushed_messages` | `20260507200000_AddPushedMessages` |
| `feedback_submissions` | `20260507172149_AddFeedbackSubmissions` |
| `artifact_records` | `20260507173056_AddArtifactRecords` |
| `agent_plugins` | `20260507180752_AddAgentPlugins` |
| `scheduled_tasks` | `20260507180721_AddScheduledTasks` |
| `scheduled_task_runs` | `20260507180721_AddScheduledTasks` |
| `scheduled_task_approvals` | `20260509075646_AddScheduledTaskApprovals` |
| `conversation_tasks` | `20260508134819_AddConversationTasks` |
| `conversations` | `20260508221221_AddConversationsAndMessages` |
| `messages` | `20260508221221_AddConversationsAndMessages` |
| `kb_teams` | `20260509050927_AddProjectDocumentsAndProjectFields` |
| `kb_entries` | `20260509050927_AddProjectDocumentsAndProjectFields` |
| `kb_team_members` | `20260509050927_AddProjectDocumentsAndProjectFields` |
| `project_documents` | `20260509050927_AddProjectDocumentsAndProjectFields` |

---

## ECS Task Definition Change Required

```
FORTRESS_DB_NAME: fait_v2_dev → fait_dev
```

- **No code change needed** — all connection string building reads from `FORTRESS_DB_NAME` env var via `builder.Configuration["FORTRESS_DB_NAME"]`
- **Code changes made in this WI** (fallback defaults only):
  - `appsettings.json` DefaultConnection: `fait_v2_dev` → `fait_dev`
  - `Program.cs` line 112 fallback default: `"fait_v2_dev"` → `"fait_dev"`
  - `FaitV2DbContextDesignTimeFactory.cs` fallback default: `"fait_v2_dev"` → `"fait_dev"`

---

## Deployment Strategy for Rhodey

Since v2 is replacing v1, the migration approach is a **clean deployment**:

1. Rename (or archive) existing `fait_dev` Aurora database to `fait_dev_v1_archive`
2. Create fresh `fait_dev` Aurora database
3. Run v2 EF migrations: `dotnet ef database update` against the new `fait_dev`
   - All 24 v2 tables will be created with correct v2 schema
   - All conflicts are resolved because the tables are created fresh
4. Update ECS Task Definition: `FORTRESS_DB_NAME` → `fait_dev`

**No intermediate data migration is needed** for dev environment. If specific user records need to be carried over from `fait_v2_dev`, a targeted SQL INSERT can be done after schema creation.

> NOTE: Do NOT attempt to run v2 migrations on top of an existing v1 `fait_dev`. The v2 `InitialSchema` migration creates `users` and `projects` which already exist in v1's `fait_dev`, causing migration failure. Clean slate is the correct approach.

---

## Migrations Created/Modified

None. All required v2 migrations already exist. No new migrations were written.

---

## Risks & Data Loss Concerns

| Risk | Severity | Notes |
|------|----------|-------|
| Loss of v1 KB data in `fait_dev` | Low | Dev environment; no production KB data. Acceptable. |
| Loss of v1 conversation/message history | Low | Dev environment. v1 chat history is not migrated. |
| Loss of v1 user accounts | Low | Users will re-onboard into v2 via Entra SSO; no password hashes to migrate. |
| `fait_v2_dev` data not migrated | Low | v2 has been running on `fait_v2_dev`; existing v2 users/data there. A targeted user migration from `fait_v2_dev` → new `fait_dev` may be desired. |
| ECS task definition not updated | High | If Rhodey doesn't update `FORTRESS_DB_NAME` in the ECS task definition, v2 will still point to `fait_v2_dev`. This is purely an ECS config change. |

---

## Build Verification

```
dotnet build /home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web

Build succeeded.
  2 Warning(s)  (pre-existing — CS0649, BedrockRuntime1002)
  0 Error(s)
```
