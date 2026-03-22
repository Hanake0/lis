using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_sender_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_message_sender_id_id",
                table: "message",
                columns: new[] { "sender_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_message_sender_name_id",
                table: "message",
                columns: new[] { "sender_name", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_message_sender_id_id",
                table: "message");

            migrationBuilder.DropIndex(
                name: "IX_message_sender_name_id",
                table: "message");
        }
    }
}
