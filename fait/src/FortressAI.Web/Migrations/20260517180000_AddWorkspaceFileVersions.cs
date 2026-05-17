using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceFileVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS workspace_file_versions (
    id CHAR(36) NOT NULL,
    file_id CHAR(36) NOT NULL,
    version_number INT NOT NULL DEFAULT 1,
    s3_key VARCHAR(1000) NOT NULL,
    size_bytes BIGINT NOT NULL DEFAULT 0,
    created_at DATETIME(6) NOT NULL,
    created_by VARCHAR(20) NOT NULL DEFAULT 'user',
    PRIMARY KEY (id),
    INDEX idx_wfv_file_id (file_id)
) CHARACTER SET utf8mb4;
");

            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'current_version');
SET @sql = IF(@col_exists = 0, 'ALTER TABLE user_workspace_uploads ADD COLUMN current_version INT NOT NULL DEFAULT 1', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'source');
SET @sql = IF(@col_exists = 0, 'ALTER TABLE user_workspace_uploads ADD COLUMN source VARCHAR(20) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS workspace_file_versions;");

            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'current_version');
SET @sql = IF(@col_exists > 0, 'ALTER TABLE user_workspace_uploads DROP COLUMN current_version', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'source');
SET @sql = IF(@col_exists > 0, 'ALTER TABLE user_workspace_uploads DROP COLUMN source', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");
        }
    }
}
