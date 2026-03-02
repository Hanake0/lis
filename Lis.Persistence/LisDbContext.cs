using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence;

public sealed class LisDbContext(DbContextOptions<LisDbContext> options) :DbContext(options) {
	public DbSet<ChatEntity>                Chats              { get; init; } = null!;
	public DbSet<MessageEntity>             Messages           { get; init; } = null!;
	public DbSet<ContactEntity>             Contacts           { get; init; } = null!;
	public DbSet<ContactIdentifierEntity>   ContactIdentifiers { get; init; } = null!;
	public DbSet<PromptSectionEntity>       PromptSections     { get; init; } = null!;
	public DbSet<MemoryEntity>              Memories           { get; init; } = null!;
	public DbSet<SessionEntity>             Sessions           { get; init; } = null!;

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		modelBuilder.HasPostgresExtension("vector");
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatEntity).Assembly);
	}
}
