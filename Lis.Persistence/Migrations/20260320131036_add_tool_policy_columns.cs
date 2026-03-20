using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_tool_policy_columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "exec_security",
                table: "agent",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "exec_timeout_seconds",
                table: "agent",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "tool_profile",
                table: "agent",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tools_allow",
                table: "agent",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tools_deny",
                table: "agent",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workspace_path",
                table: "agent",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exec_security",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "exec_timeout_seconds",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "tool_profile",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "tools_allow",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "tools_deny",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "workspace_path",
                table: "agent");
        }
    }
}
