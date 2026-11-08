using System.Globalization;
using System.Numerics;
using System.Text.Json;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class ExactDecimalTests
{
    [Theory]
    [InlineData("123456789012345678901234567890.123456789012345678901234567890")]
    [InlineData("-0.0000000000000000000000000000000000000000000000000000000000123")]
    [InlineData("1.2300")]
    [InlineData("1e100")]
    public void Json_round_trip_preserves_exact_number_text(string source)
    {
        var value = JsonSerializer.Deserialize<ExactDecimal>(source);

        Assert.Equal(source == "1e100" ? BigInteger.Pow(10, 100).ToString(CultureInfo.InvariantCulture) : ExactDecimal.Parse(source).ToString(), value.ToString());
        Assert.Equal(value, JsonSerializer.Deserialize<ExactDecimal>(JsonSerializer.Serialize(value)));
    }

    [Fact]
    public void Coefficient_chunks_round_trip_the_full_postgres_numeric_range()
    {
        var integer = new string('9', ExactDecimal.MaximumIntegerDigits);
        var fraction = new string('8', ExactDecimal.MaximumScale);
        var source = integer + "." + fraction;
        var value = ExactDecimal.Parse(source);

        var parts = ExactDecimalValueConverter.Split(value);
        var restored = ExactDecimalValueConverter.Restore(parts.High, parts.Middle, parts.Low, parts.Sign, parts.Scale);

        Assert.Equal(value, restored);
        Assert.Equal(source, restored!.Value.ToString());
        Assert.True(parts.High.ToString(CultureInfo.InvariantCulture).Length <= ExactDecimalValueConverter.ChunkDigits);
        Assert.True(parts.Middle.ToString(CultureInfo.InvariantCulture).Length <= ExactDecimalValueConverter.ChunkDigits);
        Assert.True(parts.Low.ToString(CultureInfo.InvariantCulture).Length <= ExactDecimalValueConverter.ChunkDigits);
    }

    [Fact]
    public void Coefficient_chunks_preserve_negative_and_null_values()
    {
        var negative = ExactDecimal.Parse("-12345678901234567890.00000000000000000001");
        var parts = ExactDecimalValueConverter.Split(negative);

        Assert.Equal(negative, ExactDecimalValueConverter.Restore(parts.High, parts.Middle, parts.Low, parts.Sign, parts.Scale));
        Assert.Null(ExactDecimalValueConverter.Restore(null, null, null, null, null));
        Assert.Throws<InvalidDataException>(() => ExactDecimalValueConverter.Restore(parts.High, null, parts.Low, parts.Sign, parts.Scale));
    }

    [Fact]
    public void Parser_rejects_values_outside_postgres_numeric_limits_without_rounding()
    {
        var tooManyIntegerDigits = new string('1', ExactDecimal.MaximumIntegerDigits + 1);
        var tooMuchScale = "0." + new string('1', ExactDecimal.MaximumScale + 1);

        Assert.False(ExactDecimal.TryParse(tooManyIntegerDigits, out _));
        Assert.False(ExactDecimal.TryParse(tooMuchScale, out _));
        Assert.False(ExactDecimal.TryParse("1e999999999999999999", out _));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExactDecimal>(tooMuchScale));
    }
}
