using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceFileVersionsUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // I5: Add UNIQUE constraint on (file_id, version_number) to prevent duplicate version rows
            migrationBuilder.Sql(@"
SET @idx_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'workspace_file_versions'
    AND INDEX_NAME = 'IX_workspace_file_versions_file_id_version_number');
SET @sql = IF(@idx_exists = 0,
    'ALTER TABLE workspace_file_versions ADD UNIQUE INDEX IX_workspace_file_versions_file_id_version_number (file_id, version_number)',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
SET @idx_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'workspace_file_versions'
    AND INDEX_NAME = 'IX_workspace_file_versions_file_id_version_number');
SET @sql = IF(@idx_exists > 0,
    'ALTER TABLE workspace_file_versions DROP INDEX IX_workspace_file_versions_file_id_version_number',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");
        }
    }
}
