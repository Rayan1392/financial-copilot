namespace FinancialCopilot.Domain.Financial.ValueObjects;

public sealed record SymbolCode
{
    public SymbolCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Symbol code is required.", nameof(value));
        }

        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
