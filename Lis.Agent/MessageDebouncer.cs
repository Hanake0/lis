using System.Collections.Concurrent;
using System.Diagnostics;

using Lis.Agent.Commands;
using Lis.Core.Channel;
using Lis.Core.Configuration;
using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lis.Agent;

public sealed class MessageDebouncer(
	IServiceScopeFactory      scopeFactory,
	CommandRouter             commandRouter,
	AgentService              agentService,
	IOptions<LisOptions>      lisOptions,
	ILogger<MessageDebouncer> logger) : IConversationService, IDisposable {

	private readonly ConcurrentDictionary<string, ChatState> _chats = new();

	private ChatState GetChatState(string chatId) =>
		this._chats.GetOrAdd(chatId, _ => new ChatState());

	private static IChannelClient GetChannelClient(IServiceScope scope) =>
		scope.ServiceProvider.GetRequiredService<IChannelClient>();

	[Trace("MessageDebouncer > HandleIncomingAsync")]
	public async Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) {
		Activity.Current?.SetTag("message.id", message.ExternalId);
		Activity.Current?.SetTag("chat.id",    message.ChatId);

		ChatState state    = this.GetChatState(message.ChatId);
		bool      isQueued = state.IsResponding;

		// AI is responding: ingest with full auth, queue only if authorized
		if (isQueued) {
			bool authorized;
			using (IServiceScope scope = scopeFactory.CreateScope()) {
				ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
				(_, authorized) = await svc.IngestMessageAsync(message, queued: true, ct);
			}

			// Auth failed — un-queue so flush won't trigger a response for this message
			if (!authorized) {
				using IServiceScope scope = scopeFactory.CreateScope();
				LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();
				await db.Messages.Where(m => m.Id == message.DbId)
					.ExecuteUpdateAsync(s => s.SetProperty(p => p.Queued, false), ct);
				return;
			}

			// React clock — user sees feedback that message is queued for response
			if (lisOptions.Value.ReactOnMessageQueued) {
				try {
					lock (state.Lock) { state.ReactedIds.Add(message.ExternalId); }
					using IServiceScope reactScope = scopeFactory.CreateScope();
					await GetChannelClient(reactScope).ReactAsync(
						message.ExternalId, message.ChatId, lisOptions.Value.ReactOnMessageQueuedEmoji, CancellationToken.None);
				} catch { /* best effort */ }
			}

			// /abort cancels the active AI response
			if (message.Body?.Trim() is "/abort" or "/stop" or "/cancel") {
				if (state.ActiveCts is { } activeCts) await activeCts.CancelAsync();
				CancelPendingDebounce(state);
			}
			return;
		}

		// Normal path: ingest first, then decide
		ChatEntity chat;
		bool shouldRespond;
		using (IServiceScope scope = scopeFactory.CreateScope()) {
			ConversationService svc = scope.ServiceProvider.GetRequiredService<ConversationService>();
			(chat, shouldRespond) = await svc.IngestMessageAsync(message, queued: false, ct);
		}
		if (!shouldRespond) return;

		// No AI running — handle normally
		// Commands: execute immediately
		if (commandRouter.Match(message.Body) is not null) {
			await this.ExecuteCommandAsync(message);
			return;
		}

		// Normal messages: debounce then AI response (per-chat override → global default)
		int debounceMs = chat.DebounceMs ?? lisOptions.Value.MessageDebounceMs;
		if (debounceMs <= 0) {
			await this.RespondInScopeAsync(message);
			return;
		}

		this.ScheduleDebounce(message.ChatId, message, debounceMs);
	}

	[Trace("MessageDebouncer > HandleSentEchoAsync")]
	public async Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		ConversationService svc   = scope.ServiceProvider.GetRequiredService<ConversationService>();
		await svc.HandleSentEchoAsync(echo, ct);
	}

	[Trace("MessageDebouncer > HandleReactionAsync")]
	public async Task HandleReactionAsync(string messageId, string chatId, string emoji, string senderId, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		ConversationService svc   = scope.ServiceProvider.GetRequiredService<ConversationService>();
		await svc.HandleReactionAsync(messageId, chatId, emoji, senderId, ct);
	}

	[Trace("MessageDebouncer > HandleTypingAsync")]
	public Task HandleTypingAsync(string chatId, CancellationToken ct) {
		int debounceMs = lisOptions.Value.MessageDebounceMs;
		if (debounceMs <= 0) return Task.CompletedTask;

		ChatState state = this.GetChatState(chatId);
		lock (state.Lock) {
			if (state.PendingMessage is null) return Task.CompletedTask;

			ResetDebounceTimer(state);
			this.StartTimer(chatId, state, debounceMs);
		}

		return Task.CompletedTask;
	}

	/// <summary>Flush queued messages left over from a crash and trigger AI responses.</summary>
	[Trace("MessageDebouncer > TriggerPendingResponsesAsync")]
	public async Task TriggerPendingResponsesAsync() {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		// Find chats with queued messages (leftover from crash)
		List<long> chatIds = await db.Messages
			.Where(m => m.Queued)
			.Select(m => m.ChatId)
			.Distinct()
			.ToListAsync();
		if (chatIds.Count == 0) return;

		// Flush: make queued messages visible with current timestamps
		await db.Database.ExecuteSqlRawAsync(
			"UPDATE message SET queued = false, timestamp = NOW() WHERE queued");

		// Trigger AI response for each affected chat
		foreach (long chatId in chatIds) {
			ChatEntity? chat = await db.Chats.FindAsync(chatId);
			if (chat is null) continue;

			MessageEntity? lastMsg = await db.Messages
				.Where(m => m.ChatId == chatId && m.Role == null)
				.OrderByDescending(m => m.Timestamp)
				.FirstOrDefaultAsync();
			if (lastMsg is null) continue;

			IncomingMessage synthetic = new() {
				ExternalId = lastMsg.ExternalId ?? $"recovery-{chat.ExternalId}",
				ChatId     = chat.ExternalId,
				SenderId   = lastMsg.SenderId ?? "",
				SenderName = lastMsg.SenderName,
				Timestamp  = lastMsg.Timestamp,
				Body       = lastMsg.Body
			};
			_ = Task.Run(() => this.RespondInScopeAsync(synthetic));
		}

		if (logger.IsEnabled(LogLevel.Information))
			logger.LogInformation("Triggered pending responses for {Count} chats after crash recovery", chatIds.Count);
	}

	private async Task ExecuteCommandAsync(IncomingMessage message) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			ConversationService svc   = scope.ServiceProvider.GetRequiredService<ConversationService>();
			await svc.RespondAsync(message, CancellationToken.None);
		} catch (Exception ex) {
			logger.LogError(ex, "Error executing command in {ChatId}", message.ChatId);
		}
	}

	private async Task RespondInScopeAsync(IncomingMessage message) {
		ChatState state = this.GetChatState(message.ChatId);

		await state.Gate.WaitAsync();
		state.IsResponding = true;
		try {
			// Initial AI response
			using CancellationTokenSource cts = new();
			state.ActiveCts = cts;
			try {
				using IServiceScope scope = scopeFactory.CreateScope();
				ConversationService svc   = scope.ServiceProvider.GetRequiredService<ConversationService>();
				await svc.RespondAsync(message, cts.Token);
			} catch (OperationCanceledException) {
				// Intentionally swallowed — cancellation is expected from /abort
			} catch (Exception ex) {
				logger.LogError(ex, "Error responding to {MessageId} in {ChatId}",
					message.ExternalId, message.ChatId);
			} finally {
				Interlocked.CompareExchange(ref state.ActiveCts, null, cts);
			}

			// Flush loop: process queued messages
			while (true) {
				bool needsResponse = await this.FlushQueueAsync(message.ChatId, state);
				if (!needsResponse) break;

				// New AI response for flushed messages
				using CancellationTokenSource cts2 = new();
				state.ActiveCts = cts2;
				try {
					using IServiceScope scope2 = scopeFactory.CreateScope();
					ConversationService svc2   = scope2.ServiceProvider.GetRequiredService<ConversationService>();
					await svc2.RespondAsync(message, cts2.Token);
				} catch (OperationCanceledException) {
					// Intentionally swallowed — cancellation is expected from /abort
				} catch (Exception ex) {
					logger.LogError(ex, "Error responding in {ChatId}", message.ChatId);
				} finally {
					Interlocked.CompareExchange(ref state.ActiveCts, null, cts2);
				}
			}
		} finally {
			state.IsResponding = false;
			state.Gate.Release();
		}
	}

	private async Task<bool> FlushQueueAsync(string chatId, ChatState state) {
		using IServiceScope scope   = scopeFactory.CreateScope();
		IChannelClient      channel = GetChannelClient(scope);
		LisDbContext        db      = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		// Remove clock reactions
		List<string> reacted;
		lock (state.Lock) {
			reacted = [..state.ReactedIds];
			state.ReactedIds.Clear();
		}
		foreach (string id in reacted)
			try { await channel.ReactAsync(id, chatId, ""); }
			catch (Exception ex) { logger.LogWarning(ex, "Failed to remove reaction from {MessageId}", id); }

		ChatEntity? chat = await db.Chats
			.Include(c => c.CurrentSession)
			.Include(c => c.AllowedSenders)
			.Include(c => c.Agent)
			.FirstOrDefaultAsync(c => c.ExternalId == chatId);
		if (chat?.CurrentSession is null) return false;

		AgentEntity agent = await agentService.ResolveForChatAsync(db, chat, CancellationToken.None);

		List<MessageEntity> queued = await db.Messages
			.Where(m => m.ChatId == chat.Id && m.Queued)
			.OrderBy(m => m.Id)
			.ToListAsync();
		if (queued.Count == 0) return false;

		// Flush: set queued = false, update timestamps to sort AFTER AI responses
		DateTimeOffset now = DateTimeOffset.UtcNow;
		foreach (MessageEntity msg in queued) {
			msg.Queued    = false;
			msg.Timestamp = now;
			now           = now.AddMilliseconds(1);
		}
		await db.SaveChangesAsync();

		// Execute queued commands
		SessionEntity session = chat.CurrentSession;
		foreach (MessageEntity msg in queued) {
			if (commandRouter.Match(msg.Body) is not { } match) continue;

			IncomingMessage im = new() {
				ExternalId = msg.ExternalId ?? "",
				ChatId     = chatId,
				SenderId   = msg.SenderId ?? "",
				SenderName = msg.SenderName,
				Body       = msg.Body,
				Timestamp  = msg.Timestamp
			};
			CommandContext ctx      = new(im, chat, session, db, agent, match.Args);
			string        response = await match.Command.ExecuteAsync(ctx, CancellationToken.None);
			await channel.SendMessageAsync(chatId, response, msg.ExternalId);

			db.Messages.Add(new MessageEntity {
				ChatId    = chat.Id,
				SessionId = session.Id,
				SenderId  = "me",
				IsFromMe  = true,
				Role      = "assistant",
				Body      = response,
				Timestamp = DateTimeOffset.UtcNow,
				CreatedAt = DateTimeOffset.UtcNow
			});
			await db.SaveChangesAsync();
		}

		// AI response needed only if last queued message is NOT a command
		return commandRouter.Match(queued[^1].Body) is null;
	}

	private void ScheduleDebounce(string chatId, IncomingMessage message, int debounceMs) {
		ChatState state = this.GetChatState(chatId);
		lock (state.Lock) {
			if (state.PendingMessage is not null) {
				ResetDebounceTimer(state);
				state.PendingMessage = message;
				this.StartTimer(chatId, state, debounceMs);
			} else {
				state.PendingMessage = message;
				state.DebounceCts    = new CancellationTokenSource();
				this.StartTimer(chatId, state, debounceMs);
			}
		}
	}

	private static void ResetDebounceTimer(ChatState state) {
		state.DebounceCts?.Cancel();
		state.DebounceCts?.Dispose();
		state.DebounceCts = new CancellationTokenSource();
	}

	private void StartTimer(string chatId, ChatState state, int debounceMs) {
		CancellationToken token = state.DebounceCts!.Token;

		_ = Task.Run(async () => {
			try {
				await Task.Delay(debounceMs, token);
			} catch (TaskCanceledException) {
				return;
			}

			IncomingMessage messageToRespond;
			lock (state.Lock) {
				if (state.PendingMessage is null) return;
				messageToRespond     = state.PendingMessage;
				state.PendingMessage = null;
				state.DebounceCts?.Dispose();
				state.DebounceCts = null;
			}

			await this.RespondInScopeAsync(messageToRespond);
		});
	}

	private static void CancelPendingDebounce(ChatState state) {
		lock (state.Lock) {
			state.DebounceCts?.Cancel();
			state.DebounceCts?.Dispose();
			state.DebounceCts    = null;
			state.PendingMessage = null;
		}
	}

	public void Dispose() {
		foreach (ChatState state in this._chats.Values)
			state.Dispose();

		this._chats.Clear();
	}

	private sealed class ChatState : IDisposable {
		public readonly object        Lock = new();
		public readonly SemaphoreSlim Gate = new(1, 1);
		public volatile bool          IsResponding;
		public CancellationTokenSource? ActiveCts;
		public IncomingMessage?         PendingMessage;
		public CancellationTokenSource? DebounceCts;
		public readonly List<string>  ReactedIds = [];

		public void Dispose() {
			this.DebounceCts?.Cancel();
			this.DebounceCts?.Dispose();
			this.Gate.Dispose();
		}
	}
}
