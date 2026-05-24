using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_git_identity_and_settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "git_identity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    user_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    user_email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    auth_type = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true),
                    ssh_key_path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    host = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    color = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    icon = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_git_identity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "setting",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_setting", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_git_identity_external_id",
                table: "git_identity",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_setting_key",
                table: "setting",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "git_identity");

            migrationBuilder.DropTable(
                name: "setting");
        }
    }
}
