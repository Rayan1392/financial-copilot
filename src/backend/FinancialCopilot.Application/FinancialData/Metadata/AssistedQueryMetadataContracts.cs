namespace FinancialCopilot.Application.FinancialData.Metadata;

public sealed record PeriodMetadataItem(
    string Code,
    string DisplayName,
    string DisplayNameFa);

public sealed record SymbolMetadataItem(
    string ExternalCompanyId,
    string SymbolCode,
    string CompanyName,
    string? CompanyNameEnglish,
    string? IndustryName);

public sealed record IndustryMetadataItem(
    string IndustryId,
    string DisplayName);

public interface IAssistedQueryMetadataService
{
    IReadOnlyCollection<PeriodMetadataItem> GetPeriods();

    Task<IReadOnlyCollection<SymbolMetadataItem>> SearchSymbolsAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<IndustryMetadataItem>> SearchIndustriesAsync(
        string? search,
        int limit,
        CancellationToken cancellationToken);
}
