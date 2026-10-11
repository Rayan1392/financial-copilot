using System.Text.Json;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;

namespace FinancialCopilot.UnitTests;

public sealed class CanonicalSemanticIntentTests
{
    private readonly CanonicalSemanticIntentValidator _validator = new();

    [Fact]
    public void PopulationDiscovery_IsValidWithoutNamedCompanyIdentifiers()
    {
        var intent = PopulationIntent();

        var result = _validator.Validate(intent);

        Assert.True(result.IsValid);
        Assert.Empty(intent.EntityScope.CompanyLabels);
        Assert.Equal(EntityScopeKind.UnboundCompanyPopulation, intent.EntityScope.Kind);
    }

    [Fact]
    public void Validator_RejectsMissingComparisonPeriodsAndRankingMeasure()
    {
        var intent = PopulationIntent() with
        {
            Operation = new(AnalyticalOperationKind.Compare),
            Comparison = new(ComparisonMode.PeriodOverPeriod),
            Ranking = new("descending", "revenue")
        };

        var result = _validator.Validate(intent);

        Assert.Contains("comparison_periods_incomplete", result.Errors);
        Assert.Contains("ranking_measure_not_requested", result.Errors);
    }

    [Fact]
    public void Validator_RequiresComparisonModeForComparativeOperation()
    {
        var intent = PopulationIntent() with { Operation = new(AnalyticalOperationKind.GrowthRate) };

        Assert.Contains("comparison_mode_required", _validator.Validate(intent).Errors);
    }

    [Fact]
    public void Validator_AllowsExplicitlyClarifiedGrowthWithoutInventingBaseline()
    {
        var intent = PopulationIntent() with
        {
            Operation = new(AnalyticalOperationKind.GrowthRate),
            Comparison = new(ComparisonMode.None),
            Ambiguity = new(true, ["مبنای مقایسه کدام دوره است؟"], ["comparison_baseline"])
        };

        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void Validator_AllowsPopulationDiscoveryFilteredByYearOverYearGrowth()
    {
        var measure = CanonicalizeMeasure("رشد تولید گندله", SemanticUnit.Unspecified,
            new(ValueForm: MeasureValueForm.PercentageChange, ReportingBasis: MeasureReportingBasis.MonthlyDiscrete,
                ValueGrain: MeasureValueGrain.ProductLevel)).Measures[0];
        var intent = PopulationIntent() with
        {
            Measures = [measure],
            Comparison = new(ComparisonMode.YearOverYear),
            Ambiguity = new(false, [], [])
        };

        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.ProductionQuantity, measure.ConceptId);
        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void Canonicalizer_ReclassifiesUnboundTemporalComparisonPredicateAsPopulationDiscovery()
    {
        var source = PopulationIntent() with
        {
            Operation = new(AnalyticalOperationKind.Compare, "greaterThan"),
            Comparison = new(ComparisonMode.YearOverYear),
            Output = new(RequestedOutputKind.Table, []),
            Constraints = [new("comparisonCondition", "targetGreaterThanBaseline", ConstraintProvenance.Explicit)]
        };

        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(source);

        Assert.Equal(AnalyticalOperationKind.DiscoverPopulation, actual.Operation.Kind);
        Assert.Equal(EntityScopeKind.UnboundCompanyPopulation, actual.EntityScope.Kind);
        Assert.True(_validator.Validate(actual).IsValid);
    }

    [Fact]
    public void Validator_AllowsDirectedTemporalPopulationPredicateWithoutExtraConstraintMirror()
    {
        var intent = PopulationIntent() with
        {
            Operation = new(AnalyticalOperationKind.DiscoverPopulation, "greaterThan"),
            Comparison = new(ComparisonMode.YearOverYear)
        };

        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void Validator_RejectsContradictoryNumericThresholds()
    {
        var intent = PopulationIntent() with
        {
            Filters = new FilterGroup(FilterLogicalOperator.And,
            [
                new MeasureThresholdFilter("production", new(30, SemanticUnit.Ton, NumericComparisonOperator.GreaterThan)),
                new MeasureThresholdFilter("production", new(30, SemanticUnit.Ton, NumericComparisonOperator.LessThanOrEqual))
            ])
        };

        Assert.Contains("contradictory_numeric_constraints", _validator.Validate(intent).Errors);
    }

    [Fact]
    public void Validator_UsesPercentageResultDimensionForPhysicalQuantityGrowthThreshold()
    {
        var intent = WithThreshold("production quantity", MeasureValueForm.PercentageChange,
            SemanticUnit.Percent);

        Assert.Equal(BusinessMeasureDimension.PhysicalQuantity, intent.Measures[0].Dimension);
        Assert.Equal(SemanticUnit.Percent, intent.Measures[0].RequestedUnit);
        Assert.True(_validator.Validate(intent).IsValid,
            string.Join(", ", _validator.Validate(intent).Errors));
    }

    [Fact]
    public void Validator_UsesPercentageResultDimensionForMonetarySalesGrowthThreshold()
    {
        var intent = WithThreshold("sales amount", MeasureValueForm.PercentageChange,
            SemanticUnit.Percent);

        Assert.Equal(BusinessMeasureDimension.CurrencyAmount, intent.Measures[0].Dimension);
        Assert.Equal(SemanticUnit.Percent, intent.Measures[0].RequestedUnit);
        Assert.True(_validator.Validate(intent).IsValid,
            string.Join(", ", _validator.Validate(intent).Errors));
    }

    [Fact]
    public void Validator_AcceptsDirectPhysicalAndMonetaryThresholdUnits()
    {
        var physical = WithThreshold("production quantity", MeasureValueForm.ReportedValue,
            SemanticUnit.Ton);
        var monetary = WithThreshold("sales amount", MeasureValueForm.ReportedValue,
            SemanticUnit.IRR);

        Assert.True(_validator.Validate(physical).IsValid);
        Assert.True(_validator.Validate(monetary).IsValid);
    }

    [Theory]
    [InlineData("production quantity", "production quantity", SemanticUnit.IRR)]
    [InlineData("sales amount", "sales amount", SemanticUnit.Ton)]
    public void Validator_RejectsThresholdUnitOutsideDirectMeasureDimension(
        string phrase, string thresholdMeasure, SemanticUnit unit)
    {
        var intent = WithThreshold(phrase, MeasureValueForm.ReportedValue, unit,
            thresholdMeasure: thresholdMeasure);

        Assert.Contains("measure_unit_dimension_mismatch", _validator.Validate(intent).Errors);
    }

    [Fact]
    public void Validator_PreservesMissingAndAmbiguousThresholdUnitsWithoutInventingSourceUnit()
    {
        var missingUnit = WithThreshold("production quantity", MeasureValueForm.ReportedValue,
            SemanticUnit.Unspecified);
        var ambiguousSource = UnresolvedMeasure("ambiguous sales") with
        {
            CanonicalizationStatus = MeasureCanonicalizationStatus.Ambiguous,
            CandidateConceptIds = [CanonicalBusinessMeasureConceptRegistry.SalesAmount,
                CanonicalBusinessMeasureConceptRegistry.SalesQuantity]
        };
        var ambiguousMeasure = PopulationIntent() with
        {
            Measures = [ambiguousSource],
            Ambiguity = new(true, ["Which sales measure do you mean?"], ["measure"]),
            Filters = new MeasureThresholdFilter("ambiguous sales",
                new(100, SemanticUnit.Unspecified, NumericComparisonOperator.GreaterThan))
        };

        Assert.True(_validator.Validate(missingUnit).IsValid);
        Assert.True(_validator.Validate(ambiguousMeasure).IsValid);
        Assert.True(ambiguousMeasure.Ambiguity.RequiresClarification);
        Assert.Null(missingUnit.Measures[0].EffectiveSourceUnit);
        Assert.Equal(EffectiveUnitEvidence.Unresolved, missingUnit.Measures[0].EffectiveSourceUnitEvidence);
    }

    [Theory]
    [InlineData("مقدار‌فروش", CanonicalBusinessMeasureConceptRegistry.SalesQuantity)]
    [InlineData("فروش ریالی", CanonicalBusinessMeasureConceptRegistry.SalesAmount)]
    [InlineData("مقدار تولید", CanonicalBusinessMeasureConceptRegistry.ProductionQuantity)]
    [InlineData("سود عملیاتی هر سهم", CanonicalBusinessMeasureConceptRegistry.OperatingProfitPerShare)]
    public void Canonicalizer_ResolvesReviewedPersianMeasureTerms(string phrase, string expectedId)
    {
        var intent = PopulationIntent() with
        {
            Measures = [UnresolvedMeasure(phrase)]
        };

        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(intent);

        Assert.Equal(expectedId, actual.Measures[0].ConceptId);
        Assert.Equal(MeasureCanonicalizationStatus.Canonicalized, actual.Measures[0].CanonicalizationStatus);
        Assert.True(_validator.Validate(actual).IsValid);
    }

    [Theory]
    [InlineData("مبالغ فروش", CanonicalBusinessMeasureConceptRegistry.SalesAmount)]
    [InlineData("نسبت P/E", CanonicalBusinessMeasureConceptRegistry.PriceEarningsRatio)]
    public void Canonicalizer_ResolvesReviewedPhraseVariants(string phrase, string expectedId)
    {
        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default)
            .Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure(phrase)] });

        Assert.Equal(expectedId, actual.Measures[0].ConceptId);
        Assert.Equal(phrase, actual.Measures[0].UserPhrase);
    }

    [Theory]
    [InlineData("رشد تولید", MeasureValueForm.PercentageChange, MeasureReportingBasis.Unspecified, FinancialStatementScope.Unspecified, CanonicalBusinessMeasureConceptRegistry.ProductionQuantity)]
    [InlineData("مقدار تولید ماهانه", MeasureValueForm.ReportedValue, MeasureReportingBasis.MonthlyDiscrete, FinancialStatementScope.Unspecified, CanonicalBusinessMeasureConceptRegistry.ProductionQuantity)]
    [InlineData("سود خالص تلفیقی", MeasureValueForm.ReportedValue, MeasureReportingBasis.Unspecified, FinancialStatementScope.Consolidated, CanonicalBusinessMeasureConceptRegistry.NetProfit)]
    [InlineData("سود خالص جداگانه", MeasureValueForm.ReportedValue, MeasureReportingBasis.Unspecified, FinancialStatementScope.Standalone, CanonicalBusinessMeasureConceptRegistry.NetProfit)]
    public void Canonicalizer_UsesTypedQualifiersToRemoveOnlyMeasurePhraseModifiers(string phrase,
        MeasureValueForm valueForm, MeasureReportingBasis reportingBasis, FinancialStatementScope statementScope, string expectedId)
    {
        var measure = UnresolvedMeasure(phrase) with
        {
            Qualifiers = new(valueForm, reportingBasis, statementScope)
        };
        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default)
            .Canonicalize(PopulationIntent() with { Measures = [measure] });

        Assert.Equal(expectedId, actual.Measures[0].ConceptId);
        Assert.Equal(phrase, actual.Measures[0].UserPhrase);
        Assert.Equal(measure.Qualifiers, actual.Measures[0].Qualifiers);
        if (valueForm == MeasureValueForm.PercentageChange)
            actual = actual with { Operation = new(AnalyticalOperationKind.GrowthRate), Comparison = new(ComparisonMode.YearOverYear) };
        Assert.True(_validator.Validate(actual).IsValid);
    }

    [Fact]
    public void Canonicalizer_LeavesBareSalesAmbiguousAfterRemovingGrowthModifier()
    {
        var measure = UnresolvedMeasure("رشد فروش") with
        {
            Qualifiers = new(ValueForm: MeasureValueForm.PercentageChange)
        };

        var actual = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default)
            .Canonicalize(PopulationIntent() with { Measures = [measure] });

        Assert.Null(actual.Measures[0].ConceptId);
        Assert.Equal(MeasureCanonicalizationStatus.Ambiguous, actual.Measures[0].CanonicalizationStatus);
        Assert.Equal(new[] { CanonicalBusinessMeasureConceptRegistry.SalesAmount, CanonicalBusinessMeasureConceptRegistry.SalesQuantity },
            actual.Measures[0].CandidateConceptIds);
        Assert.True(actual.Ambiguity.RequiresClarification);
    }

    [Fact]
    public void Canonicalizer_PreservesUnknownAndAmbiguousTermsWithoutNearestConceptGuess()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        var unknown = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("درآمد فروش شرکت")] });
        var ambiguous = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("فروش")] });

        Assert.Null(unknown.Measures[0].ConceptId);
        Assert.Equal(MeasureCanonicalizationStatus.Unrecognized, unknown.Measures[0].CanonicalizationStatus);
        Assert.Equal("درآمد فروش شرکت", unknown.Measures[0].UserPhrase);
        Assert.Null(ambiguous.Measures[0].ConceptId);
        Assert.Equal(MeasureCanonicalizationStatus.Ambiguous, ambiguous.Measures[0].CanonicalizationStatus);
        Assert.Equal(new[] { CanonicalBusinessMeasureConceptRegistry.SalesAmount, CanonicalBusinessMeasureConceptRegistry.SalesQuantity }, ambiguous.Measures[0].CandidateConceptIds);
        Assert.True(ambiguous.Ambiguity.RequiresClarification);
    }

    [Fact]
    public void Canonicalizer_ResolvesMeasureAfterRemovingExplicitProductLabel()
    {
        var intent = PopulationIntent() with
        {
            Measures = [UnresolvedMeasure("تولید گندله")],
            Products = [new ProductConstraint("گندله")]
        };

        var canonical = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(intent);
        var measure = Assert.Single(canonical.Measures);

        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.ProductionQuantity, measure.ConceptId);
        Assert.Equal("تولید گندله", measure.UserPhrase);
        Assert.Equal(BusinessMeasureDimension.PhysicalQuantity, measure.Dimension);
        Assert.Equal(SemanticUnit.Unspecified, measure.RequestedUnit);
        Assert.Null(measure.EffectiveSourceUnit);
        Assert.Equal(EffectiveUnitEvidence.Unresolved, measure.EffectiveSourceUnitEvidence);
        Assert.Single(canonical.Products);
    }

    [Fact]
    public void Canonicalizer_DerivesOnlyExplicitRequestedUnitFromReviewedQualifierTerm()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        var explicitRial = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("مبلغ فروش ریالی")] });
        var genericAmount = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("مبلغ فروش")] });

        Assert.Equal(SemanticUnit.IRR, explicitRial.Measures[0].RequestedUnit);
        Assert.Equal(SemanticUnit.Unspecified, genericAmount.Measures[0].RequestedUnit);
        Assert.Null(explicitRial.Measures[0].EffectiveSourceUnit);
    }

    [Fact]
    public void Validator_RejectsIncompatibleMeasureUnitAndUnsupportedSourceUnitEvidence()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        var intent = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("مقدار تولید")] });
        var incompatible = intent with
        {
            Measures = [intent.Measures[0] with { RequestedUnit = SemanticUnit.IRR }]
        };

        Assert.Contains("requested_unit_dimension_mismatch", _validator.Validate(incompatible).Errors);
        Assert.Contains("source_unit_cannot_be_inferred_by_intent_extractor",
            _validator.Validate(intent with { Measures = [intent.Measures[0] with { EffectiveSourceUnit = SemanticUnit.Ton }] }).Errors);
    }

    [Fact]
    public void Validator_RejectsStatementScopeOnMonthlySalesAndProductGrainOnFinancialStatementItem()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        var sales = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("مبلغ فروش")] });
        var salesWithStatementScope = sales with
        {
            Measures = [sales.Measures[0] with { Qualifiers = new(StatementScope: FinancialStatementScope.Consolidated) }]
        };
        Assert.Contains("incompatible_measure_qualifiers", _validator.Validate(salesWithStatementScope).Errors);

        var profit = canonicalizer.Canonicalize(PopulationIntent() with { Measures = [UnresolvedMeasure("سود خالص")] });
        var profitWithProductGrain = profit with
        {
            Measures = [profit.Measures[0] with { Qualifiers = new(ValueGrain: MeasureValueGrain.ProductLevel) }]
        };
        Assert.Contains("incompatible_measure_qualifiers", _validator.Validate(profitWithProductGrain).Errors);
    }

    [Fact]
    public void UnitSemantics_SalesAmountSupportsRialAndMillionRialDisplayUnits()
    {
        var rial = CanonicalizeMeasure("فروش ریالی", SemanticUnit.IRR);
        var millionRial = CanonicalizeMeasure("فروش ریالی", SemanticUnit.MillionIRR);

        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.SalesAmount, rial.Measures[0].ConceptId);
        Assert.Equal(BusinessMeasureDimension.CurrencyAmount, rial.Measures[0].Dimension);
        Assert.Equal(SemanticUnit.IRR, rial.Measures[0].RequestedUnit);
        Assert.True(_validator.Validate(rial).IsValid);
        Assert.Equal(SemanticUnit.MillionIRR, millionRial.Measures[0].RequestedUnit);
        Assert.Equal(BusinessMeasureDimension.CurrencyAmount, millionRial.Measures[0].Dimension);
        Assert.True(_validator.Validate(millionRial).IsValid);
    }

    [Fact]
    public void UnitSemantics_PercentageChangeUsesPercentDisplayWithoutChangingBaseDimensionOrSourceUnit()
    {
        var intent = CanonicalizeMeasure("فروش ریالی", SemanticUnit.IRR,
            new(ValueForm: MeasureValueForm.PercentageChange, ReportingBasis: MeasureReportingBasis.MonthlyDiscrete)) with
        {
            Operation = new(AnalyticalOperationKind.GrowthRate),
            Comparison = new(ComparisonMode.YearOverYear)
        };
        var measure = Assert.Single(intent.Measures);

        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.SalesAmount, measure.ConceptId);
        Assert.Equal(BusinessMeasureDimension.CurrencyAmount, measure.Dimension);
        Assert.Equal(SemanticUnit.Percent, measure.RequestedUnit);
        Assert.Null(measure.EffectiveSourceUnit);
        Assert.Equal(EffectiveUnitEvidence.Unresolved, measure.EffectiveSourceUnitEvidence);
        Assert.True(_validator.Validate(intent).IsValid);

        var contradictory = intent with { Measures = [measure with { RequestedUnit = SemanticUnit.IRR }] };
        Assert.Contains("requested_unit_dimension_mismatch", _validator.Validate(contradictory).Errors);
    }

    [Fact]
    public void UnitSemantics_ProductionQuantityCanRequestTonWithApprovedSourceUnitEvidence()
    {
        var intent = CanonicalizeMeasure("مقدار تولید", SemanticUnit.Ton) with
        {
            Measures = [CanonicalizeMeasure("مقدار تولید", SemanticUnit.Ton).Measures[0] with
            {
                EffectiveSourceUnit = SemanticUnit.Ton,
                EffectiveSourceUnitEvidence = EffectiveUnitEvidence.CatalogApproved
            }]
        };

        Assert.Equal(BusinessMeasureDimension.PhysicalQuantity, intent.Measures[0].Dimension);
        Assert.Equal(SemanticUnit.Ton, intent.Measures[0].RequestedUnit);
        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void UnitSemantics_SalesQuantityAllowsUnknownSourceUnitWithoutInferringFromProduct()
    {
        var intent = CanonicalizeMeasure("مقدار فروش");
        var measure = Assert.Single(intent.Measures);

        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.SalesQuantity, measure.ConceptId);
        Assert.Equal(BusinessMeasureDimension.PhysicalQuantity, measure.Dimension);
        Assert.Equal(SemanticUnit.Unspecified, measure.RequestedUnit);
        Assert.Null(measure.EffectiveSourceUnit);
        Assert.Equal(EffectiveUnitEvidence.Unresolved, measure.EffectiveSourceUnitEvidence);
        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void UnitSemantics_RejectsCurrencyUnitForPhysicalQuantity()
    {
        var intent = CanonicalizeMeasure("مقدار فروش", SemanticUnit.MillionIRR);

        Assert.Contains("requested_unit_dimension_mismatch", _validator.Validate(intent).Errors);
    }

    [Fact]
    public void UnitSemantics_OperatingProfitPerShareUsesPerShareRialNotTotalRial()
    {
        var perShare = CanonicalizeMeasure("سود عملیاتی هر سهم");
        var totalAmount = perShare with
        {
            Measures = [perShare.Measures[0] with { RequestedUnit = SemanticUnit.IRR }]
        };

        Assert.Equal(BusinessMeasureDimension.CurrencyPerShare, perShare.Measures[0].Dimension);
        Assert.Equal(SemanticUnit.IRRPerShare, perShare.Measures[0].RequestedUnit);
        Assert.True(_validator.Validate(perShare).IsValid);
        Assert.Contains("requested_unit_dimension_mismatch", _validator.Validate(totalAmount).Errors);
    }

    [Fact]
    public void UnitSemantics_OrdinaryFinancialStatementAmountAcceptsRial()
    {
        var intent = CanonicalizeMeasure("سود خالص", SemanticUnit.IRR);

        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.NetProfit, intent.Measures[0].ConceptId);
        Assert.Equal(BusinessMeasureDimension.CurrencyAmount, intent.Measures[0].Dimension);
        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void UnitSemantics_RequestedDisplayScaleRemainsSeparateFromApprovedSourceScale()
    {
        var intent = CanonicalizeMeasure("مبلغ فروش", SemanticUnit.MillionIRR);
        var measure = intent.Measures[0] with
        {
            EffectiveSourceUnit = SemanticUnit.IRR,
            EffectiveSourceUnitEvidence = EffectiveUnitEvidence.OwnerApproved
        };
        var separated = intent with { Measures = [measure] };

        Assert.Equal(SemanticUnit.MillionIRR, measure.RequestedUnit);
        Assert.Equal(SemanticUnit.IRR, measure.EffectiveSourceUnit);
        Assert.NotEqual(measure.RequestedUnit, measure.EffectiveSourceUnit);
        Assert.True(_validator.Validate(separated).IsValid);
    }

    [Fact]
    public void UnitSemantics_UnspecifiedUnitIsNotAContradiction()
    {
        var intent = CanonicalizeMeasure("مبلغ فروش");

        Assert.Equal(SemanticUnit.Unspecified, intent.Measures[0].RequestedUnit);
        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void UnitSemantics_AmbiguousAmountVersusQuantityRequiresClarification()
    {
        var intent = CanonicalizeMeasure("فروش");

        Assert.Null(intent.Measures[0].ConceptId);
        Assert.Equal(MeasureCanonicalizationStatus.Ambiguous, intent.Measures[0].CanonicalizationStatus);
        Assert.Equal(BusinessMeasureDimension.Unknown, intent.Measures[0].Dimension);
        Assert.True(intent.Ambiguity.RequiresClarification);
        Assert.True(_validator.Validate(intent).IsValid);
    }

    [Fact]
    public void UnitSemantics_RejectsIncompatibleApprovedSourceUnit()
    {
        var intent = CanonicalizeMeasure("مقدار فروش") with
        {
            Measures = [CanonicalizeMeasure("مقدار فروش").Measures[0] with
            {
                EffectiveSourceUnit = SemanticUnit.MillionIRR,
                EffectiveSourceUnitEvidence = EffectiveUnitEvidence.ProviderReported
            }]
        };

        Assert.Contains("effective_source_unit_dimension_mismatch", _validator.Validate(intent).Errors);
    }

    [Fact]
    public async Task Extractor_RetriesMalformedJsonOnce_ThenReturnsTypedIntent()
    {
        var validJson = """
            {"contractVersion":2,"measures":[{"userPhrase":"مقدار تولید","conceptId":null,"canonicalizationStatus":"unrecognized","candidateConceptIds":[],"kind":"productionQuantity","dimension":"unknown","qualifiers":{"valueForm":"reportedValue","reportingBasis":"unspecified","statementScope":"unspecified","valueGrain":"productLevel"},"requestedUnit":"ton","effectiveSourceUnit":null,"effectiveSourceUnitEvidence":"unresolved"}],"operation":{"kind":"discoverPopulation"},"comparison":{"mode":"none"},"period":{"kind":"latestAvailable"},"entityScope":{"kind":"unboundCompanyPopulation","companyLabels":[]},"products":[{"userLabel":"گندله","conceptLabel":"گندله","provenance":"explicit"}],"categories":[],"filters":null,"aggregation":null,"ranking":null,"output":{"kind":"table","requestedFields":[]},"constraints":[],"ambiguity":{"requiresClarification":false,"questions":[],"unresolvedPoints":[]}}
            """;
        var client = new StubAiModelClient("{not-json", validJson);
        var extractor = new LlmCanonicalSemanticIntentExtractor(client, _validator, TimeSpan.FromSeconds(2));

        var result = await extractor.ExtractAsync("کدام شرکت‌ها گندله تولید می‌کنند؟", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.ModelCalls);
        Assert.Equal(AnalyticalOperationKind.DiscoverPopulation, result.Intent!.Operation.Kind);
        Assert.Equal(EntityScopeKind.UnboundCompanyPopulation, result.Intent.EntityScope.Kind);
        Assert.Equal(CanonicalBusinessMeasureConceptRegistry.ProductionQuantity, result.Intent.Measures[0].ConceptId);
        Assert.Equal(BusinessMeasureDimension.PhysicalQuantity, result.Intent.Measures[0].Dimension);
    }

    [Fact]
    public async Task Extractor_EmitsParseableStrictStructuredOutputSchema()
    {
        const string validJson = """
            {"contractVersion":2,"measures":[],"operation":{"kind":"retrieve","direction":null,"unsupportedReason":null},"comparison":{"mode":"none","baselineDescription":null,"targetDescription":null},"period":{"kind":"latestAvailable","granularity":"unspecified","userPeriodLabel":null,"numberOfPeriods":null,"startLabel":null,"endLabel":null},"entityScope":{"kind":"namedCompanies","companyLabels":["فولاد"]},"products":[],"categories":[],"filters":null,"aggregation":null,"ranking":null,"output":{"kind":"scalar","requestedFields":[]},"constraints":[],"ambiguity":{"requiresClarification":false,"questions":[],"unresolvedPoints":[]}}
            """;
        var client = new StubAiModelClient(validJson);
        var extractor = new LlmCanonicalSemanticIntentExtractor(client, _validator, TimeSpan.FromSeconds(2));

        var result = await extractor.ExtractAsync("سوال آزمایشی", CancellationToken.None);

        Assert.True(result.Succeeded);
        var structuredOutput = Assert.Single(client.Requests).StructuredOutput;
        Assert.NotNull(structuredOutput?.JsonSchema);
        using var schema = JsonDocument.Parse(structuredOutput.JsonSchema);
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.True(schema.RootElement.GetProperty("$defs").GetProperty("filterExpression").GetProperty("anyOf").GetArrayLength() >= 2);
        AssertRequiredProperties(schema.RootElement);
        var measureSchema = schema.RootElement.GetProperty("properties").GetProperty("measures").GetProperty("items");
        AssertRequiredProperties(measureSchema);
        AssertRequiredProperties(measureSchema.GetProperty("properties").GetProperty("qualifiers"));
    }

    [Fact]
    public async Task Extractor_ReportsSemanticRejectionSeparatelyFromMalformedOutput()
    {
        const string invalidSemanticJson = """
            {"contractVersion":2,"measures":[],"operation":{"kind":"rank"},"comparison":{"mode":"none"},"period":{"kind":"unspecified"},"entityScope":{"kind":"namedCompanies","companyLabels":[]},"products":[],"categories":[],"filters":null,"aggregation":null,"ranking":null,"output":{"kind":"table","requestedFields":[]},"constraints":[],"ambiguity":{"requiresClarification":false,"questions":[],"unresolvedPoints":[]}}
            """;
        var extractor = new LlmCanonicalSemanticIntentExtractor(
            new StubAiModelClient(invalidSemanticJson), _validator, TimeSpan.FromSeconds(2));

        var result = await extractor.ExtractAsync("سؤال", CancellationToken.None);

        Assert.Equal("semantic_validation_failure", result.FailureCategory);
        Assert.NotEqual("malformed_model_output", result.FailureCategory);
    }

    private static CanonicalSemanticIntent PopulationIntent()
    {
        var intent = new CanonicalSemanticIntent(
            2,
            [UnresolvedMeasure("مقدار تولید", SemanticUnit.Ton)],
            new(AnalyticalOperationKind.DiscoverPopulation),
            new(ComparisonMode.None),
            new(ReportingPeriodKind.LatestAvailable),
            new(EntityScopeKind.UnboundCompanyPopulation, []),
            [new("گندله", "گندله")],
            [],
            null,
            null,
            null,
            new(RequestedOutputKind.Table, []),
            [],
            new(false, [], []));
        return new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(intent);
    }

    private static CanonicalSemanticIntent WithThreshold(string phrase, MeasureValueForm valueForm,
        SemanticUnit thresholdUnit, string? thresholdMeasure = null)
    {
        var measure = CanonicalizeMeasure(phrase, qualifiers: new(ValueForm: valueForm)).Measures[0];
        return PopulationIntent() with
        {
            Measures = [measure],
            Comparison = valueForm == MeasureValueForm.PercentageChange
                ? new(ComparisonMode.YearOverYear)
                : new(ComparisonMode.None),
            Filters = new MeasureThresholdFilter(thresholdMeasure ?? measure.ConceptId ?? measure.UserPhrase,
                new(100, thresholdUnit, NumericComparisonOperator.GreaterThan))
        };
    }

    private static RequestedMeasure UnresolvedMeasure(string phrase, SemanticUnit unit = SemanticUnit.Unspecified) => new(
        phrase, null, MeasureCanonicalizationStatus.Unrecognized, [], MeasureConceptKind.OtherBusinessMeasure,
        BusinessMeasureDimension.Unknown, new(), unit, null, EffectiveUnitEvidence.Unresolved);

    private static CanonicalSemanticIntent CanonicalizeMeasure(string phrase,
        SemanticUnit unit = SemanticUnit.Unspecified, MeasureQualifiers? qualifiers = null)
    {
        var intent = PopulationIntent() with
        {
            Measures = [UnresolvedMeasure(phrase, unit) with { Qualifiers = qualifiers ?? new() }]
        };
        return new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default).Canonicalize(intent);
    }

    private static void AssertRequiredProperties(JsonElement schema)
    {
        var properties = schema.GetProperty("properties");
        foreach (var required in schema.GetProperty("required").EnumerateArray())
            Assert.True(properties.TryGetProperty(required.GetString()!, out _), $"Required property '{required.GetString()}' must exist in that object's properties.");
    }

    private sealed class StubAiModelClient(params string[] responses) : IAiModelExecutionService
    {
        private readonly Queue<string> _responses = new(responses);
        public List<AiModelRequest> Requests { get; } = [];

        public Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var json = _responses.Dequeue();
            return Task.FromResult(new AiModelResult(null, json, [],
                new AiExecutionUsageFacts(request.CorrelationId, "test", "test-model", AiExecutionStatus.Completed, TimeSpan.FromMilliseconds(2), 1, 12, 20)));
        }

    }
}
