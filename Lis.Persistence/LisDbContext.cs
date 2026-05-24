using Lis.Persistence.Entities;

using Microsoft.EntityFrameworkCore;

namespace Lis.Persistence;

public class LisDbContext(DbContextOptions<LisDbContext> options) :DbContext(options) {
	public DbSet<ChatEntity>                Chats               { get; init; } = null!;
	public DbSet<ProjectEntity>             Projects            { get; init; } = null!;
	public DbSet<ProjectMetadataEntity>     ProjectMetadata     { get; init; } = null!;
	public DbSet<SessionFolderEntity>       SessionFolders      { get; init; } = null!;
	public DbSet<SessionFolderItemEntity>   SessionFolderItems  { get; init; } = null!;
	public DbSet<MessageEntity>             Messages            { get; init; } = null!;
	public DbSet<ContactEntity>             Contacts            { get; init; } = null!;
	public DbSet<ContactIdentifierEntity>   ContactIdentifiers  { get; init; } = null!;
	public DbSet<PromptSectionEntity>       PromptSections      { get; init; } = null!;
	public DbSet<MemoryEntity>              Memories            { get; init; } = null!;
	public DbSet<SessionEntity>             Sessions            { get; init; } = null!;
	public DbSet<AgentEntity>               Agents              { get; init; } = null!;
	public DbSet<ChatAllowedSenderEntity>   ChatAllowedSenders  { get; init; } = null!;
	public DbSet<ExecApprovalEntity>        ExecApprovals       { get; init; } = null!;
	public DbSet<ExecAllowlistEntity>       ExecAllowlist       { get; init; } = null!;
	public DbSet<ScheduledTaskEntity>       ScheduledTasks      { get; init; } = null!;
	public DbSet<GitHubIdentityEntity>      GitHubIdentities    { get; init; } = null!;
	public DbSet<GitIdentityEntity>         GitIdentities       { get; init; } = null!;
	public DbSet<SettingEntity>             Settings            { get; init; } = null!;

	protected override void OnModelCreating(ModelBuilder modelBuilder) {
		modelBuilder.HasPostgresExtension("vector");
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(ChatEntity).Assembly);
	}
}
