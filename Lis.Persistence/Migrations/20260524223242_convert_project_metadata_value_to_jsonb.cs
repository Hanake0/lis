using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class convert_project_metadata_value_to_jsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain AlterColumn fails for non-JSON text rows on Postgres because the implicit
            // cast doesn't exist. Use raw SQL with USING value::jsonb so any row whose value is
            // valid JSON migrates cleanly. Rows with invalid JSON would surface as an error
            // here — intentionally — so we don't silently lose data.
            migrationBuilder.Sql(@"ALTER TABLE project_metadata
                ALTER COLUMN value TYPE jsonb
                USING value::jsonb;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE project_metadata
                ALTER COLUMN value TYPE text
                USING value::text;");
        }
    }
}
