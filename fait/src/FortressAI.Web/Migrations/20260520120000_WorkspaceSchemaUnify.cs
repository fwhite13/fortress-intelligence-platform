using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceSchemaUnify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add conversation_id to user_workspace_uploads (INFORMATION_SCHEMA conditional — Aurora MySQL 8.0.40 does not support ADD COLUMN IF NOT EXISTS)
            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'conversation_id');
SET @sql = IF(@col_exists = 0, 'ALTER TABLE user_workspace_uploads ADD COLUMN conversation_id CHAR(36) NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            // 2. Add turn_index to user_workspace_uploads
            migrationBuilder.Sql(@"
SET @col_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads' AND COLUMN_NAME = 'turn_index');
SET @sql = IF(@col_exists = 0, 'ALTER TABLE user_workspace_uploads ADD COLUMN turn_index INT NULL', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            // 3. Drop old FK from user_workspace_uploads → user_workspace_folders (if it exists)
            migrationBuilder.Sql(@"
SET @fk_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads'
    AND CONSTRAINT_TYPE = 'FOREIGN KEY' AND CONSTRAINT_NAME = 'FK_user_workspace_uploads_user_workspace_folders_folder_id');
SET @sql = IF(@fk_exists > 0, 'ALTER TABLE user_workspace_uploads DROP FOREIGN KEY FK_user_workspace_uploads_user_workspace_folders_folder_id', 'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            // 4. Add new FK from user_workspace_uploads → workspace_folders (if not already exists)
            migrationBuilder.Sql(@"
SET @fk_exists = (SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'user_workspace_uploads'
    AND CONSTRAINT_TYPE = 'FOREIGN KEY' AND CONSTRAINT_NAME = 'FK_user_workspace_uploads_workspace_folders_folder_id');
SET @sql = IF(@fk_exists = 0,
    'ALTER TABLE user_workspace_uploads ADD CONSTRAINT FK_user_workspace_uploads_workspace_folders_folder_id FOREIGN KEY (folder_id) REFERENCES workspace_folders(id) ON DELETE SET NULL',
    'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
");

            // 5. Drop user_workspace_files (artifacts table — superseded by user_workspace_uploads with source='assistant')
            migrationBuilder.Sql("DROP TABLE IF EXISTS user_workspace_files;");

            // 6. Drop user_workspace_folders (old hierarchical folder table — superseded by workspace_folders)
            migrationBuilder.Sql("DROP TABLE IF EXISTS user_workspace_folders;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: Down() does not recreate the dropped tables — this migration is intentionally one-way.
            // To roll back, restore from DB backup.
            migrationBuilder.Sql("-- Down migration not supported for WorkspaceSchemaUnify — restore from backup");
        }
    }
}
