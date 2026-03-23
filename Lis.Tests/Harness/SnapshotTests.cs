namespace Lis.Tests.Harness;

public class SnapshotTests : IDisposable
{
	private readonly string _tempDir;
	private readonly SnapshotManager _sut;

	public SnapshotTests()
	{
		this._tempDir = Path.Combine(Path.GetTempPath(), $"lis_snapshots_{Guid.NewGuid():N}");
		this._sut = new SnapshotManager(this._tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(this._tempDir))
			Directory.Delete(this._tempDir, recursive: true);
		GC.SuppressFinalize(this);
	}

	private static HarnessResult MakeResult(string response = "Hello", List<HarnessToolCall>? toolCalls = null) => new()
	{
		Response = response,
		ToolCalls = toolCalls ?? [],
		OutputTokens = 5,
		Duration = TimeSpan.FromMilliseconds(50),
		History = []
	};

	// ── SaveSnapshot / LoadSnapshot ─────────────────────────────

	[Fact]
	public void SaveAndLoad_RoundTrips()
	{
		HarnessResult result = MakeResult("Test response", [
			new HarnessToolCall("mem", "create_memory", new() { ["content"] = "birthday" })
		]);

		this._sut.SaveSnapshot("save_load_test", result);
		SnapshotData? loaded = this._sut.LoadSnapshot("save_load_test");

		Assert.NotNull(loaded);
		Assert.Equal("Test response", loaded.Response);
		Assert.Single(loaded.ToolCalls);
		Assert.Equal("mem", loaded.ToolCalls[0].PluginName);
		Assert.Equal("create_memory", loaded.ToolCalls[0].FunctionName);
		Assert.Equal("birthday", loaded.ToolCalls[0].Arguments["content"]);
	}

	[Fact]
	public void LoadSnapshot_NonExistent_ReturnsNull()
	{
		SnapshotData? loaded = this._sut.LoadSnapshot("does_not_exist");

		Assert.Null(loaded);
	}

	[Fact]
	public void SaveSnapshot_CreatesDirectory()
	{
		Assert.False(Directory.Exists(this._tempDir));

		this._sut.SaveSnapshot("dir_test", MakeResult());

		Assert.True(Directory.Exists(this._tempDir));
	}

	[Fact]
	public void SaveSnapshot_OverwritesExisting()
	{
		this._sut.SaveSnapshot("overwrite_test", MakeResult("First"));
		this._sut.SaveSnapshot("overwrite_test", MakeResult("Second"));

		SnapshotData? loaded = this._sut.LoadSnapshot("overwrite_test");
		Assert.NotNull(loaded);
		Assert.Equal("Second", loaded.Response);
	}

	// ── CompareWithSnapshot ─────────────────────────────────────

	[Fact]
	public void Compare_NoExistingSnapshot_CreatesNew()
	{
		HarnessResult result = MakeResult("New response");

		SnapshotComparison comparison = this._sut.CompareWithSnapshot("new_test", result);

		Assert.False(comparison.IsMatch);
		Assert.False(comparison.SnapshotExists);
		Assert.False(comparison.IsApproved);
		Assert.Contains("needs approval", comparison.Difference);
	}

	[Fact]
	public void Compare_ExistingButNotApproved_ReportsUnapproved()
	{
		this._sut.SaveSnapshot("unapproved_test", MakeResult("Response"));

		HarnessResult result = MakeResult("Response");
		SnapshotComparison comparison = this._sut.CompareWithSnapshot("unapproved_test", result);

		Assert.False(comparison.IsMatch);
		Assert.True(comparison.SnapshotExists);
		Assert.False(comparison.IsApproved);
		Assert.Contains("not been approved", comparison.Difference);
	}

	[Fact]
	public void Compare_ApprovedAndMatching_ReturnsMatch()
	{
		this._sut.SaveSnapshot("approved_test", MakeResult("Stable response"));
		this._sut.ApproveSnapshot("approved_test");

		HarnessResult result = MakeResult("Stable response");
		SnapshotComparison comparison = this._sut.CompareWithSnapshot("approved_test", result);

		Assert.True(comparison.IsMatch);
		Assert.True(comparison.SnapshotExists);
		Assert.True(comparison.IsApproved);
		Assert.Null(comparison.Difference);
	}

	[Fact]
	public void Compare_ApprovedButChanged_ReportsDifference()
	{
		this._sut.SaveSnapshot("changed_test", MakeResult("Original"));
		this._sut.ApproveSnapshot("changed_test");

		HarnessResult changed = MakeResult("Modified");
		SnapshotComparison comparison = this._sut.CompareWithSnapshot("changed_test", changed);

		Assert.False(comparison.IsMatch);
		Assert.True(comparison.SnapshotExists);
		Assert.True(comparison.IsApproved);
		Assert.Contains("Response changed", comparison.Difference);
	}

	[Fact]
	public void Compare_ToolCallCountChanged_ReportsDifference()
	{
		HarnessResult original = MakeResult("Same", [
			new HarnessToolCall("mem", "search", [])
		]);
		this._sut.SaveSnapshot("tools_changed", original);
		this._sut.ApproveSnapshot("tools_changed");

		HarnessResult changed = MakeResult("Same", [
			new HarnessToolCall("mem", "search", []),
			new HarnessToolCall("mem", "create_memory", [])
		]);
		SnapshotComparison comparison = this._sut.CompareWithSnapshot("tools_changed", changed);

		Assert.False(comparison.IsMatch);
		Assert.Contains("Tool call count", comparison.Difference);
	}

	// ── ApproveSnapshot ─────────────────────────────────────────

	[Fact]
	public void ApproveSnapshot_SetsApprovedFields()
	{
		this._sut.SaveSnapshot("approve_test", MakeResult("Approved response"));

		this._sut.ApproveSnapshot("approve_test");

		SnapshotData? loaded = this._sut.LoadSnapshot("approve_test");
		Assert.NotNull(loaded);
		Assert.NotNull(loaded.ApprovedAt);
		Assert.NotNull(loaded.ApprovedHash);
		Assert.Equal("Approved response", loaded.Response);
	}

	[Fact]
	public void ApproveSnapshot_NonExistent_Throws()
	{
		Assert.Throws<InvalidOperationException>(() =>
			this._sut.ApproveSnapshot("nonexistent"));
	}

	// ── ComputeHash ─────────────────────────────────────────────

	[Fact]
	public void ComputeHash_SameContent_SameHash()
	{
		SnapshotData a = new() { Response = "Hello", ToolCalls = [] };
		SnapshotData b = new() { Response = "Hello", ToolCalls = [] };

		string hashA = SnapshotManager.ComputeHash(a);
		string hashB = SnapshotManager.ComputeHash(b);

		Assert.Equal(hashA, hashB);
	}

	[Fact]
	public void ComputeHash_DifferentContent_DifferentHash()
	{
		SnapshotData a = new() { Response = "Hello", ToolCalls = [] };
		SnapshotData b = new() { Response = "Goodbye", ToolCalls = [] };

		string hashA = SnapshotManager.ComputeHash(a);
		string hashB = SnapshotManager.ComputeHash(b);

		Assert.NotEqual(hashA, hashB);
	}

	[Fact]
	public void ComputeHash_IgnoresApprovedFields()
	{
		SnapshotData a = new() { Response = "Test", ToolCalls = [] };
		SnapshotData b = new() { Response = "Test", ToolCalls = [], ApprovedAt = DateTimeOffset.UtcNow, ApprovedHash = "abc" };

		string hashA = SnapshotManager.ComputeHash(a);
		string hashB = SnapshotManager.ComputeHash(b);

		Assert.Equal(hashA, hashB);
	}

	// ── File naming ─────────────────────────────────────────────

	[Fact]
	public void SaveSnapshot_HandlesSpecialCharsInName()
	{
		this._sut.SaveSnapshot("test:with/special<chars>", MakeResult());

		// Should not throw — chars are sanitized
		SnapshotData? loaded = this._sut.LoadSnapshot("test:with/special<chars>");
		Assert.NotNull(loaded);
	}
}
