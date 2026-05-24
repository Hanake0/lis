using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class expand_agent_for_oc_parity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "agent",
                type: "varchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "disabled",
                table: "agent",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "agent",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "permission_json",
                table: "agent",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt",
                table: "agent",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                table: "agent",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "temperature",
                table: "agent",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "top_p",
                table: "agent",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "disabled",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "permission_json",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "prompt",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "scope",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "temperature",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "top_p",
                table: "agent");
        }
    }
}
