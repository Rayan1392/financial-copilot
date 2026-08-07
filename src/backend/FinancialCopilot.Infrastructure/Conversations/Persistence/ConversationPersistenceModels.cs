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

public sealed class ConversationTaskStateRow
{
    public Guid ConversationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? LastCorrelationId { get; set; }
    public string StateJson { get; set; } = string.Empty;

    public static ConversationTaskStateRow From(FinancialCopilot.Application.AI.Orchestration.ConversationTaskState state) => new()
    {
        ConversationId = state.ConversationId, TenantId = state.TenantId, ActorId = state.ActorId,
        Version = state.Version, UpdatedAt = state.UpdatedAt, ExpiresAt = state.ExpiresAt,
        LastCorrelationId = state.LastCorrelationId,
        StateJson = System.Text.Json.JsonSerializer.Serialize(state)
    };
}
