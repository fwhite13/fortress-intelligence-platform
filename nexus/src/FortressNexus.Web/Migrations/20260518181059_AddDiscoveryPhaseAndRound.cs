using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressNexus.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryPhaseAndRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "phase",
                table: "discovery_sessions",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<DateTime>(
                name: "phase1_completed_at",
                table: "discovery_sessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "phase1_terminated_by_user",
                table: "discovery_sessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "phase2_completed_at",
                table: "discovery_sessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "phase2_terminated_by_user",
                table: "discovery_sessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "phase",
                table: "discovery_questions",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "round",
                table: "discovery_questions",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "phase",
                table: "discovery_sessions");

            migrationBuilder.DropColumn(
                name: "phase1_completed_at",
                table: "discovery_sessions");

            migrationBuilder.DropColumn(
                name: "phase1_terminated_by_user",
                table: "discovery_sessions");

            migrationBuilder.DropColumn(
                name: "phase2_completed_at",
                table: "discovery_sessions");

            migrationBuilder.DropColumn(
                name: "phase2_terminated_by_user",
                table: "discovery_sessions");

            migrationBuilder.DropColumn(
                name: "phase",
                table: "discovery_questions");

            migrationBuilder.DropColumn(
                name: "round",
                table: "discovery_questions");
        }
    }
}
