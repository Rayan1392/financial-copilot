using System.Text.Json;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundNonEquitySectionNormalizer(
    FinancialProviderDbContext providerDb,
    FinancialIngestionDbContext catalogDb,
    IFundPortfolioValueNormalizer valueNormalizer,
    IOptions<FundNonEquityNormalizationOptions> options,
    IFundNonEquityNormalizationTelemetry telemetry,
    ILogger<FundNonEquitySectionNormalizer> logger) : IFundPortfolioNonEquitySectionNormalizer
{
    private static readonly string[] OwnedIssueCodes =
    [
        "NON_EQUITY_HEADER_LAYOUT_MISMATCH", "NON_EQUITY_SOURCE_FORMULA_ERROR", "NON_EQUITY_UNIT_AMBIGUITY",
        "UNRESOLVED_COMMODITY_INSTRUMENT", "AMBIGUOUS_COMMODITY_INSTRUMENT", "COMMODITY_QUANTITY_RECONCILIATION_MISMATCH",
        "UNRESOLVED_BANK", "BANK_DEPOSIT_RECONCILIATION_MISMATCH", "UNRESOLVED_DERIVATIVE_INSTRUMENT",
        "UNRESOLVED_DERIVATIVE_UNDERLYING", "AMBIGUOUS_DERIVATIVE_IDENTITY", "INVALID_DERIVATIVE_JALALI_DATE",
        "NON_EQUITY_DUPLICATE_LOGICAL_ROW", "NON_EQUITY_SUMMARY_DETAIL_MISMATCH", "NON_EQUITY_SUMMARY_UNAVAILABLE_SOURCE_ERROR"
    ];

    public async Task NormalizeAsync(FundPortfolioWorkbookEnvelope envelope, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var settings = options.Value;
        var report = await providerDb.FundPortfolioReports.AsNoTracking()
            .Where(x => x.Id == envelope.ReportId)
            .Select(x => new ReportFacts(x.FundId, x.PeriodEndDate, x.SourceRevision, x.ImportedAtUtc))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Fund portfolio report '{envelope.ReportId}' was not persisted before non-equity normalization.");

        await RemoveOwnedRowsAsync(envelope.ReportId, cancellationToken);
        var allocations = new List<FundAssetAllocationSnapshotRow>();
        var commodities = new List<FundCommodityCertificatePositionRow>();
        var deposits = new List<FundBankDepositPositionRow>();
        var derivatives = new List<FundDerivativePositionRow>();
        var issues = new List<FundPortfolioExtractionIssueRow>();
        var unresolvedCount = 0;
        var depositFailures = 0;
        var resolvedUnderlyingCount = 0;
        var coverageAvailableCount = 0;

        foreach (var sheet in envelope.Sheets.Where(x => x.LogicalSheetType == FundWorkbookLogicalSheetType.AssetAllocationSummary))
        {
            AddUnitAmbiguityIssueIfNeeded(envelope, sheet, issues);
            var mapped = FundNonEquitySheetMapping.ParseAssetAllocation(sheet, valueNormalizer);
            AddLayoutIssueIfNeeded(envelope, sheet, mapped.Count, issues);
            foreach (var row in mapped)
            {
                allocations.Add(new FundAssetAllocationSnapshotRow
                {
                    Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = row.PeriodContext,
                    PeriodEndDate = report.PeriodEndDate, AssetClass = row.AssetClass, RawAssetClassLabel = row.RawLabel,
                    NormalizedAssetClassCode = row.AssetClass.ToString(), CostAmount = row.CostAmount,
                    MarketOrNetSaleValue = row.MarketOrNetSaleValue, WeightOfTotalAssetsPercentage = row.WeightOfTotalAssetsPercentage,
                    IsSectionTotal = row.IsSectionTotal, HasSourceFormulaError = row.HasSourceFormulaError,
                    SourceLogicalRow = row.SourceLogicalRow, SourceSheetId = sheet.SheetId, SourceAddress = row.SourceAddress,
                    SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc,
                    ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = row.SourceEvidenceJson
                });
                if (row.HasSourceFormulaError)
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawLabel, "NON_EQUITY_SOURCE_FORMULA_ERROR", FundExtractionIssueSeverity.Error,
                        "A source formula error was preserved as unavailable allocation data and was not converted to zero."));
            }
        }

        foreach (var sheet in envelope.Sheets.Where(x => x.LogicalSheetType == FundWorkbookLogicalSheetType.CommodityCertificatePositions))
        {
            AddUnitAmbiguityIssueIfNeeded(envelope, sheet, issues);
            var mapped = FundNonEquitySheetMapping.ParseCommodityCertificates(sheet, valueNormalizer);
            AddLayoutIssueIfNeeded(envelope, sheet, mapped.Count, issues);
            AddDuplicateIssues(envelope, sheet, mapped.Where(x => !x.IsTotalRow).Select(x => (x.SourceLogicalRow, x.NormalizedSecurityName)), issues);
            foreach (var row in mapped)
            {
                var commodity = FundCommodityCatalog.Resolve(row.NormalizedSecurityName);
                var symbol = FundNonEquitySheetMapping.ExtractInstrumentSymbol(row.RawSecurityName);
                var resolution = row.IsTotalRow
                    ? InstrumentResolution.NotApplicable
                    : await ResolveInstrumentAsync(row.RawSecurityName, row.NormalizedSecurityName, symbol, cancellationToken);
                var difference = FundNonEquityReconciliationPolicy.MovementDifference(row.BeginningQuantity, row.PurchasedQuantity, row.SoldQuantity, row.EndingQuantity);
                var reconciliation = row.IsTotalRow ? FundNonEquityReconciliationStatus.NotApplicable : FundNonEquityReconciliationPolicy.ReconcileMovement(row.BeginningQuantity, row.PurchasedQuantity, row.SoldQuantity, row.EndingQuantity, settings.QuantityTolerance);
                if (!row.IsTotalRow && resolution.Status != FundNonEquityResolutionStatus.Resolved)
                {
                    unresolvedCount++;
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawSecurityName,
                        resolution.Status == FundNonEquityResolutionStatus.Ambiguous ? "AMBIGUOUS_COMMODITY_INSTRUMENT" : "UNRESOLVED_COMMODITY_INSTRUMENT",
                        FundExtractionIssueSeverity.Warning, "Commodity identity was retained with candidates and requires governed resolution."));
                }
                if (reconciliation == FundNonEquityReconciliationStatus.Unreconciled)
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawSecurityName, "COMMODITY_QUANTITY_RECONCILIATION_MISMATCH", FundExtractionIssueSeverity.Error,
                        "Beginning quantity plus purchases less sales does not equal ending quantity; source values were preserved."));
                commodities.Add(new FundCommodityCertificatePositionRow
                {
                    Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = row.PeriodContext,
                    PeriodEndDate = report.PeriodEndDate, CommodityType = commodity.Type, CommodityCode = commodity.Code,
                    ExtractedInstrumentSymbol = symbol, TradingInstrumentId = resolution.InstrumentId,
                    RawInstrumentName = row.RawSecurityName, NormalizedInstrumentName = row.NormalizedSecurityName,
                    BeginningQuantity = row.BeginningQuantity, BeginningCostAmount = row.BeginningCostAmount, BeginningMarketValue = row.BeginningMarketOrNetSaleValue,
                    PurchasedQuantity = row.PurchasedQuantity, PurchaseCostAmount = row.PurchaseCostAmount, SoldQuantity = row.SoldQuantity,
                    SaleProceedsAmount = row.SaleProceedsAmount, EndingQuantity = row.EndingQuantity, EndingUnitPrice = row.EndingUnitMarketPrice,
                    EndingCostAmount = row.EndingCostAmount, EndingMarketValue = row.EndingMarketOrNetSaleValue,
                    WeightOfTotalAssetsPercentage = row.WeightOfTotalAssetsPercentage, QuantityReconciliationDifference = difference,
                    ReconciliationStatus = reconciliation, ResolutionStatus = resolution.Status, IsSectionTotal = row.IsTotalRow,
                    SourceLogicalRow = row.SourceLogicalRow, SourceSheetId = sheet.SheetId, SourceAddress = row.SourceAddress,
                    SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion,
                    SourceEvidenceJson = ResolutionEvidence(row.SourceEvidenceJson, resolution)
                });
            }
        }

        foreach (var sheet in envelope.Sheets.Where(x => x.LogicalSheetType == FundWorkbookLogicalSheetType.BankDepositPositions))
        {
            AddUnitAmbiguityIssueIfNeeded(envelope, sheet, issues);
            var mapped = FundNonEquitySheetMapping.ParseBankDeposits(sheet, valueNormalizer);
            AddLayoutIssueIfNeeded(envelope, sheet, mapped.Count, issues);
            AddDuplicateIssues(envelope, sheet, mapped.Where(x => !x.IsSectionTotal).Select(x => (x.SourceLogicalRow, x.NormalizedBankName)), issues);
            foreach (var row in mapped)
            {
                var resolution = row.IsSectionTotal ? FundNonEquityResolutionStatus.NotApplicable : row.BankCode is null ? FundNonEquityResolutionStatus.Unresolved : FundNonEquityResolutionStatus.Resolved;
                var difference = FundNonEquityReconciliationPolicy.MovementDifference(row.BeginningBalance, row.IncreaseAmount, row.DecreaseAmount, row.EndingBalance);
                var reconciliation = row.IsSectionTotal ? FundNonEquityReconciliationStatus.NotApplicable : FundNonEquityReconciliationPolicy.ReconcileMovement(row.BeginningBalance, row.IncreaseAmount, row.DecreaseAmount, row.EndingBalance, settings.AbsoluteValueTolerance);
                if (resolution == FundNonEquityResolutionStatus.Unresolved)
                {
                    unresolvedCount++;
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawBankName, "UNRESOLVED_BANK", FundExtractionIssueSeverity.Warning,
                        "Bank name was not present in the governed exact-alias catalog."));
                }
                if (reconciliation == FundNonEquityReconciliationStatus.Unreconciled)
                {
                    depositFailures++;
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawBankName, "BANK_DEPOSIT_RECONCILIATION_MISMATCH", FundExtractionIssueSeverity.Error,
                        "Ending balance differs from beginning plus increases less decreases; source balances were preserved."));
                }
                deposits.Add(new FundBankDepositPositionRow
                {
                    Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = row.PeriodContext,
                    PeriodEndDate = report.PeriodEndDate, BankCode = row.BankCode, RawBankName = row.RawBankName,
                    NormalizedBankName = row.NormalizedBankName, BeginningBalance = row.BeginningBalance, IncreaseAmount = row.IncreaseAmount,
                    DecreaseAmount = row.DecreaseAmount, EndingBalance = row.EndingBalance, WeightOfTotalAssetsPercentage = row.WeightOfTotalAssetsPercentage,
                    BalanceReconciliationDifference = difference, ReconciliationStatus = reconciliation, ResolutionStatus = resolution,
                    IsSectionTotal = row.IsSectionTotal, SourceLogicalRow = row.SourceLogicalRow, SourceSheetId = sheet.SheetId,
                    SourceAddress = row.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc,
                    ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = row.SourceEvidenceJson
                });
            }
        }

        foreach (var sheet in envelope.Sheets.Where(x => x.LogicalSheetType == FundWorkbookLogicalSheetType.DerivativePositions))
        {
            AddUnitAmbiguityIssueIfNeeded(envelope, sheet, issues);
            var mapped = FundNonEquitySheetMapping.ParseDerivatives(sheet, valueNormalizer);
            AddLayoutIssueIfNeeded(envelope, sheet, mapped.Count, issues);
            AddDuplicateIssues(envelope, sheet, mapped.Select(x => (x.SourceLogicalRow, x.NormalizedInstrumentName)), issues);
            foreach (var row in mapped)
            {
                var instrument = await ResolveInstrumentAsync(row.RawInstrumentName, row.NormalizedInstrumentName, FundNonEquitySheetMapping.ExtractInstrumentSymbol(row.RawInstrumentName), cancellationToken);
                var underlying = await ResolveUnderlyingAsync(row.RawUnderlyingName, cancellationToken);
                if (underlying.ExternalCompanyId is not null) resolvedUnderlyingCount++;
                var resolution = instrument.Status == FundNonEquityResolutionStatus.Ambiguous || underlying.Status == FundNonEquityResolutionStatus.Ambiguous
                    ? FundNonEquityResolutionStatus.Ambiguous
                    : instrument.Status == FundNonEquityResolutionStatus.Resolved && (string.IsNullOrWhiteSpace(row.RawUnderlyingName) || underlying.Status == FundNonEquityResolutionStatus.Resolved)
                        ? FundNonEquityResolutionStatus.Resolved
                        : FundNonEquityResolutionStatus.Unresolved;
                if (resolution != FundNonEquityResolutionStatus.Resolved) unresolvedCount++;
                if (instrument.Status == FundNonEquityResolutionStatus.Unresolved)
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawInstrumentName, "UNRESOLVED_DERIVATIVE_INSTRUMENT", FundExtractionIssueSeverity.Warning, "Derivative contract was not resolved by exact canonical metadata."));
                if (!string.IsNullOrWhiteSpace(row.RawUnderlyingName) && underlying.Status == FundNonEquityResolutionStatus.Unresolved)
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawUnderlyingName, "UNRESOLVED_DERIVATIVE_UNDERLYING", FundExtractionIssueSeverity.Warning, "Derivative underlying was retained for governed resolution."));
                if (resolution == FundNonEquityResolutionStatus.Ambiguous)
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.RawInstrumentName, "AMBIGUOUS_DERIVATIVE_IDENTITY", FundExtractionIssueSeverity.Error, "Multiple canonical derivative or underlying candidates matched exactly."));
                if (row.HasImpossibleDate)
                    issues.Add(Issue(envelope, sheet, row.SourceAddress, row.ExpiryOrExerciseJalali, "INVALID_DERIVATIVE_JALALI_DATE", FundExtractionIssueSeverity.Error, "The disclosed Jalali expiry/exercise date was preserved but could not be converted."));

                var matchingHolding = await GetMatchingUnderlyingEndingQuantityAsync(envelope.ReportId, underlying, row.RawUnderlyingName, cancellationToken);
                var coverage = FundHedgeCoveragePolicy.Classify(row.DerivativeType, row.UnderlyingCoverageQuantity, matchingHolding, settings.QuantityTolerance);
                if (coverage is FundHedgeCoverageStatus.Covered or FundHedgeCoverageStatus.PartiallyCovered or FundHedgeCoverageStatus.OverCovered) coverageAvailableCount++;
                var coverageEvidence = JsonSerializer.Serialize(new
                {
                    calculationVersion = FundHedgeCoveragePolicy.CalculationVersion,
                    row.UnderlyingCoverageQuantity,
                    matchingUnderlyingEndingQuantity = matchingHolding,
                    underlying.ExternalCompanyId,
                    underlying.InstrumentId,
                    sourceSheetId = sheet.SheetId,
                    sourceAddress = row.SourceAddress
                });
                derivatives.Add(new FundDerivativePositionRow
                {
                    Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = row.PeriodContext,
                    PeriodEndDate = report.PeriodEndDate, DerivativeType = row.DerivativeType, OptionType = row.OptionType,
                    PositionSide = row.PositionSide, TradingInstrumentId = instrument.InstrumentId,
                    UnderlyingExternalCompanyId = underlying.ExternalCompanyId, UnderlyingTradingInstrumentId = underlying.InstrumentId,
                    RawInstrumentName = row.RawInstrumentName, NormalizedInstrumentName = row.NormalizedInstrumentName,
                    RawUnderlyingName = row.RawUnderlyingName, ContractQuantity = row.ContractQuantity, ContractMultiplier = row.ContractMultiplier,
                    UnderlyingCoverageQuantity = row.UnderlyingCoverageQuantity, StrikePrice = row.StrikePrice,
                    ExpiryOrExerciseJalali = row.ExpiryOrExerciseJalali, ExpiryOrExerciseDate = row.ExpiryOrExerciseDate,
                    EffectiveReturnPercentage = row.EffectiveReturnPercentage, CostAmount = row.CostAmount, MarketValue = row.MarketValue,
                    WeightOfTotalAssetsPercentage = row.WeightOfTotalAssetsPercentage, ResolutionStatus = resolution,
                    HedgeCoverageStatus = coverage, HedgeCoverageCalculationVersion = FundHedgeCoveragePolicy.CalculationVersion,
                    HedgeCoverageEvidenceJson = coverageEvidence, SourceLogicalRow = row.SourceLogicalRow, SourceSheetId = sheet.SheetId,
                    SourceAddress = row.SourceAddress, SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc,
                    ParserProfileVersion = envelope.ParserProfileVersion,
                    SourceEvidenceJson = ResolutionEvidence(row.SourceEvidenceJson, instrument, underlying)
                });
            }
        }

        providerDb.FundAssetAllocationSnapshots.AddRange(allocations);
        providerDb.FundCommodityCertificatePositions.AddRange(commodities);
        providerDb.FundBankDepositPositions.AddRange(deposits);
        providerDb.FundDerivativePositions.AddRange(derivatives);
        var totalDifferenceCount = AddSummaryDetailIssues(envelope, allocations, commodities, deposits, derivatives, settings, issues);
        providerDb.FundPortfolioExtractionIssues.AddRange(issues);
        await providerDb.SaveChangesAsync(cancellationToken);

        telemetry.Record(envelope.ReportId, allocations.Count, commodities.Count, deposits.Count, derivatives.Count, unresolvedCount,
            depositFailures, totalDifferenceCount, resolvedUnderlyingCount, coverageAvailableCount, DateTimeOffset.UtcNow - startedAt);
        logger.LogInformation(
            "Fund non-equity normalization completed. ReportId={ReportId} Allocation={Allocation} Commodities={Commodities} Deposits={Deposits} Derivatives={Derivatives} Unresolved={Unresolved} IssueCodes={IssueCodes}",
            envelope.ReportId, allocations.Count, commodities.Count, deposits.Count, derivatives.Count, unresolvedCount,
            issues.Select(x => x.IssueCode).Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task RemoveOwnedRowsAsync(Guid reportId, CancellationToken cancellationToken)
    {
        providerDb.FundAssetAllocationSnapshots.RemoveRange(await providerDb.FundAssetAllocationSnapshots.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken));
        providerDb.FundCommodityCertificatePositions.RemoveRange(await providerDb.FundCommodityCertificatePositions.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken));
        providerDb.FundBankDepositPositions.RemoveRange(await providerDb.FundBankDepositPositions.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken));
        providerDb.FundDerivativePositions.RemoveRange(await providerDb.FundDerivativePositions.Where(x => x.ReportId == reportId).ToListAsync(cancellationToken));
        providerDb.FundPortfolioExtractionIssues.RemoveRange(await providerDb.FundPortfolioExtractionIssues.Where(x => x.ReportId == reportId && OwnedIssueCodes.Contains(x.IssueCode)).ToListAsync(cancellationToken));
    }

    private async Task<InstrumentResolution> ResolveInstrumentAsync(string rawName, string normalizedName, string? symbol, CancellationToken cancellationToken)
    {
        var values = new[] { rawName.Trim(), normalizedName.Trim(), symbol?.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()!;
        var numericCodes = values.Select(x => long.TryParse(x, out var value) ? (long?)value : null).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var matches = await catalogDb.TradingInstruments.AsNoTracking().Where(x => values.Contains(x.Symbol) || values.Contains(x.Name) || values.Contains(x.InstrumentIsin) || numericCodes.Contains(x.InstrumentCode)).ToListAsync(cancellationToken);
        return matches.Count switch
        {
            1 => new(FundNonEquityResolutionStatus.Resolved, matches[0].Id, null, [matches[0].Symbol], "canonical-instrument-exact"),
            > 1 => new(FundNonEquityResolutionStatus.Ambiguous, null, null, matches.Select(x => x.Symbol).Distinct().ToArray(), "multiple-canonical-instruments"),
            _ => new(FundNonEquityResolutionStatus.Unresolved, null, null, [], "no-exact-instrument")
        };
    }

    private async Task<InstrumentResolution> ResolveUnderlyingAsync(string? rawName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return InstrumentResolution.NotApplicable;
        var normalized = FundEquitySheetMapping.NormalizeSecurityName(valueNormalizer.NormalizeText(rawName));
        var values = new[] { rawName.Trim(), normalized };
        var companies = await catalogDb.Companies.AsNoTracking().Where(x => values.Contains(x.Name) || values.Contains(x.CompanySymbol) || values.Contains(x.TseSymbol) || values.Contains(x.Ticker)).ToListAsync(cancellationToken);
        if (companies.Count == 1)
        {
            var instrument = await catalogDb.TradingInstruments.AsNoTracking().Where(x => x.NormalizedCompanyId == companies[0].Id && x.IsActive).OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
            return new(FundNonEquityResolutionStatus.Resolved, instrument?.Id, companies[0].ExternalCompanyId, [companies[0].ExternalCompanyId], "canonical-company-exact");
        }
        if (companies.Count > 1) return new(FundNonEquityResolutionStatus.Ambiguous, null, null, companies.Select(x => x.ExternalCompanyId).Distinct().ToArray(), "multiple-canonical-companies");
        var instrumentResolution = await ResolveInstrumentAsync(rawName, normalized, null, cancellationToken);
        if (instrumentResolution.InstrumentId is Guid instrumentId)
        {
            var instrument = await catalogDb.TradingInstruments.AsNoTracking().SingleAsync(x => x.Id == instrumentId, cancellationToken);
            var company = instrument.NormalizedCompanyId is Guid companyId ? await catalogDb.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken) : null;
            return instrumentResolution with { ExternalCompanyId = company?.ExternalCompanyId };
        }
        return instrumentResolution;
    }

    private async Task<decimal?> GetMatchingUnderlyingEndingQuantityAsync(Guid reportId, InstrumentResolution underlying, string? rawUnderlying, CancellationToken cancellationToken)
    {
        var positions = providerDb.FundEquityPositionSnapshots.AsNoTracking().Where(x => x.ReportId == reportId && x.PositionState == FundPositionState.Ending);
        if (underlying.ExternalCompanyId is not null) return await positions.Where(x => x.ExternalCompanyId == underlying.ExternalCompanyId).SumAsync(x => x.Quantity, cancellationToken);
        if (underlying.InstrumentId is not null) return await positions.Where(x => x.TradingInstrumentId == underlying.InstrumentId).SumAsync(x => x.Quantity, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawUnderlying)) return null;
        var normalized = FundEquitySheetMapping.NormalizeSecurityName(valueNormalizer.NormalizeText(rawUnderlying));
        return await positions.Where(x => x.NormalizedSecurityName == normalized).SumAsync(x => x.Quantity, cancellationToken);
    }

    private static int AddSummaryDetailIssues(
        FundPortfolioWorkbookEnvelope envelope,
        IReadOnlyList<FundAssetAllocationSnapshotRow> allocations,
        IReadOnlyList<FundCommodityCertificatePositionRow> commodities,
        IReadOnlyList<FundBankDepositPositionRow> deposits,
        IReadOnlyList<FundDerivativePositionRow> derivatives,
        FundNonEquityNormalizationOptions settings,
        List<FundPortfolioExtractionIssueRow> issues)
    {
        var differences = 0;
        var detail = new Dictionary<FundAssetClass, (decimal Value, decimal Weight)>
        {
            [FundAssetClass.CommodityCertificates] = (commodities.Where(x => !x.IsSectionTotal).Sum(x => x.EndingMarketValue ?? 0m), commodities.Where(x => !x.IsSectionTotal).Sum(x => x.WeightOfTotalAssetsPercentage ?? 0m)),
            [FundAssetClass.BankDeposits] = (deposits.Where(x => !x.IsSectionTotal).Sum(x => x.EndingBalance ?? 0m), deposits.Where(x => !x.IsSectionTotal).Sum(x => x.WeightOfTotalAssetsPercentage ?? 0m)),
            [FundAssetClass.Derivatives] = (derivatives.Sum(x => x.MarketValue ?? 0m), derivatives.Sum(x => x.WeightOfTotalAssetsPercentage ?? 0m))
        };
        foreach (var pair in detail)
        {
            var summary = allocations.FirstOrDefault(x => !x.IsSectionTotal && x.AssetClass == pair.Key);
            if (summary is null) continue;
            if (summary.HasSourceFormulaError)
            {
                issues.Add(Issue(envelope, summary.SourceSheetId, summary.SourceAddress, summary.RawAssetClassLabel,
                    "NON_EQUITY_SUMMARY_UNAVAILABLE_SOURCE_ERROR", FundExtractionIssueSeverity.Warning,
                    "Summary/detail reconciliation was unavailable because the disclosed summary contains a source formula error."));
                continue;
            }
            var valueMatches = !summary.MarketOrNetSaleValue.HasValue || Math.Abs(summary.MarketOrNetSaleValue.Value - pair.Value.Value) <= settings.AbsoluteValueTolerance;
            var weightMatches = !summary.WeightOfTotalAssetsPercentage.HasValue || Math.Abs(summary.WeightOfTotalAssetsPercentage.Value - pair.Value.Weight) <= settings.PercentagePointTolerance;
            if (valueMatches && weightMatches) continue;
            differences++;
            issues.Add(Issue(envelope, summary.SourceSheetId, summary.SourceAddress, summary.RawAssetClassLabel,
                "NON_EQUITY_SUMMARY_DETAIL_MISMATCH", FundExtractionIssueSeverity.Warning,
                "The disclosed allocation summary differs from normalized detail totals; all source values were preserved."));
        }
        return differences;
    }

    private static void AddLayoutIssueIfNeeded(FundPortfolioWorkbookEnvelope envelope, FundWorkbookSheetEnvelope sheet, int count, List<FundPortfolioExtractionIssueRow> issues)
    {
        if (count != 0 || sheet.Cells.Count == 0) return;
        issues.Add(Issue(envelope, sheet, null, sheet.OriginalSheetName, "NON_EQUITY_HEADER_LAYOUT_MISMATCH", FundExtractionIssueSeverity.Error,
            "Required non-equity headers or independent derivative blocks were not identified."));
    }

    private static void AddDuplicateIssues(FundPortfolioWorkbookEnvelope envelope, FundWorkbookSheetEnvelope sheet, IEnumerable<(int Row, string Identity)> rows, List<FundPortfolioExtractionIssueRow> issues)
    {
        foreach (var group in rows.GroupBy(x => x.Identity, StringComparer.Ordinal).Where(x => x.Count() > 1))
            issues.Add(Issue(envelope, sheet, null, group.Key, "NON_EQUITY_DUPLICATE_LOGICAL_ROW", FundExtractionIssueSeverity.Warning,
                $"Duplicate logical identity was retained on source rows {string.Join(',', group.Select(x => x.Row))}."));
    }

    private static void AddUnitAmbiguityIssueIfNeeded(FundPortfolioWorkbookEnvelope envelope, FundWorkbookSheetEnvelope sheet, List<FundPortfolioExtractionIssueRow> issues)
    {
        if (!sheet.Cells.Any(x => x.RawValue?.Contains("تومان", StringComparison.OrdinalIgnoreCase) == true)) return;
        issues.Add(Issue(envelope, sheet, null, "تومان", "NON_EQUITY_UNIT_AMBIGUITY", FundExtractionIssueSeverity.Warning,
            "A non-default monetary unit was disclosed and requires governed scaling before cross-section comparison."));
    }

    private static string ResolutionEvidence(string sourceEvidenceJson, params InstrumentResolution[] resolutions) => JsonSerializer.Serialize(new
    {
        source = JsonDocument.Parse(sourceEvidenceJson).RootElement,
        resolutionDecisionVersion = "fund-non-equity-resolution-v1",
        resolutions = resolutions.Select(x => new { x.Status, x.InstrumentId, x.ExternalCompanyId, x.Candidates, x.Basis })
    });

    private static FundPortfolioExtractionIssueRow Issue(FundPortfolioWorkbookEnvelope envelope, FundWorkbookSheetEnvelope sheet, string? address, string? raw, string code, FundExtractionIssueSeverity severity, string message) =>
        Issue(envelope, sheet.SheetId, address, raw, code, severity, message);

    private static FundPortfolioExtractionIssueRow Issue(FundPortfolioWorkbookEnvelope envelope, Guid sheetId, string? address, string? raw, string code, FundExtractionIssueSeverity severity, string message) => new()
    {
        Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = sheetId, Severity = severity, IssueCode = code,
        SourceAddress = address, RawValue = raw, Message = message, ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private sealed record ReportFacts(Guid FundId, DateOnly? PeriodEndDate, int SourceRevision, DateTimeOffset ImportedAtUtc);
    private sealed record InstrumentResolution(FundNonEquityResolutionStatus Status, Guid? InstrumentId, string? ExternalCompanyId, IReadOnlyList<string> Candidates, string Basis)
    {
        public static InstrumentResolution NotApplicable { get; } = new(FundNonEquityResolutionStatus.NotApplicable, null, null, [], "not-applicable");
    }
}

public sealed class FundNonEquityNormalizationTelemetry(ILogger<FundNonEquityNormalizationTelemetry> logger) : IFundNonEquityNormalizationTelemetry
{
    public void Record(Guid reportId, int allocationCount, int commodityCount, int depositCount, int derivativeCount, int unresolvedCount,
        int depositEquationFailureCount, int totalDifferenceCount, int resolvedUnderlyingCount, int coverageAvailableCount, TimeSpan duration)
    {
        var resolvablePositionCount = commodityCount + depositCount + derivativeCount;
        var unresolvedRate = resolvablePositionCount == 0 ? 0m : (decimal)unresolvedCount / resolvablePositionCount;
        logger.LogInformation(
            "Fund non-equity metrics. ReportId={ReportId} Allocation={Allocation} Commodities={Commodities} Deposits={Deposits} Derivatives={Derivatives} Unresolved={Unresolved} UnresolvedRate={UnresolvedRate} DepositEquationFailures={DepositEquationFailures} SummaryDetailDifferences={SummaryDetailDifferences} ResolvedUnderlyings={ResolvedUnderlyings} CoverageAvailable={CoverageAvailable} DurationMs={DurationMs}",
            reportId, allocationCount, commodityCount, depositCount, derivativeCount, unresolvedCount, unresolvedRate, depositEquationFailureCount,
            totalDifferenceCount, resolvedUnderlyingCount, coverageAvailableCount, duration.TotalMilliseconds);
    }
}
