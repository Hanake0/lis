using Lis.Core.Channel;
using Lis.Core.Util;

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AudioToText;

namespace Lis.Providers.OpenAi;

#pragma warning disable SKEXP0001

public sealed class WhisperService(IAudioToTextService audioToText) : ITranscriptionService {
	private const int MaxAudioSizeBytes = 25 * 1024 * 1024;

	[Trace("WhisperService > TranscribeAsync")]
	public async Task<string?> TranscribeAsync(byte[] audioData, string mimeType, CancellationToken ct = default) {
		if (audioData.Length == 0) return null;
		if (audioData.Length > MaxAudioSizeBytes) return null;

		AudioContent content = new(audioData, mimeType);
		IReadOnlyList<TextContent> result = await audioToText.GetTextContentsAsync(content, cancellationToken: ct);
		string text = string.Join("\n", result.Select(r => r.Text));

		return string.IsNullOrWhiteSpace(text) ? null : text;
	}
}
