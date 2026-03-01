using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using OpenAI;

namespace Lis.Providers.Embedding;

public static class EmbeddingSetup {
	public static IServiceCollection AddEmbedding(this IServiceCollection services) {
		string provider = Env("MEMORIES_EMBEDDING_PROVIDER");
		string model    = Env("MEMORIES_EMBEDDING_MODEL") is { Length: > 0 } m ? m : "text-embedding-3-small";
		string apiKey   = Env("MEMORIES_EMBEDDING_API_KEY");
		string baseUrl  = Env("MEMORIES_EMBEDDING_BASE_URL");

		if (provider == "openai") {
			OpenAIClientOptions? options = null;

			if (!string.IsNullOrEmpty(baseUrl)) {
				options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };
			}

			OpenAIClient client = new(new System.ClientModel.ApiKeyCredential(apiKey), options);

			services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
				client.GetEmbeddingClient(model).AsIEmbeddingGenerator());
		}

		return services;
	}

	private static string Env(string key) => Environment.GetEnvironmentVariable(key) ?? "";
}
