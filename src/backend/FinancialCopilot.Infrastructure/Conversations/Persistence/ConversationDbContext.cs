using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

public sealed class ConversationDbContext(DbContextOptions<ConversationDbContext> options) : DbContext(options)
{
    public DbSet<ConversationRow> Conversations => Set<ConversationRow>();

    public DbSet<MessageRow> Messages => Set<MessageRow>();
    public DbSet<ConversationTaskStateRow> ConversationTaskStates => Set<ConversationTaskStateRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ConversationDbContext).Assembly,
            type => type.Namespace == typeof(ConversationDbContext).Namespace);
}
