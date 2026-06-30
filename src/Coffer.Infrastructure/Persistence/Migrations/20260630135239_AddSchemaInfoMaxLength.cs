using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Coffer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchemaInfoMaxLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op at the database level: SQLite stores all text as unbounded TEXT, so the
            // MaxLength(128)/MaxLength(64) constraints added to SchemaInfoEntry need no DDL.
            // This migration exists to keep the model snapshot in sync with the entity so
            // future migrations don't re-detect the change.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
