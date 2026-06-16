using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Domain.Financial.ValueObjects;

namespace FinancialCopilot.Domain.Financial.Entities;

public sealed record ProviderExternalReference
{
    public ProviderExternalReference(string providerName, string externalId)
    {
        ProviderName = RequireText(providerName, nameof(providerName));
        ExternalId = RequireText(externalId, nameof(externalId));
    }

    public string ProviderName { get; }

    public string ExternalId { get; }

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("External reference value is required.", parameterName)
            : value.Trim();
}

public sealed record FinancialSourceEvidence(
    string SourceProvider,
    DateTimeOffset SourceObservedAt,
    DateTimeOffset LastSynchronizedAt,
    string? SourceDocumentId = null);

public sealed class Industry
{
    public Industry(Guid id, string name)
    {
        Id = RequireId(id, nameof(id));
        Name = RequireText(name, nameof(name));
    }

    public Guid Id { get; }

    public string Name { get; }

    private static Guid RequireId(Guid id, string parameterName) =>
        id == Guid.Empty ? throw new ArgumentException("Industry id is required.", parameterName) : id;

    private static string RequireText(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Industry name is required.", parameterName)
            : value.Trim();
}

public sealed class Company
{
    public Company(
        Guid id,
        string name,
        Guid? industryId,
        IReadOnlyCollection<ProviderExternalReference>? externalReferences = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Company name is required.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
        IndustryId = industryId;
        ExternalReferences = externalReferences ?? [];
    }

    public Guid Id { get; }

    public string Name { get; }

    public Guid? IndustryId { get; }

    public IReadOnlyCollection<ProviderExternalReference> ExternalReferences { get; }
}

public sealed class Symbol
{
    public Symbol(
        Guid id,
        Guid companyId,
        SymbolCode code,
        IReadOnlyCollection<ProviderExternalReference>? externalReferences = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Symbol id is required.", nameof(id));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        Id = id;
        CompanyId = companyId;
        Code = code ?? throw new ArgumentNullException(nameof(code));
        ExternalReferences = externalReferences ?? [];
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public SymbolCode Code { get; }

    public IReadOnlyCollection<ProviderExternalReference> ExternalReferences { get; }
}

public enum FinancialStatementType
{
    IncomeStatement,
    BalanceSheet,
    CashFlow
}

public sealed class FinancialStatement
{
    public FinancialStatement(
        Guid id,
        Guid companyId,
        FinancialStatementType type,
        FiscalPeriod period,
        FinancialSourceEvidence source,
        IReadOnlyCollection<FinancialStatementLineItem> lineItems)
    {
        Id = RequireId(id, nameof(id));
        CompanyId = RequireId(companyId, nameof(companyId));
        Type = type;
        Period = period ?? throw new ArgumentNullException(nameof(period));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        LineItems = lineItems ?? throw new ArgumentNullException(nameof(lineItems));

        if (Period.IsLatestSelection)
        {
            throw new ArgumentException("A financial statement must have a closed reporting period.", nameof(period));
        }
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public FinancialStatementType Type { get; }

    public FiscalPeriod Period { get; }

    public FinancialSourceEvidence Source { get; }

    public IReadOnlyCollection<FinancialStatementLineItem> LineItems { get; }

    private static Guid RequireId(Guid id, string parameterName) =>
        id == Guid.Empty ? throw new ArgumentException("Entity id is required.", parameterName) : id;
}

public sealed record FinancialStatementLineItem(
    MetricCode Code,
    decimal? Value,
    FinancialObservationQuality Quality);

public sealed class MonthlyReport
{
    public MonthlyReport(
        Guid id,
        Guid companyId,
        FiscalPeriod period,
        FinancialSourceEvidence source,
        IReadOnlyCollection<MonthlyReportLineItem> lineItems)
    {
        if (id == Guid.Empty || companyId == Guid.Empty)
        {
            throw new ArgumentException("Report and company ids are required.");
        }

        if (period is null)
        {
            throw new ArgumentNullException(nameof(period));
        }

        if (period.Type != FiscalPeriodType.Monthly || period.IsLatestSelection)
        {
            throw new ArgumentException("A monthly report must have a closed monthly period.", nameof(period));
        }

        Id = id;
        CompanyId = companyId;
        Period = period;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        LineItems = lineItems ?? throw new ArgumentNullException(nameof(lineItems));
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    public FiscalPeriod Period { get; }

    public FinancialSourceEvidence Source { get; }

    public IReadOnlyCollection<MonthlyReportLineItem> LineItems { get; }
}

public sealed record MonthlyReportLineItem(
    string ProductCode,
    decimal? ProductionQuantity,
    decimal? SalesQuantity,
    decimal? SalesAmount,
    FinancialObservationQuality Quality);

public sealed class MarketSnapshot
{
    public MarketSnapshot(
        Guid id,
        Guid symbolId,
        DateTimeOffset asOf,
        decimal? latestPrice,
        decimal? priceChangePercentage,
        decimal? marketCapitalization,
        FinancialSourceEvidence source,
        FinancialObservationQuality quality)
    {
        if (id == Guid.Empty || symbolId == Guid.Empty)
        {
            throw new ArgumentException("Snapshot and symbol ids are required.");
        }

        if (latestPrice < 0 || marketCapitalization < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(latestPrice), "Market observations cannot be negative.");
        }

        Id = id;
        SymbolId = symbolId;
        AsOf = asOf;
        LatestPrice = latestPrice;
        PriceChangePercentage = priceChangePercentage;
        MarketCapitalization = marketCapitalization;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Quality = quality ?? throw new ArgumentNullException(nameof(quality));
    }

    public Guid Id { get; }

    public Guid SymbolId { get; }

    public DateTimeOffset AsOf { get; }

    public decimal? LatestPrice { get; }

    public decimal? PriceChangePercentage { get; }

    public decimal? MarketCapitalization { get; }

    public FinancialSourceEvidence Source { get; }

    public FinancialObservationQuality Quality { get; }
}

public sealed class DerivedMetric
{
    public DerivedMetric(
        Guid id,
        string externalCompanyId,
        MetricCode code,
        MetricVersion metricVersion,
        CalculationPolicyVersion calculationPolicyVersion,
        FiscalPeriod period,
        decimal? value,
        MetricValueUnit unit,
        FinancialObservationQuality quality,
        IReadOnlyCollection<FinancialSourceEvidence> sourceEvidence,
        IReadOnlyCollection<DerivedMetricDependencyEvidence>? dependencyEvidence = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Metric id is required.");
        }

        if (string.IsNullOrWhiteSpace(externalCompanyId))
        {
            throw new ArgumentException("ExternalCompanyId is required.", nameof(externalCompanyId));
        }

        if (period.IsLatestSelection)
        {
            throw new ArgumentException("A derived metric must identify its effective closed period.", nameof(period));
        }

        Id = id;
        ExternalCompanyId = externalCompanyId.Trim();
        Code = code ?? throw new ArgumentNullException(nameof(code));
        MetricVersion = metricVersion ?? throw new ArgumentNullException(nameof(metricVersion));
        CalculationPolicyVersion = calculationPolicyVersion ??
            throw new ArgumentNullException(nameof(calculationPolicyVersion));
        Period = period;
        Value = value;
        Unit = unit;
        Quality = quality ?? throw new ArgumentNullException(nameof(quality));
        SourceEvidence = sourceEvidence ?? throw new ArgumentNullException(nameof(sourceEvidence));
        DependencyEvidence = dependencyEvidence ?? [];
    }

    public Guid Id { get; }

    public string ExternalCompanyId { get; }

    public MetricCode Code { get; }

    public MetricVersion MetricVersion { get; }

    public CalculationPolicyVersion CalculationPolicyVersion { get; }

    public FiscalPeriod Period { get; }

    public decimal? Value { get; }

    public MetricValueUnit Unit { get; }

    public FinancialObservationQuality Quality { get; }

    public IReadOnlyCollection<FinancialSourceEvidence> SourceEvidence { get; }

    public IReadOnlyCollection<DerivedMetricDependencyEvidence> DependencyEvidence { get; }
}

public sealed record DerivedMetricDependencyEvidence(
    MetricCode MetricCode,
    MetricVersion MetricVersion,
    CalculationPolicyVersion CalculationPolicyVersion);
