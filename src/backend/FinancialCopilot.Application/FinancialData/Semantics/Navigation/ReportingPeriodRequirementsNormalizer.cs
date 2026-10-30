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

    public static QuestionSchemaRequirements Apply(QuestionSchemaRequirements requirements, PersianReportingPeriod period) =>
        Apply(requirements, period, period);

    /// <summary>
    /// Applies the bounds of a Persian month range, first month through last month inclusive. A range bounds
    /// the series by itself, so a model-provided period count is removed with the model's dates.
    /// </summary>
    public static QuestionSchemaRequirements Apply(QuestionSchemaRequirements requirements,
        PersianReportingPeriod first, PersianReportingPeriod last)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);
        if (first.Status != PersianReportingPeriodStatus.Normalized || first.Start is null
            || last.Status != PersianReportingPeriodStatus.Normalized || last.End is null || first.Start > last.End)
            throw new ArgumentException("Only normalized Persian months can be applied.", nameof(first));

        var period = (Start: first.Start, End: last.End);
        var isRange = first != last;
        var shape = requirements.StructuredShape ?? new StructuredPlanShape([], [], [], [], []);
        var replaced = shape.ReportingPeriodStart != period.Start || shape.ReportingPeriodEnd != period.End;
        var label = isRange ? $"{first.PersianLabel}..{last.PersianLabel}" : first.PersianLabel;
        var diagnostics = requirements.NormalizationDiagnostics
            .Append($"persian_period_normalized:{label}:{period.Start:yyyy-MM-dd}..{period.End:yyyy-MM-dd}");
        if (replaced && (shape.ReportingPeriodStart is not null || shape.ReportingPeriodEnd is not null))
            diagnostics = diagnostics.Append(
                $"model_period_bounds_replaced:{shape.ReportingPeriodStart:yyyy-MM-dd}..{shape.ReportingPeriodEnd:yyyy-MM-dd}");
        // An explicit month is never "latest"; the monthly compiler selects the latest report when that
        // policy is present, so it is removed together with the model's bounds.
        var periodPolicies = requirements.PeriodPolicies.Where(policy => policy != "latest")
            .Append(ExplicitPeriodPolicy).Distinct(StringComparer.Ordinal).ToArray();
        return requirements with
        {
            StructuredShape = shape with
            {
                ReportingPeriodStart = period.Start, ReportingPeriodEnd = period.End,
                MaximumPeriods = isRange ? null : shape.MaximumPeriods
            },
            PeriodPolicies = periodPolicies,
            NormalizationDiagnostics = diagnostics.Distinct(StringComparer.Ordinal).ToArray()
        };
    }
}
