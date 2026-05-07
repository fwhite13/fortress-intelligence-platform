using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.V2.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedInitialAgentPlugins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "agent_plugins",
                columns: new[] { "id", "name", "description", "skills_directory", "allowed_mcp_servers", "allowed_roles", "is_active", "created_by", "created_at", "updated_at" },
                values: new object[,]
                {
                    {
                        "00000000-0000-0000-0000-000000000001",
                        "Marketing",
                        "Brand positioning, content strategy, campaign planning, and marketing materials.",
                        "wwwroot/claude/agents/marketing.md",
                        "[]",
                        "[]",
                        true,
                        null,
                        new DateTime(2026, 5, 7, 0, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 5, 7, 0, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        "00000000-0000-0000-0000-000000000002",
                        "Finance",
                        "Financial modeling, analysis, reporting, and budget planning.",
                        "wwwroot/claude/agents/finance.md",
                        "[]",
                        "[]",
                        true,
                        null,
                        new DateTime(2026, 5, 7, 0, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 5, 7, 0, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        "00000000-0000-0000-0000-000000000003",
                        "Legal",
                        "Contract review, compliance documentation, and legal research support.",
                        "wwwroot/claude/agents/legal.md",
                        "[]",
                        "[]",
                        true,
                        null,
                        new DateTime(2026, 5, 7, 0, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 5, 7, 0, 0, 0, 0, DateTimeKind.Utc)
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "agent_plugins",
                keyColumn: "id",
                keyValue: "00000000-0000-0000-0000-000000000001");

            migrationBuilder.DeleteData(
                table: "agent_plugins",
                keyColumn: "id",
                keyValue: "00000000-0000-0000-0000-000000000002");

            migrationBuilder.DeleteData(
                table: "agent_plugins",
                keyColumn: "id",
                keyValue: "00000000-0000-0000-0000-000000000003");
        }
    }
}
