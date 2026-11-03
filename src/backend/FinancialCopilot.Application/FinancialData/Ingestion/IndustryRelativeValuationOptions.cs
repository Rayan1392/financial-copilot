namespace FinancialCopilot.Application.FinancialData.Ingestion;

/// <summary>Runtime controls shared by Feature 125 calculations, watch evaluation and reads.</summary>
public sealed class IndustryRelativeValuationOptions
{
    public const string SectionName = "IndustryRelativeValuation";
    public bool Enabled { get; init; }
    public int DailyCadenceMinutes { get; init; } = 1440;
    public int SourceFreshnessHours { get; init; } = 26;
    public decimal IqrMultiplier { get; init; } = 1.5m;
    public int DefaultResultLimit { get; init; } = 3;
    public int MaximumResultLimit { get; init; } = 100;
    public int EntryConsecutiveSnapshots { get; init; } = 3;
    public int ExitConsecutiveSnapshots { get; init; } = 3;

    public bool IsValid(out string error)
    {
        if (DailyCadenceMinutes is < 1440 or > 10080) return Invalid("DailyCadenceMinutes must be between 1440 and 10080.", out error);
        if (SourceFreshnessHours is < 1 or > 168) return Invalid("SourceFreshnessHours must be between 1 and 168.", out error);
        if (IqrMultiplier < 1.5m || IqrMultiplier > 5m) return Invalid("IqrMultiplier must be between 1.5 and 5.", out error);
        if (DefaultResultLimit is < 1 or > 100) return Invalid("DefaultResultLimit must be between 1 and 100.", out error);
        if (MaximumResultLimit is < 1 or > 1000) return Invalid("MaximumResultLimit must be between 1 and 1000.", out error);
        if (DefaultResultLimit > MaximumResultLimit) return Invalid("DefaultResultLimit cannot exceed MaximumResultLimit.", out error);
        if (EntryConsecutiveSnapshots is < 1 or > 30) return Invalid("EntryConsecutiveSnapshots must be between 1 and 30.", out error);
        if (ExitConsecutiveSnapshots is < 1 or > 30) return Invalid("ExitConsecutiveSnapshots must be between 1 and 30.", out error);
        error = string.Empty; return true;
    }

    public void Validate() { if (!IsValid(out var error)) throw new InvalidOperationException($"{SectionName}: {error}"); }
    private static bool Invalid(string message, out string error) { error = message; return false; }
}
