namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Feature 148 decision C2: in NoavaranAmin mode comprehensive analysis is disabled. This registration
/// replaces the legacy reader for every caller (semantic executor, agent tool, V1 orchestrator), so neither
/// the legacy nor the v2 analysis tables can be read; the Chat policy supplies the explicit answer.
/// </summary>
public sealed class UnavailableComprehensiveAnalysisQueryUseCase : IComprehensiveAnalysisQueryUseCase
{
    public Task<ComprehensiveAnalysisQueryResponse> ExecuteAsync(
        ComprehensiveAnalysisQueryRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ComprehensiveAnalysisQueryResponse([], request.SymbolNames));
}
