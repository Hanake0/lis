using Lis.Api.Schemas.Events;
using Lis.Api.Services;
using Lis.Core.Channel;
using Lis.Core.Events;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="SseChannelClient"/>. A capturing <see cref="IEventBus"/>
/// records every published event so we can assert the routing tag (Subject) and
/// the payload shape for each IChannelClient method.
/// </summary>
public class SseChannelClientTests {
	private readonly CapturingBus     bus = new();
	private readonly SseChannelClient sut;

	public SseChannelClientTests() {
		this.sut = new SseChannelClient(this.bus);
	}

	// ── Subject routing ───────────────────────────────────────────

	[Fact]
	public async Task Every_Method_Tags_The_Event_With_Chat_Scoped_Subject() {
		await this.sut.SendChunkAsync("c1", "m1", "delta", done: false);
		await this.sut.SetTypingAsync("c2");
		await this.sut.SendErrorAsync("c3", "boom");

		Assert.Equal(["chat:c1", "chat:c2", "chat:c3"], this.bus.Captured.Select(e => e.Subject));
	}

	// ── Token streams ─────────────────────────────────────────────

	[Fact]
	public async Task SendChunkAsync_Publishes_A_Chunk_Event_With_The_Delta_And_Done_Flag() {
		await this.sut.SendChunkAsync("c1", "m1", "hello world", done: true);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatStreamEventNames.Chunk, ev.Name);
		ChunkPayload p = Assert.IsType<ChunkPayload>(ev.Data);
		Assert.Equal("c1",           p.ChatId);
		Assert.Equal("m1",           p.MessageId);
		Assert.Equal("hello world",  p.Delta);
		Assert.True (p.Done);
	}

	[Fact]
	public async Task SendThinkingChunkAsync_Uses_Its_Own_Event_Name() {
		await this.sut.SendThinkingChunkAsync("c1", "m1", "thinking…", done: false);
		Assert.Equal(ChatStreamEventNames.ThinkingChunk, this.bus.Captured[0].Name);
		Assert.IsType<ThinkingChunkPayload>(this.bus.Captured[0].Data);
	}

	// ── Tool events ───────────────────────────────────────────────

	[Fact]
	public async Task SendToolStartAsync_Carries_Tool_Name_And_Args() {
		await this.sut.SendToolStartAsync("c1", "m1", "my_tool", "{\"k\":\"v\"}");
		ToolStartPayload p = Assert.IsType<ToolStartPayload>(this.bus.Captured[0].Data);
		Assert.Equal("my_tool",       p.ToolName);
		Assert.Equal("{\"k\":\"v\"}", p.Args);
	}

	[Fact]
	public async Task SendToolResultAsync_Carries_Result_And_Optional_Error() {
		await this.sut.SendToolResultAsync("c1", "m1", "my_tool", "result", error: "boom");
		ToolResultPayload p = Assert.IsType<ToolResultPayload>(this.bus.Captured[0].Data);
		Assert.Equal("result", p.Result);
		Assert.Equal("boom",   p.Error);
	}

	// ── Persisted echoes ──────────────────────────────────────────

	[Fact]
	public async Task SendMessageAsync_Mints_An_Id_And_Includes_Token_Usage_When_Provided() {
		string? id = await this.sut.SendMessageAsync(
			"c1", "hi", new TokenUsage(InputTokens: 12, OutputTokens: 34, CacheReadTokens: 5, CacheCreationTokens: 1, ThinkingTokens: 2)
		);

		Assert.NotNull(id);
		ChatMessagePayload p = Assert.IsType<ChatMessagePayload>(this.bus.Captured[0].Data);
		Assert.Equal(id,         p.Id);
		Assert.Equal("hi",       p.Text);
		Assert.Equal("assistant", p.Role);
		Assert.Equal(12,          p.InputTokens);
		Assert.Equal(34,          p.OutputTokens);
		Assert.Equal(5,           p.CacheReadTokens);
		Assert.Equal(1,           p.CacheCreationTokens);
		Assert.Equal(2,           p.ThinkingTokens);
	}

	[Fact]
	public async Task SendMessageAsync_Without_Usage_Leaves_Token_Counts_Null() {
		await this.sut.SendMessageAsync("c1", "hi");
		ChatMessagePayload p = Assert.IsType<ChatMessagePayload>(this.bus.Captured[0].Data);
		Assert.Null(p.InputTokens);
		Assert.Null(p.OutputTokens);
	}

	[Fact]
	public async Task SendMessageTokensAsync_Carries_Just_The_Token_Counts_With_The_Message_Id() {
		await this.sut.SendMessageTokensAsync("c1", "m1", new TokenUsage(1, 2, 3, 4, 5));
		MessageTokensPayload p = Assert.IsType<MessageTokensPayload>(this.bus.Captured[0].Data);
		Assert.Equal("m1", p.Id);
		Assert.Equal(1, p.InputTokens);
		Assert.Equal(5, p.ThinkingTokens);
	}

	// ── Interactive prompts ───────────────────────────────────────

	[Fact]
	public async Task SendPermissionRequestAsync_Builds_Patterns_And_Always_Only_When_Command_Provided() {
		bool ok = await this.sut.SendPermissionRequestAsync(
			"c1", "appr-1", "shell.run", command: "rm -rf /", cwd: "/repo", description: "boom"
		);
		Assert.True(ok);
		PermissionRequestPayload p = Assert.IsType<PermissionRequestPayload>(this.bus.Captured[0].Data);
		Assert.Equal("appr-1",   p.ApprovalId);
		Assert.Equal(["rm -rf /"], p.Patterns);
		Assert.Equal(["rm -rf /"], p.Always);
		Assert.NotNull(p.Metadata);
		Assert.Equal("/repo", p.Metadata!["cwd"]);
		Assert.Equal("boom",  p.Metadata!["description"]);
	}

	[Fact]
	public async Task SendPermissionRequestAsync_Leaves_Patterns_Null_When_No_Command() {
		await this.sut.SendPermissionRequestAsync("c1", "appr-1", "read_file", command: null);
		PermissionRequestPayload p = Assert.IsType<PermissionRequestPayload>(this.bus.Captured[0].Data);
		Assert.Null(p.Patterns);
		Assert.Null(p.Always);
	}

	[Fact]
	public async Task SendQuestionAsync_Maps_Items_And_Options_Cleanly() {
		List<QuestionItem> items = [
			new QuestionItem(
				Question: "Pick one",
				Header:   "Choose",
				Options:  [new QuestionOption("A", "first"), new QuestionOption("B", "second")],
				Multiple: false
			),
		];
		bool ok = await this.sut.SendQuestionAsync("c1", "q-1", items);
		Assert.True(ok);

		QuestionPayload p = Assert.IsType<QuestionPayload>(this.bus.Captured[0].Data);
		Assert.Equal("q-1", p.QuestionId);
		QuestionItemPayload q = Assert.Single(p.Questions);
		Assert.Equal("Pick one", q.Question);
		Assert.Equal(2, q.Options.Count);
		Assert.Equal("A", q.Options[0].Label);
	}

	// ── Markers / Reactions ───────────────────────────────────────

	[Fact]
	public async Task SetTypingAsync_And_StopTypingAsync_Have_Distinct_Event_Names() {
		await this.sut.SetTypingAsync("c1");
		await this.sut.StopTypingAsync("c1");
		Assert.Equal(ChatStreamEventNames.Typing,        this.bus.Captured[0].Name);
		Assert.Equal(ChatStreamEventNames.TypingStopped, this.bus.Captured[1].Name);
	}

	[Fact]
	public async Task ReactAsync_Carries_MessageId_And_Emoji() {
		await this.sut.ReactAsync("m1", "c1", "✅");
		ReactionPayload p = Assert.IsType<ReactionPayload>(this.bus.Captured[0].Data);
		Assert.Equal("m1", p.MessageId);
		Assert.Equal("✅", p.Emoji);
	}

	// ── No-op pieces ──────────────────────────────────────────────

	[Fact]
	public async Task MarkReadAsync_Is_A_Noop_And_Doesnt_Publish() {
		await this.sut.MarkReadAsync("m1", "c1");
		Assert.Empty(this.bus.Captured);
	}

	[Fact]
	public async Task DownloadMediaAsync_Returns_Null_Without_Publishing() {
		Assert.Null(await this.sut.DownloadMediaAsync("m1", "c1"));
		Assert.Empty(this.bus.Captured);
	}

	// ── Capturing IEventBus stub ──────────────────────────────────

	private sealed class CapturingBus :IEventBus {
		public List<ServerEvent> Captured { get; } = [];
		private long nextId;

		public ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
			this.Captured.Add(ev with { Id = Interlocked.Increment(ref this.nextId) });
			return ValueTask.CompletedTask;
		}

		public IEventSubscription Subscribe(int capacity = 256, Func<ServerEvent, bool>? filter = null) =>
			throw new NotSupportedException("not used by SseChannelClient tests");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];
	}
}
