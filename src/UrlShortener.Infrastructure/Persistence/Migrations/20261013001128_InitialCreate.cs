using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UrlShortener.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "click_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RefererHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    UserAgentFamily = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_click_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "short_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OriginalUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    IsCustomAlias = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ClickCount = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAccessedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_short_links", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_click_events_Code_OccurredAt",
                table: "click_events",
                columns: new[] { "Code", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_short_links_Code",
                table: "short_links",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_short_links_CreatedAt",
                table: "short_links",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_short_links_OriginalUrl",
                table: "short_links",
                column: "OriginalUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "click_events");

            migrationBuilder.DropTable(
                name: "short_links");
        }
    }
}
