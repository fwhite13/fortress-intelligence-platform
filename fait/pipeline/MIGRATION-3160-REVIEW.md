# Migration Review: ADO#3160 — AddAvatarUrlToUserAssistantConfig

## Migration ID
`20260510014154_AddAvatarUrlToUserAssistantConfig`

## Notes for Clint
This migration is a **cumulative catch-up** — the model had several properties that were never persisted in a migration. EF diffed the full model state and included all untracked columns. The key addition for ADO#3160 is `AvatarUrl` on `user_assistant_config`, but this migration also adds previously-missing columns and creates the `user_sessions` table.

**All operations are additive only** — no DROP/ALTER/RENAME on existing columns.

## SQL to be applied to fait_dev

```sql
START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `users` ADD `onboarding_completed_at` datetime(6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `users` ADD `onboarding_step` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `AvatarUrl` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `additional_context` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `communication_style` varchar(20) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `preferred_name` varchar(100) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `response_format` varchar(30) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `responsibilities` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `role` varchar(100) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `show_citations` tinyint(1) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    ALTER TABLE `user_assistant_config` ADD `use_cases_json` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    CREATE TABLE `user_sessions` (
        `Id` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `user_id` varchar(36) CHARACTER SET utf8mb4 NOT NULL,
        `started_at` datetime(6) NOT NULL,
        `last_active_at` datetime(6) NOT NULL,
        `ended_at` datetime(6) NULL,
        `task_arn` varchar(500) CHARACTER SET utf8mb4 NULL,
        `private_ip` varchar(45) CHARACTER SET utf8mb4 NULL,
        `fargate_status` varchar(20) CHARACTER SET utf8mb4 NULL,
        `fargate_session_id` varchar(200) CHARACTER SET utf8mb4 NULL,
        `task_definition_revision` varchar(100) CHARACTER SET utf8mb4 NULL,
        `created_at` datetime(6) NOT NULL,
        `updated_at` datetime(6) NOT NULL,
        CONSTRAINT `PK_user_sessions` PRIMARY KEY (`Id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    CREATE INDEX `ix_user_sessions_last_active_at` ON `user_sessions` (`last_active_at`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    CREATE INDEX `ix_user_sessions_user_id` ON `user_sessions` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510014154_AddAvatarUrlToUserAssistantConfig') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260510014154_AddAvatarUrlToUserAssistantConfig', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;
```

## What this migration contains

| Operation | Table | Column/Object | Safe? |
|-----------|-------|---------------|-------|
| ADD COLUMN | `users` | `onboarding_completed_at` datetime(6) NULL | ✅ |
| ADD COLUMN | `users` | `onboarding_step` int NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `AvatarUrl` longtext NULL | ✅ ← **ADO#3160** |
| ADD COLUMN | `user_assistant_config` | `additional_context` longtext NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `communication_style` varchar(20) NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `preferred_name` varchar(100) NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `response_format` varchar(30) NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `responsibilities` longtext NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `role` varchar(100) NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `show_citations` tinyint(1) NULL | ✅ |
| ADD COLUMN | `user_assistant_config` | `use_cases_json` longtext NULL | ✅ |
| CREATE TABLE | `user_sessions` | Full table with PK + 2 indexes | ✅ |

## Cross-check: DatabaseInitializationService

All columns EXCEPT `AvatarUrl` are already being added by `DatabaseInitializationService.alterStatements` on startup (with 1060 duplicate-column catch). The `user_sessions` table is also created via `CREATE TABLE IF NOT EXISTS` there. The EF migration is idempotent via `__EFMigrationsHistory` check — it won't re-run DDL that already ran.

**One concern for Clint:** WI spec says `VARCHAR(512)` for `avatar_url`. EF generated `longtext`. This should be `VARCHAR(512)` — needs correction in the migration before running.

## Checklist for Clint
- [ ] Only ADD COLUMN / CREATE TABLE / CREATE INDEX — no DROP/ALTER/RENAME
- [ ] All new columns are nullable (no NOT NULL without DEFAULT)
- [ ] No DROP TABLE or DROP INDEX on existing objects
- [ ] `user_sessions` table creation is idempotent (guarded by MigrationId check)
- [ ] Safe to run against live fait_dev
- [ ] The extra columns (beyond AvatarUrl) are pre-existing model properties that were never migrated — confirm they match expected schema
