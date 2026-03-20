using System.ComponentModel;
using System.Text;
using System.Text.Json;

using Lis.Core.Util;
using Lis.Tools.Browser;

using Microsoft.Playwright;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class BrowserPlugin(BrowserSessionManager sessionManager) {

	[KernelFunction("start")]
	[Description("Launch a headless Chrome browser. Optionally navigate to a URL immediately.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> StartAsync(
		[Description("URL to navigate to after launch (optional)")] string? url = null,
		[Description("Run in headless mode (default: true)")] bool headless = true,
		CancellationToken ct = default) {
		await ToolContext.NotifyAsync("🌐 Starting browser", ct);

		try {
			long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
			await sessionManager.GetOrStartAsync(agentId, url, headless, ct);

			return string.IsNullOrWhiteSpace(url)
				? "Browser started."
				: $"Browser started at {url}";
		} catch (Exception ex) {
			return $"Error starting browser: {ex.Message}";
		}
	}

	[KernelFunction("navigate")]
	[Description("Navigate the browser to a URL.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> NavigateAsync(
		[Description("URL to navigate to")] string url,
		[Description("Wait condition: load, domcontentloaded, or networkidle")] string? waitUntil = null) {
		try {
			IPage page = await this.GetPageOrThrowAsync();

			await page.GotoAsync(url, new PageGotoOptions {
				WaitUntil = ParseWaitUntil(waitUntil),
			});

			string title = await page.TitleAsync();
			return $"Navigated to {url} (title: {title})";
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Navigation error: {ex.Message}";
		}
	}

	[KernelFunction("snapshot")]
	[Description("Get the current page content as accessible plain text.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> SnapshotAsync(
		[Description("Maximum character length of the returned text")] int maxLength = 15000) {
		try {
			IPage page = await this.GetPageOrThrowAsync();

			string text = await page.InnerTextAsync("body");

			if (text.Length > maxLength)
				text = text[..maxLength] + " [...truncated]";

			return text;
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Snapshot error: {ex.Message}";
		}
	}

	[KernelFunction("screenshot")]
	[Description("Take a screenshot of the current page.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> ScreenshotAsync(
		[Description("Capture the full scrollable page (default: false)")] bool fullPage = false) {
		try {
			IPage page = await this.GetPageOrThrowAsync();

			byte[] bytes = await page.ScreenshotAsync(new PageScreenshotOptions {
				FullPage = fullPage,
				Type = ScreenshotType.Png,
			});

			return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Screenshot error: {ex.Message}";
		}
	}

	[KernelFunction("click")]
	[Description("Click an element on the page by CSS selector.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> ClickAsync(
		[Description("CSS selector of the element to click")] string selector) {
		try {
			IPage page = await this.GetPageOrThrowAsync();

			await page.ClickAsync(selector);

			return $"Clicked: {selector}";
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Click error: {ex.Message}";
		}
	}

	[KernelFunction("type")]
	[Description("Type text into a form field identified by CSS selector.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> TypeAsync(
		[Description("CSS selector of the input element")] string selector,
		[Description("Text to type into the element")] string text) {
		try {
			IPage page = await this.GetPageOrThrowAsync();

			await page.FillAsync(selector, text);

			return $"Typed into: {selector}";
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Type error: {ex.Message}";
		}
	}

	[KernelFunction("evaluate")]
	[Description("Execute JavaScript in the browser and return the result.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> EvaluateAsync(
		[Description("JavaScript code to execute")] string script) {
		await ToolContext.NotifyAsync("🔧 Evaluating JS");

		try {
			IPage page = await this.GetPageOrThrowAsync();

			JsonElement result = await page.EvaluateAsync<JsonElement>(script);

			return JsonSerializer.Serialize(result);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Evaluate error: {ex.Message}";
		}
	}

	[KernelFunction("tabs")]
	[Description("List all open browser tabs with their titles and URLs.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> TabsAsync() {
		try {
			IPage page = await this.GetPageOrThrowAsync();

			IReadOnlyList<IPage> pages = page.Context.Pages;
			StringBuilder sb = new();

			foreach (IPage p in pages) {
				string title = await p.TitleAsync();
				sb.AppendLine($"- {title} ({p.Url})");
			}

			string output = sb.ToString().TrimEnd();
			return output.Length > 0 ? output : "No tabs open.";
		} catch (InvalidOperationException ex) {
			return ex.Message;
		} catch (Exception ex) {
			return $"Tabs error: {ex.Message}";
		}
	}

	[KernelFunction("close")]
	[Description("Close the browser and end the session.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> CloseAsync() {
		try {
			long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
			await sessionManager.CloseAsync(agentId);

			return "Browser closed.";
		} catch (Exception ex) {
			return $"Error closing browser: {ex.Message}";
		}
	}

	private async Task<IPage> GetPageOrThrowAsync() {
		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		return await sessionManager.GetPageAsync(agentId)
			?? throw new InvalidOperationException("No browser session. Call browser_start first.");
	}

	private static WaitUntilState? ParseWaitUntil(string? waitUntil) {
		return waitUntil?.ToLowerInvariant() switch {
			"load" => WaitUntilState.Load,
			"domcontentloaded" => WaitUntilState.DOMContentLoaded,
			"networkidle" => WaitUntilState.NetworkIdle,
			_ => null,
		};
	}
}
