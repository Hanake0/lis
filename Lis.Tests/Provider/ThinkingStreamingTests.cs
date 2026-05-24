using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Anthropic.SDK;

using Lis.Core.Channel;

using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

using Xunit.Abstractions;

namespace Lis.Tests.Provider;

/// <summary>
/// Integration tests that hit the real Anthropic API to verify what actually
/// gets sent/received when thinking is enabled. Skip if no API key.
/// </summary>
public class ThinkingStreamingTests {
	private readonly ITestOutputHelper _out;

	public ThinkingStreamingTests(ITestOutputHelper output) {
		this._out = output;
		this.LoadDotEnv();
	}

	private void LoadDotEnv() {
		string envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env");
		envPath = Path.GetFullPath(envPath);
		this._out.WriteLine($"env probe: {envPath} exists={File.Exists(envPath)}");
		if (!File.Exists(envPath)) return;
		int loaded = 0;
		foreach (string line in File.ReadAllLines(envPath)) {
			if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
			int eq = line.IndexOf('=');
			if (eq <= 0) continue;
			string key = line[..eq].Trim();
			string val = line[(eq + 1)..].Trim();
			Environment.SetEnvironmentVariable(key, val);
			loaded++;
		}
		string anth = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
		this._out.WriteLine($"env loaded {loaded} vars; ANTHROPIC_API_KEY len={anth.Length} prefix={(anth.Length > 12 ? anth[..12] : anth)}");
	}

	private sealed class CapturingHandler : DelegatingHandler {
		public string? LastRequestBody;
		public string? LastRequestUrl;
		public Dictionary<string, string>? LastRequestHeaders;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			this.LastRequestUrl     = request.RequestUri?.ToString();
			this.LastRequestHeaders = new Dictionary<string, string>();
			foreach (KeyValuePair<string, IEnumerable<string>> h in request.Headers)
				this.LastRequestHeaders[h.Key] = string.Join(", ", h.Value);

			if (request.Content is not null) {
				this.LastRequestBody = await request.Content.ReadAsStringAsync(ct);
				MediaTypeHeaderValue? ct2 = request.Content.Headers.ContentType;
				request.Content = new StringContent(this.LastRequestBody, Encoding.UTF8, ct2?.MediaType ?? "application/json");
			}

			return await base.SendAsync(request, ct);
		}
	}

	private sealed class BearerHandler(string token) : DelegatingHandler {
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			request.Headers.Remove("x-api-key");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			request.Headers.TryAddWithoutValidation("User-Agent", "claude-cli/2.1.2 (external, cli)");
			request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20,interleaved-thinking-2025-05-14");
			if (request.RequestUri is { AbsolutePath: "/v1/messages" } uri && string.IsNullOrEmpty(uri.Query))
				request.RequestUri = new Uri(uri.OriginalString + "?beta=true");
			return base.SendAsync(request, ct);
		}
	}

	private sealed class OAuthSystemPromptHandler : DelegatingHandler {
		private const string RequiredPrefix = "You are Claude Code, Anthropic's official CLI for Claude.";
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			if (request.Content is null || request.RequestUri?.AbsolutePath.Contains("/messages") != true)
				return await base.SendAsync(request, ct);
			string json = await request.Content.ReadAsStringAsync(ct);
			using JsonDocument doc = JsonDocument.Parse(json);
			Dictionary<string, JsonElement> obj = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
			// Just inject a top-level system as array form with the prefix block
			var newSystem = new List<object> {
				new { type = "text", text = RequiredPrefix }
			};
			if (obj.TryGetValue("system", out JsonElement sys)) {
				if (sys.ValueKind == JsonValueKind.String)
					newSystem.Add(new { type = "text", text = sys.GetString() });
				else if (sys.ValueKind == JsonValueKind.Array)
					foreach (JsonElement el in sys.EnumerateArray())
						newSystem.Add(JsonSerializer.Deserialize<object>(el.GetRawText())!);
			}
			Dictionary<string, object?> patched = new();
			foreach (KeyValuePair<string, JsonElement> kv in obj)
				patched[kv.Key] = JsonSerializer.Deserialize<object>(kv.Value.GetRawText());
			patched["system"] = newSystem;
			string modified = JsonSerializer.Serialize(patched);
			request.Content = new StringContent(modified, Encoding.UTF8, "application/json");
			return await base.SendAsync(request, ct);
		}
	}

	[Fact]
	public async Task Thinking_Streaming_RealApi_CapturesRequestAndChunks() {
		string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
		if (string.IsNullOrEmpty(apiKey)) {
			this._out.WriteLine("SKIP: no API key");
			return;
		}

		bool useBearer = apiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);
		this._out.WriteLine($"useBearer={useBearer}");

		// Build handler chain: Capturing → ThinkingDisplay (no-op safety) → OAuthSystem → Bearer
		CapturingHandler capture = new() { InnerHandler = new HttpClientHandler() };
		HttpMessageHandler chain = capture;
		if (useBearer) chain = new OAuthSystemPromptHandler { InnerHandler = chain };
		HttpClient http = useBearer
			? new HttpClient(new BearerHandler(apiKey) { InnerHandler = chain })
			: new HttpClient(chain);

		string model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";
		this._out.WriteLine($"model={model}");

		AnthropicClient anthropic = new(apiKey, http);
		IChatClient chatClient = anthropic.Messages;
		IChatCompletionService completion = chatClient.AsChatCompletionService();

		ChatHistory history = new();
		history.AddUserMessage("Think step-by-step about why the sky is blue, then give a one-sentence answer.");

		PromptExecutionSettings settings = new() {
			ModelId = model,
			ExtensionData = new Dictionary<string, object> {
				["max_tokens"] = 8192,
				["thinking"] = new Dictionary<string, object> {
					["type"]          = "enabled",
					["budget_tokens"] = 4096,
					["display"]       = "summarized"
				}
			}
		};

		int chunkCount = 0;
		int reasoningCount = 0;
		int textCount = 0;
		StringBuilder textBuf = new();
		StringBuilder reasoningBuf = new();
		IReadOnlyDictionary<string, object?>? lastMetadata = null;

		await foreach (StreamingChatMessageContent chunk in completion.GetStreamingChatMessageContentsAsync(history, settings)) {
			chunkCount++;

			// Inspect inner content + items
			List<string> contentTypes = new();
			string? reasoningText = null;
			if (chunk.InnerContent is not null) contentTypes.Add($"inner:{chunk.InnerContent.GetType().Name}");
			if (chunk.InnerContent is TextReasoningContent rc) reasoningText = rc.Text;

			foreach (StreamingKernelContent item in chunk.Items) {
				contentTypes.Add($"item:{item.GetType().Name}/{item.InnerContent?.GetType().Name}");
				if (item.InnerContent is TextReasoningContent rc2 && reasoningText is null) reasoningText = rc2.Text;
			}

			if (reasoningText is not null) {
				reasoningCount++;
				reasoningBuf.Append(reasoningText);
			}
			if (!string.IsNullOrEmpty(chunk.Content)) {
				textCount++;
				textBuf.Append(chunk.Content);
			}

			if (chunk.Metadata is { Count: > 0 }) lastMetadata = chunk.Metadata;

			if (chunkCount <= 8 || reasoningText is not null)
				this._out.WriteLine($"chunk#{chunkCount} types=[{string.Join(",", contentTypes)}] content='{Truncate(chunk.Content, 80)}' reasoning='{Truncate(reasoningText, 80)}'");
		}

		this._out.WriteLine("");
		this._out.WriteLine("==== REQUEST BODY ====");
		this._out.WriteLine(capture.LastRequestBody ?? "(null)");
		this._out.WriteLine("");
		this._out.WriteLine("==== SUMMARY ====");
		this._out.WriteLine($"totalChunks={chunkCount} textChunks={textCount} reasoningChunks={reasoningCount}");
		this._out.WriteLine($"text='{textBuf}'");
		this._out.WriteLine($"reasoning='{reasoningBuf}'");

		this._out.WriteLine("");
		this._out.WriteLine("==== LAST METADATA KEYS ====");
		if (lastMetadata is null) {
			this._out.WriteLine("(null)");
		} else {
			foreach (KeyValuePair<string, object?> kv in lastMetadata)
				this._out.WriteLine($"  {kv.Key} = {kv.Value?.GetType().Name} :: {Truncate(kv.Value?.ToString(), 200)}");

			if (lastMetadata.TryGetValue("Usage", out object? usageObj) && usageObj is UsageContent uc) {
				this._out.WriteLine("");
				this._out.WriteLine("==== USAGE DETAILS ====");
				UsageDetails? d = uc.Details;
				this._out.WriteLine($"InputTokenCount={d?.InputTokenCount} OutputTokenCount={d?.OutputTokenCount} TotalTokenCount={d?.TotalTokenCount}");
				if (d?.AdditionalCounts is not null) {
					this._out.WriteLine("AdditionalCounts:");
					foreach (KeyValuePair<string, long> kv in d.AdditionalCounts)
						this._out.WriteLine($"  {kv.Key} = {kv.Value}");
				} else {
					this._out.WriteLine("AdditionalCounts: null");
				}

				// Reflect over UsageContent and its inner to find any raw Usage object
				this._out.WriteLine("");
				this._out.WriteLine("==== USAGECONTENT REFLECTION ====");
				this.ReflectObject(uc, "uc", maxDepth: 3);
			}
		}

		Assert.True(capture.LastRequestBody is not null, "should have captured a request body");
	}

	[Fact]
	public async Task Thinking_Streaming_DirectIChatClient_WithAdditionalProperties() {
		string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
		if (string.IsNullOrEmpty(apiKey)) { this._out.WriteLine("SKIP: no API key"); return; }

		bool useBearer = apiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);
		CapturingHandler capture = new() { InnerHandler = new HttpClientHandler() };
		HttpMessageHandler chain = capture;
		if (useBearer) chain = new OAuthSystemPromptHandler { InnerHandler = chain };
		HttpClient http = useBearer
			? new HttpClient(new BearerHandler(apiKey) { InnerHandler = chain })
			: new HttpClient(chain);

		string model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";
		AnthropicClient anthropic = new(apiKey, http);
		IChatClient chatClient = anthropic.Messages;

		// Use IChatClient directly with ChatOptions.AdditionalProperties
		ChatOptions options = new() {
			ModelId   = model,
			MaxOutputTokens = 8192,
			AdditionalProperties = new AdditionalPropertiesDictionary {
				["thinking"] = new Dictionary<string, object> {
					["type"]          = "enabled",
					["budget_tokens"] = 4096,
					["display"]       = "summarized"
				}
			}
		};

		List<ChatMessage> messages = new() {
			new(ChatRole.User, "Think step by step about whether 17 is prime, then answer.")
		};

		int reasoningCount = 0;
		StringBuilder reasoningBuf = new();
		StringBuilder textBuf = new();
		UsageDetails? finalUsage = null;

		await foreach (ChatResponseUpdate upd in chatClient.GetStreamingResponseAsync(messages, options)) {
			foreach (AIContent c in upd.Contents) {
				if (c is TextReasoningContent rc) {
					reasoningCount++;
					reasoningBuf.Append(rc.Text);
				} else if (c is Microsoft.Extensions.AI.TextContent tc) {
					textBuf.Append(tc.Text);
				} else if (c is UsageContent uc2) {
					finalUsage = uc2.Details;
				}
			}
		}

		this._out.WriteLine("==== REQUEST BODY (AdditionalProperties) ====");
		this._out.WriteLine(capture.LastRequestBody ?? "(null)");
		this._out.WriteLine($"reasoningChunks={reasoningCount}");
		this._out.WriteLine($"reasoning(first 300)='{Truncate(reasoningBuf.ToString(), 300)}'");
		this._out.WriteLine($"text(first 300)='{Truncate(textBuf.ToString(), 300)}'");
		if (finalUsage is not null) {
			this._out.WriteLine($"InputTokenCount={finalUsage.InputTokenCount} OutputTokenCount={finalUsage.OutputTokenCount} ReasoningTokenCount={finalUsage.ReasoningTokenCount}");
		}
	}

	[Fact]
	public async Task Thinking_Streaming_DirectIChatClient_WithRawRepresentationFactory() {
		string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
		if (string.IsNullOrEmpty(apiKey)) { this._out.WriteLine("SKIP: no API key"); return; }

		bool useBearer = apiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);
		CapturingHandler capture = new() { InnerHandler = new HttpClientHandler() };
		HttpMessageHandler chain = capture;
		if (useBearer) chain = new OAuthSystemPromptHandler { InnerHandler = chain };
		HttpClient http = useBearer
			? new HttpClient(new BearerHandler(apiKey) { InnerHandler = chain })
			: new HttpClient(chain);

		string model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";
		AnthropicClient anthropic = new(apiKey, http);
		IChatClient chatClient = anthropic.Messages;

		// Reflect over the Anthropic SDK to find the right raw shape
		this._out.WriteLine("Looking for thinking-related types in Anthropic.SDK:");
		System.Reflection.Assembly sdk = typeof(AnthropicClient).Assembly;
		foreach (Type t in sdk.GetTypes()) {
			if (t.Name.Contains("Think", StringComparison.OrdinalIgnoreCase) ||
			    t.Name.Contains("Reasoning", StringComparison.OrdinalIgnoreCase))
				this._out.WriteLine($"  {t.FullName}");
		}

		// Also probe what MessageParameters / request object looks like
		Type? msgParamsType = sdk.GetType("Anthropic.SDK.Messaging.MessageParameters");
		if (msgParamsType is not null) {
			this._out.WriteLine($"MessageParameters props:");
			foreach (System.Reflection.PropertyInfo p in msgParamsType.GetProperties())
				this._out.WriteLine($"  {p.Name}: {p.PropertyType.Name}");
		}

		foreach (string name in new[] { "ThinkingParameters", "ThinkingType", "ThinkingEffort", "OutputConfig" }) {
			Type? t2 = sdk.GetType($"Anthropic.SDK.Messaging.{name}");
			if (t2 is null) continue;
			this._out.WriteLine($"--- {name} ({(t2.IsEnum ? "enum" : "class")}) ---");
			if (t2.IsEnum) {
				foreach (string n in Enum.GetNames(t2)) this._out.WriteLine($"  {n}");
			} else {
				foreach (System.Reflection.PropertyInfo p in t2.GetProperties())
					this._out.WriteLine($"  {p.Name}: {p.PropertyType.Name}");
				foreach (System.Reflection.FieldInfo f in t2.GetFields())
					this._out.WriteLine($"  field {f.Name}: {f.FieldType.Name}");
			}
		}
	}

	// Mirrors the production ThinkingInjectionHandler in AnthropicProvider — reads from the
	// shared ThinkingContext.Pending AsyncLocal so the test exercises the real config flow.
	private sealed class ProdLikeThinkingInjectionHandler : DelegatingHandler {
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
			if (request.Content is null || request.RequestUri?.AbsolutePath.Contains("/messages") != true)
				return await base.SendAsync(request, ct);

			System.Text.Json.Nodes.JsonNode? pending = ThinkingContext.Pending.Value;
			if (pending is null) return await base.SendAsync(request, ct);

			string json = await request.Content.ReadAsStringAsync(ct);
			System.Text.Json.Nodes.JsonNode? root = System.Text.Json.Nodes.JsonNode.Parse(json);
			if (root is System.Text.Json.Nodes.JsonObject obj && obj["thinking"] is null)
				obj["thinking"] = pending.DeepClone();

			if (root is not null) {
				string modified = root.ToJsonString();
				request.Content = new StringContent(modified, Encoding.UTF8, "application/json");
			}
			return await base.SendAsync(request, ct);
		}
	}

	[Fact]
	public async Task Thinking_Streaming_InterceptHandler_Budget() {
		string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
		if (string.IsNullOrEmpty(apiKey)) { this._out.WriteLine("SKIP: no API key"); return; }

		bool useBearer = apiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);
		CapturingHandler capture = new() { InnerHandler = new HttpClientHandler() };
		HttpMessageHandler chain = capture;
		chain = new ProdLikeThinkingInjectionHandler { InnerHandler = chain };
		if (useBearer) chain = new OAuthSystemPromptHandler { InnerHandler = chain };
		HttpClient http = useBearer
			? new HttpClient(new BearerHandler(apiKey) { InnerHandler = chain })
			: new HttpClient(chain);

		string model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";
		AnthropicClient anthropic = new(apiKey, http);
		IChatClient chatClient = anthropic.Messages;
		IChatCompletionService completion = chatClient.AsChatCompletionService();

		ThinkingContext.Pending.Value = new System.Text.Json.Nodes.JsonObject {
			["type"]          = "enabled",
			["budget_tokens"] = 4096,
			["display"]       = "summarized"
		};

		ChatHistory history = new();
		history.AddUserMessage("Think step by step about whether 19 is prime, then answer in one sentence.");

		PromptExecutionSettings settings = new() {
			ModelId = model,
			ExtensionData = new Dictionary<string, object> { ["max_tokens"] = 8192 }
		};

		int reasoningCount = 0;
		StringBuilder reasoningBuf = new();
		StringBuilder textBuf = new();
		UsageDetails? finalUsage = null;
		HashSet<string> innerTypes = new();
		HashSet<string> itemTypes = new();

		int idx = 0;
		await foreach (StreamingChatMessageContent chunk in completion.GetStreamingChatMessageContentsAsync(history, settings)) {
			idx++;
			if (chunk.InnerContent is not null) innerTypes.Add(chunk.InnerContent.GetType().Name);
			foreach (StreamingKernelContent item in chunk.Items) {
				itemTypes.Add($"{item.GetType().Name}/{item.InnerContent?.GetType().Name ?? "(null)"}");
			}

			string? reasoningText = null;
			if (chunk.InnerContent is TextReasoningContent rc) reasoningText = rc.Text;
			else foreach (StreamingKernelContent item in chunk.Items)
				if (item.InnerContent is TextReasoningContent rc2) { reasoningText = rc2.Text; break; }

			// Probe chunk.InnerContent for reasoning via reflection — it's the raw MessageResponse
			if (reasoningText is null && chunk.InnerContent is { } inner) {
				reasoningText = this.ExtractReasoningFromInner(inner);
			}

			if (reasoningText is not null) { reasoningCount++; reasoningBuf.Append(reasoningText); }
			if (!string.IsNullOrEmpty(chunk.Content)) textBuf.Append(chunk.Content);

			if (chunk.Metadata?.TryGetValue("Usage", out object? u) == true && u is UsageContent uc)
				finalUsage = uc.Details;

			if (idx <= 12) {
				string innerSummary = "";
				if (chunk.InnerContent is not null) {
					object? typeP = chunk.InnerContent.GetType().GetProperty("Type")?.GetValue(chunk.InnerContent);
					object? delta = chunk.InnerContent.GetType().GetProperty("Delta")?.GetValue(chunk.InnerContent);
					object? block = chunk.InnerContent.GetType().GetProperty("ContentBlock")?.GetValue(chunk.InnerContent);
					innerSummary = $"Type={typeP} Delta=({delta?.GetType().Name ?? "null"}) ContentBlock=({block?.GetType().Name ?? "null"})";
					if (delta is not null) {
						foreach (System.Reflection.PropertyInfo p in delta.GetType().GetProperties()) {
							object? v;
							try { v = p.GetValue(delta); } catch { continue; }
							if (v is null) continue;
							innerSummary += $" δ.{p.Name}={Truncate(v.ToString(), 60)}";
						}
					}
					if (block is not null) {
						foreach (System.Reflection.PropertyInfo p in block.GetType().GetProperties()) {
							object? v;
							try { v = p.GetValue(block); } catch { continue; }
							if (v is null) continue;
							innerSummary += $" b.{p.Name}={Truncate(v.ToString(), 60)}";
						}
					}
				}
				this._out.WriteLine($"#{idx} {innerSummary} text='{Truncate(chunk.Content, 60)}'");
			}
		}

		this._out.WriteLine($"distinct inner types: {string.Join(",", innerTypes)}");
		this._out.WriteLine($"distinct item types: {string.Join(",", itemTypes)}");

		this._out.WriteLine("==== REQUEST BODY (intercept handler, budget) ====");
		this._out.WriteLine(capture.LastRequestBody ?? "(null)");
		this._out.WriteLine($"reasoningChunks={reasoningCount}");
		this._out.WriteLine($"reasoning(first 500)='{Truncate(reasoningBuf.ToString(), 500)}'");
		this._out.WriteLine($"text(first 300)='{Truncate(textBuf.ToString(), 300)}'");
		if (finalUsage is not null) {
			this._out.WriteLine($"Input={finalUsage.InputTokenCount} Output={finalUsage.OutputTokenCount} Reasoning={finalUsage.ReasoningTokenCount}");
			if (finalUsage.AdditionalCounts is not null)
				foreach (KeyValuePair<string, long> kv in finalUsage.AdditionalCounts)
					this._out.WriteLine($"  AdditionalCount {kv.Key}={kv.Value}");
		}

		Assert.True(reasoningCount > 0, "should have received reasoning content");
		Assert.True(capture.LastRequestBody!.Contains("\"thinking\""), "request body should contain thinking field");
	}

	[Fact]
	public async Task Thinking_Streaming_DirectIChatClient_WithRawRepresentationFactory_Typed() {
		string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
		if (string.IsNullOrEmpty(apiKey)) { this._out.WriteLine("SKIP: no API key"); return; }

		bool useBearer = apiKey.StartsWith("sk-ant-oat", StringComparison.Ordinal);
		CapturingHandler capture = new() { InnerHandler = new HttpClientHandler() };
		HttpMessageHandler chain = capture;
		if (useBearer) chain = new OAuthSystemPromptHandler { InnerHandler = chain };
		HttpClient http = useBearer
			? new HttpClient(new BearerHandler(apiKey) { InnerHandler = chain })
			: new HttpClient(chain);

		string model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-sonnet-4-6";
		AnthropicClient anthropic = new(apiKey, http);
		IChatClient chatClient = anthropic.Messages;

		System.Reflection.Assembly sdk = typeof(AnthropicClient).Assembly;
		Type? msgParamsT  = sdk.GetType("Anthropic.SDK.Messaging.MessageParameters")!;
		Type? thinkParamT = sdk.GetType("Anthropic.SDK.Messaging.ThinkingParameters")!;
		Type? thinkTypeT  = sdk.GetType("Anthropic.SDK.Messaging.ThinkingType")!;

		ChatOptions options = new() {
			ModelId = model,
			MaxOutputTokens = 8192,
			RawRepresentationFactory = _ => {
				object mp = Activator.CreateInstance(msgParamsT)!;
				object tp = Activator.CreateInstance(thinkParamT)!;
				// Default to "Enabled" — first value of ThinkingType enum
				object enabledVal = Enum.Parse(thinkTypeT, "enabled");
				thinkParamT.GetProperty("Type")?.SetValue(tp, enabledVal);
				thinkParamT.GetProperty("BudgetTokens")?.SetValue(tp, 4096);
				// Try set Display (string) if property exists
				System.Reflection.PropertyInfo? displayP = thinkParamT.GetProperty("Display");
				if (displayP is not null) {
					object? dispVal = displayP.PropertyType.IsEnum
						? Enum.Parse(displayP.PropertyType, "summarized", ignoreCase: true)
						: (object)"summarized";
					displayP.SetValue(tp, dispVal);
				}
				msgParamsT.GetProperty("Thinking")?.SetValue(mp, tp);
				return mp;
			}
		};

		List<ChatMessage> messages = new() {
			new(ChatRole.User, "Think step by step about whether 17 is prime, then answer in one sentence.")
		};

		int reasoningCount = 0;
		StringBuilder reasoningBuf = new();
		StringBuilder textBuf = new();
		UsageDetails? finalUsage = null;
		List<string> seenContentTypes = new();

		await foreach (ChatResponseUpdate upd in chatClient.GetStreamingResponseAsync(messages, options)) {
			foreach (AIContent c in upd.Contents) {
				seenContentTypes.Add(c.GetType().Name);
				if (c is TextReasoningContent rc) {
					reasoningCount++;
					reasoningBuf.Append(rc.Text);
				} else if (c is Microsoft.Extensions.AI.TextContent tc) {
					textBuf.Append(tc.Text);
				} else if (c is UsageContent uc2) {
					finalUsage = uc2.Details;
				}
			}
		}

		this._out.WriteLine("==== REQUEST BODY (RawRepresentationFactory + typed ThinkingParameters) ====");
		this._out.WriteLine(capture.LastRequestBody ?? "(null)");
		this._out.WriteLine($"distinct content types: {string.Join(",", seenContentTypes.Distinct())}");
		this._out.WriteLine($"reasoningChunks={reasoningCount}");
		this._out.WriteLine($"reasoning(first 500)='{Truncate(reasoningBuf.ToString(), 500)}'");
		this._out.WriteLine($"text(first 300)='{Truncate(textBuf.ToString(), 300)}'");
		if (finalUsage is not null) {
			this._out.WriteLine($"InputTokenCount={finalUsage.InputTokenCount} OutputTokenCount={finalUsage.OutputTokenCount} ReasoningTokenCount={finalUsage.ReasoningTokenCount}");
			if (finalUsage.AdditionalCounts is not null)
				foreach (KeyValuePair<string, long> kv in finalUsage.AdditionalCounts)
					this._out.WriteLine($"  AdditionalCount {kv.Key}={kv.Value}");
		}
	}

	private void ReflectObject(object? obj, string path, int maxDepth) {
		if (obj is null || maxDepth <= 0) return;
		Type t = obj.GetType();
		foreach (System.Reflection.PropertyInfo p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
			if (p.GetIndexParameters().Length > 0) continue;
			object? v;
			try { v = p.GetValue(obj); } catch { continue; }
			string vs = v is null ? "null" : Truncate(v.ToString(), 150) ?? "";
			this._out.WriteLine($"  {path}.{p.Name}: {p.PropertyType.Name} = {vs}");

			// Recurse into non-primitive, non-string, non-enum properties when interesting
			if (v is not null && v is not string && !p.PropertyType.IsPrimitive && !p.PropertyType.IsEnum
			    && p.PropertyType.Namespace?.StartsWith("System") != true
			    && maxDepth > 1)
				this.ReflectObject(v, $"{path}.{p.Name}", maxDepth - 1);
		}
	}

	private string? ExtractReasoningFromInner(object inner) {
		// Streaming path: chunk.InnerContent is Anthropic SDK MessageResponse (per-event).
		// thinking_delta events carry Delta.Type="thinking_delta" and Delta.Thinking=<text>.
		object? delta = inner.GetType().GetProperty("Delta")?.GetValue(inner);
		if (delta is null) return null;
		string? type = delta.GetType().GetProperty("Type")?.GetValue(delta) as string;
		if (type != "thinking_delta") return null;
		return delta.GetType().GetProperty("Thinking")?.GetValue(delta) as string;
	}

	private static string? Truncate(string? s, int n) {
		if (s is null) return null;
		s = s.Replace("\n", "\\n");
		return s.Length <= n ? s : s[..n] + "…";
	}
}
