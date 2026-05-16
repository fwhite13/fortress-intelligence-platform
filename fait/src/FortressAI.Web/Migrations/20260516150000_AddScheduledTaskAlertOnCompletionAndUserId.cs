using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledTaskAlertOnCompletionAndUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Surgical additive migration: adds AlertOnCompletion and UserId columns
            // to the scheduled_tasks table. These are present in the EF model but
            // absent from the live DB (the original CreateTable migration was recorded
            // as applied before these columns existed on the live table).
            //
            // Uses raw SQL with IF NOT EXISTS for idempotency.
            // UserId is added as nullable to allow safe apply on live data;
            // Rhodey will backfill/add FK constraint during deploy as needed.
            migrationBuilder.Sql("""
                ALTER TABLE `scheduled_tasks`
                    ADD COLUMN IF NOT EXISTS `AlertOnCompletion` tinyint(1) NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS `UserId` char(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL DEFAULT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `scheduled_tasks`
                    DROP COLUMN IF EXISTS `UserId`,
                    DROP COLUMN IF EXISTS `AlertOnCompletion`;
                """);
        }
    }
}
