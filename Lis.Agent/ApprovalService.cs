using System.Collections.Concurrent;
using System.Security.Cryptography;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lis.Agent;

public enum ApprovalDecision { Once, Always, Deny, Timeout }

public sealed record ApprovalRequest(string Command, string? Cwd, long ChatId, long? AgentId, int TimeoutSeconds = 120);

public sealed record ApprovalResult(ApprovalDecision Decision, string? ResolvedBy = null);

public interface IApprovalService {
	/// <summary>
	/// Requests approval for a command. Checks allowlist first, then sends a WhatsApp
	/// notification and waits for the owner to approve/deny (or timeout).
	/// </summary>
	Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct);

	/// <summary>
	/// Resolves a pending approval by its short ID (e.g. from /approve command or reaction).
	/// </summary>
	Task<bool> ResolveAsync(string approvalId, ApprovalDecision decision, string senderJid);

	/// <summary>
	/// Resolves a pending approval by the WhatsApp message ID that the approval notification
	/// was sent as (for reaction-based approval).
	/// </summary>
	Task<bool> ResolveByMessageAsync(string messageExternalId, ApprovalDecision decision, string senderJid);
}

public sealed class ApprovalService(
	IServiceScopeFactory          scopeFactory,
	ILogger<ApprovalService> logger) : IApprovalService {

	private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalResult>> pending = new();

	[Trace("ApprovalService > RequestApprovalAsync")]
	public async Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request, CancellationToken ct) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		// Check allowlist first
		if (await this.MatchesAllowlistAsync(db, request)) {
			if (logger.IsEnabled(LogLevel.Information))
			logger.LogInformation("Command '{Command}' matched allowlist for agent {AgentId}", request.Command, request.AgentId);
			return new ApprovalResult(ApprovalDecision.Once);
		}

		// Generate short approval ID
		string approvalId = GenerateShortId();

		// Persist to DB
		ExecApprovalEntity entity = new() {
			ApprovalId = approvalId,
			ChatId     = request.ChatId,
			AgentId    = request.AgentId,
			Command    = request.Command,
			Cwd        = request.Cwd,
			Status     = "pending",
			CreatedAt  = DateTimeOffset.UtcNow,
			ExpiresAt  = DateTimeOffset.UtcNow.AddSeconds(request.TimeoutSeconds)
		};
		db.ExecApprovals.Add(entity);
		await db.SaveChangesAsync(ct);

		// Create TCS for async waiting
		TaskCompletionSource<ApprovalResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		this.pending[approvalId] = tcs;

		// Send notification via WhatsApp
		string? messageExternalId = null;
		if (ToolContext.Channel is not null && ToolContext.ChatId is not null) {
			string cwdLine = request.Cwd is not null ? $"\nCWD: {request.Cwd}" : "";
			string notification =
				$"🔒 *Exec approval required*\n" +
				$"ID: `{approvalId}`\n" +
				$"Command: `{request.Command}`{cwdLine}\n" +
				$"Expires in: {request.TimeoutSeconds}s\n\n" +
				$"Reply: `/approve {approvalId}`\n" +
				$"Or react: 👍 once | ✅ always | ❌ deny";

			messageExternalId = await ToolContext.Channel.SendMessageAsync(ToolContext.ChatId, notification, null, ct);

			// Store the message ID for reaction-based approval
			if (messageExternalId is not null) {
				entity.MessageExternalId = messageExternalId;
				await db.SaveChangesAsync(ct);
			}
		}

		// Wait for resolution or timeout
		try {
			using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(request.TimeoutSeconds));
			using CancellationTokenSource linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

			linkedCts.Token.Register(() => tcs.TrySetResult(new ApprovalResult(ApprovalDecision.Timeout)));
			ApprovalResult result = await tcs.Task;

			// Update DB with result
			await this.FinalizeApprovalAsync(approvalId, result);

			// If always, add to allowlist
			if (result.Decision == ApprovalDecision.Always)
				await this.AddToAllowlistAsync(request.AgentId, request.Command);

			return result;
		} finally {
			this.pending.TryRemove(approvalId, out _);
		}
	}

	public async Task<bool> ResolveAsync(string approvalId, ApprovalDecision decision, string senderJid) {
		if (!this.pending.TryGetValue(approvalId, out TaskCompletionSource<ApprovalResult>? tcs))
			return false;

		return tcs.TrySetResult(new ApprovalResult(decision, senderJid));
	}

	public async Task<bool> ResolveByMessageAsync(string messageExternalId, ApprovalDecision decision, string senderJid) {
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		ExecApprovalEntity? entity = await db.ExecApprovals
			.FirstOrDefaultAsync(e => e.MessageExternalId == messageExternalId && e.Status == "pending");

		if (entity is null) return false;

		return await this.ResolveAsync(entity.ApprovalId, decision, senderJid);
	}

	private async Task<bool> MatchesAllowlistAsync(LisDbContext db, ApprovalRequest request) {
		List<ExecAllowlistEntity> entries = await db.ExecAllowlist
			.Where(e => e.AgentId == request.AgentId || e.AgentId == null)
			.ToListAsync();

		foreach (ExecAllowlistEntity entry in entries) {
			if (MatchesGlob(request.Command, entry.Pattern)) {
				entry.LastUsedAt = DateTimeOffset.UtcNow;
				entry.LastCommand = request.Command;
				await db.SaveChangesAsync();
				return true;
			}
		}

		return false;
	}

	private async Task FinalizeApprovalAsync(string approvalId, ApprovalResult result) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

			ExecApprovalEntity? entity = await db.ExecApprovals
				.FirstOrDefaultAsync(e => e.ApprovalId == approvalId);

			if (entity is null) return;

			entity.Status     = result.Decision == ApprovalDecision.Timeout ? "expired" : result.Decision == ApprovalDecision.Deny ? "denied" : "approved";
			entity.Decision   = result.Decision.ToString().ToLowerInvariant();
			entity.ResolvedBy = result.ResolvedBy;
			entity.ResolvedAt = DateTimeOffset.UtcNow;
			await db.SaveChangesAsync();
		} catch (Exception ex) {
			logger.LogError(ex, "Failed to finalize approval {ApprovalId}", approvalId);
		}
	}

	private async Task AddToAllowlistAsync(long? agentId, string command) {
		try {
			using IServiceScope scope = scopeFactory.CreateScope();
			LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

			// Use the command as a literal pattern
			bool exists = await db.ExecAllowlist
				.AnyAsync(e => e.AgentId == agentId && e.Pattern == command);

			if (exists) return;

			db.ExecAllowlist.Add(new ExecAllowlistEntity {
				AgentId     = agentId,
				Pattern     = command,
				LastUsedAt  = DateTimeOffset.UtcNow,
				LastCommand = command,
				CreatedAt   = DateTimeOffset.UtcNow
			});
			await db.SaveChangesAsync();

			if (logger.IsEnabled(LogLevel.Information))
			logger.LogInformation("Added '{Command}' to exec allowlist for agent {AgentId}", command, agentId);
		} catch (Exception ex) {
			logger.LogError(ex, "Failed to add command to allowlist");
		}
	}

	/// <summary>
	/// Simple glob matching: supports * (any chars) and ? (single char).
	/// </summary>
	internal static bool MatchesGlob(string input, string pattern) {
		// Use .NET's built-in simple expression matching
		return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, input, ignoreCase: true);
	}

	private static string GenerateShortId() {
		Span<byte> bytes = stackalloc byte[2];
		RandomNumberGenerator.Fill(bytes);
		return Convert.ToHexString(bytes).ToLowerInvariant();
	}
}
