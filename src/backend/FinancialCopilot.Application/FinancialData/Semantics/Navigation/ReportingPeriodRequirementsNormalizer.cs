using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>
/// Replaces model-provided Gregorian period bounds in extracted requirements with the deterministic
/// bounds of the Persian month the user named. The replacement is recorded as a diagnostic so a
/// disagreement with the model is visible in traces.
/// </summary>
public static class ReportingPeriodRequirementsNormalizer
{
    public const string ExplicitPeriodPolicy = "explicit-period";

    public static QuestionSchemaRequirements Apply(QuestionSchemaRequirements requirements, PersianReportingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(period);
        if (period.Status != PersianReportingPeriodStatus.Normalized || period.Start is null || period.End is null)
            throw new ArgumentException("Only a normalized Persian month can be applied.", nameof(period));

        var shape = requirements.StructuredShape ?? new StructuredPlanShape([], [], [], [], []);
        var replaced = shape.ReportingPeriodStart != period.Start || shape.ReportingPeriodEnd != period.End;
        var diagnostics = requirements.NormalizationDiagnostics
            .Append($"persian_period_normalized:{period.PersianLabel}:{period.Start:yyyy-MM-dd}..{period.End:yyyy-MM-dd}");
        if (replaced && (shape.ReportingPeriodStart is not null || shape.ReportingPeriodEnd is not null))
            diagnostics = diagnostics.Append(
                $"model_period_bounds_replaced:{shape.ReportingPeriodStart:yyyy-MM-dd}..{shape.ReportingPeriodEnd:yyyy-MM-dd}");
        // An explicit month is never "latest"; the monthly compiler selects the latest report when that
        // policy is present, so it is removed together with the model's bounds.
        var periodPolicies = requirements.PeriodPolicies.Where(policy => policy != "latest")
            .Append(ExplicitPeriodPolicy).Distinct(StringComparer.Ordinal).ToArray();
        return requirements with
        {
            StructuredShape = shape with { ReportingPeriodStart = period.Start, ReportingPeriodEnd = period.End },
            PeriodPolicies = periodPolicies,
            NormalizationDiagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }
}
