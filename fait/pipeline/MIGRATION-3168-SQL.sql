CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `DataProtectionKeys` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `FriendlyName` longtext CHARACTER SET utf8mb4 NULL,
        `Xml` longtext CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_DataProtectionKeys` PRIMARY KEY (`Id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `users` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `Email` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `PasswordHash` longtext CHARACTER SET utf8mb4 NOT NULL,
        `DisplayName` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Role` varchar(20) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'user',
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `LastLogin` datetime(6) NULL,
        CONSTRAINT `PK_users` PRIMARY KEY (`Id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `briefing_history` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `BriefingDate` date NOT NULL,
        `Content` longtext CHARACTER SET utf8mb4 NOT NULL,
        `EmailSummary` longtext CHARACTER SET utf8mb4 NULL,
        `calendar_events` longtext CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_briefing_history` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_briefing_history_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `email_alerts` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `MessageId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `SenderEmail` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `Subject` longtext CHARACTER SET utf8mb4 NOT NULL,
        `Importance` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `Summary` longtext CHARACTER SET utf8mb4 NULL,
        `SuggestedResponse` longtext CHARACTER SET utf8mb4 NULL,
        `Dismissed` tinyint(1) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_email_alerts` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_email_alerts_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `email_log` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `MessageId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `SenderEmail` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `Subject` longtext CHARACTER SET utf8mb4 NOT NULL,
        `Importance` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `ReceivedAt` datetime(6) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_email_log` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_email_log_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `graph_subscriptions` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `SubscriptionId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `ClientState` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `ExpiresAt` datetime(6) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_graph_subscriptions` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_graph_subscriptions_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `meeting_briefs` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `EventId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `MeetingTitle` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `MeetingStart` datetime(6) NOT NULL,
        `BriefContent` TEXT CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_meeting_briefs` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_meeting_briefs_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `meeting_notes` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `EventId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `MeetingTitle` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `Notes` TEXT CHARACTER SET utf8mb4 NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_meeting_notes` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_meeting_notes_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `projects` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Description` longtext CHARACTER SET utf8mb4 NULL,
        `CustomInstructions` longtext CHARACTER SET utf8mb4 NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'claude-sonnet-4-6',
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `EnableFortressKb` tinyint(1) NOT NULL,
        `EnablePersonalKb` tinyint(1) NOT NULL,
        CONSTRAINT `PK_projects` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_projects_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `task_digest` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `DigestDate` date NOT NULL,
        `MondayTasksJson` JSON NULL,
        `PlannerTasksJson` JSON NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_task_digest` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_task_digest_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `user_assistant_config` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `AssistantName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `AvatarId` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `ColorHex` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
        `PersonalityPreset` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_user_assistant_config` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_user_assistant_config_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `user_briefing_schedule` (
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `DeliveryTimeUtc` time(6) NOT NULL,
        `EmailDigestEnabled` tinyint(1) NOT NULL,
        CONSTRAINT `PK_user_briefing_schedule` PRIMARY KEY (`UserId`),
        CONSTRAINT `FK_user_briefing_schedule_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `user_microsoft_tokens` (
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `AccessToken` longtext CHARACTER SET utf8mb4 NOT NULL,
        `RefreshToken` longtext CHARACTER SET utf8mb4 NOT NULL,
        `ExpiresAt` datetime(6) NOT NULL,
        `MicrosoftEmail` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_user_microsoft_tokens` PRIMARY KEY (`UserId`),
        CONSTRAINT `FK_user_microsoft_tokens_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `conversations` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ProjectId` char(36) COLLATE ascii_general_ci NULL,
        `Title` varchar(500) CHARACTER SET utf8mb4 NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'claude-sonnet-4-6',
        `EnableFortressKb` tinyint(1) NOT NULL,
        `EnablePersonalKb` tinyint(1) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_conversations` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_conversations_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE SET NULL,
        CONSTRAINT `FK_conversations_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `project_documents` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `ProjectId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Filename` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `ContentType` varchar(100) CHARACTER SET utf8mb4 NULL,
        `Content` longtext CHARACTER SET utf8mb4 NULL,
        `FileSize` bigint NOT NULL,
        `UploadedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_project_documents` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_project_documents_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE TABLE `messages` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `ConversationId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Role` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `Content` longtext CHARACTER SET utf8mb4 NOT NULL,
        `Model` varchar(100) CHARACTER SET utf8mb4 NULL,
        `TokensIn` int NULL,
        `TokensOut` int NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_messages` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_messages_conversations_ConversationId` FOREIGN KEY (`ConversationId`) REFERENCES `conversations` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_briefing_history_UserId_BriefingDate` ON `briefing_history` (`UserId`, `BriefingDate`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_conversations_ProjectId` ON `conversations` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_conversations_UserId` ON `conversations` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_email_alerts_Dismissed` ON `email_alerts` (`Dismissed`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_email_alerts_UserId` ON `email_alerts` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_email_log_UserId` ON `email_log` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_graph_subscriptions_ExpiresAt` ON `graph_subscriptions` (`ExpiresAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_graph_subscriptions_UserId` ON `graph_subscriptions` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_meeting_briefs_UserId_EventId` ON `meeting_briefs` (`UserId`, `EventId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_meeting_notes_UserId_EventId` ON `meeting_notes` (`UserId`, `EventId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_messages_ConversationId` ON `messages` (`ConversationId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_project_documents_ProjectId` ON `project_documents` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_projects_UserId` ON `projects` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE INDEX `IX_task_digest_UserId_DigestDate` ON `task_digest` (`UserId`, `DigestDate`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE UNIQUE INDEX `IX_user_assistant_config_UserId` ON `user_assistant_config` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    CREATE UNIQUE INDEX `IX_users_Email` ON `users` (`Email`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302054759_AddDataProtectionKeys') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260302054759_AddDataProtectionKeys', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    DROP TABLE `meeting_briefs`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    DROP TABLE `meeting_notes`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    DROP TABLE `task_digest`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    CREATE TABLE `calendar_cache` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `EventId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `Subject` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `StartTime` datetime(6) NOT NULL,
        `EndTime` datetime(6) NOT NULL,
        `Location` varchar(500) CHARACTER SET utf8mb4 NULL,
        `OnlineMeetingUrl` longtext CHARACTER SET utf8mb4 NULL,
        `AttendeesJson` longtext CHARACTER SET utf8mb4 NULL,
        `Category` varchar(100) CHARACTER SET utf8mb4 NULL,
        `LastFetchedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_calendar_cache` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_calendar_cache_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    CREATE TABLE `task_cache` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `TaskId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `Title` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `DueDate` datetime(6) NULL,
        `PercentComplete` int NOT NULL,
        `Priority` int NOT NULL,
        `PlanTitle` varchar(255) CHARACTER SET utf8mb4 NULL,
        `BucketName` varchar(255) CHARACTER SET utf8mb4 NULL,
        `LastFetchedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_task_cache` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_task_cache_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    CREATE UNIQUE INDEX `IX_calendar_cache_UserId_EventId` ON `calendar_cache` (`UserId`, `EventId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    CREATE INDEX `IX_calendar_cache_UserId_StartTime` ON `calendar_cache` (`UserId`, `StartTime`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    CREATE INDEX `IX_task_cache_UserId_DueDate` ON `task_cache` (`UserId`, `DueDate`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    CREATE UNIQUE INDEX `IX_task_cache_UserId_TaskId` ON `task_cache` (`UserId`, `TaskId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260302132430_Phase3_GraphAPIIntegration') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260302132430_Phase3_GraphAPIIntegration', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    ALTER TABLE `conversations` ADD `EnableTeamKbId` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE TABLE `kb_projects` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `CreatorId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Description` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_kb_projects` PRIMARY KEY (`Id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE TABLE `post_meeting_notes` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `EventId` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `EventSubject` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `MeetingEndTime` datetime(6) NOT NULL,
        `Notes` longtext CHARACTER SET utf8mb4 NOT NULL,
        `Summary` longtext CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_post_meeting_notes` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_post_meeting_notes_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE TABLE `kb_entries` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ProjectId` int NULL,
        `Tier` int NOT NULL,
        `Title` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `Content` TEXT CHARACTER SET utf8mb4 NOT NULL,
        `Tags` varchar(500) CHARACTER SET utf8mb4 NULL,
        `SourceUrl` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        `UpdatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_kb_entries` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_kb_entries_kb_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `kb_projects` (`Id`) ON DELETE SET NULL
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE TABLE `kb_project_members` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `ProjectId` int NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Role` int NOT NULL,
        `JoinedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_kb_project_members` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_kb_project_members_kb_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `kb_projects` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE INDEX `IX_kb_entries_ProjectId` ON `kb_entries` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE INDEX `IX_kb_entries_UserId` ON `kb_entries` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE INDEX `IX_kb_entries_UserId_Tier` ON `kb_entries` (`UserId`, `Tier`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE UNIQUE INDEX `IX_kb_project_members_ProjectId_UserId` ON `kb_project_members` (`ProjectId`, `UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE INDEX `IX_kb_project_members_UserId` ON `kb_project_members` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE INDEX `IX_post_meeting_notes_UserId` ON `post_meeting_notes` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    CREATE INDEX `IX_post_meeting_notes_UserId_EventId` ON `post_meeting_notes` (`UserId`, `EventId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305044657_AddTeamKbToConversation') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260305044657_AddTeamKbToConversation', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE TABLE `mcp_servers` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `Slug` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `Description` longtext CHARACTER SET utf8mb4 NULL,
        `IconUrl` longtext CHARACTER SET utf8mb4 NULL,
        `TransportType` varchar(20) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'http',
        `EndpointUrl` varchar(500) CHARACTER SET utf8mb4 NULL,
        `AuthType` varchar(20) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'none',
        `auth_config` JSON NULL,
        `tool_manifest` JSON NULL,
        `IsActive` tinyint(1) NOT NULL,
        `RequiresUserAuth` tinyint(1) NOT NULL,
        `SystemApiKey` TEXT CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_mcp_servers` PRIMARY KEY (`Id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE TABLE `conversation_mcp_servers` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `ConversationId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ServerId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Enabled` tinyint(1) NOT NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_conversation_mcp_servers` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_conversation_mcp_servers_conversations_ConversationId` FOREIGN KEY (`ConversationId`) REFERENCES `conversations` (`Id`) ON DELETE CASCADE,
        CONSTRAINT `FK_conversation_mcp_servers_mcp_servers_ServerId` FOREIGN KEY (`ServerId`) REFERENCES `mcp_servers` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE TABLE `mcp_tool_call_log` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ConversationId` char(36) COLLATE ascii_general_ci NOT NULL,
        `MessageId` char(36) COLLATE ascii_general_ci NULL,
        `ServerId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ToolName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `InputJson` JSON NULL,
        `OutputJson` JSON NULL,
        `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `ErrorMessage` longtext CHARACTER SET utf8mb4 NULL,
        `LatencyMs` int NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_mcp_tool_call_log` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_mcp_tool_call_log_mcp_servers_ServerId` FOREIGN KEY (`ServerId`) REFERENCES `mcp_servers` (`Id`) ON DELETE RESTRICT,
        CONSTRAINT `FK_mcp_tool_call_log_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE TABLE `user_mcp_tokens` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ServerId` char(36) COLLATE ascii_general_ci NOT NULL,
        `AccessToken` TEXT CHARACTER SET utf8mb4 NOT NULL,
        `RefreshToken` TEXT CHARACTER SET utf8mb4 NULL,
        `TokenExpiresAt` datetime(6) NULL,
        `Scopes` varchar(1000) CHARACTER SET utf8mb4 NULL,
        `ExternalUserId` varchar(255) CHARACTER SET utf8mb4 NULL,
        `ExternalEmail` varchar(255) CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_user_mcp_tokens` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_user_mcp_tokens_mcp_servers_ServerId` FOREIGN KEY (`ServerId`) REFERENCES `mcp_servers` (`Id`) ON DELETE CASCADE,
        CONSTRAINT `FK_user_mcp_tokens_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE UNIQUE INDEX `IX_conversation_mcp_servers_ConversationId_ServerId` ON `conversation_mcp_servers` (`ConversationId`, `ServerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE INDEX `IX_conversation_mcp_servers_ServerId` ON `conversation_mcp_servers` (`ServerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE UNIQUE INDEX `IX_mcp_servers_Slug` ON `mcp_servers` (`Slug`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE INDEX `IX_mcp_tool_call_log_ConversationId` ON `mcp_tool_call_log` (`ConversationId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE INDEX `IX_mcp_tool_call_log_ServerId` ON `mcp_tool_call_log` (`ServerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE INDEX `IX_mcp_tool_call_log_UserId_CreatedAt` ON `mcp_tool_call_log` (`UserId`, `CreatedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE INDEX `IX_user_mcp_tokens_ServerId` ON `user_mcp_tokens` (`ServerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    CREATE UNIQUE INDEX `IX_user_mcp_tokens_UserId_ServerId` ON `user_mcp_tokens` (`UserId`, `ServerId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305184759_AddMcpTables') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260305184759_AddMcpTables', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` DROP FOREIGN KEY `FK_conversation_mcp_servers_conversations_ConversationId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` DROP FOREIGN KEY `FK_conversation_mcp_servers_mcp_servers_ServerId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` DROP FOREIGN KEY `FK_mcp_tool_call_log_mcp_servers_ServerId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` DROP FOREIGN KEY `FK_mcp_tool_call_log_users_UserId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` DROP FOREIGN KEY `FK_user_mcp_tokens_mcp_servers_ServerId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` DROP FOREIGN KEY `FK_user_mcp_tokens_users_UserId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversations` DROP COLUMN `EnableTeamKbId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `UserId` TO `user_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `UpdatedAt` TO `updated_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `TokenExpiresAt` TO `token_expires_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `ServerId` TO `server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `RefreshToken` TO `refresh_token`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `ExternalUserId` TO `external_user_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `ExternalEmail` TO `external_email`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `CreatedAt` TO `created_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME COLUMN `AccessToken` TO `access_token`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME INDEX `IX_user_mcp_tokens_UserId_ServerId` TO `IX_user_mcp_tokens_user_id_server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` RENAME INDEX `IX_user_mcp_tokens_ServerId` TO `IX_user_mcp_tokens_server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `UserId` TO `user_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `ToolName` TO `tool_name`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `ServerId` TO `server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `OutputJson` TO `output_json`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `MessageId` TO `message_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `LatencyMs` TO `latency_ms`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `InputJson` TO `input_json`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `ErrorMessage` TO `error_message`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `CreatedAt` TO `created_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME COLUMN `ConversationId` TO `conversation_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME INDEX `IX_mcp_tool_call_log_UserId_CreatedAt` TO `IX_mcp_tool_call_log_user_id_created_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME INDEX `IX_mcp_tool_call_log_ServerId` TO `IX_mcp_tool_call_log_server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` RENAME INDEX `IX_mcp_tool_call_log_ConversationId` TO `IX_mcp_tool_call_log_conversation_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `UpdatedAt` TO `updated_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `TransportType` TO `transport_type`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `SystemApiKey` TO `system_api_key`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `RequiresUserAuth` TO `requires_user_auth`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `IsActive` TO `is_active`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `IconUrl` TO `icon_url`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `EndpointUrl` TO `endpoint_url`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `CreatedAt` TO `created_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` RENAME COLUMN `AuthType` TO `auth_type`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` RENAME COLUMN `ServerId` TO `server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` RENAME COLUMN `CreatedAt` TO `created_at`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` RENAME COLUMN `ConversationId` TO `conversation_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` RENAME INDEX `IX_conversation_mcp_servers_ServerId` TO `IX_conversation_mcp_servers_server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` RENAME INDEX `IX_conversation_mcp_servers_ConversationId_ServerId` TO `IX_conversation_mcp_servers_conversation_id_server_id`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` MODIFY COLUMN `icon_url` varchar(500) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` ADD `oauth_client_secret` TEXT CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_servers` ADD `rate_limit_per_minute` int NOT NULL DEFAULT 30;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    CREATE TABLE `conversation_team_kbs` (
        `conversation_id` char(36) COLLATE ascii_general_ci NOT NULL,
        `team_id` int NOT NULL,
        `enabled_at` datetime(6) NOT NULL,
        CONSTRAINT `PK_conversation_team_kbs` PRIMARY KEY (`conversation_id`, `team_id`),
        CONSTRAINT `FK_conversation_team_kbs_conversations_conversation_id` FOREIGN KEY (`conversation_id`) REFERENCES `conversations` (`Id`) ON DELETE CASCADE,
        CONSTRAINT `FK_conversation_team_kbs_kb_projects_team_id` FOREIGN KEY (`team_id`) REFERENCES `kb_projects` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    CREATE INDEX `IX_conversation_team_kbs_team_id` ON `conversation_team_kbs` (`team_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` ADD CONSTRAINT `FK_conversation_mcp_servers_conversations_conversation_id` FOREIGN KEY (`conversation_id`) REFERENCES `conversations` (`Id`) ON DELETE CASCADE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `conversation_mcp_servers` ADD CONSTRAINT `FK_conversation_mcp_servers_mcp_servers_server_id` FOREIGN KEY (`server_id`) REFERENCES `mcp_servers` (`Id`) ON DELETE CASCADE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` ADD CONSTRAINT `FK_mcp_tool_call_log_mcp_servers_server_id` FOREIGN KEY (`server_id`) REFERENCES `mcp_servers` (`Id`) ON DELETE RESTRICT;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `mcp_tool_call_log` ADD CONSTRAINT `FK_mcp_tool_call_log_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`Id`) ON DELETE CASCADE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` ADD CONSTRAINT `FK_user_mcp_tokens_mcp_servers_server_id` FOREIGN KEY (`server_id`) REFERENCES `mcp_servers` (`Id`) ON DELETE CASCADE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    ALTER TABLE `user_mcp_tokens` ADD CONSTRAINT `FK_user_mcp_tokens_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`Id`) ON DELETE CASCADE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260305204521_AddConversationTeamKbs') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260305204521_AddConversationTeamKbs', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `project_documents` DROP FOREIGN KEY `FK_project_documents_projects_ProjectId`;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `users` ADD `is_active` tinyint(1) NOT NULL DEFAULT TRUE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `users` ADD `is_entra_user` tinyint(1) NOT NULL DEFAULT FALSE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `project_documents` ADD `IngestedAt` datetime(6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `project_documents` ADD `IngestionStatus` longtext CHARACTER SET utf8mb4 NOT NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `project_documents` ADD `S3Key` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `mcp_tool_call_log` MODIFY COLUMN `output_json` LONGTEXT CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `mcp_tool_call_log` MODIFY COLUMN `input_json` LONGTEXT CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    CREATE TABLE `chat_attachments` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `ConversationId` char(36) COLLATE ascii_general_ci NOT NULL,
        `MessageId` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `Filename` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `ContentType` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `S3Key` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `SizeBytes` bigint NOT NULL,
        `TokenEstimate` int NULL,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_chat_attachments` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_chat_attachments_conversations_ConversationId` FOREIGN KEY (`ConversationId`) REFERENCES `conversations` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    CREATE TABLE `user_module_permissions` (
        `id` int NOT NULL AUTO_INCREMENT,
        `user_id` char(36) COLLATE ascii_general_ci NOT NULL,
        `module` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `permission` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `granted` tinyint(1) NOT NULL DEFAULT TRUE,
        `granted_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `granted_by_user_id` char(36) COLLATE ascii_general_ci NULL,
        CONSTRAINT `PK_user_module_permissions` PRIMARY KEY (`id`),
        CONSTRAINT `FK_user_module_permissions_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    CREATE INDEX `IX_chat_attachments_ConversationId` ON `chat_attachments` (`ConversationId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    CREATE INDEX `IX_chat_attachments_MessageId` ON `chat_attachments` (`MessageId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    CREATE UNIQUE INDEX `IX_user_module_permissions_user_id_module_permission` ON `user_module_permissions` (`user_id`, `module`, `permission`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    ALTER TABLE `project_documents` ADD CONSTRAINT `FK_project_documents_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE SET NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260310064054_AddChatAttachments') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260310064054_AddChatAttachments', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260331184318_AddCalendarEventOrganizerEmail') THEN

    ALTER TABLE `users` ADD `entra_oid` varchar(255) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260331184318_AddCalendarEventOrganizerEmail') THEN

    ALTER TABLE `user_assistant_config` ADD `firm_auto_summary` tinyint(1) NOT NULL DEFAULT FALSE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260331184318_AddCalendarEventOrganizerEmail') THEN

    ALTER TABLE `user_assistant_config` ADD `firm_auto_transcript` tinyint(1) NOT NULL DEFAULT FALSE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260331184318_AddCalendarEventOrganizerEmail') THEN

    ALTER TABLE `calendar_cache` ADD `organizer_email` varchar(255) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260331184318_AddCalendarEventOrganizerEmail') THEN

    CREATE TABLE `user_devops_connections` (
        `user_id` char(36) COLLATE ascii_general_ci NOT NULL,
        `org_url` varchar(512) CHARACTER SET utf8mb4 NOT NULL,
        `pat_encrypted` LONGTEXT CHARACTER SET utf8mb4 NOT NULL,
        `created_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `updated_at` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_user_devops_connections` PRIMARY KEY (`user_id`),
        CONSTRAINT `FK_user_devops_connections_users_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260331184318_AddCalendarEventOrganizerEmail') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260331184318_AddCalendarEventOrganizerEmail', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

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

    ALTER TABLE `user_assistant_config` ADD `AvatarUrl` varchar(512) CHARACTER SET utf8mb4 NULL;

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

START TRANSACTION;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE TABLE `scheduled_tasks` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
        `ProjectId` char(36) COLLATE ascii_general_ci NULL,
        `Name` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `Prompt` TEXT CHARACTER SET utf8mb4 NOT NULL,
        `ScheduleType` ENUM('recurring','on_demand') CHARACTER SET utf8mb4 NOT NULL,
        `CronExpression` varchar(100) CHARACTER SET utf8mb4 NULL,
        `NextRunAt` datetime(6) NULL,
        `LastRunAt` datetime(6) NULL,
        `LastRunStatus` ENUM('success','failed','cancelled') CHARACTER SET utf8mb4 NULL,
        `FailureCount` int NOT NULL DEFAULT 0,
        `AlertOnCompletion` tinyint(1) NOT NULL DEFAULT FALSE,
        `AlertOnFailure` tinyint(1) NOT NULL DEFAULT TRUE,
        `IsActive` tinyint(1) NOT NULL DEFAULT TRUE,
        `TaskMode` tinyint(1) NOT NULL DEFAULT FALSE,
        `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
        CONSTRAINT `PK_scheduled_tasks` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_scheduled_tasks_projects_ProjectId` FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE SET NULL,
        CONSTRAINT `FK_scheduled_tasks_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE TABLE `scheduled_task_runs` (
        `Id` char(36) COLLATE ascii_general_ci NOT NULL,
        `TaskId` char(36) COLLATE ascii_general_ci NOT NULL,
        `StartedAt` datetime(6) NOT NULL,
        `CompletedAt` datetime(6) NULL,
        `Status` ENUM('success','failed','cancelled') CHARACTER SET utf8mb4 NOT NULL,
        `Error` TEXT CHARACTER SET utf8mb4 NULL,
        `ResultSummary` varchar(500) CHARACTER SET utf8mb4 NULL,
        `ArtifactBlobPath` varchar(500) CHARACTER SET utf8mb4 NULL,
        `SandboxId` varchar(200) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_scheduled_task_runs` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_scheduled_task_runs_scheduled_tasks_TaskId` FOREIGN KEY (`TaskId`) REFERENCES `scheduled_tasks` (`Id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE INDEX `IX_scheduled_task_runs_StartedAt` ON `scheduled_task_runs` (`StartedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE INDEX `IX_scheduled_task_runs_TaskId` ON `scheduled_task_runs` (`TaskId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE INDEX `IX_scheduled_tasks_NextRunAt` ON `scheduled_tasks` (`NextRunAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE INDEX `IX_scheduled_tasks_ProjectId` ON `scheduled_tasks` (`ProjectId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE INDEX `IX_scheduled_tasks_UserId` ON `scheduled_tasks` (`UserId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    CREATE INDEX `IX_scheduled_tasks_UserId_IsActive` ON `scheduled_tasks` (`UserId`, `IsActive`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260510040449_AddScheduledTasksAndRuns', '8.0.12');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

