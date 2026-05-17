using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledTaskAlertOnCompletionAndUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Surgical additive migration: adds AlertOnCompletion, AlertOnFailure and UserId columns
            // to the scheduled_tasks table. These are present in the EF model but
            // absent from the live DB (the original CreateTable migration was recorded
            // as applied before these columns existed on the live table).
            //
            // Uses INFORMATION_SCHEMA conditional pattern for idempotency — Aurora MySQL 8.0.40
            // does not support ADD COLUMN IF NOT EXISTS (banned pattern).
            // UserId is added as nullable to allow safe apply on live data;
            // Rhodey will backfill/add FK constraint during deploy as needed.
            migrationBuilder.Sql("""
                SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'scheduled_tasks' AND COLUMN_NAME = 'AlertOnCompletion');
                SET @sql = IF(@col_exists = 0, 'ALTER TABLE scheduled_tasks ADD COLUMN AlertOnCompletion TINYINT(1) NOT NULL DEFAULT 0', 'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
                SET @col_exists2 = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'scheduled_tasks' AND COLUMN_NAME = 'AlertOnFailure');
                SET @sql2 = IF(@col_exists2 = 0, 'ALTER TABLE scheduled_tasks ADD COLUMN AlertOnFailure TINYINT(1) NOT NULL DEFAULT 1', 'SELECT 1');
                PREPARE stmt2 FROM @sql2; EXECUTE stmt2; DEALLOCATE PREPARE stmt2;
                SET @col_exists3 = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'scheduled_tasks' AND COLUMN_NAME = 'UserId');
                SET @sql3 = IF(@col_exists3 = 0, 'ALTER TABLE scheduled_tasks ADD COLUMN UserId char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL DEFAULT NULL', 'SELECT 1');
                PREPARE stmt3 FROM @sql3; EXECUTE stmt3; DEALLOCATE PREPARE stmt3;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'scheduled_tasks' AND COLUMN_NAME = 'UserId');
                SET @sql = IF(@col_exists > 0, 'ALTER TABLE scheduled_tasks DROP COLUMN UserId', 'SELECT 1');
                PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
                SET @col_exists2 = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'scheduled_tasks' AND COLUMN_NAME = 'AlertOnFailure');
                SET @sql2 = IF(@col_exists2 > 0, 'ALTER TABLE scheduled_tasks DROP COLUMN AlertOnFailure', 'SELECT 1');
                PREPARE stmt2 FROM @sql2; EXECUTE stmt2; DEALLOCATE PREPARE stmt2;
                SET @col_exists3 = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'scheduled_tasks' AND COLUMN_NAME = 'AlertOnCompletion');
                SET @sql3 = IF(@col_exists3 > 0, 'ALTER TABLE scheduled_tasks DROP COLUMN AlertOnCompletion', 'SELECT 1');
                PREPARE stmt3 FROM @sql3; EXECUTE stmt3; DEALLOCATE PREPARE stmt3;
            """);
        }
    }
}
