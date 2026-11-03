using FinancialCopilot.Application.AI.Orchestration;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2;

/// <summary>
/// Emits the single manual-diagnostic event for a V2 semantic routing decision.
/// Query text is intentionally omitted; QueryHash is safe for production correlation.
/// </summary>
internal sealed class LoggingSemanticRoutingDiagnosticSink(
    ILogger<LoggingSemanticRoutingDiagnosticSink> logger) : ISemanticRoutingDiagnosticSink
{
    private static readonly EventId EventId = new(12801, "SemanticRoutingDecision");

    public void Record(SemanticRoutingDiagnostic diagnostic)
    {
        logger.LogInformation(
            EventId,
            "[SemanticRouting] SemanticRoutingDecision " +
            "CorrelationId={CorrelationId} RolloutMode={RolloutMode} " +
            "LegacyCapability={LegacyCapability} LegacyConfidence={LegacyConfidence} " +
            "SemanticIntent={SemanticIntent} SemanticCapabilityCandidate={SemanticCapabilityCandidate} " +
            "SemanticConfidence={SemanticConfidence} ExtractedCompany={ExtractedCompany} " +
            "ExtractedProduct={ExtractedProduct} ExtractedMetric={ExtractedMetric} " +
            "ExtractedPeriod={ExtractedPeriod} ResolvedCompany={ResolvedCompany} " +
            "CompanyResolutionState={CompanyResolutionState} ResolvedProduct={ResolvedProduct} " +
            "ProductResolutionState={ProductResolutionState} ArbitrationWinner={ArbitrationWinner} " +
            "ArbitrationReason={ArbitrationReason} ArbitrationVetoes={ArbitrationVetoes} " +
            "ActualExecutedCapability={ActualExecutedCapability} ExecutionSource={ExecutionSource} " +
            "FinalFrameCapability={FinalFrameCapability} FinalFrameCompany={FinalFrameCompany} " +
            "FinalFrameCompanyState={FinalFrameCompanyState} FinalFrameProduct={FinalFrameProduct} " +
            "FinalFrameProductState={FinalFrameProductState} ExecutorCapability={ExecutorCapability} " +
            "ExecutorCompany={ExecutorCompany} ExecutorProduct={ExecutorProduct} " +
            "FinalFrameCompanyId={FinalFrameCompanyId} FinalFrameProductKey={FinalFrameProductKey} " +
            "FinalFrameProviderProductCode={FinalFrameProviderProductCode} " +
            "FinalFrameProviderProductId={FinalFrameProviderProductId} " +
            "ExecutorCompanyId={ExecutorCompanyId} ExecutorProductKey={ExecutorProductKey} " +
            "ExecutorProviderProductCode={ExecutorProviderProductCode} " +
            "ExecutorProviderProductId={ExecutorProviderProductId} " +
            "SemanticProvider={SemanticProvider} SemanticModel={SemanticModel} " +
            "SemanticStatus={SemanticStatus} QueryHash={QueryHash}",
            diagnostic.CorrelationId,
            diagnostic.RolloutMode,
            diagnostic.LegacyCapability,
            diagnostic.LegacyConfidence,
            diagnostic.SemanticIntent,
            diagnostic.SemanticCapabilityCandidate,
            diagnostic.SemanticConfidence,
            diagnostic.ExtractedCompany,
            diagnostic.ExtractedProduct,
            diagnostic.ExtractedMetric,
            diagnostic.ExtractedPeriod,
            diagnostic.ResolvedCompany,
            diagnostic.CompanyResolutionState,
            diagnostic.ResolvedProduct,
            diagnostic.ProductResolutionState,
            diagnostic.ArbitrationWinner,
            diagnostic.ArbitrationReason,
            string.Join(',', diagnostic.ArbitrationVetoes),
            diagnostic.ActualExecutedCapability,
            diagnostic.ExecutionSource,
            diagnostic.FinalFrameCapability,
            diagnostic.FinalFrameCompany,
            diagnostic.FinalFrameCompanyState,
            diagnostic.FinalFrameProduct,
            diagnostic.FinalFrameProductState,
            diagnostic.ExecutorCapability,
            diagnostic.ExecutorCompany,
            diagnostic.ExecutorProduct,
            diagnostic.FinalFrameCompanyId,
            diagnostic.FinalFrameProductKey,
            diagnostic.FinalFrameProviderProductCode,
            diagnostic.FinalFrameProviderProductId,
            diagnostic.ExecutorCompanyId,
            diagnostic.ExecutorProductKey,
            diagnostic.ExecutorProviderProductCode,
            diagnostic.ExecutorProviderProductId,
            diagnostic.SemanticProvider,
            diagnostic.SemanticModel,
            diagnostic.SemanticStatus,
            diagnostic.QueryHash);
    }
}
