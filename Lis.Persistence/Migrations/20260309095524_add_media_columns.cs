using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_media_columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "media_data",
                table: "message",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_mime_type",
                table: "message",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "media_data",
                table: "message");

            migrationBuilder.DropColumn(
                name: "media_mime_type",
                table: "message");
        }
    }
}
