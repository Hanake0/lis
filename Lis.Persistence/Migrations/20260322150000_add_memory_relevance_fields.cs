using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
	/// <inheritdoc />
	public partial class add_memory_relevance_fields : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "last_accessed_at",
				table: "memory",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.AddColumn<float>(
				name: "relevance_score",
				table: "memory",
				type: "real",
				nullable: false,
				defaultValue: 1f);
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropColumn(
				name: "last_accessed_at",
				table: "memory");

			migrationBuilder.DropColumn(
				name: "relevance_score",
				table: "memory");
		}
	}
}
