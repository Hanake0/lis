# Media Handling: Images, Audio, Stickers

## Context

Media messages are ignored by Lis. Three problems:

1. **Webhook payload mismatch**: Lis reads flat `media_type`/`media_caption` fields that Gowa never sends. Gowa sends nested objects: `payload["image"]`, `payload["audio"]`, `payload["sticker"]` containing file paths (auto-download ON) or WhatsApp CDN URLs (auto-download OFF).
2. **Media-only messages dropped**: `GowaWebhookController.cs:80` does `if (string.IsNullOrEmpty(payload.Body)) return Ok()` — drops images without captions, all audio, all stickers.
3. **No media processing**: Even when messages get through (image with caption), the AI only sees caption text, never the actual image.

Additionally, `MediaDownloadResult` in Lis expects `data` (base64) and `mime_type` fields that Gowa's download endpoint never returns — it returns `file_path` and `media_type`.

## Approach

Two modes based on Gowa's `whatsapp_auto_download_media` setting:

**Auto-download ON**: Gowa downloads media to `statics/media/` and includes the file path in the webhook (`payload["image"] = "statics/media/1234-uuid.jpg"` or `{"path": "...", "caption": "..."}`). We parse the path and fetch from Gowa's static server — fast, no re-download.

**Auto-download OFF**: Webhook includes WhatsApp CDN URLs (`payload["image"] = {"url": "https://media-cdn.whatsapp.net/...", "caption": "..."}`). These are encrypted/temporary. We call Gowa's download endpoint (`GET /message/{id}/download`) which downloads from WA, decrypts, saves to disk, returns the `file_path`. Then fetch from static server.

Both paths converge: get a file path → fetch bytes from `{GOWA_BASE_URL}/{path}` → store as `byte[]` in DB.

**AI communication**: SK's `IChatCompletionService` already handles `ImageContent` serialization to Anthropic format internally — no custom serialization needed for the main AI path. `ChatHistorySerializer` only needs updating for accurate token counting (count_tokens endpoint).

**Audio transcription**: Following RickAi's pattern — SK's `IAudioToTextService` with OpenAI Whisper connector. WhatsApp audio (ogg/opus) is natively supported by Whisper, no FFMpeg conversion needed.

---

## Implementation

### Step 1 — Fix WebhookPayload to parse Gowa's actual format

**`Lis.Channels/WhatsApp/Schemas/WebhookPayload.cs`**

Gowa sends media in nested fields. Format varies:
- String: `"audio": "statics/media/1234-uuid.ogg"`
- Object: `"image": {"path": "statics/media/1234-uuid.jpg", "caption": "photo desc"}`

Replace the dead `MediaType`/`MediaCaption` fields with `JsonExtensionData` + computed properties:

```csharp
[JsonExtensionData]
public Dictionary<string, JsonElement>? Extensions { get; set; }

// Derived from which nested media field is present
public string? MediaType =>
    Extensions?.ContainsKey("image") == true   ? "image" :
    Extensions?.ContainsKey("audio") == true   ? "audio" :
    Extensions?.ContainsKey("sticker") == true ? "sticker" :
    Extensions?.ContainsKey("video") == true   ? "video" :
    null;

// Extract file path — auto-download ON: string or {"path":"..."}
// Returns null when auto-download OFF (webhook has {"url":"..."} instead)
public string? MediaPath => GetMediaField("path") ?? GetMediaString();

// Caption from {"path":"...", "caption":"..."} or {"url":"...", "caption":"..."}
public string? MediaCaption => GetMediaField("caption");

private string? GetMediaString() {
    if (MediaType is null || Extensions?.TryGetValue(MediaType, out var el) != true) return null;
    return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}

private string? GetMediaField(string field) {
    if (MediaType is null || Extensions?.TryGetValue(MediaType, out var el) != true) return null;
    if (el.ValueKind != JsonValueKind.Object) return null;
    return el.TryGetProperty(field, out var prop) ? prop.GetString() : null;
}
```

This handles all formats:
- `"audio": "statics/media/.../audio.ogg"` → `MediaPath = "statics/media/.../audio.ogg"`
- `"image": {"path": "statics/media/.../img.jpg", "caption": "hello"}` → `MediaPath = "statics/media/.../img.jpg"`, `MediaCaption = "hello"`
- `"image": {"url": "https://cdn...", "caption": "hello"}` → `MediaPath = null` (triggers download endpoint fallback), `MediaCaption = "hello"`

### Step 2 — Add MediaPath to IncomingMessage

**`Lis.Core/Channel/IncomingMessage.cs`** — add:
```csharp
public string? MediaPath { get; init; }
```

**`Lis.Channels/WhatsApp/GowaWebhookController.cs`** — populate from webhook:
```csharp
IncomingMessage message = new() {
    ...
    MediaType    = payload.MediaType,     // now derived from nested fields
    MediaCaption = payload.MediaCaption,  // extracted from nested object
    MediaPath    = payload.MediaPath,     // file path from auto-download
};
```

Also fix line 80 — allow media-only messages:
```csharp
if (string.IsNullOrEmpty(payload.Body) && payload.MediaType is null)
    return this.Ok();
```

### Step 3 — Fix MediaDownloadResult + add static file fetch

**`Lis.Channels/WhatsApp/Schemas/MessageModels.cs`** — fix to match Gowa's actual response:
```csharp
public sealed class MediaDownloadResult {
    [JsonPropertyName("media_type")]  // was "mime_type" (wrong)
    public string? MediaType { get; init; }

    [JsonPropertyName("file_path")]   // was "data" (wrong)
    public string? FilePath { get; init; }

    [JsonPropertyName("filename")]    // was "file_name" (wrong)
    public string? Filename { get; init; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; init; }
}
```

**`Lis.Channels/WhatsApp/GowaClient.cs`** (base partial) — add static file fetch:
```csharp
[Trace("GowaClient > FetchFileAsync")]
public async Task<byte[]> FetchFileAsync(string path, CancellationToken ct = default) {
    HttpResponseMessage response = await httpClient.GetAsync($"/{path}", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsByteArrayAsync(ct);
}
```

Uses the same authenticated `HttpClient` — no extra security concern.

### Step 4 — Channel abstraction: DownloadMediaAsync

**`Lis.Core/Channel/MediaDownload.cs`** — new:
```csharp
public sealed record MediaDownload(byte[] Data, string MimeType);
```

**`Lis.Core/Channel/IChannelClient.cs`** — add:
```csharp
Task<MediaDownload?> DownloadMediaAsync(string messageId, string chatId,
    string? mediaPath = null, CancellationToken ct = default);
```

`mediaPath` is optional — if provided (from webhook), fetches from static server directly. If null, falls back to download endpoint + static fetch.

**`Lis.Channels/WhatsApp/WhatsAppClient.cs`** — implement:
```csharp
[Trace("WhatsAppClient > DownloadMediaAsync")]
public async Task<MediaDownload?> DownloadMediaAsync(
    string messageId, string chatId, string? mediaPath = null, CancellationToken ct = default) {

    string path;
    if (mediaPath is not null) {
        path = mediaPath;
    } else {
        // Fallback: call download endpoint (re-downloads from WA — slower)
        MediaDownloadResult? result = await gowa.DownloadMediaAsync(
            messageId, StripJidSuffix(chatId), ct);
        if (result?.FilePath is null) return null;
        path = result.FilePath;
    }

    byte[] data = await gowa.FetchFileAsync(path, ct);
    if (data.Length == 0) return null;

    string mimeType = MimeFromExtension(Path.GetExtension(path));
    return new MediaDownload(data, mimeType);
}

private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch {
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png"            => "image/png",
    ".webp"           => "image/webp",
    ".gif"            => "image/gif",
    ".ogg"            => "audio/ogg",
    ".mp3"            => "audio/mpeg",
    ".m4a"            => "audio/mp4",
    ".wav"            => "audio/wav",
    ".mp4"            => "video/mp4",
    ".pdf"            => "application/pdf",
    _                 => "application/octet-stream"
};
```

### Step 5 — Schema: add media columns

**`Lis.Persistence/Entities/MessageEntity.cs`** — add:
```csharp
[Column("media_data")]
[JsonIgnore]
public byte[]? MediaData { get; set; }

[MaxLength(64)]
[Column("media_mime_type", TypeName = "varchar(64)")]
[JsonPropertyName("media_mime_type")]
public string? MediaMimeType { get; set; }
```

`byte[]` → PostgreSQL `bytea`. Raw binary, no base64 overhead. `JsonIgnore` prevents accidental serialization.

**Migration** for the two new columns.

### Step 6 — MediaProcessor service

Dedicated service (SRP) — owns media download, validation, transcription. Keeps ConversationService focused on conversation flow.

**`Lis.Agent/MediaProcessor.cs`** — new:
```csharp
public interface IMediaProcessor {
    Task<ProcessedMedia?> ProcessAsync(IncomingMessage message, CancellationToken ct);
}

public sealed record ProcessedMedia(byte[] Data, string MimeType, string? Transcription);

public sealed class MediaProcessor(
    IChannelClient          channelClient,
    ILogger<MediaProcessor> logger,
    ITranscriptionService?  transcriptionService = null) : IMediaProcessor {

    private const int MaxMediaSizeBytes = 10 * 1024 * 1024;

    [Trace("MediaProcessor > ProcessAsync")]
    public async Task<ProcessedMedia?> ProcessAsync(IncomingMessage message, CancellationToken ct) {
        if (message.MediaType is null) return null;

        MediaDownload? download = await channelClient.DownloadMediaAsync(
            message.ExternalId, message.ChatId, message.MediaPath, ct);

        if (download is null) return null;
        if (download.Data.Length == 0) return null;

        if (download.Data.Length > MaxMediaSizeBytes) {
            logger.LogWarning("Media too large ({Size} bytes), skipping {Id}",
                download.Data.Length, message.ExternalId);
            return null;
        }

        string? transcription = message.MediaType is "audio" or "ptt"
            ? await this.TranscribeAsync(download.Data, download.MimeType, ct)
            : null;

        return new ProcessedMedia(download.Data, download.MimeType, transcription);
    }

    private async Task<string?> TranscribeAsync(byte[] data, string mimeType, CancellationToken ct) {
        if (transcriptionService is null) return null;
        try {
            return await transcriptionService.TranscribeAsync(data, mimeType, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "Audio transcription failed");
            return null;
        }
    }
}
```

**`Lis.Agent/AgentSetup.cs`** — register:
```csharp
services.AddSingleton<IMediaProcessor, MediaProcessor>();
```

### Step 7 — ConversationService: delegate to MediaProcessor

**`Lis.Agent/ConversationService.cs`** — add `IMediaProcessor` to constructor.

In `IngestMessageAsync`, after `PersistMessageAsync`:
```csharp
if (message.MediaType is not null) {
    try {
        ProcessedMedia? media = await mediaProcessor.ProcessAsync(message, ct);
        if (media is not null) {
            MessageEntity? entity = await db.Messages.FindAsync([message.DbId], ct);
            if (entity is not null) {
                entity.MediaData     = media.Data;
                entity.MediaMimeType = media.MimeType;

                if (media.Transcription is { Length: > 0 } transcript)
                    entity.Body = $"<Audio transcript: {transcript}>";
                else if (message.MediaType is "audio" or "ptt")
                    entity.Body = "<Audio message>";

                await db.SaveChangesAsync(ct);
            }
        }
    } catch (Exception ex) {
        logger.LogWarning(ex, "Failed to process media for {Id}", message.ExternalId);
    }
}
```

### Step 8 — ContextWindowBuilder: multimodal content for images

**`Lis.Agent/ContextWindowBuilder.cs`** — modify user message path (lines 80-84):

```csharp
} else {
    if (msg.MediaData is not null && msg.MediaType is "image" or "sticker") {
        ChatMessageContent imgMsg = new(AuthorRole.User, content: (string?)null);
        if (msg.Body is { Length: > 0 } text)
            imgMsg.Items.Add(new TextContent(text));
        else if (msg.MediaCaption is { Length: > 0 } caption)
            imgMsg.Items.Add(new TextContent(caption));
        imgMsg.Items.Add(new ImageContent(msg.MediaData, msg.MediaMimeType ?? "image/jpeg"));
        history.Add(imgMsg);
    } else {
        string content = msg.Body ?? msg.MediaCaption ?? "[media]";
        if (msg.IsFromMe) history.AddAssistantMessage(content);
        else              history.AddUserMessage(content);
    }
}
```

SK's `IChatCompletionService` (backed by Anthropic SDK) already handles `ImageContent` → Anthropic image blocks internally. No custom serialization needed for the main AI path.

### Step 9 — ChatHistorySerializer: ImageContent for token counting

`ChatHistorySerializer` is only used for the `count_tokens` endpoint, not for AI communication. We still need to add `ImageContent` handling for accurate token counting.

**`Lis.Agent/ChatHistorySerializer.cs`** — in `ToAnthropicJson`, add before the plain text user message fallback:

```csharp
// User messages with images → multimodal content blocks (for token counting)
if (msg.Items.Any(i => i is ImageContent)) {
    JsonArray content = [];
    foreach (KernelContent item in msg.Items) {
        if (item is TextContent tc && tc.Text is { Length: > 0 })
            content.Add(new JsonObject { ["type"] = "text", ["text"] = tc.Text });
        else if (item is ImageContent ic && ic.Data is { IsEmpty: false } data)
            content.Add(new JsonObject {
                ["type"] = "image",
                ["source"] = new JsonObject {
                    ["type"]       = "base64",
                    ["media_type"] = ic.MimeType ?? "image/jpeg",
                    ["data"]       = Convert.ToBase64String(data.Value.ToArray())
                }
            });
    }
    messages.Add(new JsonObject { ["role"] = "user", ["content"] = content });
    continue;
}
```

### Step 10 — Audio transcription via SK's IAudioToTextService

Following RickAi's pattern (`RickAi/Api/Services/KernelService.cs` + `MessageAdapterService.cs`):
- RickAi registers `builder.AddOpenAIAudioToText(model, client)` → gets `IAudioToTextService`
- Transcription: `AudioContent` → `kernel.AudioToText.GetTextContentsAsync(content)`

**`Lis.Core/Channel/ITranscriptionService.cs`** — new interface:
```csharp
public interface ITranscriptionService {
    Task<string?> TranscribeAsync(byte[] audioData, string mimeType, CancellationToken ct = default);
}
```

**`Lis.Providers/OpenAi/WhisperService.cs`** — new, wraps SK's `IAudioToTextService`:
```csharp
public sealed class WhisperService(IAudioToTextService audioToText) : ITranscriptionService {
    private const int MaxAudioSizeBytes = 25 * 1024 * 1024;

    [Trace("WhisperService > TranscribeAsync")]
    public async Task<string?> TranscribeAsync(byte[] audioData, string mimeType, CancellationToken ct) {
        if (audioData.Length == 0) return null;
        if (audioData.Length > MaxAudioSizeBytes) return null;

        AudioContent content = new(audioData, mimeType);
        IReadOnlyList<TextContent> result = await audioToText.GetTextContentsAsync(content, cancellationToken: ct);
        string text = string.Join("\n", result.Select(r => r.Text));

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
```

**`Lis.Providers/OpenAi/OpenAiSetup.cs`** — new, builds a separate SK kernel just for audio:
```csharp
public static class OpenAiSetup {
    public static IServiceCollection AddOpenAiTranscription(this IServiceCollection services) {
        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        string model  = Environment.GetEnvironmentVariable("OPENAI_WHISPER_MODEL") is { Length: > 0 } m
            ? m : "whisper-1";

        IKernelBuilder builder = Kernel.CreateBuilder();
        builder.AddOpenAIAudioToText(model, apiKey);
        Kernel kernel = builder.Build();

        IAudioToTextService audioToText = kernel.GetRequiredService<IAudioToTextService>();
        services.AddSingleton<ITranscriptionService>(new WhisperService(audioToText));
        return services;
    }
}
```

**`Lis.Api/Program.cs`** — conditional:
```csharp
if (Env("OPENAI_API_KEY") is { Length: > 0 })
    builder.Services.AddOpenAiTranscription();
```

Without the key, `ITranscriptionService` is not registered → `MediaProcessor` receives null → audio falls back to `<Audio message>` placeholder.

Package dependency: add `Microsoft.SemanticKernel.Connectors.OpenAI` to `Lis.Providers.csproj`.

---

## Files

| File | Action |
|------|--------|
| `Lis.Channels/WhatsApp/Schemas/WebhookPayload.cs` | **REWRITE** — parse nested media fields via `JsonExtensionData` |
| `Lis.Channels/WhatsApp/Schemas/MessageModels.cs` | **FIX** — `MediaDownloadResult` fields to match Gowa's actual response |
| `Lis.Channels/WhatsApp/GowaWebhookController.cs` | **MODIFY** — populate MediaPath, allow media-only messages |
| `Lis.Channels/WhatsApp/GowaClient.cs` | **MODIFY** — add `FetchFileAsync` for static files |
| `Lis.Channels/WhatsApp/WhatsAppClient.cs` | **MODIFY** — implement `DownloadMediaAsync` |
| `Lis.Core/Channel/IncomingMessage.cs` | **MODIFY** — add `MediaPath` |
| `Lis.Core/Channel/MediaDownload.cs` | **NEW** — `record MediaDownload(byte[] Data, string MimeType)` |
| `Lis.Core/Channel/IChannelClient.cs` | **MODIFY** — add `DownloadMediaAsync` |
| `Lis.Core/Channel/ITranscriptionService.cs` | **NEW** — audio transcription interface |
| `Lis.Persistence/Entities/MessageEntity.cs` | **MODIFY** — add `MediaData` (byte[]/bytea), `MediaMimeType` |
| `Lis.Persistence/Migrations/` | **NEW** — migration for media columns |
| `Lis.Agent/MediaProcessor.cs` | **NEW** — `IMediaProcessor` + `MediaProcessor` |
| `Lis.Agent/AgentSetup.cs` | **MODIFY** — register `IMediaProcessor` |
| `Lis.Agent/ConversationService.cs` | **MODIFY** — delegate to `IMediaProcessor` on ingest |
| `Lis.Agent/ContextWindowBuilder.cs` | **MODIFY** — `ImageContent` for image/sticker messages |
| `Lis.Agent/ChatHistorySerializer.cs` | **MODIFY** — `ImageContent` for token counting only |
| `Lis.Providers/OpenAi/WhisperService.cs` | **NEW** — wraps `IAudioToTextService` (RickAi pattern) |
| `Lis.Providers/OpenAi/OpenAiSetup.cs` | **NEW** — builds SK kernel with OpenAI audio-to-text |
| `Lis.Providers/Lis.Providers.csproj` | **MODIFY** — add `Microsoft.SemanticKernel.Connectors.OpenAI` |
| `Lis.Api/Program.cs` | **MODIFY** — conditional `AddOpenAiTranscription()` |

---

## Step 11 — Documentation

**`Plans/media-handling.md`** — save this plan.

**`docs/MEDIA_HANDLING.md`** — document:
- Supported media types: image, sticker, audio (video as future)
- Gowa auto-download ON vs OFF: two paths to media bytes
- Webhook payload parsing: nested `image`/`audio`/`sticker` fields → `JsonExtensionData`
- Storage: `byte[]` → PostgreSQL `bytea`, `JsonIgnore` on `MediaData`
- MediaProcessor: download → validate size → transcribe audio → return `ProcessedMedia`
- Context building: `ImageContent` for images/stickers, text transcript for audio
- SK handles `ImageContent` serialization to Anthropic format for AI communication
- `ChatHistorySerializer` updated for accurate token counting only
- Audio transcription: SK `IAudioToTextService` + OpenAI Whisper (optional, needs `OPENAI_API_KEY`)
- Configuration: `OPENAI_API_KEY`, `OPENAI_WHISPER_MODEL` (defaults to `whisper-1`)

---

## Commit Plan

Each step is a standalone commit that builds cleanly.

| # | Commit | Steps |
|---|--------|-------|
| 1 | `🔧 fix(channels): fix MediaDownloadResult to match Gowa response` | Step 3 (MessageModels.cs only) |
| 2 | `✨ feat(channels): parse nested media fields from Gowa webhook` | Steps 1-2 (WebhookPayload rewrite, IncomingMessage.MediaPath, GowaWebhookController fix) |
| 3 | `✨ feat(channels): add media download via static file server` | Steps 3-4 (GowaClient.FetchFileAsync, MediaDownload record, IChannelClient.DownloadMediaAsync, WhatsAppClient impl) |
| 4 | `✨ feat(persistence): add media storage columns` | Step 5 (MessageEntity columns + migration) |
| 5 | `✨ feat(agent): add MediaProcessor service` | Steps 6-7 (MediaProcessor, AgentSetup registration, ConversationService integration) |
| 6 | `✨ feat(agent): multimodal image support in context window` | Steps 8-9 (ContextWindowBuilder ImageContent, ChatHistorySerializer token counting) |
| 7 | `✨ feat(providers): add OpenAI Whisper transcription` | Step 10 (ITranscriptionService, WhisperService, OpenAiSetup, Program.cs, package ref) |
| 8 | `📝 docs: media handling plan and documentation` | Step 11 (Plans/, docs/) |

---

## Verify

- `dotnet build` clean
- Migration applies
- Send image → fetched from Gowa static server, stored as bytea, AI sees and describes it
- Send image with caption → AI sees both caption and image
- Send sticker → treated as image, AI describes it
- Send audio (with `OPENAI_API_KEY`) → transcribed via Whisper, AI sees transcript
- Send audio (without key) → AI sees `<Audio message>` placeholder
- Send text-only message → no regression
- Media-only messages (no caption) no longer dropped
- Token counting (count_tokens) includes image content blocks
