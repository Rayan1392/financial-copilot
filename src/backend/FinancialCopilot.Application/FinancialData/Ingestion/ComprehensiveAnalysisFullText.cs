namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>
/// Feature 159 change «max-plan-full-analysis-text» (owner, 2026-11-11): a reader whose plan grants
/// <see cref="CapabilityCode"/> (Max only) gets the whole analysis text; everyone else keeps the partial preview.
/// </summary>
public static class ComprehensiveAnalysisFullText
{
    /// <summary>Plan capability code in <c>billing_plan_capabilities</c>.</summary>
    public const string CapabilityCode = "ComprehensiveAnalysis.FullText";

    /// <summary>Longest wait for the entitlement answer; slower means the preview.</summary>
    public static readonly TimeSpan CheckBudget = TimeSpan.FromSeconds(2);
}

/// <summary>Who asks. An API client never gets the whole text; a user is checked by their individual account.</summary>
public sealed record ComprehensiveAnalysisReader(Guid TenantId, Guid ActorId, Guid? UserId, Guid? ApiClientId);

/// <summary>Port owned by the use case; Infrastructure answers it from the billing plan. Must fail closed.</summary>
public interface IComprehensiveAnalysisFullTextEntitlement
{
    Task<bool> IsGrantedAsync(ComprehensiveAnalysisReader reader, CancellationToken cancellationToken);
}

/// <summary>Used when billing is not configured: nobody gets the whole text.</summary>
public sealed class NoComprehensiveAnalysisFullTextEntitlement : IComprehensiveAnalysisFullTextEntitlement
{
    public Task<bool> IsGrantedAsync(ComprehensiveAnalysisReader reader, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
