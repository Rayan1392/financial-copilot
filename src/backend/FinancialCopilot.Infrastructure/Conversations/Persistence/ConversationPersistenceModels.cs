namespace FinancialCopilot.Infrastructure.Conversations.Persistence;

public sealed class ConversationRow
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ActorId { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int MessageCount { get; set; }

    public string Title { get; set; } = string.Empty;
}

public sealed class MessageRow
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? ScannerQueryPlanJson { get; set; }

    public string? AssistantPayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
