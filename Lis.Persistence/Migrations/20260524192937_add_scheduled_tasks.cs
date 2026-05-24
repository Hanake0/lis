using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_scheduled_tasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "scheduled_task",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    project_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    cron_expr = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    agent_name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    timezone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scheduled_task", x => x.id);
                    table.ForeignKey(
                        name: "FK_scheduled_task_project_project_id",
                        column: x => x.project_id,
                        principalTable: "project",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_task_enabled_next_run_at",
                table: "scheduled_task",
                columns: new[] { "enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_task_external_id",
                table: "scheduled_task",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_task_project_id",
                table: "scheduled_task",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduled_task");
        }
    }
}
