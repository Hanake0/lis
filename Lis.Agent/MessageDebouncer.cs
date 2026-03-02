using System.Collections.Concurrent;
using System.Diagnostics;

using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lis.Agent;

public sealed class MessageDebouncer(
	IServiceScopeFactory      scopeFactory,
	IOptions<LisOptions>      lisOptions,
	ILogger<MessageDebouncer> logger) : IConversationService, IDisposable {
	private readonly ConcurrentDictionary<string, object> _locks = new();

	private readonly ConcurrentDictionary<string, DebounceEntry> _pending = new();

	[Trace("MessageDebouncer > HandleIncomingAsync")]
	public async Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id",    message.ChatId);

		bool shouldRespond;
		using (IServiceScope scope = scopeFactory.CreateScope()) {
			ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
			(_, shouldRespond) = await svc.IngestMessageAsync(message, ct);
		}

		if (!shouldRespond) return;

		int debounceMs = lisOptions.Value.MessageDebounceMs;

		if (debounceMs <= 0) {
			await this.RespondInScopeAsync(message);
			return;
		}

		this.ScheduleDebounce(message.ChatId, message, debounceMs);
	}

	[Trace("MessageDebouncer > HandleTypingAsync")]
	public Task HandleTypingAsync(string chatId, CancellationToken ct) {
		int debounceMs = lisOptions.Value.MessageDebounceMs;
		if (debounceMs <= 0) return Task.CompletedTask;

		object chatLock = this._locks.GetOrAdd(chatId, _ => new object());
		lock (chatLock) {
			if (!this._pending.TryGetValue(chatId, out DebounceEntry? entry)) return Task.CompletedTask;

			ResetEntry(entry);
			this.StartTimer(chatId, entry, debounceMs);
		}

		return Task.CompletedTask;
	}

	private void ScheduleDebounce(string chatId, IncomingMessage message, int debounceMs) {
		object chatLock = this._locks.GetOrAdd(chatId, _ => new object());
		lock (chatLock)
			if (this._pending.TryGetValue(chatId, out DebounceEntry? existing)) {
				ResetEntry(existing);
				existing.LatestMessage = message;
				this.StartTimer(chatId, existing, debounceMs);
			} else {
				DebounceEntry entry = new() { LatestMessage = message, Cts = new CancellationTokenSource() };
				this._pending[chatId] = entry;
				this.StartTimer(chatId, entry, debounceMs);
			}
	}

	private static void ResetEntry(DebounceEntry entry) {
		entry.Cts?.Cancel();
		entry.Cts?.Dispose();
		entry.Cts = new CancellationTokenSource();
	}

	private void StartTimer(string chatId, DebounceEntry entry, int debounceMs) {
		CancellationToken token = entry.Cts!.Token;

		_ = Task.Run(async () => {
			try {
				await Task.Delay(debounceMs, token);
			} catch (TaskCanceledException) {
				return;
			}

			IncomingMessage messageToRespond;
			object          chatLock = this._locks.GetOrAdd(chatId, _ => new object());
			lock (chatLock) {
				if (!this._pending.TryRemove(chatId, out DebounceEntry? removed)) return;
				messageToRespond = removed.LatestMessage;
				removed.Dispose();
			}

			await this.RespondInScopeAsync(messageToRespond);
		});
	}

	private async Task RespondInScopeAsync(IncomingMessage message) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			ConversationService svc   = scope.ServiceProvider.GetRequiredService<ConversationService>();
			await svc.RespondAsync(message, CancellationToken.None);
		} catch (Exception ex) {
			logger.LogError(ex, "Error responding to message {MessageId} in chat {ChatId}",
							message.ExternalId, message.ChatId);
		}
	}

	public void Dispose() {
		foreach (KeyValuePair<string, DebounceEntry> kvp in this._pending)
			kvp.Value.Dispose();

		this._pending.Clear();
	}

	private sealed class DebounceEntry : IDisposable {
		public IncomingMessage          LatestMessage { get; set; } = null!;
		public CancellationTokenSource? Cts           { get; set; }

		public void Dispose() {
			this.Cts?.Cancel();
			this.Cts?.Dispose();
		}
	}
}
