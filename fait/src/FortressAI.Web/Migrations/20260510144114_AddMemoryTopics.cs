using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FortressAI.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddMemoryTopics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "memory_topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "CHAR(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "CHAR(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "DATETIME(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "DATETIME(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory_topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_memory_topics_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_memory_topics_UserId_Slug",
                table: "memory_topics",
                columns: new[] { "UserId", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "memory_topics");
        }
    }
}
