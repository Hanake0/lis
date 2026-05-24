using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_session_folders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "session_folder",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    scope_key = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    parent_external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    sort_index = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_folder", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "session_folder_item",
                columns: table => new
                {
                    folder_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    sort_index = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_folder_item", x => new { x.folder_id, x.chat_external_id });
                    table.ForeignKey(
                        name: "FK_session_folder_item_session_folder_folder_id",
                        column: x => x.folder_id,
                        principalTable: "session_folder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_folder_external_id",
                table: "session_folder",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_session_folder_scope_key_parent_external_id_sort_index",
                table: "session_folder",
                columns: new[] { "scope_key", "parent_external_id", "sort_index" });

            migrationBuilder.CreateIndex(
                name: "IX_session_folder_item_chat_external_id",
                table: "session_folder_item",
                column: "chat_external_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_folder_item");

            migrationBuilder.DropTable(
                name: "session_folder");
        }
    }
}
