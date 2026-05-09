using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.V2.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDocumentsAndProjectFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_instructions",
                table: "projects",
                type: "TEXT",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "enable_fortress_kb",
                table: "projects",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_personal_kb",
                table: "projects",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "model",
                table: "projects",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "claude-sonnet-4-6")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "project_id",
                table: "conversation_tasks",
                type: "char(36)",
                maxLength: 36,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "kb_teams",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    creator_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_teams", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_documents",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    filename = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content_type = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    s3_key = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ingestion_status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "none")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ingested_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_documents_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "kb_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    team_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tier = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tags = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_url = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_kb_entries_kb_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "kb_teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "kb_team_members",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    team_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    role = table.Column<int>(type: "int", nullable: false),
                    joined_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kb_team_members", x => x.id);
                    table.ForeignKey(
                        name: "FK_kb_team_members_kb_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "kb_teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tasks_project_id",
                table: "conversation_tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_kb_entries_team_id",
                table: "kb_entries",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_kb_entries_user_id",
                table: "kb_entries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_kb_team_members_team_id",
                table: "kb_team_members",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_kb_team_members_team_user",
                table: "kb_team_members",
                columns: new[] { "team_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kb_team_members_user_id",
                table: "kb_team_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_kb_teams_creator_id",
                table: "kb_teams",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_documents_project_id",
                table: "project_documents",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_tasks_projects_project_id",
                table: "conversation_tasks",
                column: "project_id",
                principalTable: "projects",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversation_tasks_projects_project_id",
                table: "conversation_tasks");

            migrationBuilder.DropTable(
                name: "kb_entries");

            migrationBuilder.DropTable(
                name: "kb_team_members");

            migrationBuilder.DropTable(
                name: "project_documents");

            migrationBuilder.DropTable(
                name: "kb_teams");

            migrationBuilder.DropIndex(
                name: "ix_conversation_tasks_project_id",
                table: "conversation_tasks");

            migrationBuilder.DropColumn(
                name: "custom_instructions",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "enable_fortress_kb",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "enable_personal_kb",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "model",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "conversation_tasks");
        }
    }
}
