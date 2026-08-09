using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class SalesGrowthScannerTelemetryTests
{
    [Fact]
    public void Telemetry_projection_is_redacted_and_has_safe_defaults()
    {
        var telemetry = SalesGrowthScannerTelemetry.Create(
            "corr-116",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            null,
            null,
            TimeSpan.FromMilliseconds(12),
            "ClarificationRequired",
            "not-reserved",
            parserOutcome: "ambiguity");

        Assert.Equal("none", telemetry.AliasFamily);
        Assert.Equal(0, telemetry.EligibleSymbolCount);
        Assert.Equal(0, telemetry.EvaluatedSymbolCount);
        Assert.Equal(0, telemetry.MatchedSymbolCount);
        Assert.Empty(telemetry.ExcludedByReason);
        Assert.Equal("ambiguity", telemetry.ParserOutcome);
        Assert.DoesNotContain("user", telemetry.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
