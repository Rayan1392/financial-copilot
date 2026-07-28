namespace FinancialCopilot.Domain.Financial.ValueObjects;

public readonly record struct Percentage(decimal Value)
{
    public static Percentage FromRatio(decimal ratio) => new(ratio * 100m);

    public override string ToString() => $"{Value}%";
}
