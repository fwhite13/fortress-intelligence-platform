using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.V2.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultAgentPlugins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use INSERT IGNORE keyed on name — idempotent, safe to re-run
            migrationBuilder.Sql(@"
                INSERT IGNORE INTO agent_plugins
                    (id, name, description, skills_directory, allowed_mcp_servers, allowed_roles, is_active, created_by, created_at, updated_at)
                SELECT
                    UUID(), v.name, v.description, NULL, '[]', '[]', 1, 'system', UTC_TIMESTAMP(), UTC_TIMESTAMP()
                FROM (
                    SELECT 'Marketing Assistant' AS name,
                           'Specializes in marketing strategy, content creation, campaign planning, and brand messaging.' AS description
                    UNION ALL
                    SELECT 'Finance Assistant',
                           'Handles financial analysis, budget planning, forecasting, and financial reporting.'
                    UNION ALL
                    SELECT 'Legal Assistant',
                           'Assists with contract review, compliance questions, policy drafting, and legal research.'
                ) v
                WHERE NOT EXISTS (
                    SELECT 1 FROM agent_plugins ap WHERE ap.name = v.name
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM agent_plugins
                WHERE name IN ('Marketing Assistant', 'Finance Assistant', 'Legal Assistant')
                  AND created_by = 'system';
            ");
        }
    }
}
