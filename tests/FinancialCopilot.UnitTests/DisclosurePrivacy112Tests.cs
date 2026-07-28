using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class DisclosurePrivacy112Tests
{
    [Fact]
    public void Public_disclosure_json_does_not_include_internal_source_identifiers()
    {
        var item = new CompanyDisclosureFeedItem("disclosure-1", "logical-internal", CompanyDisclosureType.BalanceSheet,
            "Provider", "external-internal", Guid.NewGuid(), "نماد", "شرکت", "ترازنامه", null, null,
            DateTimeOffset.UtcNow, "source-record-internal", 1, false, DisclosureCoverageStatus.Complete, "Persisted");

        var json = JsonSerializer.Serialize(item);

        Assert.DoesNotContain("logical-internal", json);
        Assert.DoesNotContain("external-internal", json);
        Assert.DoesNotContain("source-record-internal", json);
        Assert.Contains("disclosure-1", json);
    }
}
