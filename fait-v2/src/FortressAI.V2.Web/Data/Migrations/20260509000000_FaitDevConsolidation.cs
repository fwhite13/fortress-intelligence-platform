using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace FortressAI.V2.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class FaitDevConsolidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Bootstrap EF migrations tracking table ────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;
");

            // ── Add new columns to existing fait_dev tables ───────────────────
            // users: onboarding_completed_at, onboarding_step, updated_at, avatar_url
            migrationBuilder.Sql("ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `onboarding_completed_at` datetime(6) NULL;");
            migrationBuilder.Sql("ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `onboarding_step` int NULL;");
            migrationBuilder.Sql("ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `updated_at` datetime(6) NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6);");
            migrationBuilder.Sql("ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `avatar_url` varchar(1000) NULL;");

            // conversations: last_active_at, estimated_token_count
            migrationBuilder.Sql("ALTER TABLE `conversations` ADD COLUMN IF NOT EXISTS `last_active_at` datetime(6) NULL;");
            migrationBuilder.Sql("ALTER TABLE `conversations` ADD COLUMN IF NOT EXISTS `estimated_token_count` int NOT NULL DEFAULT 0;");

            // messages: compacted_at, is_compaction_summary, session_type, plugin_agent_id, token_count
            migrationBuilder.Sql("ALTER TABLE `messages` ADD COLUMN IF NOT EXISTS `compacted_at` datetime(6) NULL;");
            migrationBuilder.Sql("ALTER TABLE `messages` ADD COLUMN IF NOT EXISTS `is_compaction_summary` tinyint(1) NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE `messages` ADD COLUMN IF NOT EXISTS `session_type` varchar(10) NOT NULL DEFAULT 'main';");
            migrationBuilder.Sql("ALTER TABLE `messages` ADD COLUMN IF NOT EXISTS `plugin_agent_id` varchar(50) NULL;");
            migrationBuilder.Sql("ALTER TABLE `messages` ADD COLUMN IF NOT EXISTS `token_count` int NOT NULL DEFAULT 0;");

            // projects: v1_project_id
            migrationBuilder.Sql("ALTER TABLE `projects` ADD COLUMN IF NOT EXISTS `v1_project_id` int NULL;");

            // mcp_servers: default_read, default_write
            migrationBuilder.Sql("ALTER TABLE `mcp_servers` ADD COLUMN IF NOT EXISTS `default_read` tinyint(1) NOT NULL DEFAULT 1;");
            migrationBuilder.Sql("ALTER TABLE `mcp_servers` ADD COLUMN IF NOT EXISTS `default_write` tinyint(1) NOT NULL DEFAULT 0;");

            // user_mcp_tokens: server_name (new column for v2 connector service)
            migrationBuilder.Sql("ALTER TABLE `user_mcp_tokens` ADD COLUMN IF NOT EXISTS `server_name` varchar(100) NULL;");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS `ix_mcp_user_tokens_user_server`
    ON `user_mcp_tokens` (`user_id`, `server_name`);
");

            // ── Create new v2-only tables ─────────────────────────────────────

            migrationBuilder.CreateTable(
                name: "main_assistants",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    soul_blob_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    memory_blob_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    workspace_s3_prefix = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fargate_session_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fargate_task_arn = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_main_assistants", x => x.id);
                    table.ForeignKey(
                        name: "FK_main_assistants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "memory_topics",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    topic_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    topic_slug = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    blob_path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_topics", x => x.id);
                    table.ForeignKey(
                        name: "FK_memory_topics_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    last_active_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ip_address = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_agent = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    task_arn = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    private_ip = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fargate_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fargate_session_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    task_definition_revision = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "design_agent_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    conversation_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stitch_project_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    design_dna = table.Column<string>(type: "TEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_design_agent_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_design_agent_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "design_agent_artifacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    session_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    artifact_name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    s3_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    stitch_screen_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_fallback = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_design_agent_artifacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_design_agent_artifacts_design_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "design_agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "pushed_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    content = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    external_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_read = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    meeting_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pushed_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_pushed_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "feedback_submissions",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    page_url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    screenshot_s3_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "pending")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ado_wi_id = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    triage_result = table.Column<string>(type: "TEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    triaged_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedback_submissions", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "artifact_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    file_name = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    s3_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    task_description = table.Column<string>(type: "TEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_artifact_records", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "agent_plugins",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    description = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    skills_directory = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    allowed_mcp_servers = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    allowed_roles = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    allow_kb_write = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_by = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_plugins", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scheduled_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prompt = table.Column<string>(type: "TEXT", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    schedule_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    cron_expression = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    next_run_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_run_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_run_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    failure_count = table.Column<int>(type: "int", nullable: false),
                    alert_on_completion = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    alert_on_failure = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    task_mode = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_tasks", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scheduled_task_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    task_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    started_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    error_message = table.Column<string>(type: "TEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    artifact_s3_key = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sandbox_id = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    output_text = table.Column<string>(type: "LONGTEXT", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_task_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduled_task_runs_scheduled_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "scheduled_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "scheduled_task_approvals",
                columns: table => new
                {
                    id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    scheduled_task_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    intervention_id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    action_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    action_summary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_task_approvals", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "conversation_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "char(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    user_id = table.Column<string>(type: "char(36)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    title = table.Column<string>(type: "varchar(500)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    project_id = table.Column<string>(type: "char(36)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_tasks_user",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // ── Indexes for new tables ────────────────────────────────────────

            migrationBuilder.CreateIndex(
                name: "ix_main_assistants_user_id",
                table: "main_assistants",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memory_topics_user_id",
                table: "memory_topics",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_memory_topics_user_slug",
                table: "memory_topics",
                columns: new[] { "user_id", "topic_slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_user_id",
                table: "user_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_started_at",
                table: "user_sessions",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_design_agent_sessions_user_id",
                table: "design_agent_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_design_agent_artifacts_session_id",
                table: "design_agent_artifacts",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_pushed_messages_user_id",
                table: "pushed_messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pushed_messages_created_at",
                table: "pushed_messages",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_submissions_user_id",
                table: "feedback_submissions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_submissions_status",
                table: "feedback_submissions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_artifact_records_user_id",
                table: "artifact_records",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_artifact_records_created_at",
                table: "artifact_records",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_agent_plugins_is_active",
                table: "agent_plugins",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_agent_plugins_name",
                table: "agent_plugins",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_user_id",
                table: "scheduled_tasks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_tasks_next_run_at",
                table: "scheduled_tasks",
                column: "next_run_at");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_task_runs_task_id",
                table: "scheduled_task_runs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_task_approvals_task_id",
                table: "scheduled_task_approvals",
                column: "scheduled_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_task_approvals_status",
                table: "scheduled_task_approvals",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tasks_user_id",
                table: "conversation_tasks",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tasks_updated_at",
                table: "conversation_tasks",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tasks_project_id",
                table: "conversation_tasks",
                column: "project_id");

            // ── Indexes for existing tables (new ones only) ───────────────────
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_conversations_user_id` ON `conversations` (`UserId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_conversations_last_active_at` ON `conversations` (`last_active_at`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_messages_conversation_id` ON `messages` (`ConversationId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_messages_created_at` ON `messages` (`CreatedAt`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_projects_user_id` ON `projects` (`UserId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_kb_entries_user_id` ON `kb_entries` (`UserId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_kb_entries_team_id` ON `kb_entries` (`TeamId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_kb_teams_creator_id` ON `kb_teams` (`CreatorId`);
");
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS `ix_kb_team_members_team_user` ON `kb_team_members` (`TeamId`, `UserId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_kb_team_members_team_id` ON `kb_team_members` (`TeamId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_kb_team_members_user_id` ON `kb_team_members` (`UserId`);
");
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS `ix_project_documents_project_id` ON `project_documents` (`ProjectId`);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Surgical migration — Down() intentionally left empty.
            // Rollback is not supported for fait_dev consolidation.
        }
    }
}
