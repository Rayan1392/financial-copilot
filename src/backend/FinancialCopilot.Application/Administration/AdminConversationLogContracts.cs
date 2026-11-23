using FinancialCopilot.Application.Conversations;

namespace FinancialCopilot.Application.Administration;

// Feature 164: cross-user, read-only view of question/answer turns for administrators.

/// <summary>The status of a turn: the outcome of its first answer, or <see cref="NoResponse"/> when it has none.</summary>
public enum AdminConversationLogStatus
{
    Answered,
    PartialAnswer,
    ClarificationNeeded,
    DisambiguationNeeded,
    NoData,
    Unsupported,
    TemporarilyUnavailable,
    Failed,
    NoResponse
}

public enum AdminConversationLogChannel
{
    Web,
    Telegram
}

/// <summary>
/// List filters. <see cref="FromUtc"/> is inclusive and <see cref="ToUtc"/> exclusive, both on the question time.
/// <see cref="User"/> is a user id, an Iranian mobile number, or a fragment of an email or name.
/// </summary>
public sealed record AdminConversationLogQuery(
    int Page = 1,
    int PageSize = AdminConversationLogQueryRules.DefaultPageSize,
    string? Text = null,
    string? User = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    AdminConversationLogStatus? Status = null);

/// <summary>What a conversation's actor ID resolves to in the active tenant.</summary>
public enum AdminConversationLogActorKind
{
    /// <summary>A user account that is a member of the tenant; Customer 360 is available.</summary>
    User,

    /// <summary>An API client configured for the tenant; it has no customer profile.</summary>
    ApiClient,

    /// <summary>Neither a tenant member nor a configured API client of the tenant.</summary>
    Unknown
}

/// <summary>
/// The actor who asked. <see cref="UserId"/> is the conversation's actor ID whatever its kind. Profile fields are set
/// only for <see cref="AdminConversationLogActorKind.User"/>; <see cref="ApiClientName"/> only for
/// <see cref="AdminConversationLogActorKind.ApiClient"/>. The mobile number is always masked.
/// </summary>
public sealed record AdminConversationLogUser(
    Guid UserId,
    string? DisplayName,
    string? Email,
    string? MaskedMobile,
    AdminConversationLogActorKind ActorKind,
    string? ApiClientName = null)
{
    public bool HasUserAccount => ActorKind == AdminConversationLogActorKind.User;
}

public sealed record AdminConversationLogItem(
    Guid TurnId,
    Guid ConversationId,
    string? ConversationTitle,
    AdminConversationLogUser User,
    string QuestionPreview,
    string? AnswerPreview,
    DateTimeOffset QuestionCreatedAtUtc,
    DateTimeOffset? AnswerCreatedAtUtc,
    AdminConversationLogStatus Status,
    AdminConversationLogChannel Channel);

public sealed record AdminConversationLogPage(
    IReadOnlyList<AdminConversationLogItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>Usage recorded with an answer. Cost and pricing-policy fields are deliberately not exposed.</summary>
public sealed record AdminConversationLogUsage(
    string? ProviderName,
    string? ModelName,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal CreditsCharged,
    string CompletionStatus,
    string? ExecutionRoute);

/// <summary>
/// One stored answer. <see cref="Payload"/> is the persisted structured answer, mapped for display by the same mapper
/// the chat uses; it is never regenerated.
/// </summary>
public sealed record AdminConversationLogAnswer(
    Guid MessageId,
    string Content,
    DateTimeOffset CreatedAtUtc,
    AdminConversationLogStatus Status,
    string? OutcomeReasonCode,
    AdminConversationLogUsage? Usage,
    AssistantMessagePayload? Payload);

public sealed record AdminConversationLogTurn(
    Guid TurnId,
    Guid ConversationId,
    string? ConversationTitle,
    AdminConversationLogChannel Channel,
    AdminConversationLogUser User,
    string Question,
    DateTimeOffset QuestionCreatedAtUtc,
    AdminConversationLogStatus Status,
    IReadOnlyList<AdminConversationLogAnswer> Answers);

public interface IAdminConversationLogQueries
{
    /// <exception cref="AdminConversationLogUserSearchTooBroadException">The user filter matches too many users.</exception>
    Task<AdminConversationLogPage> GetPageAsync(
        Guid tenantId,
        AdminConversationLogQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the full turn, or null when the id is not a question of the tenant. A content-free audit record is
    /// written before any content is read; when it cannot be written, the call fails and no content is returned.
    /// </summary>
    Task<AdminConversationLogTurn?> GetTurnAsync(
        Guid tenantId,
        Guid administratorId,
        string administratorType,
        string correlationId,
        Guid turnId,
        CancellationToken cancellationToken);
}

public sealed class AdminConversationLogUserSearchTooBroadException()
    : Exception($"The user filter matches more than {AdminConversationLogQueryRules.MaximumMatchedUsers} users.")
{
    public const string Code = "user_search_too_broad";
}
