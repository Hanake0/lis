using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class ExecPlugin(IServiceScopeFactory scopeFactory) {

	private const int MaxOutputBytes = 50 * 1024;

	[KernelFunction("run_command")]
	[Description("Execute a shell command and return stdout, stderr, and exit code.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	[ToolAuthorization(ToolAuthLevel.ApprovalRequired)]
	public async Task<string> RunCommandAsync(
		[Description("Shell command to execute")] string command,
		[Description("Working directory (default: workspace root)")] string? cwd = null,
		[Description("Timeout in seconds (default: 30, max: 300)")] int timeoutSeconds = 30) {
		await ToolContext.NotifyAsync($"🖥️ Running: {command}");

		string workspacePath = await this.ResolveWorkspacePathAsync();
		string workingDirectory = ResolveWorkingDirectory(workspacePath, cwd);
		timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 300);

		ProcessStartInfo psi = new() {
			RedirectStandardOutput = true,
			RedirectStandardError  = true,
			UseShellExecute        = false,
			CreateNoWindow         = true,
			WorkingDirectory       = workingDirectory,
		};

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			psi.FileName  = "cmd";
			psi.Arguments = $"/c \"{command}\"";
		} else {
			psi.FileName  = "/bin/bash";
			psi.Arguments = $"-c \"{command}\"";
		}

		using Process process = new() { StartInfo = psi };
		process.Start();

		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(timeoutSeconds));

		Task<string> stdoutTask = ReadStreamAsync(process.StandardOutput, cts.Token);
		Task<string> stderrTask = ReadStreamAsync(process.StandardError, cts.Token);

		try {
			await process.WaitForExitAsync(cts.Token);
		} catch (OperationCanceledException) {
			KillProcessTree(process);
			return $"Exit: -1\n--- stderr ---\nProcess timed out after {timeoutSeconds}s and was killed.";
		}

		string stdout = await stdoutTask;
		string stderr = await stderrTask;

		return FormatResult(process.ExitCode, stdout, stderr);
	}

	private async Task<string> ResolveWorkspacePathAsync() {
		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext        db    = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		AgentEntity? agent = await db.Agents.FindAsync(agentId);
		return agent?.WorkspacePath ?? Directory.GetCurrentDirectory();
	}

	private static string ResolveWorkingDirectory(string workspacePath, string? cwd) {
		if (string.IsNullOrWhiteSpace(cwd))
			return workspacePath;

		string resolved = Path.GetFullPath(cwd, workspacePath);
		string normalizedWorkspace = Path.GetFullPath(workspacePath);

		if (!resolved.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				$"Working directory '{cwd}' is outside the workspace '{normalizedWorkspace}'.");

		return resolved;
	}

	private static async Task<string> ReadStreamAsync(StreamReader reader, CancellationToken ct) {
		StringBuilder sb = new();
		char[] buffer = new char[4096];
		int totalBytes = 0;
		bool truncated = false;

		while (true) {
			int read = await reader.ReadAsync(buffer, ct);
			if (read == 0) break;

			int byteCount = Encoding.UTF8.GetByteCount(buffer, 0, read);

			if (totalBytes + byteCount > MaxOutputBytes) {
				int remaining = MaxOutputBytes - totalBytes;
				if (remaining > 0) {
					// Approximate char count for remaining bytes
					int charsToTake = Math.Min(read, remaining);
					sb.Append(buffer, 0, charsToTake);
				}
				truncated = true;
				break;
			}

			sb.Append(buffer, 0, read);
			totalBytes += byteCount;
		}

		if (truncated)
			sb.Append("\n[...truncated at 50KB]");

		return sb.ToString().TrimEnd();
	}

	private static void KillProcessTree(Process process) {
		try {
			process.Kill(entireProcessTree: true);
		} catch {
			// Process may have already exited
		}
	}

	private static string FormatResult(int exitCode, string stdout, string stderr) {
		StringBuilder sb = new();
		sb.Append($"Exit: {exitCode}");

		if (!string.IsNullOrWhiteSpace(stdout)) {
			sb.Append('\n');
			sb.Append("--- stdout ---\n");
			sb.Append(stdout);
		}

		if (!string.IsNullOrWhiteSpace(stderr)) {
			sb.Append('\n');
			sb.Append("--- stderr ---\n");
			sb.Append(stderr);
		}

		return sb.ToString();
	}
}
