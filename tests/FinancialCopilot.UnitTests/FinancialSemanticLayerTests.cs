using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.UnitTests;

public sealed class FinancialSemanticLayerTests
{
    private static readonly DateOnly ActiveDate = new(2026, 5, 26);

    [Fact]
    public void Registry_SelectsHistoricallyEffectiveDefinitionVersion()
    {
        var v1 = CreateDefinition("PE_TTM", "v1", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var v2 = CreateDefinition("PE_TTM", "v2", new DateOnly(2026, 1, 1), null);
        var registry = new FinancialMetricRegistry([v1, v2], []);

        Assert.Equal("v1", registry.ResolveDefinition(new MetricCode("PE_TTM"), new DateOnly(2025, 8, 1)).Version.Value);
        Assert.Equal("v2", registry.ResolveDefinition(new MetricCode("PE_TTM"), ActiveDate).Version.Value);
    }

    [Fact]
    public void AliasResolver_MapsEquivalentEnglishAndPersianAliasesToCanonicalMetric()
    {
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []);
        var resolver = new MetricAliasResolver(registry);

        var english = resolver.ResolveAlias(
            "monthly sales growth mom",
            "en-US",
            new MetricResolutionContext(Comparison: GrowthComparison.MonthOverMonth),
            ActiveDate);
        var persian = resolver.ResolveAlias(
            "رشد فروش نسبت به ماه قبل",
            "fa-IR",
            new MetricResolutionContext(Comparison: GrowthComparison.MonthOverMonth),
            ActiveDate);

        Assert.Equal(MetricResolutionStatus.Resolved, english.Status);
        Assert.Equal(MetricResolutionStatus.Resolved, persian.Status);
        Assert.Equal("MONTHLY_SALES_GROWTH_MOM", english.ResolvedDefinition!.Code.Value);
        Assert.Equal(english.ResolvedDefinition.Code, persian.ResolvedDefinition!.Code);
    }

    [Theory]
    [InlineData("PE", "en-US", "PE_TTM")]
    [InlineData("pe", "fa-IR", "PE_TTM")]
    [InlineData("PS", "en-US", "PS_TTM")]
    [InlineData("ps", "fa-IR", "PS_TTM")]
    public void AliasResolver_MapsBarePeAndPsTermsToTtmValuationMetrics(
        string expression,
        string language,
        string expectedMetricCode)
    {
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []);
        var resolver = new MetricAliasResolver(registry);

        var result = resolver.ResolveAlias(
            expression,
            language,
            new MetricResolutionContext(FiscalPeriodType.ThreeMonths),
            ActiveDate);

        Assert.Equal(MetricResolutionStatus.Resolved, result.Status);
        Assert.Equal(expectedMetricCode, result.ResolvedDefinition!.Code.Value);
    }

    [Fact]
    public void AliasResolver_RequiresClarificationForPersianLatestQuarterNetProfitGrowth()
    {
        var resolver = new MetricAliasResolver(
            new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []));

        var ambiguous = resolver.ResolveAlias(
            "رشد سود خالص آخرین فصل",
            "fa-IR",
            new MetricResolutionContext(FiscalPeriodType.ThreeMonths),
            ActiveDate);
        var yoy = resolver.ResolveAlias(
            "رشد سود خالص آخرین فصل",
            "fa-IR",
            new MetricResolutionContext(FiscalPeriodType.ThreeMonths, GrowthComparison.YearOverYear),
            ActiveDate);
        var qoq = resolver.ResolveAlias(
            "رشد سود خالص آخرین فصل",
            "fa-IR",
            new MetricResolutionContext(FiscalPeriodType.ThreeMonths, GrowthComparison.QuarterOverQuarter),
            ActiveDate);

        Assert.Equal(MetricResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal(
            ["NET_PROFIT_GROWTH_QOQ", "NET_PROFIT_GROWTH_YOY"],
            ambiguous.Candidates.Select(candidate => candidate.Code.Value).Order().ToArray());
        Assert.Equal("NET_PROFIT_GROWTH_YOY", yoy.ResolvedDefinition!.Code.Value);
        Assert.Equal("NET_PROFIT_GROWTH_QOQ", qoq.ResolvedDefinition!.Code.Value);
    }

    [Fact]
    public void Registry_AndPolicyProvider_ResolveDependencyAndControlledFormulaMetadata()
    {
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, []);
        var policies = new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies);

        var dependencies = registry.ResolveDependencies(new MetricCode("PE_TTM"), ActiveDate);
        var policy = policies.GetPolicy(new MetricCode("PE_TTM"), new CalculationPolicyVersion("vendor-pe-ratio-passthrough-v1"));

        Assert.Equal(
            ["PE_RATIO"],
            dependencies.Select(dependency => dependency.MetricCode.Value).Order().ToArray());
        Assert.Equal("vendor-pe-ratio-passthrough", policy.Formula!.Identifier);
        Assert.Equal("v1", policy.DefinitionVersion!.Value);
    }

    [Fact]
    public async Task RegisteredCalculator_IsResolvedAsStrategyAndCalculatesIndependently()
    {
        var calculator = new TestMetricCalculator(new MetricCode("PE_TTM"));
        var registry = new FinancialMetricRegistry(PhaseOneFinancialSemanticCatalog.Definitions, [calculator]);
        var definition = registry.ResolveDefinition(calculator.MetricCode, ActiveDate);
        var context = new MetricCalculationContext(
            Guid.NewGuid(),
            definition,
            new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies)
                .GetPolicy(calculator.MetricCode, new CalculationPolicyVersion("vendor-pe-ratio-passthrough-v1")),
            FiscalPeriod.Closed(
                FiscalPeriodType.ThreeMonths,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31)),
            []);

        var result = await registry.ResolveCalculator(calculator.MetricCode)
            .CalculateAsync(context, CancellationToken.None);

        Assert.Same(calculator, registry.ResolveCalculator(calculator.MetricCode));
        Assert.Equal(4.2m, result.Value);
        Assert.Equal("vendor-pe-ratio-passthrough-v1", result.CalculationPolicyVersion.Value);
    }

    [Fact]
    public void DerivedMetric_RetainsOriginalDependencyVersionsForHistoricalAudit()
    {
        var historical = new DerivedMetric(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new MetricCode("PE_TTM"),
            new MetricVersion("v1"),
            new CalculationPolicyVersion("vendor-pe-ratio-passthrough-v1"),
            FiscalPeriod.Closed(
                FiscalPeriodType.TrailingTwelveMonths,
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 12, 31)),
            4.2m,
            MetricValueUnit.Ratio,
            FinancialObservationQuality.Current(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            [],
            [new DerivedMetricDependencyEvidence(
                new MetricCode("TTM_EPS"),
                new MetricVersion("v1"),
                new CalculationPolicyVersion("ttm-eps-v1"))]);

        Assert.Equal("v1", historical.MetricVersion.Value);
        Assert.Equal("vendor-pe-ratio-passthrough-v1", historical.CalculationPolicyVersion.Value);
        Assert.Equal("ttm-eps-v1", historical.DependencyEvidence.Single().CalculationPolicyVersion.Value);
    }

    [Fact]
    public void ScannerAndExplanationHandoffContracts_RetainOriginalTermAndSemanticVersionEvidence()
    {
        var reference = new ScannerMetricReference(
            "رشد سود خالص سالانه",
            new MetricCode("NET_PROFIT_GROWTH_YOY"),
            new MetricVersion("v1"),
            new CalculationPolicyVersion("yoy-quarterly-v1"),
            FiscalPeriodType.ThreeMonths,
            GrowthComparison.YearOverYear);
        var evidence = new ExplainableMetricEvidence(
            reference.MetricCode,
            reference.MetricVersion,
            reference.CalculationPolicyVersion,
            72.4m,
            new MetricUnit("percent", "Percent"),
            FiscalPeriod.Closed(
                FiscalPeriodType.ThreeMonths,
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 3, 31)));

        Assert.Equal("رشد سود خالص سالانه", reference.OriginalUserTerminology);
        Assert.Equal(reference.MetricCode, evidence.MetricCode);
        Assert.Equal("yoy-quarterly-v1", evidence.CalculationPolicyVersion.Value);
    }

    private static FinancialMetricDefinition CreateDefinition(
        string code,
        string version,
        DateOnly from,
        DateOnly? to) =>
        new(
            new MetricCode(code),
            new MetricVersion(version),
            "P/E",
            "Versioned P/E definition.",
            MetricCategory.Valuation,
            new MetricUnit("ratio", "Ratio"),
            from,
            to,
            [FiscalPeriodType.TrailingTwelveMonths],
            [],
            [],
            []);

    private sealed class TestMetricCalculator(MetricCode metricCode) : IFinancialMetricCalculator
    {
        public MetricCode MetricCode { get; } = metricCode;

        public Task<MetricCalculationResult> CalculateAsync(
            MetricCalculationContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MetricCalculationResult(
                MetricCode,
                context.Definition.Version,
                context.Policy.Version,
                context.EffectivePeriod,
                4.2m,
                context.Definition.Unit,
                FinancialObservationQuality.Current(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                [],
                []));
    }
}
