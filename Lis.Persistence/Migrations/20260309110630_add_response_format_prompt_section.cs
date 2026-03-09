using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lis.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class add_response_format_prompt_section : Migration
    {
        private const string Content = """
            Response format:
            - Messages are sent as plain messages by default (not quoting/replying)
            - Start your message with [QUOTE] to reply/quote the user's message (use only when referencing a specific message)
            - Output only NO_RESPONSE to skip sending a text message (use after reacting with an emoji to just acknowledge)
            - Use the react_to_message tool to react with an emoji — you can target any message by its ID (the number in brackets, e.g. [42] Alice: hello → messageId=42)
            - In group chats, each user message shows as [id] Name: message — use the name to address the right person
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                INSERT INTO prompt_section (name, content, sort_order, is_enabled, agent_id, created_at, updated_at)
                SELECT 'response_format', '{Content.Replace("'", "''")}', 900, true, a.id, NOW(), NOW()
                FROM agent a
                WHERE NOT EXISTS (
                    SELECT 1 FROM prompt_section ps WHERE ps.agent_id = a.id AND ps.name = 'response_format'
                )
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM prompt_section WHERE name = 'response_format'");
        }
    }
}
