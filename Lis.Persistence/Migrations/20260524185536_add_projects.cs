using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_projects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pinned_at",
                table: "chat",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "project_id",
                table: "chat",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "project",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    label = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    icon = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    icon_image_mime = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    icon_image_blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    icon_image_source = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true),
                    icon_image_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    icon_background = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    color = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sidebar_collapsed = table.Column<bool>(type: "boolean", nullable: false),
                    sort_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_pinned_at",
                table: "chat",
                column: "pinned_at");

            migrationBuilder.CreateIndex(
                name: "IX_chat_project_id",
                table: "chat",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_external_id",
                table: "project",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_sort_index",
                table: "project",
                column: "sort_index");

            migrationBuilder.AddForeignKey(
                name: "FK_chat_project_project_id",
                table: "chat",
                column: "project_id",
                principalTable: "project",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chat_project_project_id",
                table: "chat");

            migrationBuilder.DropTable(
                name: "project");

            migrationBuilder.DropIndex(
                name: "IX_chat_pinned_at",
                table: "chat");

            migrationBuilder.DropIndex(
                name: "IX_chat_project_id",
                table: "chat");

            migrationBuilder.DropColumn(
                name: "pinned_at",
                table: "chat");

            migrationBuilder.DropColumn(
                name: "project_id",
                table: "chat");
        }
    }
}
