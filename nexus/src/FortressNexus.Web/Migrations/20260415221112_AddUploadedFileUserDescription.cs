using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedFileUserDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_description",
                table: "uploaded_files",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "user_description",
                table: "uploaded_files");
        }
    }
}
