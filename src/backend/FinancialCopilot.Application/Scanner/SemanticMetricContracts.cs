using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Application.Scanner;

public sealed record ScannerMetricReference(
    string OriginalUserTerminology,
    MetricCode MetricCode,
    MetricVersion MetricVersion,
    CalculationPolicyVersion CalculationPolicyVersion,
    FiscalPeriodType PeriodType,
    GrowthComparison? Comparison);

public sealed record ExplainableMetricEvidence(
    MetricCode MetricCode,
    MetricVersion MetricVersion,
    CalculationPolicyVersion CalculationPolicyVersion,
    decimal? ActualValue,
    MetricUnit Unit,
    FiscalPeriod Period);
