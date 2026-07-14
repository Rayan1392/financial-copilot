namespace FinancialCopilot.API.Contracts;

public sealed record ProfessionalScannerScopeRequest(string? IndustryCode = null, string? InstrumentClass = null);

public sealed record ExecuteProfessionalFilterRequest(
    IReadOnlyDictionary<string, string>? Parameters,
    DateOnly? FromDate,
    DateOnly? ToDate,
    ProfessionalScannerScopeRequest? Scope,
    int Page = 1,
    int PageSize = 20,
    string? FilterVersion = null);

public sealed record SaveProfessionalFilterRequest(
    string Name, string FilterCodeOrAlias, string? FilterVersion,
    IReadOnlyDictionary<string, string>? Parameters);

public sealed record UpdateProfessionalFilterRequest(
    int ExpectedVersion, string Name, string FilterCodeOrAlias, string? FilterVersion,
    IReadOnlyDictionary<string, string>? Parameters);

public sealed record RunSavedProfessionalFilterRequest(
    DateOnly? FromDate, DateOnly? ToDate, ProfessionalScannerScopeRequest? Scope,
    int Page = 1, int PageSize = 20);
