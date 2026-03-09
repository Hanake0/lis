# Media Handling

Lis processes images, stickers, and audio messages from WhatsApp via Gowa.

## Supported Media Types

| Type | Processing | AI sees |
|------|-----------|---------|
| `image` | Download → store as `bytea` | Image content block (Claude vision) |
| `sticker` | Download → store as `bytea` | Image content block (Claude vision) |
| `audio` / `ptt` | Download → transcribe (Whisper) | `<Audio transcript: ...>` or `<Audio message>` |
| `video` | Not processed | `[media]` placeholder |

## Architecture

```
Webhook → GowaWebhookController
  ├─ Parse nested media fields (image/audio/sticker)
  ├─ Extract file path (auto-download) or detect CDN URL
  └─ ConversationService.IngestMessageAsync
       └─ MediaProcessor.ProcessAsync
            ├─ Download via IChannelClient.DownloadMediaAsync
            │   ├─ Fast path: fetch from Gowa static server (auto-download ON)
            │   └─ Fallback: download endpoint → static server (auto-download OFF)
            ├─ Validate size (10 MB max)
            ├─ Audio: transcribe via ITranscriptionService
            └─ Store byte[] + MIME type on MessageEntity
```

## Gowa Webhook Payload

Gowa sends media as nested objects, not flat fields:

```json
// Auto-download ON — file path
{"image": "statics/media/1234-uuid.jpg"}
{"image": {"path": "statics/media/1234-uuid.jpg", "caption": "photo"}}
{"audio": "statics/media/1234-uuid.ogg"}

// Auto-download OFF — WhatsApp CDN URL
{"image": {"url": "https://media-cdn.whatsapp.net/...", "caption": "photo"}}
```

Lis uses `JsonExtensionData` on `WebhookPayload` to capture these dynamically and derives `MediaType`, `MediaPath`, and `MediaCaption` via computed properties.

## Storage

- `media_data` column: `byte[]` → PostgreSQL `bytea` (raw binary, no base64 overhead)
- `media_mime_type` column: `varchar(64)` (e.g., `image/jpeg`, `audio/ogg`)
- `JsonIgnore` on `MediaData` prevents accidental serialization in API responses

## Context Window

`ContextWindowBuilder` creates multimodal `ChatMessageContent` with `ImageContent` for image/sticker messages. SK's `IChatCompletionService` handles serialization to Anthropic format automatically.

`ChatHistorySerializer` also handles `ImageContent` for accurate token counting via the `count_tokens` endpoint.

## Audio Transcription

Optional — requires `OPENAI_API_KEY` env var.

Uses SK's `IAudioToTextService` with the OpenAI Whisper connector (following RickAi's pattern). A separate SK kernel is built just for audio-to-text, keeping it isolated from the main chat kernel.

Without the key, `ITranscriptionService` is not registered and audio messages fall back to `<Audio message>` placeholder text.

### Configuration

| Env Var | Default | Description |
|---------|---------|-------------|
| `OPENAI_API_KEY` | — | Enables Whisper transcription |
| `OPENAI_WHISPER_MODEL` | `whisper-1` | OpenAI model for audio-to-text |
