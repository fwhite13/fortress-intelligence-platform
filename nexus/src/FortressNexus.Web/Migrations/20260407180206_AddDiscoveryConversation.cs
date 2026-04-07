using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryConversation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "discovery_status",
                table: "submissions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "discovery_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    submission_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kb_query_used = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    kb_passages_retrieved = table.Column<int>(type: "int", nullable: true),
                    question_count = table.Column<int>(type: "int", nullable: true),
                    skipped_by_user = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    generated_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    answered_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_discovery_sessions_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "discovery_questions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    discovery_session_id = table.Column<string>(type: "varchar(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    question_text = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_blocking = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    rationale = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_questions", x => x.id);
                    table.ForeignKey(
                        name: "FK_discovery_questions_discovery_sessions_discovery_session_id",
                        column: x => x.discovery_session_id,
                        principalTable: "discovery_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "discovery_answers",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    discovery_question_id = table.Column<string>(type: "varchar(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    answer_text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    answered_by = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    answered_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_answers", x => x.id);
                    table.ForeignKey(
                        name: "FK_discovery_answers_discovery_questions_discovery_question_id",
                        column: x => x.discovery_question_id,
                        principalTable: "discovery_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_answers_discovery_question_id",
                table: "discovery_answers",
                column: "discovery_question_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovery_questions_discovery_session_id",
                table: "discovery_questions",
                column: "discovery_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_sessions_submission_id",
                table: "discovery_sessions",
                column: "submission_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_answers");

            migrationBuilder.DropTable(
                name: "discovery_questions");

            migrationBuilder.DropTable(
                name: "discovery_sessions");

            migrationBuilder.DropColumn(
                name: "discovery_status",
                table: "submissions");
        }
    }
}
