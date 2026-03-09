using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationTeamKbs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversation_mcp_servers_conversations_ConversationId",
                table: "conversation_mcp_servers");

            migrationBuilder.DropForeignKey(
                name: "FK_conversation_mcp_servers_mcp_servers_ServerId",
                table: "conversation_mcp_servers");

            migrationBuilder.DropForeignKey(
                name: "FK_mcp_tool_call_log_mcp_servers_ServerId",
                table: "mcp_tool_call_log");

            migrationBuilder.DropForeignKey(
                name: "FK_mcp_tool_call_log_users_UserId",
                table: "mcp_tool_call_log");

            migrationBuilder.DropForeignKey(
                name: "FK_user_mcp_tokens_mcp_servers_ServerId",
                table: "user_mcp_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_user_mcp_tokens_users_UserId",
                table: "user_mcp_tokens");

            migrationBuilder.DropColumn(
                name: "EnableTeamKbId",
                table: "conversations");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "user_mcp_tokens",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "user_mcp_tokens",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TokenExpiresAt",
                table: "user_mcp_tokens",
                newName: "token_expires_at");

            migrationBuilder.RenameColumn(
                name: "ServerId",
                table: "user_mcp_tokens",
                newName: "server_id");

            migrationBuilder.RenameColumn(
                name: "RefreshToken",
                table: "user_mcp_tokens",
                newName: "refresh_token");

            migrationBuilder.RenameColumn(
                name: "ExternalUserId",
                table: "user_mcp_tokens",
                newName: "external_user_id");

            migrationBuilder.RenameColumn(
                name: "ExternalEmail",
                table: "user_mcp_tokens",
                newName: "external_email");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "user_mcp_tokens",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AccessToken",
                table: "user_mcp_tokens",
                newName: "access_token");

            migrationBuilder.RenameIndex(
                name: "IX_user_mcp_tokens_UserId_ServerId",
                table: "user_mcp_tokens",
                newName: "IX_user_mcp_tokens_user_id_server_id");

            migrationBuilder.RenameIndex(
                name: "IX_user_mcp_tokens_ServerId",
                table: "user_mcp_tokens",
                newName: "IX_user_mcp_tokens_server_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "mcp_tool_call_log",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ToolName",
                table: "mcp_tool_call_log",
                newName: "tool_name");

            migrationBuilder.RenameColumn(
                name: "ServerId",
                table: "mcp_tool_call_log",
                newName: "server_id");

            migrationBuilder.RenameColumn(
                name: "OutputJson",
                table: "mcp_tool_call_log",
                newName: "output_json");

            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "mcp_tool_call_log",
                newName: "message_id");

            migrationBuilder.RenameColumn(
                name: "LatencyMs",
                table: "mcp_tool_call_log",
                newName: "latency_ms");

            migrationBuilder.RenameColumn(
                name: "InputJson",
                table: "mcp_tool_call_log",
                newName: "input_json");

            migrationBuilder.RenameColumn(
                name: "ErrorMessage",
                table: "mcp_tool_call_log",
                newName: "error_message");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "mcp_tool_call_log",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "mcp_tool_call_log",
                newName: "conversation_id");

            migrationBuilder.RenameIndex(
                name: "IX_mcp_tool_call_log_UserId_CreatedAt",
                table: "mcp_tool_call_log",
                newName: "IX_mcp_tool_call_log_user_id_created_at");

            migrationBuilder.RenameIndex(
                name: "IX_mcp_tool_call_log_ServerId",
                table: "mcp_tool_call_log",
                newName: "IX_mcp_tool_call_log_server_id");

            migrationBuilder.RenameIndex(
                name: "IX_mcp_tool_call_log_ConversationId",
                table: "mcp_tool_call_log",
                newName: "IX_mcp_tool_call_log_conversation_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "mcp_servers",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TransportType",
                table: "mcp_servers",
                newName: "transport_type");

            migrationBuilder.RenameColumn(
                name: "SystemApiKey",
                table: "mcp_servers",
                newName: "system_api_key");

            migrationBuilder.RenameColumn(
                name: "RequiresUserAuth",
                table: "mcp_servers",
                newName: "requires_user_auth");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "mcp_servers",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "IconUrl",
                table: "mcp_servers",
                newName: "icon_url");

            migrationBuilder.RenameColumn(
                name: "EndpointUrl",
                table: "mcp_servers",
                newName: "endpoint_url");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "mcp_servers",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "AuthType",
                table: "mcp_servers",
                newName: "auth_type");

            migrationBuilder.RenameColumn(
                name: "ServerId",
                table: "conversation_mcp_servers",
                newName: "server_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "conversation_mcp_servers",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConversationId",
                table: "conversation_mcp_servers",
                newName: "conversation_id");

            migrationBuilder.RenameIndex(
                name: "IX_conversation_mcp_servers_ServerId",
                table: "conversation_mcp_servers",
                newName: "IX_conversation_mcp_servers_server_id");

            migrationBuilder.RenameIndex(
                name: "IX_conversation_mcp_servers_ConversationId_ServerId",
                table: "conversation_mcp_servers",
                newName: "IX_conversation_mcp_servers_conversation_id_server_id");

            migrationBuilder.AlterColumn<string>(
                name: "icon_url",
                table: "mcp_servers",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "oauth_client_secret",
                table: "mcp_servers",
                type: "TEXT",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "rate_limit_per_minute",
                table: "mcp_servers",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.CreateTable(
                name: "conversation_team_kbs",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    team_id = table.Column<int>(type: "int", nullable: false),
                    enabled_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_team_kbs", x => new { x.conversation_id, x.team_id });
                    table.ForeignKey(
                        name: "FK_conversation_team_kbs_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_team_kbs_kb_projects_team_id",
                        column: x => x.team_id,
                        principalTable: "kb_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_team_kbs_team_id",
                table: "conversation_team_kbs",
                column: "team_id");

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_mcp_servers_conversations_conversation_id",
                table: "conversation_mcp_servers",
                column: "conversation_id",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_mcp_servers_mcp_servers_server_id",
                table: "conversation_mcp_servers",
                column: "server_id",
                principalTable: "mcp_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_mcp_tool_call_log_mcp_servers_server_id",
                table: "mcp_tool_call_log",
                column: "server_id",
                principalTable: "mcp_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mcp_tool_call_log_users_user_id",
                table: "mcp_tool_call_log",
                column: "user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_mcp_tokens_mcp_servers_server_id",
                table: "user_mcp_tokens",
                column: "server_id",
                principalTable: "mcp_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_mcp_tokens_users_user_id",
                table: "user_mcp_tokens",
                column: "user_id",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversation_mcp_servers_conversations_conversation_id",
                table: "conversation_mcp_servers");

            migrationBuilder.DropForeignKey(
                name: "FK_conversation_mcp_servers_mcp_servers_server_id",
                table: "conversation_mcp_servers");

            migrationBuilder.DropForeignKey(
                name: "FK_mcp_tool_call_log_mcp_servers_server_id",
                table: "mcp_tool_call_log");

            migrationBuilder.DropForeignKey(
                name: "FK_mcp_tool_call_log_users_user_id",
                table: "mcp_tool_call_log");

            migrationBuilder.DropForeignKey(
                name: "FK_user_mcp_tokens_mcp_servers_server_id",
                table: "user_mcp_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_user_mcp_tokens_users_user_id",
                table: "user_mcp_tokens");

            migrationBuilder.DropTable(
                name: "conversation_team_kbs");

            migrationBuilder.DropColumn(
                name: "oauth_client_secret",
                table: "mcp_servers");

            migrationBuilder.DropColumn(
                name: "rate_limit_per_minute",
                table: "mcp_servers");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "user_mcp_tokens",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "user_mcp_tokens",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "token_expires_at",
                table: "user_mcp_tokens",
                newName: "TokenExpiresAt");

            migrationBuilder.RenameColumn(
                name: "server_id",
                table: "user_mcp_tokens",
                newName: "ServerId");

            migrationBuilder.RenameColumn(
                name: "refresh_token",
                table: "user_mcp_tokens",
                newName: "RefreshToken");

            migrationBuilder.RenameColumn(
                name: "external_user_id",
                table: "user_mcp_tokens",
                newName: "ExternalUserId");

            migrationBuilder.RenameColumn(
                name: "external_email",
                table: "user_mcp_tokens",
                newName: "ExternalEmail");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "user_mcp_tokens",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "access_token",
                table: "user_mcp_tokens",
                newName: "AccessToken");

            migrationBuilder.RenameIndex(
                name: "IX_user_mcp_tokens_user_id_server_id",
                table: "user_mcp_tokens",
                newName: "IX_user_mcp_tokens_UserId_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_user_mcp_tokens_server_id",
                table: "user_mcp_tokens",
                newName: "IX_user_mcp_tokens_ServerId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "mcp_tool_call_log",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "tool_name",
                table: "mcp_tool_call_log",
                newName: "ToolName");

            migrationBuilder.RenameColumn(
                name: "server_id",
                table: "mcp_tool_call_log",
                newName: "ServerId");

            migrationBuilder.RenameColumn(
                name: "output_json",
                table: "mcp_tool_call_log",
                newName: "OutputJson");

            migrationBuilder.RenameColumn(
                name: "message_id",
                table: "mcp_tool_call_log",
                newName: "MessageId");

            migrationBuilder.RenameColumn(
                name: "latency_ms",
                table: "mcp_tool_call_log",
                newName: "LatencyMs");

            migrationBuilder.RenameColumn(
                name: "input_json",
                table: "mcp_tool_call_log",
                newName: "InputJson");

            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "mcp_tool_call_log",
                newName: "ErrorMessage");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "mcp_tool_call_log",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "conversation_id",
                table: "mcp_tool_call_log",
                newName: "ConversationId");

            migrationBuilder.RenameIndex(
                name: "IX_mcp_tool_call_log_user_id_created_at",
                table: "mcp_tool_call_log",
                newName: "IX_mcp_tool_call_log_UserId_CreatedAt");

            migrationBuilder.RenameIndex(
                name: "IX_mcp_tool_call_log_server_id",
                table: "mcp_tool_call_log",
                newName: "IX_mcp_tool_call_log_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_mcp_tool_call_log_conversation_id",
                table: "mcp_tool_call_log",
                newName: "IX_mcp_tool_call_log_ConversationId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "mcp_servers",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "transport_type",
                table: "mcp_servers",
                newName: "TransportType");

            migrationBuilder.RenameColumn(
                name: "system_api_key",
                table: "mcp_servers",
                newName: "SystemApiKey");

            migrationBuilder.RenameColumn(
                name: "requires_user_auth",
                table: "mcp_servers",
                newName: "RequiresUserAuth");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "mcp_servers",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "icon_url",
                table: "mcp_servers",
                newName: "IconUrl");

            migrationBuilder.RenameColumn(
                name: "endpoint_url",
                table: "mcp_servers",
                newName: "EndpointUrl");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "mcp_servers",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "auth_type",
                table: "mcp_servers",
                newName: "AuthType");

            migrationBuilder.RenameColumn(
                name: "server_id",
                table: "conversation_mcp_servers",
                newName: "ServerId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "conversation_mcp_servers",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "conversation_id",
                table: "conversation_mcp_servers",
                newName: "ConversationId");

            migrationBuilder.RenameIndex(
                name: "IX_conversation_mcp_servers_server_id",
                table: "conversation_mcp_servers",
                newName: "IX_conversation_mcp_servers_ServerId");

            migrationBuilder.RenameIndex(
                name: "IX_conversation_mcp_servers_conversation_id_server_id",
                table: "conversation_mcp_servers",
                newName: "IX_conversation_mcp_servers_ConversationId_ServerId");

            migrationBuilder.AlterColumn<string>(
                name: "IconUrl",
                table: "mcp_servers",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "EnableTeamKbId",
                table: "conversations",
                type: "int",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_mcp_servers_conversations_ConversationId",
                table: "conversation_mcp_servers",
                column: "ConversationId",
                principalTable: "conversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_conversation_mcp_servers_mcp_servers_ServerId",
                table: "conversation_mcp_servers",
                column: "ServerId",
                principalTable: "mcp_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_mcp_tool_call_log_mcp_servers_ServerId",
                table: "mcp_tool_call_log",
                column: "ServerId",
                principalTable: "mcp_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_mcp_tool_call_log_users_UserId",
                table: "mcp_tool_call_log",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_mcp_tokens_mcp_servers_ServerId",
                table: "user_mcp_tokens",
                column: "ServerId",
                principalTable: "mcp_servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_mcp_tokens_users_UserId",
                table: "user_mcp_tokens",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
