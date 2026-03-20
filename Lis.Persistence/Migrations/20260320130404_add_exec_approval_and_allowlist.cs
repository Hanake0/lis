using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_exec_approval_and_allowlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exec_allowlist",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    agent_id = table.Column<long>(type: "bigint", nullable: true),
                    pattern = table.Column<string>(type: "text", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_command = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exec_allowlist", x => x.id);
                    table.ForeignKey(
                        name: "FK_exec_allowlist_agent_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "exec_approval",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    approval_id = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    agent_id = table.Column<long>(type: "bigint", nullable: true),
                    command = table.Column<string>(type: "text", nullable: false),
                    cwd = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    decision = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    resolved_by = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    message_external_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exec_approval", x => x.id);
                    table.ForeignKey(
                        name: "FK_exec_approval_agent_agent_id",
                        column: x => x.agent_id,
                        principalTable: "agent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_exec_approval_chat_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chat",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_exec_allowlist_agent_id_pattern",
                table: "exec_allowlist",
                columns: new[] { "agent_id", "pattern" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exec_approval_agent_id",
                table: "exec_approval",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "IX_exec_approval_approval_id",
                table: "exec_approval",
                column: "approval_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exec_approval_chat_id",
                table: "exec_approval",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_exec_approval_message_external_id",
                table: "exec_approval",
                column: "message_external_id");

            migrationBuilder.CreateIndex(
                name: "IX_exec_approval_status",
                table: "exec_approval",
                column: "status",
                filter: "status = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exec_allowlist");

            migrationBuilder.DropTable(
                name: "exec_approval");
        }
    }
}
