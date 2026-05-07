using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.V2.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mcp_servers",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    endpoint_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    auth_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "oauth_entra")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    default_read = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    default_write = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_servers", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "mcp_user_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    server_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    access_token = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    refresh_token = table.Column<string>(type: "TEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    token_expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mcp_user_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_mcp_user_tokens_user",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_servers_name",
                table: "mcp_servers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mcp_user_tokens_user_id",
                table: "mcp_user_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mcp_user_tokens_user_server",
                table: "mcp_user_tokens",
                columns: new[] { "user_id", "server_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mcp_servers");

            migrationBuilder.DropTable(
                name: "mcp_user_tokens");
        }
    }
}
