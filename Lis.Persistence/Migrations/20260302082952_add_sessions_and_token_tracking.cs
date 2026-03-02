using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_sessions_and_token_tracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cache_creation_tokens",
                table: "message",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cache_read_tokens",
                table: "message",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens",
                table: "message",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "output_tokens",
                table: "message",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "thinking_tokens",
                table: "message",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "current_session_id",
                table: "chat",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "session",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_session_id = table.Column<long>(type: "bigint", nullable: true),
                    start_message_id = table.Column<long>(type: "bigint", nullable: true),
                    end_message_id = table.Column<long>(type: "bigint", nullable: true),
                    tools_pruned_through_id = table.Column<long>(type: "bigint", nullable: true),
                    is_compacting = table.Column<bool>(type: "boolean", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    summary_embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    total_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_cache_read_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_cache_creation_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_thinking_tokens = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session", x => x.id);
                    table.ForeignKey(
                        name: "FK_session_chat_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chat",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_session_session_parent_session_id",
                        column: x => x.parent_session_id,
                        principalTable: "session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_current_session_id",
                table: "chat",
                column: "current_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_session_chat_id",
                table: "session",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_session_parent_session_id",
                table: "session",
                column: "parent_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_session_summary_embedding",
                table: "session",
                column: "summary_embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.AddForeignKey(
                name: "FK_chat_session_current_session_id",
                table: "chat",
                column: "current_session_id",
                principalTable: "session",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_chat_session_current_session_id",
                table: "chat");

            migrationBuilder.DropTable(
                name: "session");

            migrationBuilder.DropIndex(
                name: "IX_chat_current_session_id",
                table: "chat");

            migrationBuilder.DropColumn(
                name: "cache_creation_tokens",
                table: "message");

            migrationBuilder.DropColumn(
                name: "cache_read_tokens",
                table: "message");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "message");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "message");

            migrationBuilder.DropColumn(
                name: "thinking_tokens",
                table: "message");

            migrationBuilder.DropColumn(
                name: "current_session_id",
                table: "chat");
        }
    }
}
