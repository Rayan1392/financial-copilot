namespace FinancialCopilot.API.Contracts;

public sealed record GrantConsentRequest(
    string MemoryType,
    string Purpose,
    DateTimeOffset? ExpiresAt = null);

public sealed record ConsentStatusResponse(
    string MemoryType,
    string Purpose,
    string Status,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? ExpiresAt);

public sealed record MemoryRecordResponse(
    Guid MemoryId,
    string Type,
    string Purpose,
    string Sensitivity,
    string Summary,
    DateTimeOffset CapturedAt,
    DateTimeOffset? ExpiresAt);

public sealed record WriteMemoryRecordRequest(
    string Type,
    string Purpose,
    string Sensitivity,
    string Summary);

public sealed record WriteMemoryRecordResponse(Guid MemoryId);

public sealed record MemoryDisclosureResponse(
    string Type,
    string Purpose,
    string Explanation);
