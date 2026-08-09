using System.Text.Json;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundEquitySectionNormalizer(
    FinancialProviderDbContext providerDb,
    FinancialIngestionDbContext catalogDb,
    IFundPortfolioValueNormalizer valueNormalizer,
    IFundEquityNormalizationTelemetry telemetry,
    ILogger<FundEquitySectionNormalizer> logger,
    IFundEquityCorporateActionAdjustmentProvider corporateActionAdjustments) : IFundPortfolioEquitySectionNormalizer
{
    public async Task NormalizeAsync(FundPortfolioWorkbookEnvelope envelope, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var report = await providerDb.FundPortfolioReports.AsNoTracking()
            .Where(row => row.Id == envelope.ReportId)
            .Select(row => new { row.FundId, row.PeriodEndDate, row.SourceRevision, row.ImportedAtUtc })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Fund portfolio report '{envelope.ReportId}' was not persisted before equity normalization.");

        var oldPositions = await providerDb.FundEquityPositionSnapshots.Where(row => row.ReportId == envelope.ReportId).ToListAsync(cancellationToken);
        var oldActivities = await providerDb.FundEquityPeriodActivities.Where(row => row.ReportId == envelope.ReportId).ToListAsync(cancellationToken);
        var oldTotals = await providerDb.FundEquitySectionTotals.Where(row => row.ReportId == envelope.ReportId).ToListAsync(cancellationToken);
        var oldEquityIssues = await providerDb.FundPortfolioExtractionIssues.Where(row => row.ReportId == envelope.ReportId &&
            (row.IssueCode == "EQUITY_HEADER_LAYOUT_MISMATCH" || row.IssueCode == "EQUITY_QUANTITY_RECONCILIATION_MISMATCH" || row.IssueCode == "EQUITY_SECTION_TOTAL_MISMATCH" || row.IssueCode == "NEGATIVE_EQUITY_QUANTITY" || row.IssueCode == "UNRESOLVED_FUND_SECURITY" || row.IssueCode == "AMBIGUOUS_FUND_SECURITY")).ToListAsync(cancellationToken);
        providerDb.FundEquityPositionSnapshots.RemoveRange(oldPositions);
        providerDb.FundEquityPeriodActivities.RemoveRange(oldActivities);
        providerDb.FundEquitySectionTotals.RemoveRange(oldTotals);
        providerDb.FundPortfolioExtractionIssues.RemoveRange(oldEquityIssues);

        var positions = new List<FundEquityPositionSnapshotRow>();
        var activities = new List<FundEquityPeriodActivityRow>();
        var totals = new List<FundEquitySectionTotalRow>();
        var unresolvedIssues = new List<FundPortfolioExtractionIssueRow>();
        var resolvedCount = 0;
        var unresolvedCount = 0;
        var newPositionCount = 0;
        var fullExitCount = 0;
        var mismatchCount = 0;

        foreach (var sheet in envelope.Sheets.Where(sheet => sheet.LogicalSheetType is FundWorkbookLogicalSheetType.EquityPortfolioCurrent or FundWorkbookLogicalSheetType.EquityPortfolioComparative))
        {
            var context = sheet.LogicalSheetType == FundWorkbookLogicalSheetType.EquityPortfolioCurrent
                ? FundWorkbookPeriodContext.CurrentPeriod
                : FundWorkbookPeriodContext.PriorComparablePeriod;
            var mappedRows = FundEquitySheetMapping.Parse(sheet, context, valueNormalizer);
            if (mappedRows.Count == 0 && sheet.Cells.Count > 0)
            {
                unresolvedIssues.Add(new FundPortfolioExtractionIssueRow
                {
                    Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = sheet.SheetId,
                    Severity = FundExtractionIssueSeverity.Error, IssueCode = "EQUITY_HEADER_LAYOUT_MISMATCH",
                    Message = "Required equity headers were not identified from the sheet header path.",
                    ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow
                });
                continue;
            }

            foreach (var mapped in mappedRows)
            {
                if (mapped.IsTotalRow)
                {
                    totals.Add(new FundEquitySectionTotalRow
                    {
                        Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, SourceSheetId = sheet.SheetId,
                        PeriodContext = mapped.PeriodContext, SourceLogicalRow = mapped.SourceLogicalRow, RawLabel = mapped.RawSecurityName,
                        Quantity = mapped.EndingQuantity ?? mapped.BeginningQuantity, CostAmount = mapped.EndingCostAmount ?? mapped.BeginningCostAmount,
                        MarketOrNetSaleValue = mapped.EndingMarketOrNetSaleValue ?? mapped.BeginningMarketOrNetSaleValue,
                        WeightOfTotalAssetsPercentage = mapped.WeightOfTotalAssetsPercentage, SourceEvidenceJson = mapped.SourceEvidenceJson
                    });
                    continue;
                }

                if (HasImpossibleNegativeQuantity(mapped))
                {
                    unresolvedIssues.Add(new FundPortfolioExtractionIssueRow
                    {
                        Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = sheet.SheetId,
                        Severity = FundExtractionIssueSeverity.Error, IssueCode = "NEGATIVE_EQUITY_QUANTITY",
                        SourceAddress = mapped.SourceAddress, RawValue = mapped.RawSecurityName,
                        Message = "A negative position or period quantity was rejected because the equity workbook does not declare signed movement semantics.",
                        ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                    continue;
                }

                var resolution = await ResolveSecurityAsync(mapped.RawSecurityName, mapped.NormalizedSecurityName, mapped.SecurityType, cancellationToken);
                if (resolution.Status == FundSecurityResolutionStatus.Resolved) resolvedCount++; else unresolvedCount++;
                var evidence = JsonSerializer.Serialize(new
                {
                    source = JsonDocument.Parse(mapped.SourceEvidenceJson).RootElement,
                    resolution.Candidates,
                    resolution.Basis,
                    decisionVersion = "fund-security-resolution-v1"
                });

                if (mapped.BeginningQuantity.HasValue || mapped.BeginningCostAmount.HasValue || mapped.BeginningMarketOrNetSaleValue.HasValue)
                    positions.Add(CreatePosition(envelope, report, sheet, mapped, FundPositionState.Beginning, resolution, evidence));
                if (mapped.EndingQuantity.HasValue || mapped.EndingUnitMarketPrice.HasValue || mapped.EndingCostAmount.HasValue || mapped.EndingMarketOrNetSaleValue.HasValue || mapped.WeightOfTotalAssetsPercentage.HasValue)
                    positions.Add(CreatePosition(envelope, report, sheet, mapped, FundPositionState.Ending, resolution, evidence));

                var knownCorporateActionAdjustment = await corporateActionAdjustments.GetKnownQuantityAdjustmentAsync(envelope.ReportId, mapped.PeriodContext, mapped.NormalizedSecurityName, cancellationToken);
                var difference = FundEquityActivityPolicy.CalculateQuantityDifference(mapped.BeginningQuantity, mapped.PurchasedQuantity, mapped.SoldQuantity, mapped.EndingQuantity, knownCorporateActionAdjustment);
                var reconciliation = FundEquityActivityPolicy.Reconcile(mapped.BeginningQuantity, mapped.PurchasedQuantity, mapped.SoldQuantity, mapped.EndingQuantity, knownCorporateActionAdjustment);
                var classification = FundEquityActivityPolicy.Classify(mapped.BeginningQuantity, mapped.PurchasedQuantity, mapped.SoldQuantity, mapped.EndingQuantity, reconciliation);
                if (classification == FundEquityActivityClassification.NewPosition) newPositionCount++;
                if (classification == FundEquityActivityClassification.FullExit) fullExitCount++;
                if (reconciliation == FundEquityReconciliationStatus.Unreconciled)
                {
                    mismatchCount++;
                    unresolvedIssues.Add(new FundPortfolioExtractionIssueRow
                    {
                        Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = sheet.SheetId,
                        Severity = FundExtractionIssueSeverity.Error, IssueCode = "EQUITY_QUANTITY_RECONCILIATION_MISMATCH",
                        SourceAddress = mapped.SourceAddress, RawValue = mapped.EndingQuantity?.ToString(),
                        Message = "Beginning quantity plus disclosed purchases less disclosed sales does not equal the disclosed ending quantity; source values were preserved.",
                        ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                }
                if (resolution.Status != FundSecurityResolutionStatus.Resolved)
                {
                    unresolvedIssues.Add(new FundPortfolioExtractionIssueRow
                    {
                        Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = sheet.SheetId,
                        Severity = resolution.Status == FundSecurityResolutionStatus.Ambiguous ? FundExtractionIssueSeverity.Error : FundExtractionIssueSeverity.Warning,
                        IssueCode = resolution.Status == FundSecurityResolutionStatus.Ambiguous ? "AMBIGUOUS_FUND_SECURITY" : "UNRESOLVED_FUND_SECURITY",
                        SourceAddress = mapped.SourceAddress, RawValue = mapped.RawSecurityName,
                        Message = "Security identity was retained as a source row and requires governed resolution.",
                        ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                }
                activities.Add(new FundEquityPeriodActivityRow
                {
                    Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext,
                    PeriodEndDate = report.PeriodEndDate, SecurityType = mapped.SecurityType, ExternalCompanyId = resolution.ExternalCompanyId,
                    TradingInstrumentId = resolution.TradingInstrumentId, RawSecurityName = mapped.RawSecurityName, NormalizedSecurityName = mapped.NormalizedSecurityName,
                    PurchasedQuantity = mapped.PurchasedQuantity, PurchaseCostAmount = mapped.PurchaseCostAmount,
                    SoldQuantity = mapped.SoldQuantity, SaleProceedsAmount = mapped.SaleProceedsAmount, ActivityClassification = classification,
                    QuantityReconciliationDifference = difference, ReconciliationStatus = reconciliation, KnownCorporateActionAdjustment = knownCorporateActionAdjustment,
                    SourceLogicalRow = mapped.SourceLogicalRow, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress,
                    SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = evidence
                });
            }
        }

        providerDb.FundEquityPositionSnapshots.AddRange(positions);
        providerDb.FundEquityPeriodActivities.AddRange(activities);
        providerDb.FundEquitySectionTotals.AddRange(totals);
        AddSectionTotalMismatchIssues(providerDb, envelope, totals, mappedSectionRows: positions, unresolvedIssues);
        providerDb.FundPortfolioExtractionIssues.AddRange(unresolvedIssues);
        await providerDb.SaveChangesAsync(cancellationToken);
        telemetry.Record(envelope.ReportId, positions.Count + activities.Count, resolvedCount, unresolvedCount, newPositionCount, fullExitCount, mismatchCount, DateTimeOffset.UtcNow - startedAt);
        logger.LogInformation("Fund equity normalization completed. ReportId={ReportId} Rows={Rows} Resolved={Resolved} Unresolved={Unresolved} Mismatches={Mismatches} NormalizedIdentifiers={NormalizedIdentifiers} IssueCodes={IssueCodes}", envelope.ReportId, positions.Count + activities.Count, resolvedCount, unresolvedCount, mismatchCount,
            positions.Select(row => row.NormalizedSecurityName).Distinct(StringComparer.Ordinal).ToArray(), unresolvedIssues.Select(issue => issue.IssueCode).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool HasImpossibleNegativeQuantity(FundEquityMappedRow row) =>
        new[] { row.BeginningQuantity, row.PurchasedQuantity, row.SoldQuantity, row.EndingQuantity }.Any(value => value < 0);

    private static void AddSectionTotalMismatchIssues(
        FinancialProviderDbContext providerDb,
        FundPortfolioWorkbookEnvelope envelope,
        IReadOnlyList<FundEquitySectionTotalRow> totals,
        IReadOnlyList<FundEquityPositionSnapshotRow> mappedSectionRows,
        List<FundPortfolioExtractionIssueRow> issues)
    {
        foreach (var total in totals.Where(row => row.MarketOrNetSaleValue.HasValue || row.WeightOfTotalAssetsPercentage.HasValue))
        {
            var detailValue = mappedSectionRows.Where(row => row.ReportId == envelope.ReportId && row.SourceSheetId == total.SourceSheetId && row.PeriodContext == total.PeriodContext && row.PositionState == FundPositionState.Ending)
                .Sum(row => row.MarketOrNetSaleValue ?? 0m);
            var detailWeight = mappedSectionRows.Where(row => row.ReportId == envelope.ReportId && row.SourceSheetId == total.SourceSheetId && row.PeriodContext == total.PeriodContext && row.PositionState == FundPositionState.Ending)
                .Sum(row => row.WeightOfTotalAssetsPercentage ?? 0m);
            var valueMatches = !total.MarketOrNetSaleValue.HasValue || Math.Abs(detailValue - total.MarketOrNetSaleValue.Value) <= 0.01m;
            var weightMatches = !total.WeightOfTotalAssetsPercentage.HasValue || Math.Abs(detailWeight - total.WeightOfTotalAssetsPercentage.Value) <= 0.01m;
            if (valueMatches && weightMatches) continue;
            issues.Add(new FundPortfolioExtractionIssueRow
            {
                Id = Guid.NewGuid(), ReportId = envelope.ReportId, SheetId = total.SourceSheetId, Severity = FundExtractionIssueSeverity.Warning,
                IssueCode = "EQUITY_SECTION_TOTAL_MISMATCH", RawValue = total.RawLabel,
                Message = "The disclosed equity section total differs from the sum of disclosed detail values; detail values were preserved.",
                ParserProfileVersion = envelope.ParserProfileVersion, CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task<SecurityResolution> ResolveSecurityAsync(string rawName, string normalizedName, FundEquitySecurityType securityType, CancellationToken cancellationToken)
    {
        var lookupNames = new[] { rawName.Trim(), normalizedName.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var companies = await catalogDb.Companies.AsNoTracking().Where(row => lookupNames.Contains(row.Name) || lookupNames.Contains(row.CompanySymbol) || lookupNames.Contains(row.TseSymbol) || lookupNames.Contains(row.Ticker)).ToListAsync(cancellationToken);
        if (companies.Count == 1)
        {
            var company = companies[0];
            var instrument = await catalogDb.TradingInstruments.AsNoTracking().Where(row => row.NormalizedCompanyId == company.Id && row.IsActive).OrderBy(row => row.Id).FirstOrDefaultAsync(cancellationToken);
            return new(FundSecurityResolutionStatus.Resolved, company.ExternalCompanyId, instrument?.Id, [company.ExternalCompanyId], "canonical-company");
        }
        if (companies.Count > 1)
            return new(FundSecurityResolutionStatus.Ambiguous, null, null, companies.Select(row => row.ExternalCompanyId).Distinct().ToArray(), "multiple-canonical-companies");

        var instruments = await catalogDb.TradingInstruments.AsNoTracking().Where(row => lookupNames.Contains(row.Symbol) || lookupNames.Contains(row.Name) || lookupNames.Contains(row.InstrumentIsin)).ToListAsync(cancellationToken);
        if (instruments.Count == 1)
        {
            var instrument = instruments[0];
            var company = instrument.NormalizedCompanyId is Guid companyId
                ? await catalogDb.Companies.AsNoTracking().SingleOrDefaultAsync(row => row.Id == companyId, cancellationToken)
                : null;
            return new(FundSecurityResolutionStatus.Resolved, company?.ExternalCompanyId, instrument.Id, [instrument.Symbol], "canonical-instrument");
        }
        if (instruments.Count > 1)
            return new(FundSecurityResolutionStatus.Ambiguous, null, null, instruments.Select(row => row.Symbol).Distinct().ToArray(), "multiple-canonical-instruments");
        return new(FundSecurityResolutionStatus.Unresolved, null, null, [], securityType.ToString());
    }

    private static FundEquityPositionSnapshotRow CreatePosition(FundPortfolioWorkbookEnvelope envelope, dynamic report, FundWorkbookSheetEnvelope sheet, FundEquityMappedRow mapped, FundPositionState state, SecurityResolution resolution, string evidence) =>
        new()
        {
            Id = Guid.NewGuid(), ReportId = envelope.ReportId, FundId = report.FundId, PeriodContext = mapped.PeriodContext, PeriodEndDate = report.PeriodEndDate,
            PositionState = state, SecurityType = mapped.SecurityType, ExternalCompanyId = resolution.ExternalCompanyId, TradingInstrumentId = resolution.TradingInstrumentId,
            RawSecurityName = mapped.RawSecurityName, NormalizedSecurityName = mapped.NormalizedSecurityName,
            Quantity = state == FundPositionState.Beginning ? mapped.BeginningQuantity : mapped.EndingQuantity,
            UnitMarketPrice = state == FundPositionState.Ending ? mapped.EndingUnitMarketPrice : null,
            CostAmount = state == FundPositionState.Beginning ? mapped.BeginningCostAmount : mapped.EndingCostAmount,
            MarketOrNetSaleValue = state == FundPositionState.Beginning ? mapped.BeginningMarketOrNetSaleValue : mapped.EndingMarketOrNetSaleValue,
            WeightOfTotalAssetsPercentage = state == FundPositionState.Ending ? mapped.WeightOfTotalAssetsPercentage : null,
            ResolutionStatus = resolution.Status, SourceLogicalRow = mapped.SourceLogicalRow, SourceSheetId = sheet.SheetId, SourceAddress = mapped.SourceAddress,
            SourceRevision = report.SourceRevision, ImportedAtUtc = report.ImportedAtUtc, ParserProfileVersion = envelope.ParserProfileVersion, SourceEvidenceJson = evidence
        };

    private sealed record SecurityResolution(FundSecurityResolutionStatus Status, string? ExternalCompanyId, Guid? TradingInstrumentId, IReadOnlyList<string> Candidates, string Basis);
}

public sealed class FundEquityNormalizationTelemetry(ILogger<FundEquityNormalizationTelemetry> logger) : IFundEquityNormalizationTelemetry
{
    public void Record(Guid reportId, int rowCount, int resolvedCount, int unresolvedCount, int newPositionCount, int fullExitCount, int reconciliationMismatchCount, TimeSpan duration) =>
        logger.LogInformation("Fund equity metrics. ReportId={ReportId} Rows={Rows} Resolved={Resolved} Unresolved={Unresolved} NewPositions={NewPositions} FullExits={FullExits} ReconciliationMismatches={ReconciliationMismatches} DurationMs={DurationMs}", reportId, rowCount, resolvedCount, unresolvedCount, newPositionCount, fullExitCount, reconciliationMismatchCount, duration.TotalMilliseconds);
}
