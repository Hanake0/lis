using Lis.Core.Channel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.AudioToText;

namespace Lis.Providers.OpenAi;

#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0010

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
