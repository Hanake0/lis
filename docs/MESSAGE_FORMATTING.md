# Message Formatting

## Overview

AI models output standard markdown, but messaging platforms use their own formatting syntax. The `IMessageFormatter` interface provides a per-channel normalization pipeline that converts markdown to platform-native formatting before messages are sent.

## WhatsApp Formatting

WhatsApp uses custom delimiters instead of standard markdown:

| Markdown | WhatsApp | Rendered As |
|----------|----------|-------------|
| `**bold**` | `*bold*` | **bold** |
| `_italic_` | `_italic_` | _italic_ |
| `~~strike~~` | `~strike~` | ~~strike~~ |
| `` `code` `` | `` `code` `` | `code` |
| ` ``` ` blocks | ` ``` ` blocks | code block |
| `# Header` | `*Header*` | **Header** |
| `- item` | `• item` | bullet |
| `[text](url)` | `text (url)` | link |

## Architecture

```
IMessageFormatter (Lis.Core)
    ↑
    └── WhatsAppFormatter (Lis.Channels/WhatsApp)
        └── Injected into WhatsAppClient via DI
```

### Flow

```
AI Response → ResponseDirectives.Parse() → IMessageFormatter.Format() → GOWA → WhatsApp
                                                   ↑
                                          WhatsAppFormatter
```

The formatter runs inside `WhatsAppClient.SendMessageAsync()`, so:
- The DB stores the original markdown (better for AI context window)
- WhatsApp receives the platform-formatted version

### Code Block Protection

Code blocks (fenced and inline) are extracted into placeholders before any transformations run, then restored at the end. This ensures formatting inside code examples is never modified.

## Adding a New Channel Formatter

1. Create a class implementing `IMessageFormatter` in the channel's namespace
2. Register it in the channel's `Add*()` setup method
3. Inject into the channel's `IChannelClient` implementation

Example for a hypothetical Telegram channel:
```csharp
public sealed class TelegramFormatter : IMessageFormatter
{
    public string Format(string content) { /* ... */ }
}
```
