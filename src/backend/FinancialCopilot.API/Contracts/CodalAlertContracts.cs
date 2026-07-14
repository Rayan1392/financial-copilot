namespace FinancialCopilot.API.Contracts;

public sealed record CodalAlertSubscriptionRequest(
    string ExternalCompanyId,
    IReadOnlyCollection<string>? AnnouncementTypes = null,
    string MinimumImportance = "Notice",
    bool RawAlertEnabled = true,
    bool AiSummaryEnabled = false);

public sealed record UpdateCodalAlertSubscriptionRequest(
    IReadOnlyCollection<string>? AnnouncementTypes = null,
    string MinimumImportance = "Notice",
    bool RawAlertEnabled = true,
    bool AiSummaryEnabled = false,
    string State = "Active");

public sealed record CodalAlertSubscriptionsResponse(
    IReadOnlyCollection<CodalAlertSubscriptionResponse> Items);

public sealed record CodalAlertSubscriptionResponse(
    Guid Id,
    string ExternalCompanyId,
    string Symbol,
    string CompanyName,
    IReadOnlyCollection<string> AnnouncementTypes,
    string MinimumImportance,
    bool RawAlertEnabled,
    bool AiSummaryEnabled,
    string State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CodalAlertSummaryResponse(
    Guid Id,
    Guid InsightEventId,
    string Status,
    string? SummaryText,
    string EvidenceHash,
    string PromptPolicyVersion,
    string? ProviderName,
    string? ModelName,
    string? FailureReason,
    DateTimeOffset UpdatedAtUtc);
