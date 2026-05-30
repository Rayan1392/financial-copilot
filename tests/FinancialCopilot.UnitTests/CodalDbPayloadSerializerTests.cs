using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

namespace FinancialCopilot.UnitTests;

public sealed class CodalDbPayloadSerializerTests
{
    private static CodalDbCompanyRecord Company(int coId, string name) =>
        new(coId, name, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public void Serialize_IdenticalInput_ProducesIdenticalChecksum()
    {
        var rows = new[] { Company(1, "A"), Company(2, "B") };

        var first = CodalDbPayloadSerializer.Serialize(rows, r => r.CoID);
        var second = CodalDbPayloadSerializer.Serialize(rows, r => r.CoID);

        Assert.Equal(first.Checksum, second.Checksum);
        Assert.Equal(first.Json, second.Json);
    }

    [Fact]
    public void Serialize_RowReordering_DoesNotChangeChecksum()
    {
        var ascending = new[] { Company(1, "A"), Company(2, "B"), Company(3, "C") };
        var shuffled = new[] { Company(3, "C"), Company(1, "A"), Company(2, "B") };

        var fromAscending = CodalDbPayloadSerializer.Serialize(ascending, r => r.CoID);
        var fromShuffled = CodalDbPayloadSerializer.Serialize(shuffled, r => r.CoID);

        Assert.Equal(fromAscending.Checksum, fromShuffled.Checksum);
    }

    [Fact]
    public void Serialize_DifferentData_ProducesDifferentChecksum()
    {
        var baseline = new[] { Company(1, "A") };
        var changed = new[] { Company(1, "A-renamed") };

        Assert.NotEqual(
            CodalDbPayloadSerializer.Serialize(baseline, r => r.CoID).Checksum,
            CodalDbPayloadSerializer.Serialize(changed, r => r.CoID).Checksum);
    }

    [Fact]
    public void Serialize_ChecksumIsUppercaseHexSha256()
    {
        var result = CodalDbPayloadSerializer.Serialize(new[] { Company(1, "A") }, r => r.CoID);

        Assert.Equal(64, result.Checksum.Length);
        Assert.Matches("^[0-9A-F]+$", result.Checksum);
    }
}
