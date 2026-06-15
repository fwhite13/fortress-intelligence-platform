using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class WidenHarnessVersion_ADO5170 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                SET @sql = IF(
                    EXISTS(SELECT 1 FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_sessions' AND COLUMN_NAME = 'harness_version'),
                    'ALTER TABLE user_sessions MODIFY COLUMN harness_version VARCHAR(100) NULL',
                    'SELECT 1'
                );
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                SET @sql = IF(
                    EXISTS(SELECT 1 FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_sessions' AND COLUMN_NAME = 'harness_version'),
                    'ALTER TABLE user_sessions MODIFY COLUMN harness_version VARCHAR(20) NULL',
                    'SELECT 1'
                );
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;");
        }
    }
}
