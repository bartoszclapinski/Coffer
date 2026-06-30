using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringFlows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    MatchMerchant = table.Column<string>(type: "TEXT", nullable: true),
                    MatchCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntervalMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    AnchorDayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    AnchorMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    TypicalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AmountStdDev = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AccrualOffsetMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringFlows", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringFlows_IsActive",
                table: "RecurringFlows",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringFlows");
        }
    }
}
