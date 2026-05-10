-- Migration: AddMemoryTopics (ADO#3186)
-- Generated: 2026-05-10
-- Migration file: 20260510144114_AddMemoryTopics.cs
-- Apply to: fait_dev (and prod after deploy)
-- Idempotent: CREATE TABLE IF NOT EXISTS

CREATE TABLE IF NOT EXISTS `memory_topics` (
    `Id` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    `UserId` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    `Slug` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Title` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedAt` DATETIME(6) NOT NULL,
    `UpdatedAt` DATETIME(6) NOT NULL,
    CONSTRAINT `PK_memory_topics` PRIMARY KEY (`Id`),
    CONSTRAINT `FK_memory_topics_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
) CHARACTER SET utf8mb4;

-- Unique index (idempotent via IF NOT EXISTS)
CREATE UNIQUE INDEX IF NOT EXISTS `IX_memory_topics_UserId_Slug`
    ON `memory_topics` (`UserId`, `Slug`);

-- EF migrations history entry (marks this migration as applied)
INSERT IGNORE INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260510144114_AddMemoryTopics', '8.0.8');
