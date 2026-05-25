using Lis.Agent;
using Lis.Api.Controllers;
using Lis.Api.Schemas.Agents;
using Lis.Api.Schemas.Chats;
using Lis.Api.Schemas.Events;
using Lis.Core.Channel;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

/// <summary>
/// Tests for <see cref="ChatsController"/>'s create endpoint — the one we need wired
/// for the multi-run orchestrator to mint chats from the webview. Backed by EF Core
/// InMemory + a capturing <see cref="IEventBus"/>.
/// </summary>
public class ChatsControllerTests :IDisposable {
	private readonly LisDbContext             db;
	private readonly FakeTimeProvider         time;
	private readonly CapturingEventBus        bus;
	private readonly FakeGenerationService    generation;
	private readonly FakeContextBreakdown     context;
	private readonly NoOpConversationService  conversation;
	private readonly ChatsController          sut;

	public ChatsControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"chats-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db           = new TestDbContext(options);
		this.time         = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus          = new CapturingEventBus();
		this.generation   = new FakeGenerationService();
		this.context      = new FakeContextBreakdown();
		this.conversation = new NoOpConversationService();
		this.sut          = new ChatsController(this.db, this.bus, this.time, this.generation, this.context, this.conversation);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task Create_Without_ChatId_Mints_Web_Prefixed_Id_And_Returns_201() {
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: null, Name: "Hello", ProjectExternalId: null, Channel: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.StartsWith("web:", dto.ChatId);
		Assert.Equal("Hello", dto.Name);
		Assert.Null(dto.ProjectExternalId);
		Assert.Equal(this.time.GetUtcNow(), dto.CreatedAt);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal(dto.ChatId, persisted.ExternalId);
		Assert.Equal("web",      persisted.Channel);
		Assert.True (persisted.Enabled);
	}

	[Fact]
	public async Task Create_With_Explicit_ChatId_Uses_It_Verbatim_And_Trims_Whitespace() {
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "  my-chat  ", Name: "  Trimmed  ", ProjectExternalId: null, Channel: "  ide  "),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.Equal("my-chat",  dto.ChatId);
		Assert.Equal("Trimmed",  dto.Name);
		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal("ide",      persisted.Channel);
	}

	[Fact]
	public async Task Create_Without_Name_Defaults_To_New_Chat() {
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "c-1", Name: null, ProjectExternalId: null, Channel: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.Equal("New Chat", dto.Name);
	}

	[Fact]
	public async Task Create_Without_Channel_Defaults_To_Web() {
		await this.sut.Create(
			new CreateChatRequest(ChatId: "c-1", Name: "x", ProjectExternalId: null, Channel: null),
			default
		);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal("web", persisted.Channel);
	}

	[Fact]
	public async Task Create_With_ProjectId_Binds_The_Project() {
		ProjectEntity project = await this.SeedProjectAsync("proj-1", "/repo");

		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: null, Name: "x", ProjectExternalId: "proj-1", Channel: null),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(created.Value);
		Assert.Equal("proj-1", dto.ProjectExternalId);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		Assert.Equal(project.Id, persisted.ProjectId);
	}

	[Fact]
	public async Task Create_With_Unknown_Project_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Create(
			new CreateChatRequest(ChatId: null, Name: "x", ProjectExternalId: "missing-project", Channel: null),
			default
		));

		// Nothing persisted, nothing emitted — the SaveChanges never ran.
		Assert.Empty(this.db.Chats);
		Assert.Empty(this.bus.Captured);
	}

	[Fact]
	public async Task Create_Emits_Single_ChatCreated_Event_With_The_New_Dto() {
		await this.sut.Create(
			new CreateChatRequest(ChatId: "c-evt", Name: "x", ProjectExternalId: null, Channel: null),
			default
		);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Created, ev.Name);
		ChatCreatedPayload payload = Assert.IsType<ChatCreatedPayload>(ev.Data);
		ChatDto carried = Assert.IsType<ChatDto>(payload.Chat);
		Assert.Equal("c-evt", carried.ChatId);
	}

	[Fact]
	public async Task Create_Is_Idempotent_On_Existing_ChatId_Returning_200_And_Not_Re_Emitting() {
		// First POST: 201 + event.
		await this.sut.Create(
			new CreateChatRequest(ChatId: "dup", Name: "first", ProjectExternalId: null, Channel: null),
			default
		);
		Assert.Single(this.bus.Captured);

		// Second POST with the same id: 200, returns the *existing* row, no new event.
		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "dup", Name: "second-tries-to-rename", ProjectExternalId: null, Channel: null),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(ok.Value);
		Assert.Equal("dup",   dto.ChatId);
		Assert.Equal("first", dto.Name); // existing name preserved — not "second-tries-to-rename"
		Assert.Single(this.bus.Captured); // still just the one event
		Assert.Single(this.db.Chats);     // no duplicate row
	}

	// ── List ──────────────────────────────────────────────────────

	[Fact]
	public async Task List_Empty_Returns_Ok_With_Empty_Array() {
		ActionResult<ChatDto[]> result = await this.sut.List(default);
		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(Assert.IsType<ChatDto[]>(ok.Value));
	}

	[Fact]
	public async Task List_Returns_All_Chats_Newest_Updated_First() {
		await this.SeedChatAsync("oldest", updatedHoursAgo: 5);
		await this.SeedChatAsync("middle", updatedHoursAgo: 2);
		await this.SeedChatAsync("newest", updatedHoursAgo: 0);

		ActionResult<ChatDto[]> result = await this.sut.List(default);
		ChatDto[] dtos = Assert.IsType<ChatDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal(["newest", "middle", "oldest"], dtos.Select(d => d.ChatId));
	}

	[Fact]
	public async Task List_Includes_Archived_Chats_With_Archived_At_Populated() {
		await this.SeedChatAsync("active",   updatedHoursAgo: 1);
		await this.SeedChatAsync("archived", updatedHoursAgo: 2, archivedHoursAgo: 1);

		ActionResult<ChatDto[]> result = await this.sut.List(default);
		ChatDto[] dtos = Assert.IsType<ChatDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal(2, dtos.Length);
		ChatDto archived = dtos.Single(d => d.ChatId == "archived");
		Assert.NotNull(archived.ArchivedAt);
		Assert.Null(dtos.Single(d => d.ChatId == "active").ArchivedAt);
	}

	[Fact]
	public async Task List_Includes_Project_External_Id_When_The_Chat_Is_Bound_To_One() {
		ProjectEntity project = await this.SeedProjectAsync("proj-x", "/repo");
		await this.SeedChatAsync("bound",   updatedHoursAgo: 0, projectId: project.Id);
		await this.SeedChatAsync("orphan",  updatedHoursAgo: 1);

		ActionResult<ChatDto[]> result = await this.sut.List(default);
		ChatDto[] dtos = Assert.IsType<ChatDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal("proj-x", dtos.Single(d => d.ChatId == "bound").ProjectExternalId);
		Assert.Null(dtos.Single(d => d.ChatId == "orphan").ProjectExternalId);
	}

	// ── Rename (PATCH) ────────────────────────────────────────────

	[Fact]
	public async Task Update_Renames_The_Chat_And_Returns_The_Patched_Dto() {
		await this.SeedChatAsync("rn", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		ActionResult<ChatDto> result = await this.sut.Update("rn", new UpdateChatRequest(Name: "  fresh name  "), default);

		ChatDto dto = Assert.IsType<ChatDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("fresh name", dto.Name);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Updated, ev.Name);
		Assert.IsType<ChatUpdatedPayload>(ev.Data);
	}

	[Fact]
	public async Task Update_Rejects_Empty_Name_With_ArgumentException() {
		await this.SeedChatAsync("rn", updatedHoursAgo: 1);
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.Update("rn", new UpdateChatRequest(Name: "   "), default));
	}

	[Fact]
	public async Task Update_With_Null_Name_Is_A_Noop_For_Name_But_Still_Bumps_UpdatedAt() {
		await this.SeedChatAsync("rn", updatedHoursAgo: 1);
		ActionResult<ChatDto> result = await this.sut.Update("rn", new UpdateChatRequest(Name: null), default);
		ChatDto dto = Assert.IsType<ChatDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("rn", dto.Name);
		Assert.Equal(this.time.GetUtcNow(), dto.UpdatedAt);
	}

	[Fact]
	public async Task Update_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Update("missing", new UpdateChatRequest(Name: "x"), default));
	}

	// ── Archive ───────────────────────────────────────────────────

	[Fact]
	public async Task Archive_Sets_ArchivedAt_And_Emits_Event() {
		await this.SeedChatAsync("ar", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		ActionResult<ChatDto> result = await this.sut.Archive("ar", default);
		ChatDto dto = Assert.IsType<ChatDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.NotNull(dto.ArchivedAt);
		Assert.Equal(this.time.GetUtcNow(), dto.ArchivedAt);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Archived, ev.Name);
		Assert.IsType<ChatArchivedPayload>(ev.Data);
	}

	[Fact]
	public async Task Archive_Already_Archived_Is_Idempotent_And_Does_Not_Re_Emit() {
		await this.SeedChatAsync("ar", updatedHoursAgo: 1, archivedHoursAgo: 0.5);
		this.bus.Captured.Clear();

		ActionResult<ChatDto> result = await this.sut.Archive("ar", default);
		Assert.IsType<OkObjectResult>(result.Result);
		Assert.Empty(this.bus.Captured);
	}

	[Fact]
	public async Task Archive_Missing_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.Archive("missing", default));
	}

	// ── Delete ────────────────────────────────────────────────────

	[Fact]
	public async Task Delete_Removes_The_Chat_And_Returns_204() {
		await this.SeedChatAsync("del", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		IActionResult result = await this.sut.Delete("del", default);

		Assert.IsType<NoContentResult>(result);
		Assert.Empty(this.db.Chats);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Deleted, ev.Name);
		ChatDeletedPayload payload = Assert.IsType<ChatDeletedPayload>(ev.Data);
		Assert.Equal("del", payload.ChatId);
	}

	[Fact]
	public async Task Delete_Missing_Is_Idempotent_And_Still_Publishes_The_Deletion_Event() {
		this.bus.Captured.Clear();
		IActionResult result = await this.sut.Delete("never-existed", default);
		Assert.IsType<NoContentResult>(result);
		// Idempotent broadcast — racing clients still get the "drop this row" signal.
		Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Deleted, this.bus.Captured[0].Name);
	}

	// ── Abort ─────────────────────────────────────────────────────

	[Fact]
	public async Task Abort_Returns_NoContent_Even_When_Conversation_Service_Cant_Cancel() {
		// The test conversation service isn't a MessageDebouncer, so the abort is a no-op —
		// the controller still returns 204 to keep the wire contract consistent across
		// environments where a different orchestrator is wired (alternative IConversationService impls).
		IActionResult result = await this.sut.Abort("any-chat", default);
		Assert.IsType<NoContentResult>(result);
	}

	// ── Context breakdown ─────────────────────────────────────────

	[Fact]
	public async Task GetContext_Returns_The_Mapped_Breakdown_From_The_Service() {
		this.context.Returned = new ContextBreakdownService.Breakdown(
			Total:    1500,
			Budget:   12000,
			Segments: new List<ContextBreakdownService.Segment> {
				new("system",  120),
				new("history", 1380),
			},
			Model:  "anthropic/claude-sonnet-4",
			Effort: "high"
		);

		ActionResult<ContextBreakdownDto> result = await this.sut.GetContext("c1", force: null, default);
		ContextBreakdownDto dto = Assert.IsType<ContextBreakdownDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal("c1",                        dto.Id);
		Assert.Equal(1500,                        dto.Total);
		Assert.Equal(12000,                       dto.Budget);
		Assert.Equal("anthropic/claude-sonnet-4", dto.Model);
		Assert.Equal("high",                      dto.Effort);
		Assert.Equal(2,                           dto.Segments.Length);
		Assert.Equal("system",                    dto.Segments[0].Name);
		Assert.Equal(120,                         dto.Segments[0].Tokens);
	}

	[Fact]
	public async Task GetContext_Null_Breakdown_Throws_KeyNotFoundException() {
		this.context.Returned = null;
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.GetContext("c1", force: null, default));
	}

	[Fact]
	public async Task GetContext_Force_Query_Param_Is_Propagated_To_The_Service() {
		this.context.Returned = new ContextBreakdownService.Breakdown(0, 1, new List<ContextBreakdownService.Segment>(), "m", null);
		await this.sut.GetContext("c1", force: true,  default);
		await this.sut.GetContext("c2", force: false, default);
		await this.sut.GetContext("c3", force: null,  default);

		Assert.Equal(3, this.context.Calls.Count);
		Assert.True (this.context.Calls[0].Force);
		Assert.False(this.context.Calls[1].Force);
		Assert.False(this.context.Calls[2].Force);  // null becomes false
	}

	// ── List messages (history) ───────────────────────────────────

	[Fact]
	public async Task ListMessages_Empty_Chat_Returns_Ok_With_Empty_Array() {
		await this.SeedChatAsync("c1", updatedHoursAgo: 0);
		ActionResult<MessageDto[]> result = await this.sut.ListMessages("c1", limit: null, default);
		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Empty(dtos);
	}

	[Fact]
	public async Task ListMessages_Missing_Chat_Throws_KeyNotFoundException() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.ListMessages("ghost", limit: null, default));
	}

	[Fact]
	public async Task ListMessages_Returns_Rows_Oldest_First_With_Token_Counts_Filled() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		await this.SeedMessageAsync(chat, role: "user",      body: "first",  inputTokens: 12, outputTokens: 0);
		await this.SeedMessageAsync(chat, role: "assistant", body: "second", inputTokens: 0,  outputTokens: 34, cacheRead: 7, cacheCreate: 3, thinkingTokens: 2);

		ActionResult<MessageDto[]> result = await this.sut.ListMessages("c1", limit: null, default);
		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);

		Assert.Equal(2, dtos.Length);
		Assert.Equal("first",     dtos[0].Text);
		Assert.Equal("user",      dtos[0].Role);
		Assert.Equal(12,          dtos[0].InputTokens);
		Assert.Equal("second",    dtos[1].Text);
		Assert.Equal("assistant", dtos[1].Role);
		Assert.Equal(34,          dtos[1].OutputTokens);
		Assert.Equal(7,           dtos[1].CacheReadTokens);
		Assert.Equal(3,           dtos[1].CacheCreationTokens);
		Assert.Equal(2,           dtos[1].ThinkingTokens);
	}

	[Fact]
	public async Task ListMessages_Skips_Queued_Rows() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		await this.SeedMessageAsync(chat, role: "user",      body: "live");
		await this.SeedMessageAsync(chat, role: "user",      body: "queued", queued: true);

		ActionResult<MessageDto[]> result = await this.sut.ListMessages("c1", limit: null, default);
		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Single(dtos);
		Assert.Equal("live", dtos[0].Text);
	}

	[Fact]
	public async Task ListMessages_Limit_Takes_The_Most_Recent_Rows() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		for (int i = 0; i < 5; i++) await this.SeedMessageAsync(chat, role: "user", body: $"msg-{i}");

		ActionResult<MessageDto[]> result = await this.sut.ListMessages("c1", limit: 2, default);
		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);
		// Latest two ids, oldest-first ordering => msg-3, msg-4
		Assert.Equal(["msg-3", "msg-4"], dtos.Select(d => d.Text));
	}

	[Fact]
	public async Task ListMessages_Limit_Is_Clamped_Above_500() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		await this.SeedMessageAsync(chat, role: "user", body: "x");
		// Any oversized limit just returns whatever exists (clamping is upper-bound only —
		// no exception, the user gets a normal response).
		ActionResult<MessageDto[]> result = await this.sut.ListMessages("c1", limit: 9999, default);
		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Single(dtos);
	}

	[Fact]
	public async Task ListMessages_Tool_Row_Extracts_Name_And_Args_And_IsError_From_SkContent() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		string skContent = "{\"FunctionName\":\"my_tool\",\"Metadata\":{\"ToolArgs\":\"{\\\"k\\\":\\\"v\\\"}\",\"IsError\":true}}";
		await this.SeedMessageAsync(chat, role: "tool", body: "tool output", skContent: skContent);

		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(
			(await this.sut.ListMessages("c1", limit: null, default)).Result).Value);
		MessageDto dto = Assert.Single(dtos);
		Assert.Equal("my_tool",       dto.ToolName);
		Assert.Equal("{\"k\":\"v\"}", dto.ToolArgs);
		Assert.True (dto.ToolIsError);
	}

	[Fact]
	public async Task ListMessages_Assistant_Row_Extracts_LisThinking_From_SkContent() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		string skContent = "{\"Metadata\":{\"LisThinking\":\"I think therefore I am\"}}";
		await this.SeedMessageAsync(chat, role: "assistant", body: "answer", skContent: skContent);

		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(
			(await this.sut.ListMessages("c1", limit: null, default)).Result).Value);
		Assert.Equal("I think therefore I am", dtos[0].ThinkingContent);
	}

	[Fact]
	public async Task ListMessages_Malformed_SkContent_Falls_Back_To_Null_Extracted_Fields() {
		ChatEntity chat = await this.SeedChatAndReturnAsync("c1");
		await this.SeedMessageAsync(chat, role: "tool", body: "x", skContent: "not json");

		MessageDto[] dtos = Assert.IsType<MessageDto[]>(Assert.IsType<OkObjectResult>(
			(await this.sut.ListMessages("c1", limit: null, default)).Result).Value);
		Assert.Null(dtos[0].ToolName);
		Assert.Null(dtos[0].ToolArgs);
		Assert.Null(dtos[0].ToolIsError);
	}

	// ── Agent: get/set/overrides ──────────────────────────────────

	[Fact]
	public async Task GetAgent_Returns_Bound_Agent_When_Chat_Has_One() {
		AgentEntity bound = await this.SeedAgentAsync("opus",   model: "anthropic/claude-opus-4");
		await this.SeedAgentAsync("default", model: "anthropic/claude-sonnet-4", isDefault: true);
		await this.SeedChatAsync("c1", updatedHoursAgo: 0, agentId: bound.Id);

		ActionResult<AgentDto> result = await this.sut.GetAgent("c1", default);
		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("opus", dto.Name);
	}

	[Fact]
	public async Task GetAgent_Falls_Back_To_System_Default_When_Chat_Has_No_Agent() {
		await this.SeedAgentAsync("default", model: "anthropic/claude-sonnet-4", isDefault: true);
		await this.SeedChatAsync("c1", updatedHoursAgo: 0);

		ActionResult<AgentDto> result = await this.sut.GetAgent("c1", default);
		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("default", dto.Name);
	}

	[Fact]
	public async Task GetAgent_Throws_KeyNotFound_When_Chat_Missing() {
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.GetAgent("ghost", default));
	}

	[Fact]
	public async Task GetAgent_Throws_KeyNotFound_When_Chat_Has_No_Agent_And_No_Default_Exists() {
		await this.SeedChatAsync("c1", updatedHoursAgo: 0);
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.GetAgent("c1", default));
	}

	[Fact]
	public async Task SetAgent_Persists_The_Binding_And_Emits_ChatUpdated() {
		await this.SeedAgentAsync("opus", model: "anthropic/claude-opus-4");
		await this.SeedChatAsync("c1", updatedHoursAgo: 1);
		this.bus.Captured.Clear();

		ActionResult<AgentDto> result = await this.sut.SetAgent("c1", new SetChatAgentRequest("opus"), default);

		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("opus", dto.Name);

		ChatEntity persisted = Assert.Single(this.db.Chats);
		AgentEntity persistedAgent = Assert.Single(this.db.Agents);
		Assert.Equal(persistedAgent.Id, persisted.AgentId);

		Assert.Single(this.bus.Captured);
		Assert.Equal(ChatEventNames.Updated, this.bus.Captured[0].Name);
	}

	[Fact]
	public async Task SetAgent_Rejects_Missing_Agent_With_KeyNotFoundException() {
		await this.SeedChatAsync("c1", updatedHoursAgo: 1);
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.SetAgent("c1", new SetChatAgentRequest("nope"), default));
	}

	[Fact]
	public async Task SetAgent_Rejects_Empty_Agent_Name_With_ArgumentException() {
		await this.SeedChatAsync("c1", updatedHoursAgo: 1);
		await Assert.ThrowsAsync<ArgumentException>(() => this.sut.SetAgent("c1", new SetChatAgentRequest("   "), default));
	}

	[Fact]
	public async Task SetAgent_Missing_Chat_Throws_KeyNotFoundException() {
		await this.SeedAgentAsync("opus", model: "x");
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.SetAgent("ghost", new SetChatAgentRequest("opus"), default));
	}

	[Fact]
	public async Task SaveOverrides_Mutates_The_Bound_Agent_When_Chat_Has_One_And_Emits_AgentUpdated() {
		AgentEntity bound = await this.SeedAgentAsync("opus", model: "anthropic/claude-opus-4", effort: "low", toolProfile: "default");
		await this.SeedChatAsync("c1", updatedHoursAgo: 1, agentId: bound.Id);
		this.bus.Captured.Clear();

		ActionResult<AgentDto> result = await this.sut.SaveOverrides("c1", new SaveChatOverridesRequest(
			Model: "  anthropic/claude-sonnet-4  ", Effort: "high", ToolProfile: "  read-only  "
		), default);

		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("anthropic/claude-sonnet-4", dto.Model);
		Assert.Equal("high",                      dto.ThinkingEffort);
		Assert.Equal("read-only",                 dto.ToolProfile);

		Assert.Single(this.bus.Captured);
		Assert.Equal(AgentEventNames.Updated, this.bus.Captured[0].Name);
		Assert.IsType<AgentUpdatedPayload>(this.bus.Captured[0].Data);
	}

	[Fact]
	public async Task SaveOverrides_Falls_Back_To_System_Default_When_Chat_Has_No_Bound_Agent() {
		await this.SeedAgentAsync("default", model: "anthropic/claude-sonnet-4", isDefault: true);
		await this.SeedChatAsync("c1", updatedHoursAgo: 1);

		ActionResult<AgentDto> result = await this.sut.SaveOverrides("c1", new SaveChatOverridesRequest(
			Model: "anthropic/claude-opus-4", Effort: null, ToolProfile: null
		), default);
		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("default",                 dto.Name);
		Assert.Equal("anthropic/claude-opus-4", dto.Model);
	}

	[Fact]
	public async Task SaveOverrides_Null_Fields_Leave_Agent_Unchanged() {
		await this.SeedAgentAsync("default", model: "anthropic/claude-sonnet-4", effort: "med", toolProfile: "default", isDefault: true);
		await this.SeedChatAsync("c1", updatedHoursAgo: 1);

		ActionResult<AgentDto> result = await this.sut.SaveOverrides("c1", new SaveChatOverridesRequest(null, null, null), default);
		AgentDto dto = Assert.IsType<AgentDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
		Assert.Equal("anthropic/claude-sonnet-4", dto.Model);
		Assert.Equal("med",                       dto.ThinkingEffort);
		Assert.Equal("default",                   dto.ToolProfile);
	}

	[Fact]
	public async Task SaveOverrides_Missing_Chat_Throws_KeyNotFoundException() {
		await this.SeedAgentAsync("default", model: "x", isDefault: true);
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.SaveOverrides("ghost", new SaveChatOverridesRequest(null, null, null), default));
	}

	[Fact]
	public async Task SaveOverrides_Chat_Without_Agent_And_No_Default_Throws_KeyNotFoundException() {
		await this.SeedChatAsync("c1", updatedHoursAgo: 1);
		await Assert.ThrowsAsync<KeyNotFoundException>(() => this.sut.SaveOverrides("c1", new SaveChatOverridesRequest(null, null, null), default));
	}

	// ── Generate ──────────────────────────────────────────────────

	[Fact]
	public async Task Generate_Calls_Generation_Service_With_Composed_Request_And_Returns_The_Text() {
		this.generation.ReturnedText  = "{\"subject\":\"feat: thing\",\"highlights\":[\"a\",\"b\"]}";
		this.generation.InputTokens   = 123;
		this.generation.OutputTokens  = 45;

		ActionResult<GenerateResponseDto> result = await this.sut.Generate(
			"chat-1",
			new GenerateRequest(
				VisiblePrompt: "Summarise the staged changes",
				HiddenPrompt:  "{ subject: string, highlights: string[] }",
				Kind:          "commit",
				Model:         "claude-sonnet-4"
			),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		GenerateResponseDto dto = Assert.IsType<GenerateResponseDto>(ok.Value);
		Assert.Equal(this.generation.ReturnedText, dto.Text);
		Assert.Equal(123,                          dto.InputTokens);
		Assert.Equal(45,                           dto.OutputTokens);

		ChatGenerationService.GenerationRequest forwarded = Assert.Single(this.generation.Calls);
		Assert.Equal("chat-1",                                       forwarded.ChatExternalId);
		Assert.Equal("Summarise the staged changes",                 forwarded.VisiblePrompt);
		Assert.Equal("{ subject: string, highlights: string[] }",    forwarded.HiddenPrompt);
		Assert.Equal("commit",                                       forwarded.Kind);
		Assert.Equal("claude-sonnet-4",                              forwarded.ModelOverride);
	}

	[Fact]
	public async Task Generate_Defaults_Kind_To_Generate_When_Omitted() {
		await this.sut.Generate(
			"chat-1",
			new GenerateRequest(VisiblePrompt: "hi", HiddenPrompt: null, Kind: null, Model: null),
			default
		);

		ChatGenerationService.GenerationRequest forwarded = Assert.Single(this.generation.Calls);
		Assert.Equal("generate", forwarded.Kind);
	}

	[Fact]
	public async Task Create_Idempotent_Hit_Includes_Project_So_Dto_Carries_ProjectExternalId() {
		await this.SeedProjectAsync("p1", "/r");
		await this.sut.Create(
			new CreateChatRequest(ChatId: "with-proj", Name: "x", ProjectExternalId: "p1", Channel: null),
			default
		);

		ActionResult<ChatDto> result = await this.sut.Create(
			new CreateChatRequest(ChatId: "with-proj", Name: "x", ProjectExternalId: "p1", Channel: null),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ChatDto dto = Assert.IsType<ChatDto>(ok.Value);
		Assert.Equal("p1", dto.ProjectExternalId); // require the Include(c => c.Project) round-trip
	}

	// ── Helpers ────────────────────────────────────────────────────

	private async Task SeedChatAsync(
		string  externalId,
		double  updatedHoursAgo,
		double? archivedHoursAgo = null,
		long?   projectId        = null,
		long?   agentId          = null
	) {
		DateTimeOffset now = this.time.GetUtcNow();
		this.db.Chats.Add(new ChatEntity {
			ExternalId = externalId,
			Name       = externalId,
			Channel    = "web",
			ProjectId  = projectId,
			AgentId    = agentId,
			CreatedAt  = now.AddHours(-updatedHoursAgo - 1),
			UpdatedAt  = now.AddHours(-updatedHoursAgo),
			ArchivedAt = archivedHoursAgo is null ? null : now.AddHours(-archivedHoursAgo.Value),
			Enabled    = true,
		});
		await this.db.SaveChangesAsync();
	}

	private async Task<ChatEntity> SeedChatAndReturnAsync(string externalId) {
		ChatEntity chat = new() {
			ExternalId = externalId,
			Name       = externalId,
			Channel    = "web",
			CreatedAt  = this.time.GetUtcNow(),
			UpdatedAt  = this.time.GetUtcNow(),
			Enabled    = true,
		};
		this.db.Chats.Add(chat);
		await this.db.SaveChangesAsync();
		return chat;
	}

	private async Task SeedMessageAsync(
		ChatEntity chat,
		string     role,
		string?    body            = null,
		int?       inputTokens     = null,
		int?       outputTokens    = null,
		int?       cacheRead       = null,
		int?       cacheCreate     = null,
		int?       thinkingTokens  = null,
		string?    skContent       = null,
		bool       queued          = false
	) {
		this.db.Messages.Add(new MessageEntity {
			Chat                = chat,
			ChatId              = chat.Id,
			ExternalId          = Guid.NewGuid().ToString("N"),
			Role                = role,
			Body                = body,
			Timestamp           = this.time.GetUtcNow(),
			IsFromMe            = role == "assistant",
			SenderId            = "tester",
			InputTokens         = inputTokens,
			OutputTokens        = outputTokens,
			CacheReadTokens     = cacheRead,
			CacheCreationTokens = cacheCreate,
			ThinkingTokens      = thinkingTokens,
			SkContent           = skContent,
			Queued              = queued,
			CreatedAt           = this.time.GetUtcNow(),
		});
		await this.db.SaveChangesAsync();
	}

	private async Task<AgentEntity> SeedAgentAsync(
		string  name,
		string  model,
		bool    isDefault   = false,
		string? effort      = null,
		string? toolProfile = null
	) {
		DateTimeOffset now = this.time.GetUtcNow();
		AgentEntity agent = new() {
			Name           = name,
			Model          = model,
			IsDefault      = isDefault,
			ThinkingEffort = effort,
			ToolProfile    = toolProfile,
			MaxTokens      = 4096,
			ContextBudget  = 12000,
			CreatedAt      = now,
			UpdatedAt      = now,
		};
		this.db.Agents.Add(agent);
		await this.db.SaveChangesAsync();
		return agent;
	}

	private async Task<ProjectEntity> SeedProjectAsync(string externalId, string path) {
		ProjectEntity project = new() {
			ExternalId = externalId,
			Path       = path,
			AddedAt    = this.time.GetUtcNow(),
			CreatedAt  = this.time.GetUtcNow(),
			UpdatedAt  = this.time.GetUtcNow(),
		};
		this.db.Projects.Add(project);
		await this.db.SaveChangesAsync();
		return project;
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	private sealed class CapturingEventBus :IEventBus {
		public List<ServerEvent> Captured { get; } = [];
		private long nextId;

		public ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
			this.Captured.Add(ev with { Id = Interlocked.Increment(ref this.nextId) });
			return ValueTask.CompletedTask;
		}

		public IEventSubscription Subscribe(int capacity = 256) =>
			throw new NotSupportedException("Tests do not subscribe directly.");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];
	}

	/// <summary>
	/// Stand-in for <see cref="IConversationService"/>. The controller only uses it
	/// for the runtime cast inside <c>Abort</c>; this stub fails that cast so the
	/// abort no-ops (the right behaviour for any non-MessageDebouncer impl).
	/// </summary>
	private sealed class NoOpConversationService :IConversationService {
		public Task HandleIncomingAsync(IncomingMessage message, CancellationToken ct) => Task.CompletedTask;
		public Task HandleTypingAsync(string chatId, CancellationToken ct) => Task.CompletedTask;
		public Task HandleSentEchoAsync(IncomingMessage echo, CancellationToken ct) => Task.CompletedTask;
		public Task HandleReactionAsync(string messageId, string chatId, string emoji, string senderId, CancellationToken ct) => Task.CompletedTask;
		public Task<string?> ResolvePhoneToNameAsync(string chatId, string phone, CancellationToken ct) => Task.FromResult<string?>(null);
		public Task<string?> ResolveNameToPhoneAsync(string chatId, string name, string? preferSenderId, CancellationToken ct) => Task.FromResult<string?>(null);
	}

	/// <summary>
	/// Captures the chat id + force flag and returns a canned breakdown. The
	/// real service touches kernel + token counter + DB — way too much to set
	/// up for a controller unit test.
	/// </summary>
	private sealed class FakeContextBreakdown :IContextBreakdownService {
		public List<(string ChatId, bool Force)> Calls { get; } = [];
		public ContextBreakdownService.Breakdown? Returned { get; set; }

		public Task<ContextBreakdownService.Breakdown?> ComputeAsync(string chatId, CancellationToken ct, bool forceRefresh = false) {
			this.Calls.Add((chatId, forceRefresh));
			return Task.FromResult(this.Returned);
		}
	}

	/// <summary>
	/// Captures generation requests + returns a canned <c>{"text":"..."}</c> body so
	/// the Generate test can assert the prompt/model wiring without spinning up a real LLM.
	/// </summary>
	private sealed class FakeGenerationService :IChatGenerationService {
		public List<ChatGenerationService.GenerationRequest> Calls { get; } = [];
		public string ReturnedText  { get; set; } = "{\"subject\":\"chore: stub\",\"highlights\":[]}";
		public int    InputTokens   { get; set; }
		public int    OutputTokens  { get; set; }

		public Task<ChatGenerationService.GenerationResult> GenerateAsync(
			ChatGenerationService.GenerationRequest request,
			CancellationToken                        ct
		) {
			this.Calls.Add(request);
			return Task.FromResult(new ChatGenerationService.GenerationResult(this.ReturnedText, this.InputTokens, this.OutputTokens));
		}
	}
}
