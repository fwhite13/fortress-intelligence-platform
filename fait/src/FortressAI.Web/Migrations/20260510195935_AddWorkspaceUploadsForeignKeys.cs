using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceUploadsForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_user_workspace_folders_user_workspace_folders_parent_id",
                table: "user_workspace_folders",
                column: "parent_id",
                principalTable: "user_workspace_folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_workspace_uploads_user_workspace_folders_folder_id",
                table: "user_workspace_uploads",
                column: "folder_id",
                principalTable: "user_workspace_folders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_workspace_folders_user_workspace_folders_parent_id",
                table: "user_workspace_folders");

            migrationBuilder.DropForeignKey(
                name: "FK_user_workspace_uploads_user_workspace_folders_folder_id",
                table: "user_workspace_uploads");
        }
    }
}
