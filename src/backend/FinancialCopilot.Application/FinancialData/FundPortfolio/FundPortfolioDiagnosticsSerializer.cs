using System.Text.Json;

namespace FinancialCopilot.Application.FinancialData.FundPortfolio;

public static class FundPortfolioDiagnosticsSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static string Serialize(FundPortfolioWorkbookEnvelope envelope) => JsonSerializer.Serialize(envelope, Options);
}
