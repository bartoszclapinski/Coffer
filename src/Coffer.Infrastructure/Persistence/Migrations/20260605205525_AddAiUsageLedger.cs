using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", nullable: false),
                    InputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    EstimatedCostPln = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageEntries_At",
                table: "AiUsageEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageEntries_Purpose",
                table: "AiUsageEntries",
                column: "Purpose");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageEntries");

            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
