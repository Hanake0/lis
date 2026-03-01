using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_prompt_sections_and_memories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    is_group = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contact",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_section",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_section", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "message",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    sender_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    sender_name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    is_from_me = table.Column<bool>(type: "boolean", nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    media_type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    media_caption = table.Column<string>(type: "text", nullable: true),
                    reply_to_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message", x => x.id);
                    table.ForeignKey(
                        name: "FK_message_chat_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chat",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_identifier",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    contact_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_identifier", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_identifier_contact_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contact",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memory",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    content = table.Column<string>(type: "text", nullable: false),
                    contact_id = table.Column<long>(type: "bigint", nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memory", x => x.id);
                    table.ForeignKey(
                        name: "FK_memory_contact_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contact",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_external_id",
                table: "chat",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_identifier_channel_external_id",
                table: "contact_identifier",
                columns: new[] { "channel", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_contact_identifier_contact_id",
                table: "contact_identifier",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_memory_contact_id",
                table: "memory",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_memory_embedding",
                table: "memory",
                column: "embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_message_chat_id_timestamp",
                table: "message",
                columns: new[] { "chat_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_message_external_id",
                table: "message",
                column: "external_id",
                unique: true,
                filter: "\"external_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_prompt_section_name",
                table: "prompt_section",
                column: "name",
                unique: true);

            // Seed default prompt sections
            DateTimeOffset now = DateTimeOffset.UtcNow;

            migrationBuilder.InsertData(
                table: "prompt_section",
                columns: ["name", "content", "sort_order", "is_enabled", "created_at", "updated_at"],
                values: new object[] {
                    "soul",
                    "You are Lis, a personal AI assistant.\n\nYou speak in pt-BR.\n\nBe concise. Messages should be brief and natural.\nUse plain text only, no markdown. Use line breaks for clarity.\nIf unsure, ask rather than guess.",
                    1, true, now, now,
                });

            migrationBuilder.InsertData(
                table: "prompt_section",
                columns: ["name", "content", "sort_order", "is_enabled", "created_at", "updated_at"],
                values: new object[] {
                    "user",
                    "Owner: Hanake\nLanguage: pt-BR",
                    2, true, now, now,
                });

            migrationBuilder.InsertData(
                table: "prompt_section",
                columns: ["name", "content", "sort_order", "is_enabled", "created_at", "updated_at"],
                values: new object[] {
                    "instructions",
                    "Current date/time: {{datetime}}",
                    3, true, now, now,
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_identifier");

            migrationBuilder.DropTable(
                name: "memory");

            migrationBuilder.DropTable(
                name: "message");

            migrationBuilder.DropTable(
                name: "prompt_section");

            migrationBuilder.DropTable(
                name: "contact");

            migrationBuilder.DropTable(
                name: "chat");
        }
    }
}
