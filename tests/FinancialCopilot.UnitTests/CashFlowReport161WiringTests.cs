using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.CashFlowReport;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinancialCopilot.UnitTests;

public sealed class CashFlowReport161WiringTests
{
    private sealed class Resolver : ICompanyResolverService
    {
        public int Calls;
        public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default)
        { Calls++; return Task.FromResult<ResolvedCompany?>(new(Guid.NewGuid(), "30", "Ø´Ú¯Ù„", null, null, null, null, CompanySymbol: symbol)); }
    }

    private sealed class Repository : IFinancialStatementTableRepository
    {
        public Task<FinancialStatementTableSource?> FindLatestStatementAsync(FinancialStatementTableSelection selection, CancellationToken ct = default) =>
            Task.FromResult<FinancialStatementTableSource?>(new(Guid.NewGuid(), "statement", selection.ProviderName, selection.ExternalCompanyId,
                selection.CompanySymbol ?? "symbol", null, selection.StatementType, "ThreeMonths", 3,
                new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), null, null, null, null, false, false, false, null));
        public Task<IReadOnlyList<FinancialStatementTableLineItem>> GetStatementLineItemsAsync(Guid statementId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FinancialStatementTableLineItem>>([]);
        public Task<IReadOnlyList<QuarterlyReportObservation>> GetQuarterlyIncomeStatementReportsAsync(QuarterlyFinancialReportSelection selection, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QuarterlyReportObservation>>([]);
    }

    private sealed class Quarterly : IQuarterlyFinancialReportsQueryUseCase
    {
        public Task<QuarterlyFinancialReportTableResult> ExecuteAsync(QuarterlyFinancialReportTableQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not used in this test.");
    }

    private sealed class Renderer : IFinancialStatementTableRenderer
    {
        public string Render(FinancialStatementTableResult result) => "legacy-table";
    }

    private sealed class BalanceSheet : IBalanceSheetReportQueryUseCase
    {
        public Task<BalanceSheetReportResult> ExecuteAsync(BalanceSheetReportRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Not used in this test.");
    }

    private sealed class CashFlow : ICashFlowReportQueryUseCase
    {
        public int Calls;
        public Task<CashFlowReportResult> ExecuteAsync(CashFlowReportRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new CashFlowReportResult("161.1", DateTimeOffset.UnixEpoch, "unsupported_request", null, null,
                [], false, null, new(request.CompanyQuery, null, request.ProviderName, "30"), request,
                new Dictionary<Guid, CashFlowReportStatement>(), null, [], [], null, [], null, new("unverified", []), [], [], null));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ReportBranchRunsOnlyWhenTheExplicitFlagIsEnabled(bool enabled, bool expectedCashFlowPayload)
    {
        var resolver = new Resolver();
        var cashFlow = new CashFlow();
        var useCase = new FinancialStatementTableQueryUseCase(resolver, new Repository(), new Quarterly(), new Renderer(),
            Options.Create(new NadpcoApiProviderOptions { ProviderName = "NoavaranCurrentApi" }), TimeProvider.System,
            new BalanceSheet(), cashFlow, Options.Create(new CashFlowReportOptions { Enabled = enabled }));
        var query = new FinancialStatementTableQuery("cash-flow test", "Ø´Ú¯Ù„", FinancialStatementType.CashFlow,
            CashFlowReportRequest: new("Ø´Ú¯Ù„"));

        var result = await useCase.ExecuteAsync(query);

        Assert.Equal(expectedCashFlowPayload, result!.CashFlowReport is not null);
        Assert.Equal(enabled ? 0 : 1, resolver.Calls);
        Assert.Equal(enabled ? 1 : 0, cashFlow.Calls);
    }
}
