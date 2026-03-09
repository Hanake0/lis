# WhatsApp Message Normalizer

## Context

The AI (Claude) outputs standard markdown formatting (`**bold**`, `*italic*`, `# headers`, `- bullets`, etc.) but WhatsApp uses its own formatting syntax (`*bold*`, `_italic_`, `~strikethrough~`). This causes raw markdown artifacts to appear in sent messages — double asterisks, hash headers, etc. The normalizer converts standard markdown to WhatsApp-native formatting before sending.

Reference: Openclaw has a full markdown IR pipeline (`openclaw/src/markdown/ir.ts` + `render.ts`) with per-channel renderers (Slack, Telegram, Signal, Discord). We'll implement a simpler regex-based approach in C# since we only need WhatsApp for now, but with a clean interface so future channels can plug in their own formatters.

## Architecture

### Interface: `IMessageFormatter` (Lis.Core/Channel/)

```csharp
public interface IMessageFormatter
{
    string Format(string content);
}
```

This follows **Interface Segregation** and **Dependency Inversion** — channels depend on the abstraction, not a concrete formatter. Future channels (Telegram, Discord) implement their own `IMessageFormatter`.

### Implementation: `WhatsAppFormatter` (Lis.Channels/WhatsApp/)

Implements `IMessageFormatter`. Internally delegates to focused, single-responsibility private methods — each transformation is isolated and testable through the public API.

```csharp
public sealed class WhatsAppFormatter : IMessageFormatter
{
    public string Format(string content)
    {
        // Guard clause
        if (string.IsNullOrWhiteSpace(content)) return content ?? string.Empty;

        // Pipeline: each step is a focused transformation
        string result = content;
        result = ProtectCodeBlocks(result, out var codeBlocks);
        result = ConvertHeaders(result);
        result = ConvertBold(result);
        result = ConvertStrikethrough(result);
        result = ConvertBulletLists(result);
        result = ConvertHorizontalRules(result);
        result = ConvertTables(result);
        result = ConvertLinks(result);
        result = CollapseBlankLines(result);
        result = RestoreCodeBlocks(result, codeBlocks);
        return result.TrimEnd();
    }
}
```

**Design decisions:**
- **Sealed class** — no inheritance needed, makes intent clear
- **Guard clauses** — null/empty handled upfront, no downstream null checks
- **Pipeline pattern** — each transformation is independent, order is explicit, easy to add/remove/reorder steps
- **Code block protection** — extract code blocks into placeholders first, restore last, so no transformation accidentally modifies code content
- **No static abuse** — injectable via DI for testability and substitutability

### Transformation Details

| Step | Input | Output | Regex |
|------|-------|--------|-------|
| Headers | `# Text`, `## Text`, etc. | `*Text*` (bold) | `^#{1,6}\s+(.+)$` multiline |
| Bold | `**text**` | `*text*` | `\*\*(.+?)\*\*` |
| Strikethrough | `~~text~~` | `~text~` | `~~(.+?)~~` |
| Bullets | `- item` or `* item` | `• item` | `^[\-\*]\s+` multiline |
| Horizontal rules | `---`, `***`, `___` | `───` | `^[-*_]{3,}\s*$` multiline |
| Tables | `\| col \| col \|` | Bulleted key-values | Custom parser |
| Links | `[text](url)` | `text (url)` | `\[([^\]]+)\]\(([^)]+)\)` |
| Blank lines | 3+ consecutive `\n` | 2 `\n` | `\n{3,}` |

## Plan

### 1. Create `IMessageFormatter` in `Lis.Core/Channel/IMessageFormatter.cs`

Single-method interface. Follows ISP — channels that don't need formatting can use a `PassthroughFormatter` or simply not register one.

### 2. Create `WhatsAppFormatter` in `Lis.Channels/WhatsApp/WhatsAppFormatter.cs`

Full implementation with:
- Guard clause for null/empty input
- Code block protection (placeholder swap pattern)
- Each transformation as a focused private static method
- Compiled regex instances as `private static readonly` fields (performance — avoids recompilation)
- Table conversion inspired by Openclaw's `renderTableAsBullets` approach: headers become labels, rows become `• header: value` bullets

### 3. Register in DI and inject into `WhatsAppClient`

**File:** `Lis.Channels/WhatsApp/WhatsAppServiceExtensions.cs` (or wherever `AddWhatsApp()` lives)
```csharp
services.AddSingleton<IMessageFormatter, WhatsAppFormatter>();
```

**File:** `Lis.Channels/WhatsApp/WhatsAppClient.cs`
```csharp
public sealed class WhatsAppClient(GowaClient gowa, IMessageFormatter formatter) : IChannelClient
{
    public async Task<string?> SendMessageAsync(
        string chatId, string message, string? replyToId = null, CancellationToken ct = default)
    {
        string formatted = formatter.Format(message);
        SendResult? result = await gowa.SendMessageAsync(
            StripJidSuffix(chatId), formatted, replyToId, ct: ct);
        return result?.MessageId;
    }
}
```

The DB keeps the original markdown content (better for AI context), while WhatsApp gets the formatted version.

### 4. Add tests in `Lis.Tests/WhatsAppFormatterTests.cs`

Comprehensive test class covering:
- **Guard clauses:** null → `""`, empty → `""`, whitespace-only → unchanged
- **Bold:** `**bold**` → `*bold*`, nested `**bold *italic***` edge case
- **Strikethrough:** `~~text~~` → `~text~`
- **Headers:** `# H1`, `## H2`, `### H3` → `*Text*`
- **Bullets:** `- item`, `* item` → `• item`
- **Code preservation:** fenced blocks and inline code pass through untouched
- **Tables:** full table → bullet list format
- **Links:** `[text](url)` → `text (url)`
- **Horizontal rules:** `---` → `───`
- **Mixed formatting:** a realistic AI response with multiple formatting types
- **Blank line collapsing:** excessive whitespace normalized
- **No false positives:** plain text with asterisks in math (`2 * 3 = 6`) not mangled

### Files

| Action | Path |
|--------|------|
| **New** | `Lis.Core/Channel/IMessageFormatter.cs` |
| **New** | `Lis.Channels/WhatsApp/WhatsAppFormatter.cs` |
| **Edit** | `Lis.Channels/WhatsApp/WhatsAppClient.cs` — inject `IMessageFormatter`, call in `SendMessageAsync` |
| **Edit** | WhatsApp DI registration — register `WhatsAppFormatter` as `IMessageFormatter` |
| **New** | `Lis.Tests/WhatsAppFormatterTests.cs` |

### Verification

1. `dotnet build` — compiles without errors
2. `dotnet test Lis.Tests/Lis.Tests.csproj` — all tests pass
3. Manual test: send a message with mixed markdown through the bot, verify WhatsApp renders correctly
