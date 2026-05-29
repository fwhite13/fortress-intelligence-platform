using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceUploadS3KeyUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE user_workspace_uploads ADD CONSTRAINT uq_user_s3_key UNIQUE (user_id, s3_key(500))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE user_workspace_uploads DROP INDEX uq_user_s3_key");
        }
    }
}
