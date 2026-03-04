using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionIdToMessage_RemoveStartEndMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_message_id",
                table: "session");

            migrationBuilder.DropColumn(
                name: "start_message_id",
                table: "session");

            migrationBuilder.AddColumn<long>(
                name: "session_id",
                table: "message",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_message_session_id_timestamp",
                table: "message",
                columns: new[] { "session_id", "timestamp" });

            migrationBuilder.AddForeignKey(
                name: "FK_message_session_session_id",
                table: "message",
                column: "session_id",
                principalTable: "session",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_message_session_session_id",
                table: "message");

            migrationBuilder.DropIndex(
                name: "IX_message_session_id_timestamp",
                table: "message");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "message");

            migrationBuilder.AddColumn<long>(
                name: "end_message_id",
                table: "session",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "start_message_id",
                table: "session",
                type: "bigint",
                nullable: true);
        }
    }
}
