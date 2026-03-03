using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class remove_token_count_add_role : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_count",
                table: "message");

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "message",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true);

            // Backfill role for existing messages
            migrationBuilder.Sql(
                "UPDATE message SET role = CASE WHEN is_from_me THEN 'assistant' ELSE 'user' END WHERE role IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "role",
                table: "message");

            migrationBuilder.AddColumn<int>(
                name: "token_count",
                table: "message",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
