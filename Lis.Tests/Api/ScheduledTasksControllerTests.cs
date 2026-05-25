using Lis.Api.Controllers;
using Lis.Api.Schemas.Events;
using Lis.Api.Schemas.Scheduled;
using Lis.Api.Services;
using Lis.Core.Events;
using Lis.Persistence;
using Lis.Persistence.Entities;
using Lis.Persistence.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Lis.Tests.Api;

public sealed class ScheduledTasksControllerTests :IDisposable {
	private readonly LisDbContext              db;
	private readonly FakeTimeProvider          time;
	private readonly CapturingEventBus         bus;
	private readonly ScheduledTaskService      service;
	private readonly ScheduledTaskCoordinator  coordinator;
	private readonly ScheduledTasksController  sut;

	public ScheduledTasksControllerTests() {
		DbContextOptions<LisDbContext> options = new DbContextOptionsBuilder<LisDbContext>()
			.UseInMemoryDatabase($"tasks-api-{Guid.NewGuid()}")
			.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
			.Options;
		this.db          = new TestDbContext(options);
		this.time        = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
		this.bus         = new CapturingEventBus();
		this.service     = new ScheduledTaskService(this.db, this.time);
		this.coordinator = new ScheduledTaskCoordinator(NullLogger<ScheduledTaskCoordinator>.Instance);
		this.sut         = new ScheduledTasksController(this.service, this.db, this.coordinator, this.bus, this.time);
	}

	public void Dispose() {
		this.db.Dispose();
		GC.SuppressFinalize(this);
	}

	private sealed class TestDbContext(DbContextOptions<LisDbContext> options) :LisDbContext(options) {
		protected override void OnModelCreating(ModelBuilder modelBuilder) {
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<MemoryEntity>().Ignore(e => e.Embedding);
			modelBuilder.Entity<SessionEntity>().Ignore(e => e.SummaryEmbedding);
		}
	}

	[Fact]
	public async Task Create_Returns_CreatedAtAction_And_Emits_Created_Event() {
		ActionResult<ScheduledTaskDto> result = await this.sut.Create(
			new CreateScheduledTaskRequest(
				Name:           "every minute",
				CronExpression: "* * * * *",
				Prompt:         "x",
				ProjectId:      null,
				AgentName:      null,
				Model:          null,
				Timezone:       null,
				Enabled:        true
			),
			default
		);

		CreatedAtActionResult created = Assert.IsType<CreatedAtActionResult>(result.Result);
		ScheduledTaskDto dto = Assert.IsType<ScheduledTaskDto>(created.Value);
		Assert.Equal("every minute", dto.Name);
		Assert.NotNull(dto.NextRunAt);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ScheduledTaskEventNames.Created, ev.Name);
	}

	[Fact]
	public async Task Update_Returns_Ok_And_Emits_Updated_Event() {
		ScheduledTaskEntity task = await this.service.CreateAsync(new CreateScheduledTaskInput(
			Name: "n", CronExpression: "* * * * *", Prompt: "x"
		));

		ActionResult<ScheduledTaskDto> result = await this.sut.Update(
			task.ExternalId,
			new UpdateScheduledTaskRequest(Name: "renamed", CronExpression: null, Prompt: null, ProjectId: null, AgentName: null, Model: null, Timezone: null, Enabled: null),
			default
		);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ScheduledTaskDto dto = Assert.IsType<ScheduledTaskDto>(ok.Value);
		Assert.Equal("renamed", dto.Name);

		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ScheduledTaskEventNames.Updated, ev.Name);
	}

	[Fact]
	public async Task Delete_Returns_NoContent_And_Emits_Deleted_Event() {
		ScheduledTaskEntity task = await this.service.CreateAsync(new CreateScheduledTaskInput(
			Name: "gone", CronExpression: "* * * * *", Prompt: "x"
		));

		IActionResult result = await this.sut.Delete(task.ExternalId, default);

		Assert.IsType<NoContentResult>(result);
		ServerEvent ev = Assert.Single(this.bus.Captured);
		Assert.Equal(ScheduledTaskEventNames.Deleted, ev.Name);
		ScheduledTaskDeletedPayload payload = Assert.IsType<ScheduledTaskDeletedPayload>(ev.Data);
		Assert.Equal(task.ExternalId, payload.TaskId);
	}

	[Fact]
	public async Task Delete_Missing_Returns_NotFound() {
		IActionResult result = await this.sut.Delete("absent", default);
		Assert.IsType<NotFoundObjectResult>(result);
		Assert.Empty(this.bus.Captured);
	}

	[Fact]
	public async Task ListForProject_Returns_Only_Project_Tasks() {
		ProjectService projects = new(this.db, this.time);
		ProjectEntity project = await projects.CreateAsync("/x");
		await this.service.CreateAsync(new CreateScheduledTaskInput(Name: "a", CronExpression: "* * * * *", Prompt: "x", ProjectExternalId: project.ExternalId));
		await this.service.CreateAsync(new CreateScheduledTaskInput(Name: "b", CronExpression: "* * * * *", Prompt: "x"));

		ActionResult<ScheduledTaskDto[]> result = await this.sut.ListForProject(project.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ScheduledTaskDto[] dtos = Assert.IsType<ScheduledTaskDto[]>(ok.Value);
		Assert.Single(dtos);
		Assert.Equal("a",                  dtos[0].Name);
		Assert.Equal(project.ExternalId,   dtos[0].ProjectId);
	}

	[Fact]
	public async Task Get_Includes_Project_External_Id_When_Bound() {
		ProjectService projects = new(this.db, this.time);
		ProjectEntity project = await projects.CreateAsync("/x");
		ScheduledTaskEntity task = await this.service.CreateAsync(new CreateScheduledTaskInput(
			Name: "n", CronExpression: "* * * * *", Prompt: "x", ProjectExternalId: project.ExternalId
		));

		ActionResult<ScheduledTaskDto> result = await this.sut.Get(task.ExternalId, default);

		OkObjectResult ok = Assert.IsType<OkObjectResult>(result.Result);
		ScheduledTaskDto dto = Assert.IsType<ScheduledTaskDto>(ok.Value);
		Assert.Equal(project.ExternalId, dto.ProjectId);
	}

	private sealed class CapturingEventBus :IEventBus {
		public List<ServerEvent> Captured { get; } = [];
		private long nextId;

		public ValueTask PublishAsync(ServerEvent ev, CancellationToken ct = default) {
			this.Captured.Add(ev with { Id = Interlocked.Increment(ref this.nextId) });
			return ValueTask.CompletedTask;
		}

		public IEventSubscription Subscribe(int capacity = 256, Func<ServerEvent, bool>? filter = null) =>
			throw new NotSupportedException("Tests do not subscribe directly.");

		public IReadOnlyList<ServerEvent> ReplaySince(long? lastEventId) => [];
	}
}
