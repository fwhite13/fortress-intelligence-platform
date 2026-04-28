using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDecompositionUpgradeFields_20260427 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_owner",
                table: "work_item_records",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "is_external_dependency",
                table: "work_item_records",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "predecessor_titles",
                table: "work_item_records",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "tested_by_titles",
                table: "work_item_records",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "wi_template",
                table: "work_item_records",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "external_dependency_count",
                table: "artifact_sets",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_owner",
                table: "work_item_records");

            migrationBuilder.DropColumn(
                name: "is_external_dependency",
                table: "work_item_records");

            migrationBuilder.DropColumn(
                name: "predecessor_titles",
                table: "work_item_records");

            migrationBuilder.DropColumn(
                name: "tested_by_titles",
                table: "work_item_records");

            migrationBuilder.DropColumn(
                name: "wi_template",
                table: "work_item_records");

            migrationBuilder.DropColumn(
                name: "external_dependency_count",
                table: "artifact_sets");
        }
    }
}
