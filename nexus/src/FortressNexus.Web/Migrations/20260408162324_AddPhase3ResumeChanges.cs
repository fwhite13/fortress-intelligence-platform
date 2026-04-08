using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3ResumeChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discovery_questions_discovery_sessions_discovery_session_id",
                table: "discovery_questions");

            migrationBuilder.DropForeignKey(
                name: "FK_discovery_answers_discovery_questions_discovery_question_id",
                table: "discovery_answers");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                table: "discovery_sessions",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(36)")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<Guid>(
                name: "discovery_session_id",
                table: "discovery_questions",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(36)")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                table: "discovery_questions",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(36)")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<Guid>(
                name: "discovery_question_id",
                table: "discovery_answers",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(36)")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                table: "discovery_answers",
                type: "char(36)",
                nullable: false,
                collation: "ascii_general_ci",
                oldClrType: typeof(string),
                oldType: "varchar(36)")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddForeignKey(
                name: "FK_discovery_questions_discovery_sessions_discovery_session_id",
                table: "discovery_questions",
                column: "discovery_session_id",
                principalTable: "discovery_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_discovery_answers_discovery_questions_discovery_question_id",
                table: "discovery_answers",
                column: "discovery_question_id",
                principalTable: "discovery_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discovery_questions_discovery_sessions_discovery_session_id",
                table: "discovery_questions");

            migrationBuilder.DropForeignKey(
                name: "FK_discovery_answers_discovery_questions_discovery_question_id",
                table: "discovery_answers");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "discovery_sessions",
                type: "varchar(36)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "discovery_session_id",
                table: "discovery_questions",
                type: "varchar(36)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "discovery_questions",
                type: "varchar(36)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "discovery_question_id",
                table: "discovery_answers",
                type: "varchar(36)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "discovery_answers",
                type: "varchar(36)",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddForeignKey(
                name: "FK_discovery_questions_discovery_sessions_discovery_session_id",
                table: "discovery_questions",
                column: "discovery_session_id",
                principalTable: "discovery_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_discovery_answers_discovery_questions_discovery_question_id",
                table: "discovery_answers",
                column: "discovery_question_id",
                principalTable: "discovery_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
