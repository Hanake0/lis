using System.ComponentModel;
using System.Diagnostics;

using Lis.Agent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Lis.Tests.Harness;

/// <summary>
/// Options to configure a harness simulation run.
/// </summary>
public sealed class HarnessOptions
{
	/// <summary>System prompt prepended to the chat history.</summary>
	public string SystemPrompt { get; set; } = "You are a helpful assistant.";

	/// <summary>Maximum tool call iterations before stopping.</summary>
	public int MaxIterations { get; set; } = 5;
}

/// <summary>
/// Core test runner that simulates AI conversations using a <see cref="MockChatCompletionService"/>.
/// Creates a minimal Semantic Kernel, sends user messages, captures responses and tool calls.
/// </summary>
public sealed class LlmTestHarness
{
	private readonly MockChatCompletionService _mockService;
	private readonly Kernel _kernel;

	public LlmTestHarness(MockChatCompletionService mockService, Action<IKernelBuilder>? configureKernel = null)
	{
		this._mockService = mockService;

		IKernelBuilder builder = Kernel.CreateBuilder();
		builder.Services.AddSingleton<IChatCompletionService>(mockService);
		configureKernel?.Invoke(builder);
		this._kernel = builder.Build();
	}

	/// <summary>
	/// Simulate sending a user message and capture the AI response.
	/// </summary>
	public Task<HarnessResult> SimulateMessageAsync(string userMessage) =>
		this.SimulateMessageAsync(userMessage, _ => { });

	/// <summary>
	/// Simulate sending a user message with custom options.
	/// </summary>
	public async Task<HarnessResult> SimulateMessageAsync(string userMessage, Action<HarnessOptions> configure)
	{
		HarnessOptions options = new();
		configure(options);

		Stopwatch sw = Stopwatch.StartNew();

		ChatHistory history = [];
		history.AddSystemMessage(options.SystemPrompt);
		history.AddUserMessage(userMessage);

		List<HarnessToolCall> allToolCalls = [];
		string finalResponse = string.Empty;

		for (int i = 0; i < options.MaxIterations; i++)
		{
			IReadOnlyList<ChatMessageContent> results =
				await this._mockService.GetChatMessageContentsAsync(history, kernel: this._kernel);

			foreach (ChatMessageContent msg in results)
			{
				history.Add(msg);

				// Check for tool calls
				List<FunctionCallContent> functionCalls = msg.Items
					.OfType<FunctionCallContent>()
					.ToList();

				if (functionCalls.Count > 0)
				{
					foreach (FunctionCallContent call in functionCalls)
					{
						Dictionary<string, string> args = [];
						if (call.Arguments is not null)
						{
							foreach (KeyValuePair<string, object?> kvp in call.Arguments)
								args[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
						}

						allToolCalls.Add(new HarnessToolCall(
							call.PluginName ?? string.Empty,
							call.FunctionName,
							args));

						// Try to invoke the function if it exists in the kernel
						string toolResult;
						try
						{
							FunctionResultContent result = await call.InvokeAsync(this._kernel);
							toolResult = result.Result?.ToString() ?? string.Empty;
						}
						catch
						{
							toolResult = "Tool execution simulated.";
						}

						ChatMessageContent toolMessage = new(AuthorRole.Tool, toolResult);
						toolMessage.Items.Add(new FunctionResultContent(call, toolResult));
						history.Add(toolMessage);
					}

					// Continue loop to get next response after tool results
					continue;
				}

				// Plain text response — we're done
				if (msg.Content is not null)
					finalResponse = msg.Content;
			}

			// If last message was a plain text assistant response, stop
			if (history.Count > 0 && history[^1].Role == AuthorRole.Assistant
				&& !history[^1].Items.OfType<FunctionCallContent>().Any())
				break;
		}

		sw.Stop();

		return new HarnessResult
		{
			Response = finalResponse,
			ToolCalls = allToolCalls,
			OutputTokens = TokenEstimator.Count(finalResponse),
			Duration = sw.Elapsed,
			History = history
		};
	}
}

/// <summary>
/// A simple kernel function for testing — echoes back the input.
/// </summary>
public sealed class EchoPlugin
{
	[KernelFunction("echo")]
	[Description("Echoes the input back.")]
	public string Echo([Description("Text to echo")] string input) => $"Echo: {input}";
}
