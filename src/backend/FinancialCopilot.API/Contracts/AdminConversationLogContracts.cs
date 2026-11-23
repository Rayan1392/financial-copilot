namespace FinancialCopilot.API.Contracts;

// Feature 164: admin AI conversation logs. Status and channel are strings (see AdminConversationLogStatus and
// AdminConversationLogChannel); times are UTC.

/// <summary>
/// <see cref="ActorKind"/> is <c>User</c>, <c>ApiClient</c> or <c>Unknown</c> (see AdminConversationLogActorKind);
/// <see cref="HasUserAccount"/> is true exactly for <c>User</c> and is kept for older clients.
/// </summary>
public sealed record AdminConversationLogUserHttpResponse(
    Guid UserId,
    string? DisplayName,
    string? Email,
    string? MaskedMobile,
    bool HasUserAccount,
    string ActorKind,
    string? ApiClientName);

public sealed record AdminConversationLogItemHttpResponse(
    Guid TurnId,
    Guid ConversationId,
    string? ConversationTitle,
    AdminConversationLogUserHttpResponse User,
    string QuestionPreview,
    string? AnswerPreview,
    DateTimeOffset QuestionCreatedAtUtc,
    DateTimeOffset? AnswerCreatedAtUtc,
    string Status,
    string Channel);

public sealed record AdminConversationLogPageHttpResponse(
    IReadOnlyList<AdminConversationLogItemHttpResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminConversationLogUsageHttpResponse(
    string? ProviderName,
    string? ModelName,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal CreditsCharged,
    string CompletionStatus,
    string? ExecutionRoute);

/// <summary>
/// <see cref="AssistantContent"/> is the stored answer mapped exactly as the chat's messages endpoint maps it, without
/// the scanner plan, usage and memory disclosures (usage is given separately, without cost).
/// </summary>
public sealed record AdminConversationLogAnswerHttpResponse(
    Guid MessageId,
    string Content,
    DateTimeOffset CreatedAtUtc,
    string Status,
    string? OutcomeReasonCode,
    AdminConversationLogUsageHttpResponse? Usage,
    AssistantMessageContentResponse? AssistantContent);

public sealed record AdminConversationLogTurnHttpResponse(
    Guid TurnId,
    Guid ConversationId,
    string? ConversationTitle,
    string Channel,
    AdminConversationLogUserHttpResponse User,
    string Question,
    DateTimeOffset QuestionCreatedAtUtc,
    string Status,
    IReadOnlyList<AdminConversationLogAnswerHttpResponse> Answers);
