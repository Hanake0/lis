namespace Lis.Core.Channel;

public interface ITranscriptionService {
	Task<string?> TranscribeAsync(byte[] audioData, string mimeType, CancellationToken ct = default);
}
