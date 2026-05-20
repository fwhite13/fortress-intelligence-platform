using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceFolderParentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid?>(
                name: "parent_id",
                table: "workspace_folders",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddForeignKey(
                name: "FK_workspace_folders_workspace_folders_parent_id",
                table: "workspace_folders",
                column: "parent_id",
                principalTable: "workspace_folders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workspace_folders_workspace_folders_parent_id",
                table: "workspace_folders");

            migrationBuilder.DropColumn(
                name: "parent_id",
                table: "workspace_folders");
        }
    }
}
