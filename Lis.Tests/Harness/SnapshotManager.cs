using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lis.Tests.Harness;

/// <summary>
/// Comparison result between a current run and an approved snapshot.
/// </summary>
public sealed class SnapshotComparison
{
	public bool IsMatch { get; init; }
	public bool SnapshotExists { get; init; }
	public bool IsApproved { get; init; }
	public string? Difference { get; init; }
}

/// <summary>
/// Persisted snapshot data for a test.
/// </summary>
public sealed class SnapshotData
{
	[JsonPropertyName("response")]
	public string Response { get; init; } = string.Empty;

	[JsonPropertyName("tool_calls")]
	public List<SnapshotToolCall> ToolCalls { get; init; } = [];

	[JsonPropertyName("approved_at")]
	public DateTimeOffset? ApprovedAt { get; init; }

	[JsonPropertyName("approved_hash")]
	public string? ApprovedHash { get; init; }
}

/// <summary>
/// Tool call data stored in a snapshot.
/// </summary>
public sealed class SnapshotToolCall
{
	[JsonPropertyName("plugin_name")]
	public string PluginName { get; init; } = string.Empty;

	[JsonPropertyName("function_name")]
	public string FunctionName { get; init; } = string.Empty;

	[JsonPropertyName("arguments")]
	public Dictionary<string, string> Arguments { get; init; } = [];
}

/// <summary>
/// Manages snapshot files for LLM test output comparison.
/// Snapshots are saved as JSON and can be approved for future regression checks.
/// </summary>
public sealed class SnapshotManager
{
	private readonly string _snapshotDirectory;
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public SnapshotManager(string? snapshotDirectory = null)
	{
		this._snapshotDirectory = snapshotDirectory
			?? Path.Combine(AppContext.BaseDirectory, "__snapshots__");
	}

	/// <summary>Save a snapshot for the given test name.</summary>
	public void SaveSnapshot(string testName, HarnessResult result)
	{
		Directory.CreateDirectory(this._snapshotDirectory);

		SnapshotData data = ToSnapshotData(result);
		string json = JsonSerializer.Serialize(data, JsonOptions);
		string path = this.GetSnapshotPath(testName);
		File.WriteAllText(path, json);
	}

	/// <summary>Load a previously saved snapshot, or null if none exists.</summary>
	public SnapshotData? LoadSnapshot(string testName)
	{
		string path = this.GetSnapshotPath(testName);
		if (!File.Exists(path)) return null;

		string json = File.ReadAllText(path);
		return JsonSerializer.Deserialize<SnapshotData>(json, JsonOptions);
	}

	/// <summary>Compare current result with the approved snapshot.</summary>
	public SnapshotComparison CompareWithSnapshot(string testName, HarnessResult result)
	{
		SnapshotData? existing = this.LoadSnapshot(testName);

		if (existing is null)
		{
			// First run — save and indicate new snapshot
			this.SaveSnapshot(testName, result);
			return new SnapshotComparison
			{
				IsMatch = false,
				SnapshotExists = false,
				IsApproved = false,
				Difference = "New snapshot created — needs approval."
			};
		}

		if (existing.ApprovedHash is null)
		{
			return new SnapshotComparison
			{
				IsMatch = false,
				SnapshotExists = true,
				IsApproved = false,
				Difference = "Snapshot exists but has not been approved yet."
			};
		}

		// Compare by content hash
		SnapshotData currentData = ToSnapshotData(result);
		string currentHash = ComputeHash(currentData);

		if (currentHash == existing.ApprovedHash)
		{
			return new SnapshotComparison
			{
				IsMatch = true,
				SnapshotExists = true,
				IsApproved = true
			};
		}

		// Determine difference
		string difference = BuildDifference(existing, currentData);

		// Save the new result for review
		this.SaveSnapshot(testName, result);

		return new SnapshotComparison
		{
			IsMatch = false,
			SnapshotExists = true,
			IsApproved = true,
			Difference = difference
		};
	}

	/// <summary>Mark the current snapshot as approved.</summary>
	public void ApproveSnapshot(string testName)
	{
		SnapshotData? existing = this.LoadSnapshot(testName);
		if (existing is null)
			throw new InvalidOperationException($"No snapshot found for '{testName}'.");

		string hash = ComputeHash(existing);
		SnapshotData approved = new()
		{
			Response = existing.Response,
			ToolCalls = existing.ToolCalls,
			ApprovedAt = DateTimeOffset.UtcNow,
			ApprovedHash = hash
		};

		string json = JsonSerializer.Serialize(approved, JsonOptions);
		string path = this.GetSnapshotPath(testName);
		File.WriteAllText(path, json);
	}

	private string GetSnapshotPath(string testName)
	{
		// Sanitize test name for file system
		string safe = string.Join("_", testName.Split(Path.GetInvalidFileNameChars()));
		return Path.Combine(this._snapshotDirectory, $"{safe}.json");
	}

	private static SnapshotData ToSnapshotData(HarnessResult result) => new()
	{
		Response = result.Response,
		ToolCalls = result.ToolCalls.Select(tc => new SnapshotToolCall
		{
			PluginName = tc.PluginName,
			FunctionName = tc.FunctionName,
			Arguments = tc.Arguments
		}).ToList()
	};

	internal static string ComputeHash(SnapshotData data)
	{
		string json = JsonSerializer.Serialize(new { data.Response, data.ToolCalls }, JsonOptions);
		byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
		return Convert.ToHexStringLower(bytes);
	}

	private static string BuildDifference(SnapshotData approved, SnapshotData current)
	{
		List<string> diffs = [];

		if (approved.Response != current.Response)
			diffs.Add($"Response changed: '{Truncate(approved.Response)}' → '{Truncate(current.Response)}'");

		if (approved.ToolCalls.Count != current.ToolCalls.Count)
			diffs.Add($"Tool call count: {approved.ToolCalls.Count} → {current.ToolCalls.Count}");

		return diffs.Count > 0 ? string.Join("; ", diffs) : "Content hash mismatch (subtle difference).";
	}

	private static string Truncate(string text, int maxLength = 80) =>
		text.Length <= maxLength ? text : text[..maxLength] + "…";
}
