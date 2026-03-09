using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "agent",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    max_tokens = table.Column<int>(type: "integer", nullable: false),
                    context_budget = table.Column<int>(type: "integer", nullable: false),
                    thinking_effort = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    tool_notifications = table.Column<bool>(type: "boolean", nullable: false),
                    compaction_threshold = table.Column<int>(type: "integer", nullable: false),
                    keep_recent_tokens = table.Column<int>(type: "integer", nullable: false),
                    tool_prune_threshold = table.Column<int>(type: "integer", nullable: false),
                    tool_keep_threshold = table.Column<int>(type: "integer", nullable: false),
                    tool_summarization_policy = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent", x => x.id);
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
                    agent_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_section", x => x.id);
                    table.ForeignKey(
                        name: "FK_prompt_section_agent_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agent",
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
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_session_id = table.Column<long>(type: "bigint", nullable: true),
                    agent_id = table.Column<long>(type: "bigint", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    require_mention = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_agent_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "chat_allowed_sender",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    sender_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_allowed_sender", x => x.id);
                    table.ForeignKey(
                        name: "FK_chat_allowed_sender_chat_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chat",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_session_id = table.Column<long>(type: "bigint", nullable: true),
                    tools_pruned_through_id = table.Column<long>(type: "bigint", nullable: true),
                    is_compacting = table.Column<bool>(type: "boolean", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    summary_embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    total_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_cache_read_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_cache_creation_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_thinking_tokens = table.Column<long>(type: "bigint", nullable: false),
                    context_tokens = table.Column<long>(type: "bigint", nullable: false),
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
                    media_data = table.Column<byte[]>(type: "bytea", nullable: true),
                    media_mime_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    reply_to_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    sk_content = table.Column<string>(type: "jsonb", nullable: true),
                    role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    cache_read_tokens = table.Column<int>(type: "integer", nullable: true),
                    cache_creation_tokens = table.Column<int>(type: "integer", nullable: true),
                    thinking_tokens = table.Column<int>(type: "integer", nullable: true),
                    queued = table.Column<bool>(type: "boolean", nullable: false),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
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
                    table.ForeignKey(
                        name: "FK_message_session_session_id",
                        column: x => x.session_id,
                        principalTable: "session",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_name",
                table: "agent",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_agent_id",
                table: "chat",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_current_session_id",
                table: "chat",
                column: "current_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_external_id",
                table: "chat",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_allowed_sender_chat_id",
                table: "chat_allowed_sender",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_chat_allowed_sender_chat_id_sender_id",
                table: "chat_allowed_sender",
                columns: new[] { "chat_id", "sender_id" },
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
                name: "IX_message_session_id_timestamp",
                table: "message",
                columns: new[] { "session_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_prompt_section_agent_id_name",
                table: "prompt_section",
                columns: new[] { "agent_id", "name" },
                unique: true);

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
                name: "FK_chat_agent_agent_id",
                table: "chat");

            migrationBuilder.DropForeignKey(
                name: "FK_chat_session_current_session_id",
                table: "chat");

            migrationBuilder.DropTable(
                name: "chat_allowed_sender");

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
                name: "agent");

            migrationBuilder.DropTable(
                name: "session");

            migrationBuilder.DropTable(
                name: "chat");
        }
    }
}
