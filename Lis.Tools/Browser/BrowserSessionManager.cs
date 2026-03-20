using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Lis.Tools.Browser;

public sealed partial class BrowserSessionManager(ILogger<BrowserSessionManager> logger) : IAsyncDisposable {

	private IPlaywright? _playwright;
	private readonly ConcurrentDictionary<long, BrowserSession> _sessions = new();
	private readonly SemaphoreSlim _initLock = new(1, 1);

	public async Task<IPage> GetOrStartAsync(long agentId, string? url, bool headless, CancellationToken ct) {
		if (this._sessions.TryGetValue(agentId, out BrowserSession? existing)) {
			existing.LastActivityAt = DateTimeOffset.UtcNow;
			LogReusingSession(logger, agentId);
			return existing.Page;
		}

		await this._initLock.WaitAsync(ct);
		try {
			// Double-check after acquiring lock
			if (this._sessions.TryGetValue(agentId, out existing)) {
				existing.LastActivityAt = DateTimeOffset.UtcNow;
				return existing.Page;
			}

			if (this._playwright is null) {
				LogInitializingPlaywright(logger);
				this._playwright = await Playwright.CreateAsync();
			}

			LogLaunchingBrowser(logger, agentId, headless);
			IBrowser browser = await this._playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions {
				Headless = headless,
			});

			IBrowserContext context = await browser.NewContextAsync();
			IPage page = await context.NewPageAsync();

			if (!string.IsNullOrWhiteSpace(url)) {
				LogNavigating(logger, url, agentId);
				await page.GotoAsync(url);
			}

			BrowserSession session = new() {
				Browser = browser,
				Context = context,
				Page = page,
				LastActivityAt = DateTimeOffset.UtcNow,
			};

			this._sessions[agentId] = session;
			LogSessionStarted(logger, agentId);

			return page;
		} catch (Exception ex) {
			LogSessionStartFailed(logger, ex, agentId);
			throw;
		} finally {
			this._initLock.Release();
		}
	}

	public Task<IPage?> GetPageAsync(long agentId) {
		if (this._sessions.TryGetValue(agentId, out BrowserSession? session)) {
			session.LastActivityAt = DateTimeOffset.UtcNow;
			return Task.FromResult<IPage?>(session.Page);
		}

		return Task.FromResult<IPage?>(null);
	}

	public async Task CloseAsync(long agentId) {
		if (!this._sessions.TryRemove(agentId, out BrowserSession? session))
			return;

		LogClosingSession(logger, agentId);
		try {
			await session.DisposeAsync();
		} catch (Exception ex) {
			LogSessionDisposeError(logger, ex, agentId);
		}
	}

	public async ValueTask DisposeAsync() {
		LogDisposing(logger, this._sessions.Count);

		foreach (long agentId in this._sessions.Keys) {
			if (this._sessions.TryRemove(agentId, out BrowserSession? session)) {
				try {
					await session.DisposeAsync();
				} catch (Exception ex) {
					LogSessionDisposeError(logger, ex, agentId);
				}
			}
		}

		if (this._playwright is not null) {
			this._playwright.Dispose();
			this._playwright = null;
		}

		this._initLock.Dispose();
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "Reusing browser session for agent {AgentId}")]
	private static partial void LogReusingSession(ILogger logger, long agentId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Initializing Playwright runtime")]
	private static partial void LogInitializingPlaywright(ILogger logger);

	[LoggerMessage(Level = LogLevel.Information, Message = "Launching browser for agent {AgentId} (headless={Headless})")]
	private static partial void LogLaunchingBrowser(ILogger logger, long agentId, bool headless);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Navigating to {Url} for agent {AgentId}")]
	private static partial void LogNavigating(ILogger logger, string url, long agentId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Browser session started for agent {AgentId}")]
	private static partial void LogSessionStarted(ILogger logger, long agentId);

	[LoggerMessage(Level = LogLevel.Error, Message = "Failed to start browser session for agent {AgentId}")]
	private static partial void LogSessionStartFailed(ILogger logger, Exception ex, long agentId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Closing browser session for agent {AgentId}")]
	private static partial void LogClosingSession(ILogger logger, long agentId);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Error disposing browser session for agent {AgentId}")]
	private static partial void LogSessionDisposeError(ILogger logger, Exception ex, long agentId);

	[LoggerMessage(Level = LogLevel.Information, Message = "Disposing BrowserSessionManager, closing {Count} session(s)")]
	private static partial void LogDisposing(ILogger logger, int count);
}

internal sealed class BrowserSession : IAsyncDisposable {

	public required IBrowser Browser { get; init; }
	public required IBrowserContext Context { get; init; }
	public required IPage Page { get; set; }
	public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

	public async ValueTask DisposeAsync() {
		try { await this.Context.CloseAsync(); } catch { }
		try { await this.Browser.CloseAsync(); } catch { }
	}
}
