using System.ComponentModel;
using System.IO.Enumeration;
using System.Text;

using Lis.Core.Util;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Lis.Tools;

public sealed class FileSystemPlugin(IServiceScopeFactory scopeFactory) {

	[KernelFunction("read_file")]
	[Description("Read file contents with line numbers. Returns lines from offset (1-based) up to limit lines.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> ReadFileAsync(
		[Description("Path to file (relative to workspace or absolute)")] string path,
		[Description("Starting line number (1-based)")] int offset = 1,
		[Description("Maximum number of lines to return")] int limit = 200) {
		string resolved = this.ResolveSafePath(path);
		string relativePath = Path.GetRelativePath(this.ResolveWorkspacePath(), resolved);
		await ToolContext.NotifyAsync($"\ud83d\udcc4 Reading file\n{relativePath} (offset={offset}, limit={limit})");

		string[] allLines = await File.ReadAllLinesAsync(resolved);
		int total = allLines.Length;

		if (offset < 1) offset = 1;
		int startIndex = offset - 1;
		if (startIndex >= total) return $"File has only {total} lines.";

		int count = Math.Min(limit, total - startIndex);
		int maxLineNum = startIndex + count;
		int numWidth = maxLineNum.ToString().Length;

		StringBuilder sb = new();
		for (int i = 0; i < count; i++) {
			int lineNum = startIndex + i + 1;
			sb.Append(lineNum.ToString().PadLeft(numWidth));
			sb.Append(" | ");
			sb.AppendLine(allLines[startIndex + i]);
		}

		if (startIndex + count < total) {
			sb.AppendLine($"[...truncated, {total} lines total]");
		}

		return sb.ToString().TrimEnd();
	}

	[KernelFunction("write_file")]
	[Description("Write content to a file. Creates parent directories as needed.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> WriteFileAsync(
		[Description("Path to file (relative to workspace or absolute)")] string path,
		[Description("Content to write")] string content) {
		string resolved = this.ResolveSafePath(path);
		string relativePath = Path.GetRelativePath(this.ResolveWorkspacePath(), resolved);
		await ToolContext.NotifyAsync($"\u270f\ufe0f Writing file\n{relativePath}");

		Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
		await File.WriteAllTextAsync(resolved, content);

		long bytes = new FileInfo(resolved).Length;
		return $"Written {bytes} bytes to {relativePath}";
	}

	[KernelFunction("edit_file")]
	[Description("Find and replace text in a file. Replaces the first occurrence of old_text with new_text.")]
	[ToolSummarization(SummarizationPolicy.Prune)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> EditFileAsync(
		[Description("Path to file (relative to workspace or absolute)")] string path,
		[Description("Exact text to find")] string oldText,
		[Description("Replacement text")] string newText) {
		string resolved = this.ResolveSafePath(path);
		string relativePath = Path.GetRelativePath(this.ResolveWorkspacePath(), resolved);
		await ToolContext.NotifyAsync($"\u270f\ufe0f Editing file\n{relativePath}");

		string fileContent = await File.ReadAllTextAsync(resolved);
		int idx = fileContent.IndexOf(oldText, StringComparison.Ordinal);
		if (idx < 0) return "Error: old_text not found in file.";

		string updated = string.Concat(fileContent.AsSpan(0, idx), newText, fileContent.AsSpan(idx + oldText.Length));
		await File.WriteAllTextAsync(resolved, updated);

		return $"Edit applied to {relativePath}";
	}

	[KernelFunction("list_directory")]
	[Description("List files and directories. Shows directories first (with trailing /), then files with human-readable sizes.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> ListDirectoryAsync(
		[Description("Path to directory (relative to workspace or absolute)")] string path,
		[Description("Show hidden files and directories")] bool showHidden = false) {
		string resolved = this.ResolveSafePath(path);
		string relativePath = Path.GetRelativePath(this.ResolveWorkspacePath(), resolved);
		await ToolContext.NotifyAsync($"\ud83d\udcc2 Listing directory\n{relativePath}");

		if (!Directory.Exists(resolved)) return $"Directory not found: {relativePath}";

		DirectoryInfo dir = new(resolved);
		StringBuilder sb = new();

		List<DirectoryInfo> dirs = [.. dir.EnumerateDirectories()
			.Where(d => showHidden || !d.Name.StartsWith('.'))
			.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];

		foreach (DirectoryInfo d in dirs) {
			sb.AppendLine($"{d.Name}/");
		}

		List<FileInfo> files = [.. dir.EnumerateFiles()
			.Where(f => showHidden || !f.Name.StartsWith('.'))
			.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];

		foreach (FileInfo f in files) {
			sb.AppendLine($"{f.Name}  {FormatSize(f.Length)}");
		}

		if (sb.Length == 0) return "Directory is empty.";
		return sb.ToString().TrimEnd();
	}

	[KernelFunction("search_files")]
	[Description("Search for files matching a glob pattern recursively within the workspace.")]
	[ToolSummarization(SummarizationPolicy.Summarize)]
	[ToolAuthorization(ToolAuthLevel.OwnerOnly)]
	public async Task<string> SearchFilesAsync(
		[Description("Glob pattern to match (e.g. '*.cs', '*.json')")] string pattern,
		[Description("Root directory to search from (relative to workspace, defaults to workspace root)")] string? root = null) {
		string workspace = this.ResolveWorkspacePath();
		string searchRoot = root is not null
			? this.ResolveSafePath(root)
			: workspace;
		string relativeRoot = Path.GetRelativePath(workspace, searchRoot);
		await ToolContext.NotifyAsync($"\ud83d\udd0d Searching files\npattern: {pattern}\nroot: {relativeRoot}");

		if (!Directory.Exists(searchRoot)) return $"Directory not found: {relativeRoot}";

		List<string> results = [];
		foreach (string file in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)) {
			string fileName = Path.GetFileName(file);
			if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true)) {
				results.Add(Path.GetRelativePath(workspace, file));
				if (results.Count >= 100) break;
			}
		}

		if (results.Count == 0) return "No files matched.";

		StringBuilder sb = new();
		foreach (string r in results) {
			sb.AppendLine(r);
		}

		if (results.Count >= 100) {
			sb.AppendLine("[...truncated at 100 results]");
		}

		return sb.ToString().TrimEnd();
	}

	private string ResolveWorkspacePath() {
		long agentId = ToolContext.AgentId ?? throw new InvalidOperationException("No agent context");
		using IServiceScope scope = scopeFactory.CreateScope();
		LisDbContext db = scope.ServiceProvider.GetRequiredService<LisDbContext>();

		AgentEntity? agent = db.Agents.Find(agentId);
		return agent?.WorkspacePath ?? Directory.GetCurrentDirectory();
	}

	private string ResolveSafePath(string userPath) {
		string workspace = this.ResolveWorkspacePath();
		string resolved = Path.GetFullPath(userPath, workspace);
		if (!resolved.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
			throw new UnauthorizedAccessException($"Path outside workspace: {userPath}");
		if (File.Exists(resolved)) {
			string? linkTarget = File.ResolveLinkTarget(resolved, true)?.FullName;
			if (linkTarget is not null && !linkTarget.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
				throw new UnauthorizedAccessException("Symlink target outside workspace");
		}
		return resolved;
	}

	private static string FormatSize(long bytes) {
		return bytes switch {
			>= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
			>= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
			>= 1_024         => $"{bytes / 1_024.0:F1} KB",
			_                => $"{bytes} B",
		};
	}
}
