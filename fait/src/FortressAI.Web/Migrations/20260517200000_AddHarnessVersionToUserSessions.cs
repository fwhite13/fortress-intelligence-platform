using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    public partial class AddHarnessVersionToUserSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_sessions' AND COLUMN_NAME = 'harness_version');
SET @sql = IF(@col_exists = 0,
    'ALTER TABLE user_sessions ADD COLUMN harness_version VARCHAR(20) NULL',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_sessions' AND COLUMN_NAME = 'harness_version');
SET @sql = IF(@col_exists > 0,
    'ALTER TABLE user_sessions DROP COLUMN harness_version',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");
        }
    }
}
