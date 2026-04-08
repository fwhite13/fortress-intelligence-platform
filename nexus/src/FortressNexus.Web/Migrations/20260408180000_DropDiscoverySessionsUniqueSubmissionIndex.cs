using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class DropDiscoverySessionsUniqueSubmissionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discovery_sessions_submissions_submission_id",
                table: "discovery_sessions");

            migrationBuilder.DropIndex(
                name: "IX_discovery_sessions_submission_id",
                table: "discovery_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_sessions_submission_id",
                table: "discovery_sessions",
                column: "submission_id",
                unique: false);

            migrationBuilder.AddForeignKey(
                name: "FK_discovery_sessions_submissions_submission_id",
                table: "discovery_sessions",
                column: "submission_id",
                principalTable: "submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_discovery_sessions_submissions_submission_id",
                table: "discovery_sessions");

            migrationBuilder.DropIndex(
                name: "IX_discovery_sessions_submission_id",
                table: "discovery_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_sessions_submission_id",
                table: "discovery_sessions",
                column: "submission_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_discovery_sessions_submissions_submission_id",
                table: "discovery_sessions",
                column: "submission_id",
                principalTable: "submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
