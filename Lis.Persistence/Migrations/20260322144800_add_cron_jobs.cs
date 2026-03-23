using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_cron_jobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cron_job",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    cron_expression = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    handler = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    is_deterministic = table.Column<bool>(type: "boolean", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cron_job", x => x.id);
                    table.ForeignKey(
                        name: "FK_cron_job_chat_chat_id",
                        column: x => x.chat_id,
                        principalTable: "chat",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cron_job_chat_id",
                table: "cron_job",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_cron_job_enabled_next_run_at",
                table: "cron_job",
                columns: new[] { "enabled", "next_run_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cron_job");
        }
    }
}
