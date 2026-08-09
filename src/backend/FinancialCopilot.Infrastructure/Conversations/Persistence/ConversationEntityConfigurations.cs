using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

public sealed class ConversationRowConfiguration : IEntityTypeConfiguration<ConversationRow>
{
    public void Configure(EntityTypeBuilder<ConversationRow> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.UpdatedAt });
        builder.Property(row => row.Title).HasMaxLength(160);
    }
}

public sealed class MessageRowConfiguration : IEntityTypeConfiguration<MessageRow>
{
    public void Configure(EntityTypeBuilder<MessageRow> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ConversationId, row.CreatedAt });
        builder.Property(row => row.Role).HasMaxLength(20);
    }
}

public sealed class ConversationTaskStateRowConfiguration : IEntityTypeConfiguration<ConversationTaskStateRow>
{
    public void Configure(EntityTypeBuilder<ConversationTaskStateRow> builder)
    {
        builder.ToTable("ConversationTaskStates");
        builder.HasKey(row => row.ConversationId);
        builder.HasIndex(row => new { row.TenantId, row.ActorId, row.ExpiresAt });
        builder.Property(row => row.LastCorrelationId).HasMaxLength(200);
        builder.Property(row => row.StateJson).IsRequired();
    }
}
