using System.Text.RegularExpressions;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

namespace FinancialCopilot.UnitTests;

public sealed class CanonicalJsonHasherTests
{
    private readonly CanonicalJsonHasher _hasher = new();

    [Theory]
    [InlineData("{\"b\":2,\"a\":1}", " { \"a\" : 1.0, \"b\" : 2.00 }")]
    [InlineData("{\"outer\":{\"z\":null,\"a\":true}}", "{\"outer\":{\"a\":true,\"z\":null}}")]
    [InlineData("{\"text\":\"سلام\\nدنیا\"}", "{\"text\":\"سلام\\u000aدنیا\"}")]
    public void EquivalentDocuments_ProduceSameHash(string first, string second)
    {
        Assert.Equal(_hasher.ComputeHash(first), _hasher.ComputeHash(second));
    }

    [Fact]
    public void NestedSemanticChange_ProducesDifferentHash()
    {
        var first = _hasher.ComputeHash("{\"items\":[{\"value\":1},2]}");
        var second = _hasher.ComputeHash("{\"items\":[{\"value\":2},2]}");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ArrayOrder_RemainsSignificant()
    {
        Assert.NotEqual(
            _hasher.ComputeHash("{\"items\":[1,2]}") ,
            _hasher.ComputeHash("{\"items\":[2,1]}"));
    }

    [Fact]
    public void Hash_IsLowercaseSha256_AndInputIsUnchanged()
    {
        const string raw = "{ \"unknown\": \"value\", \"n\": 1.00 }";

        var hash = _hasher.ComputeHash(raw);

        Assert.Matches(new Regex("^[0-9a-f]{64}$"), hash);
        Assert.Equal("{ \"unknown\": \"value\", \"n\": 1.00 }", raw);
    }
}
