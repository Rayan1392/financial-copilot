using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Feature 141 slice 3a contracts without a database: shape classification, typed binding, the cross-company
/// handoff, plan validation and compilation over the real catalog, the deterministic comparison policy and the
/// chat-routing predicate.
/// </summary>
public sealed class Feature141CrossCompanyStatementComparisonTests
{
    private const string Provider = "NoavaranCurrentApi";
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");

    // ---------- shape classification and typed binding ----------

    [Fact]
    public void ClassifierRecognizesComparisonRankingAndMultiCompanyRetrieval()
    {
        Assert.Equal(StatementQueryShape.MultiCompanyComparison, StatementQueryShapeClassifier.Classify(Compare(["کگل", "کگهر"])).Shape);
        Assert.Equal(StatementQueryShape.SingleCompanyValue, StatementQueryShapeClassifier.Classify(Retrieve(["کگل"])).Shape);
        Assert.Equal(StatementQueryShape.MultiCompanyComparison, StatementQueryShapeClassifier.Classify(Retrieve(["کگل", "کگهر"])).Shape);
        var population = StatementQueryShapeClassifier.Classify(RankPopulation("descending"));
        Assert.Equal(StatementQueryShape.Ranking, population.Shape);
        Assert.True(population.PopulationScope);
        Assert.Equal(PlanSortDirection.Descending, population.Direction);
        var ascending = StatementQueryShapeClassifier.Classify(Rank(["کگل", "کگهر", "فملی"], "ascending", 2));
        Assert.Equal(PlanSortDirection.Ascending, ascending.Direction);
        Assert.Equal(2, ascending.TopN);
        // Observed in chat ("شرکت‌ها را بر اساس سود خالص رتبه‌بندی کن"): no direction stated ranks highest first.
        foreach (var unstated in new[] { "", "unspecified" })
            Assert.Equal(PlanSortDirection.Descending, StatementQueryShapeClassifier.Classify(RankPopulation(unstated)).Direction);
    }

    [Theory]
    [InlineData("rank-one-named", "ranking_requires_two_companies_or_population")]
    [InlineData("rank-other-measure", "ranking_measure_is_not_the_requested_measure")]
    [InlineData("rank-bad-direction", "ranking_direction_unrecognized")]
    [InlineData("rank-zero-limit", "ranking_limit_out_of_bounds")]
    [InlineData("compare-one", "comparison_requires_two_named_companies")]
    [InlineData("compare-mom", "comparison_mode_not_supported_for_statement_items")]
    [InlineData("explicit-month", "statement_query_period_not_supported")]
    [InlineData("population-retrieve", "retrieval_scope_not_supported")]
    public void ClassifierRejectsUnrepresentableShapes(string variant, string diagnostic)
    {
        var intent = variant switch
        {
            "rank-one-named" => Rank(["کگل"], "descending"),
            "rank-other-measure" => Rank(["کگل", "کگهر"], "descending") with { Ranking = new("descending", "operatingProfit") },
            "rank-bad-direction" => Rank(["کگل", "کگهر"], "sideways"),
            "rank-zero-limit" => Rank(["کگل", "کگهر"], "descending", 0),
            "compare-one" => Compare(["کگل"]),
            "compare-mom" => Compare(["کگل", "کگهر"]) with { Comparison = new(ComparisonMode.MonthOverMonth) },
            "explicit-month" => Compare(["کگل", "کگهر"]) with { Period = new(ReportingPeriodKind.ExplicitMonth, ReportingPeriodGranularity.Month, "شهریور ۱۴۰۵") },
            "population-retrieve" => Retrieve([]) with { EntityScope = new(EntityScopeKind.UnboundCompanyPopulation, []) },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        var result = StatementQueryShapeClassifier.Classify(intent);
        Assert.False(result.IsSupported);
        Assert.Equal(diagnostic, result.Diagnostic);
    }

    [Fact]
    public async Task BindingAcceptsComparisonRankingAndExplicitStatementScope()
    {
        var references = await CatalogReferences();
        foreach (var intent in new[] { Compare(["کگل", "کگهر"]), RankPopulation("descending"),
                     Compare(["کگل", "کگهر"], FinancialStatementScope.Consolidated) })
        {
            var result = CanonicalIntentCatalogBinding.Bind(intent, references);
            Assert.Equal(CanonicalCatalogBindingStatus.Bound, result.Status);
            Assert.Equal(143, Assert.Single(result.References).SourceItemId);
        }
    }

    [Fact]
    public async Task BindingRejectsMissingMetricAndUnitConversion()
    {
        var references = await CatalogReferences();
        var missing = Compare(["کگل", "کگهر"]) with
        {
            Measures = [Compare(["کگل", "کگهر"]).Measures[0] with { UserPhrase = "هزینه استهلاک", ConceptId = "depreciationExpense",
                CandidateConceptIds = ["depreciationExpense"] }]
        };
        var missingResult = CanonicalIntentCatalogBinding.Bind(missing, references);
        Assert.Equal(CanonicalCatalogBindingStatus.Incomplete, missingResult.Status);
        Assert.Equal("approved_catalog_binding_missing:depreciationExpense", Assert.Single(missingResult.Diagnostics));

        var conversion = Compare(["کگل", "کگهر"]) with
        {
            Measures = [Compare(["کگل", "کگهر"]).Measures[0] with { RequestedUnit = SemanticUnit.BillionIRR }]
        };
        var conversionResult = CanonicalIntentCatalogBinding.Bind(conversion, references);
        Assert.Equal(CanonicalCatalogBindingStatus.Unsupported, conversionResult.Status);
        Assert.Equal("requested_result_unit_conversion_not_supported", Assert.Single(conversionResult.Diagnostics));
    }

    // ---------- handoffs ----------

    [Fact]
    public async Task SingleCompanyHandoffUsedByChatStillRejectsComparisons()
    {
        var (resolution, snapshot) = await NetProfitResolution();
        var handoff = FinancialSourcePlannerHandoff.Create(Compare(["کگل", "کگهر"]), resolution, snapshot.CatalogVersion,
            snapshot.CatalogHash, snapshot.DirectSources.Single(x => x.CatalogId == resolution.Reference!.CatalogId));
        Assert.Equal(FinancialSourcePlannerHandoffStatus.Incomplete, handoff.Status);
        Assert.Equal("operation_or_comparison_not_supported_by_planner_slice", handoff.DiagnosticCode);
    }

    [Fact]
    public async Task CrossCompanyHandoffBuildsComparisonAndRankingRequirements()
    {
        var (resolution, snapshot) = await NetProfitResolution();
        var source = snapshot.DirectSources.Single(x => x.CatalogId == resolution.Reference!.CatalogId);
        var compare = FinancialSourcePlannerHandoff.CreateCrossCompany(Compare(["کگل", "کگهر"]), resolution, snapshot.CatalogVersion,
            snapshot.CatalogHash, source);
        Assert.Equal(FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation, compare.Status);
        var requirements = compare.Binding!.PlannerRequirements;
        Assert.Equal(["compare"], requirements.ComparisonScopes);
        Assert.Equal(["latest"], requirements.PeriodPolicies);
        Assert.Equal(["کگل", "کگهر"], requirements.EntityReferences);
        Assert.Equal(PlanComparisonMode.MultiCompanyComparison, requirements.StructuredShape!.ComparisonMode);
        Assert.Empty(requirements.StructuredShape.Sort);
        Assert.Empty(requirements.StructuredShape.Aggregations);
        Assert.False(requirements.PopulationDiscoveryRequested);

        var rank = FinancialSourcePlannerHandoff.CreateCrossCompany(RankPopulation("ascending", 5), resolution, snapshot.CatalogVersion,
            snapshot.CatalogHash, source).Binding!.PlannerRequirements;
        Assert.True(rank.PopulationDiscoveryRequested);
        Assert.Empty(rank.EntityReferences ?? []);
        Assert.Equal([new PlanSort(source.CatalogId, PlanSortDirection.Ascending)], rank.StructuredShape!.Sort);
        Assert.Equal(5, rank.StructuredShape.Limit);
        Assert.Equal(["ranked-table"], rank.PresentationRequirements);

        var single = FinancialSourcePlannerHandoff.CreateCrossCompany(Retrieve(["کگل"]), resolution, snapshot.CatalogVersion,
            snapshot.CatalogHash, source);
        Assert.Equal(FinancialSourcePlannerHandoffStatus.Incomplete, single.Status);
        Assert.StartsWith("cross_company_shape_not_supported", single.DiagnosticCode, StringComparison.Ordinal);
    }

    // ---------- planner + validator + compiler over the real catalog ----------

    [Fact]
    public async Task NamedComparisonPlanIsEligibleAndCompilesToBoundedLatestCommonPeriodSql()
    {
        var (plan, snapshot) = await PlanAsync(Compare(["کگل", "کگهر"]));
        Assert.True(plan.ExecutionEligible, string.Join("|", plan.Diagnostics));
        Assert.True(StatementComparisonPlans.IsStatementComparison(plan));
        var compilation = new StructuredQueryCompiler().Compile(plan, snapshot);
        Assert.True(compilation.Success, string.Join("|", compilation.Diagnostics));
        var query = compilation.Query!;
        Assert.Contains("common_period", query.Sql, StringComparison.Ordinal);
        Assert.Contains("= ANY(@company_ids)", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Ticker\"", query.Sql, StringComparison.Ordinal);
        Assert.Equal(2, ((Guid[])query.Parameters.Single(p => p.Name == "company_ids").Value!).Length);
        Assert.Equal(143, query.Parameters.Single(p => p.Name == "source_item_id").Value);
        Assert.Equal("MillionIRR", query.EffectiveUnit);
        Assert.NotNull(query.StatementComparison);
        Assert.False(query.StatementComparison!.PopulationScope);
        Assert.False(query.IsPopulationDiscoveryQuery);
        Assert.Equal(100, query.MaximumRows);
        Assert.Equal(new[] { "کگل", "کگهر" }, query.StatementComparison.CompanyLabels.Values.Order());
    }

    [Fact]
    public async Task PopulationRankingCompilesWithReviewedUniverseAndApprovedLabelColumnsOnly()
    {
        var (plan, snapshot) = await PlanAsync(RankPopulation("descending"), scopeConfigured: true);
        Assert.True(plan.ExecutionEligible, string.Join("|", plan.Diagnostics));
        var compilation = new StructuredQueryCompiler().Compile(plan, snapshot);
        Assert.True(compilation.Success, string.Join("|", compilation.Diagnostics));
        var query = compilation.Query!;
        Assert.True(query.IsPopulationDiscoveryQuery);
        Assert.Empty(query.CompanyIds);
        Assert.DoesNotContain(query.Parameters, p => p.Name == "company_ids");
        Assert.Contains("AS \"CompanySymbol\"", query.Sql, StringComparison.Ordinal);
        Assert.True(query.StatementComparison!.PopulationScope);
        Assert.True(query.StatementComparison.Ranked);

        // Without the catalog approval of Companies.Name/Ticker the population answer could not name its members.
        var companies = snapshot.Tables.Single(t => t.Name == "Companies");
        var withoutLabels = new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true, snapshot.Tables,
            snapshot.Columns.Select(c => c.TableCatalogId == companies.CatalogId && c.Name is "Ticker" or "Name" ? c with { Queryable = false } : c).ToArray(),
            snapshot.Relationships, snapshot.DirectSources, snapshot.MonthlyMeasures);
        var refused = new StructuredQueryCompiler().Compile(plan, withoutLabels);
        Assert.False(refused.Success);
        Assert.Equal("statement_population_company_label_columns_unapproved", Assert.Single(refused.Diagnostics));
    }

    [Fact]
    public async Task PopulationWithoutAuthorizedScopeAndUnresolvedCompanyAreRefusedBeforeSql()
    {
        var (population, _) = await PlanAsync(RankPopulation("descending"), scopeConfigured: false);
        Assert.False(population.ExecutionEligible);
        Assert.Contains("company_discovery_scope_not_authorized", population.Diagnostics);

        var (unresolved, _) = await PlanAsync(Compare(["کگل", "زاگرس"]), unknownCompany: "زاگرس");
        Assert.False(unresolved.ExecutionEligible);
        Assert.Contains("statement_comparison_company_binding_unresolved", unresolved.Diagnostics);
    }

    [Fact]
    public async Task ComparisonWithoutResolvedEffectiveUnitIsNotCompiled()
    {
        var (plan, snapshot) = await PlanAsync(Compare(["کگل", "کگهر"]));
        var source = plan.Sources[0].CatalogId;
        var unresolved = new StructuredQueryCatalogSnapshot(snapshot.CatalogVersion, snapshot.CatalogHash, true, snapshot.Tables,
            snapshot.Columns, snapshot.Relationships,
            snapshot.DirectSources.Select(s => s.CatalogId == source
                ? s with { EffectiveUnit = null, EffectiveUnitStatus = "requires-semantic-review", SourceUnitStatus = "unresolved" } : s).ToArray(),
            snapshot.MonthlyMeasures);
        var result = new StructuredQueryCompiler().Compile(plan, unresolved);
        Assert.False(result.Success);
        Assert.Equal("statement_comparison_requires_resolved_effective_unit", Assert.Single(result.Diagnostics));
    }

    // ---------- deterministic comparison policy ----------

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static IReadOnlyDictionary<string, object?> Row(Guid company, decimal? value, string end = "2026-06-21",
        bool composing = false, long basisCount = 1, string unit = "N/A", string latest = "2026-06-21", Guid? statement = null) =>
        new Dictionary<string, object?>
        {
            ["CompanyId"] = company, ["Value"] = value, ["PeriodStart"] = new DateOnly(2026, 3, 21),
            ["PeriodEnd"] = DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), ["PeriodType"] = "ThreeMonths",
            ["IsComposing"] = composing, ["CompanyBasisCount"] = basisCount, ["SourceUnit"] = unit,
            ["CompanyLatestPeriodEnd"] = DateOnly.Parse(latest, System.Globalization.CultureInfo.InvariantCulture),
            ["StatementId"] = statement ?? Guid.NewGuid()
        };

    private static StatementComparisonSpecification Spec(PlanSortDirection direction = PlanSortDirection.Descending, int? topN = null,
        bool population = false, bool ranked = true) =>
        new(direction, topN, population, null, population ? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string> { [A] = "کگل", [B] = "کگهر", [C] = "فملی" }, ranked);

    [Fact]
    public void PolicyRanksDeterministicallyWithCompetitionRanksForTiesRegardlessOfRowOrder()
    {
        var rows = new[] { Row(A, 1_200m), Row(B, 1_200m), Row(C, 900m) };
        var first = CrossCompanyStatementComparisonPolicy.Resolve(rows, [A, B, C], Spec());
        var reversed = CrossCompanyStatementComparisonPolicy.Resolve(rows.Reverse().ToArray(), [C, B, A], Spec());
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, first.Status);
        Assert.Equal(["کگل", "کگهر", "فملی"], first.Rows.Select(r => (string)r["CompanyLabel"]!));
        Assert.Equal([1, 1, 3], first.Rows.Select(r => (int)r["Rank"]!));
        Assert.Equal([true, true, false], first.Rows.Select(r => (bool)r["TiedWithOthers"]!));
        Assert.Equal(first.Rows.Select(r => r["CompanyLabel"]), reversed.Rows.Select(r => r["CompanyLabel"]));
        var ascending = CrossCompanyStatementComparisonPolicy.Resolve(rows, [A, B, C], Spec(PlanSortDirection.Ascending, topN: 1));
        Assert.Equal(["فملی"], ascending.Rows.Select(r => (string)r["CompanyLabel"]!));
        var boundaryTie = CrossCompanyStatementComparisonPolicy.Resolve([Row(C, 2_000m), Row(A, 1_200m), Row(B, 1_200m)], [A, B, C], Spec(topN: 2));
        Assert.Equal(3, boundaryTie.Rows.Count);
        Assert.Contains(CrossCompanyStatementComparisonPolicy.WarningTopNExtendedByTies, boundaryTie.Warnings);
    }

    [Fact]
    public void PolicyNeverTreatsMissingValuesAsZero()
    {
        var named = CrossCompanyStatementComparisonPolicy.Resolve([Row(A, 1_200m), Row(B, null)], [A, B], Spec(ranked: false));
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, named.Status);
        Assert.Equal("comparison_company_value_missing", named.DiagnosticCode);
        Assert.Contains("company_value_missing:کگهر", named.Warnings);

        var missingCompany = CrossCompanyStatementComparisonPolicy.Resolve([Row(A, 1_200m)], [A, B], Spec());
        Assert.Equal("comparison_company_statement_missing", missingCompany.DiagnosticCode);
        Assert.Empty(missingCompany.Rows);

        var populationRows = new[] { Row(A, 1_200m), Row(B, null) }
            .Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(r) { ["CompanySymbol"] = r["CompanyId"]!.Equals(A) ? "کگل" : "کگهر" }).ToArray();
        var population = CrossCompanyStatementComparisonPolicy.Resolve(populationRows, [A, B, C], Spec(population: true));
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, population.Status);
        Assert.Equal(["کگل"], population.Rows.Select(r => (string)r["CompanyLabel"]!));
        Assert.Contains("company_value_missing:کگهر", population.Warnings);
        Assert.Contains("population_members_without_statements:1", population.Warnings);
    }

    [Fact]
    public void PolicyRefusesMixedBasesUnitsAndMissingCommonPeriod()
    {
        Assert.Equal("cross_company_consolidation_basis_mismatch", CrossCompanyStatementComparisonPolicy.Resolve(
            [Row(A, 1_200m, composing: true), Row(B, 900m)], [A, B], Spec()).DiagnosticCode);
        Assert.Equal("statement_scope_unspecified_across_consolidation_bases", CrossCompanyStatementComparisonPolicy.Resolve(
            [Row(A, 1_200m, basisCount: 2), Row(B, 900m)], [A, B], Spec()).DiagnosticCode);
        Assert.Equal("cross_company_units_incompatible", CrossCompanyStatementComparisonPolicy.Resolve(
            [Row(A, 1_200m, unit: "MillionIRR"), Row(B, 900m, unit: "IRR")], [A, B], Spec()).DiagnosticCode);
        Assert.Equal("requested_item_values_conflict", CrossCompanyStatementComparisonPolicy.Resolve(
            [Row(A, 1_200m), Row(A, 1_250m), Row(B, 900m)], [A, B], Spec()).DiagnosticCode);
        var noCommon = CrossCompanyStatementComparisonPolicy.Resolve(
            new[] { Row(A, null), Row(B, null) }.Select(r =>
                (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(r) { ["StatementId"] = null }).ToArray(), [A, B], Spec());
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, noCommon.Status);
        Assert.Equal("no_common_reporting_period", noCommon.DiagnosticCode);
        Assert.Equal("comparison_row_outside_compared_companies", CrossCompanyStatementComparisonPolicy.Resolve(
            [Row(A, 1_200m), Row(C, 900m)], [A, B], Spec()).DiagnosticCode);
    }

    [Fact]
    public void PolicyDisclosesNewerPeriodsOfIndividualCompanies()
    {
        var result = CrossCompanyStatementComparisonPolicy.Resolve(
            [Row(A, 1_100m, end: "2026-03-20", latest: "2026-06-21"), Row(B, 800m, end: "2026-03-20", latest: "2026-03-20")], [A, B], Spec());
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, result.Status);
        Assert.Contains("newer_period_available:کگل:2026-06-21", result.Warnings);
    }

    // ---------- canonicalizer ----------

    [Fact]
    public void CompanyIdentityRequestedFieldIsDroppedOnlyForMultiCompanyResults()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        var ranking = RankPopulation("descending") with { Output = new(RequestedOutputKind.RankedList, ["company"]) };
        Assert.Empty(canonicalizer.Canonicalize(ranking).Output.RequestedFields);
        var single = Retrieve(["کگل"]) with { Output = new(RequestedOutputKind.Scalar, ["company"]) };
        Assert.Equal(["company"], canonicalizer.Canonicalize(single).Output.RequestedFields);
        // Observed live: the model repeated the named companies as requested output fields.
        var repeatedCompanies = Compare(["کگل", "کگهر"]) with { Output = new(RequestedOutputKind.Comparison, ["کگل", "کگهر"]) };
        Assert.Empty(canonicalizer.Canonicalize(repeatedCompanies).Output.RequestedFields);
        // Observed in chat (C5): "rank" requested for a ranking; kept for a non-ranked result.
        Assert.Empty(canonicalizer.Canonicalize(RankPopulation("descending") with { Output = new(RequestedOutputKind.RankedList, ["rank"]) })
            .Output.RequestedFields);
        Assert.Equal(["rank"], canonicalizer.Canonicalize(Retrieve(["کگل"]) with { Output = new(RequestedOutputKind.Scalar, ["rank"]) })
            .Output.RequestedFields);
        var otherField = Compare(["کگل", "کگهر"]) with { Output = new(RequestedOutputKind.Comparison, ["سود عملیاتی"]) };
        Assert.Equal(["سود عملیاتی"], canonicalizer.Canonicalize(otherField).Output.RequestedFields);
    }

    [Fact]
    public void InferredCompanyTotalGrainConstraintIsDroppedOnlyForStatementItems()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        // Observed live (F141-E27): the model restated the statement item's company-total grain as an inferred constraint.
        var inferred = Compare(["کگل", "کگهر"]) with { Constraints = [new("valueGrain", "companyTotal", ConstraintProvenance.Inferred)] };
        Assert.Empty(canonicalizer.Canonicalize(inferred).Constraints);
        var explicitConstraint = Compare(["کگل", "کگهر"]) with { Constraints = [new("valueGrain", "companyTotal", ConstraintProvenance.Explicit)] };
        Assert.Single(canonicalizer.Canonicalize(explicitConstraint).Constraints);
        var other = Compare(["کگل", "کگهر"]) with { Constraints = [new("minimumValue", "1000", ConstraintProvenance.Inferred)] };
        Assert.Single(canonicalizer.Canonicalize(other).Constraints);
        // Observed live (F141-E17): the same restatement named per measure.
        var perMeasure = Compare(["کگل", "کگهر"]) with { Constraints = [new("netProfitValueGrain", "companyTotal", ConstraintProvenance.Inferred)] };
        Assert.Empty(canonicalizer.Canonicalize(perMeasure).Constraints);
        var otherMeasure = Compare(["کگل", "کگهر"]) with { Constraints = [new("revenueValueGrain", "companyTotal", ConstraintProvenance.Inferred)] };
        Assert.Single(canonicalizer.Canonicalize(otherMeasure).Constraints);
    }

    [Fact]
    public void RialThresholdOnAPerShareMeasureIsRialPerShareAndNoOtherUnitIsRewritten()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        // Observed live (F141-E17): "EPS بالای ۲۰۰۰ ریال" extracted with unit irr.
        SemanticUnit EpsUnit(SemanticUnit stated) => ((MeasureThresholdFilter)((FilterGroup)canonicalizer.Canonicalize(FilterPopulation(
            And(Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR), Threshold("earningsPerShare", 2_000m, stated)), withEps: true))
            .Filters!).Children[1]).Threshold.Unit;
        Assert.Equal(SemanticUnit.IRRPerShare, EpsUnit(SemanticUnit.IRR));
        Assert.Equal(SemanticUnit.MillionIRR, EpsUnit(SemanticUnit.MillionIRR));
        var netProfitInRial = canonicalizer.Canonicalize(FilterPopulation(Threshold("netProfit", 1_000m, SemanticUnit.IRR)));
        Assert.Equal(SemanticUnit.IRR, ((MeasureThresholdFilter)netProfitInRial.Filters!).Threshold.Unit);
        // Observed live (F141-E30): the EPS measure's requested unit extracted as irr.
        var epsInRial = FilterPopulation(Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR), withEps: true);
        epsInRial = epsInRial with { Measures = [epsInRial.Measures[0], epsInRial.Measures[1] with { RequestedUnit = SemanticUnit.IRR }] };
        Assert.Equal(SemanticUnit.IRRPerShare, canonicalizer.Canonicalize(epsInRial).Measures[1].RequestedUnit);
        Assert.Equal(SemanticUnit.Unspecified, canonicalizer.Canonicalize(netProfitInRial).Measures[0].RequestedUnit);
        // Observed live (F141-E29): "companyLabel" requested for a population result.
        var labelField = FilterPopulation(Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR)) with { Output = new(RequestedOutputKind.Table, ["companyLabel"]) };
        Assert.Empty(canonicalizer.Canonicalize(labelField).Output.RequestedFields);
    }

    [Fact]
    public void IdenticalRepeatedMeasureIsOneMeasureButDistinctStatementScopesAreKept()
    {
        var canonicalizer = new CanonicalSemanticIntentCanonicalizer(CanonicalBusinessMeasureConceptRegistry.Default);
        // Observed live (F141-E21): the model emitted the compared measure once per named company.
        var repeated = Compare(["کگل", "زاگرس"]) with { Measures = [NetProfit(), NetProfit()] };
        Assert.Single(canonicalizer.Canonicalize(repeated).Measures);
        var scopes = Compare(["کگل", "کگهر"]) with
            { Measures = [NetProfit(FinancialStatementScope.Standalone), NetProfit(FinancialStatementScope.Consolidated)] };
        Assert.Equal(2, canonicalizer.Canonicalize(scopes).Measures.Count);
    }

    // ---------- slice 3b: threshold filters ----------

    [Fact]
    public async Task ThresholdFiltersAreClassifiedAndBoundOnlyInTheItemsOwnUnit()
    {
        var references = await CatalogReferences();
        var single = StatementQueryShapeClassifier.Classify(FilterPopulation(Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR)));
        Assert.Equal(StatementQueryShape.Filter, single.Shape);
        Assert.True(single.PopulationScope);
        var both = FilterPopulation(And(Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR),
            Threshold("earningsPerShare", 2_000m, SemanticUnit.IRRPerShare)), withEps: true);
        var bound = CanonicalIntentCatalogBinding.Bind(both, references);
        Assert.Equal(CanonicalCatalogBindingStatus.Bound, bound.Status);
        Assert.Equal([143, 160], bound.References.Select(r => r.SourceItemId));

        var or = FilterPopulation(new FilterGroup(FilterLogicalOperator.Or, [Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR),
            Threshold("earningsPerShare", 2_000m, SemanticUnit.IRRPerShare)]), withEps: true);
        Assert.Contains("statement_filter_expression_not_supported", CanonicalIntentCatalogBinding.Bind(or, references).Diagnostics);
        var billion = CanonicalIntentCatalogBinding.Bind(FilterPopulation(Threshold("netProfit", 1m, SemanticUnit.BillionIRR)), references);
        Assert.Equal(CanonicalCatalogBindingStatus.Unsupported, billion.Status);
        Assert.Equal("statement_filter_threshold_unit_conversion_not_supported", Assert.Single(billion.Diagnostics));
        var unitless = CanonicalIntentCatalogBinding.Bind(FilterPopulation(Threshold("netProfit", 1_000m, SemanticUnit.Unspecified)), references);
        Assert.Equal(CanonicalCatalogBindingStatus.Incomplete, unitless.Status);
        Assert.Equal("statement_filter_threshold_unit_unspecified", Assert.Single(unitless.Diagnostics));
    }

    [Fact]
    public async Task ThresholdFilterPlanCompilesPredicatesIntoBoundParametersOnTheComparedStatement()
    {
        var intent = FilterPopulation(And(Threshold("netProfit", 1_000m, SemanticUnit.MillionIRR),
            Threshold("earningsPerShare", 2_000m, SemanticUnit.IRRPerShare)), withEps: true);
        var (plan, snapshot) = await PlanAsync(intent, catalogReferences: await CatalogReferences());
        Assert.True(plan.ExecutionEligible, string.Join("|", plan.Diagnostics));
        Assert.Equal(2, plan.Filters.Count);
        Assert.Single(plan.Sources);
        var compilation = new StructuredQueryCompiler().Compile(plan, snapshot);
        Assert.True(compilation.Success, string.Join("|", compilation.Diagnostics));
        var query = compilation.Query!;
        Assert.Contains("> @filter_threshold_0) AS \"FilterMatched0\"", query.Sql, StringComparison.Ordinal);
        Assert.Contains("> @filter_threshold_1) AS \"FilterMatched1\"", query.Sql, StringComparison.Ordinal);
        Assert.Contains("lf0.\"FinancialStatementId\" = st.\"Id\"", query.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("1000", query.Sql, StringComparison.Ordinal);
        Assert.Equal(1_000m, query.Parameters.Single(p => p.Name == "filter_threshold_0").Value);
        Assert.Equal(2_000m, query.Parameters.Single(p => p.Name == "filter_threshold_1").Value);
        Assert.Equal(2, query.SourceCatalogIds.Count);
        Assert.Equal(["MillionIRR", "IRRPerShare"], query.StatementComparison!.Filters!.Select(f => f.Unit));
        Assert.False(query.StatementComparison.Ranked);
    }

    [Fact]
    public void PolicyFiltersAfterResolutionAndNeverTreatsAMissingFilterValueAsAFailedOrZeroValue()
    {
        IReadOnlyDictionary<string, object?> Filtered(Guid company, decimal value, bool? matched, string symbol, decimal? filterValue = null) =>
            new Dictionary<string, object?>(Row(company, value)) { ["FilterValue0"] = filterValue ?? value, ["FilterMatched0"] = matched, ["CompanySymbol"] = symbol };
        var spec = Spec(population: true) with { Filters = [new(0, "source", "سود خالص", PlanFilterOperator.GreaterThan, 1_000m, "MillionIRR")] };
        var population = CrossCompanyStatementComparisonPolicy.Resolve(
            [Filtered(A, 1_200m, true, "کگل"), Filtered(B, 900m, false, "کگهر"), Filtered(C, 1_600m, null, "فملی")], [A, B, C], spec);
        Assert.Equal(StructuredQueryExecutionStatus.Succeeded, population.Status);
        Assert.Equal(["کگل"], population.Rows.Select(r => (string)r["CompanyLabel"]!));
        Assert.Contains("statement_filter_excluded_companies:1", population.Warnings);
        Assert.Contains("statement_filter_value_missing:فملی", population.Warnings);

        var named = CrossCompanyStatementComparisonPolicy.Resolve([Filtered(A, 1_200m, true, "کگل"), Filtered(B, 900m, null, "کگهر")],
            [A, B], Spec() with { Filters = spec.Filters });
        Assert.Equal(StructuredQueryExecutionStatus.Incomplete, named.Status);
        Assert.Equal("statement_filter_value_missing", named.DiagnosticCode);
        Assert.Equal("statement_filter_no_matching_company", CrossCompanyStatementComparisonPolicy.Resolve(
            [Filtered(A, 900m, false, "کگل")], [A], spec).DiagnosticCode);
        // Competing statements of one company with the same listed value but different filter values are not resolved arbitrarily.
        Assert.Equal("requested_item_values_conflict", CrossCompanyStatementComparisonPolicy.Resolve(
            [Filtered(A, 1_200m, true, "کگل", 2_400m), Filtered(A, 1_200m, false, "کگل", 1_900m)], [A], spec).DiagnosticCode);
    }

    // ---------- helpers ----------

    private static RequestedMeasure NetProfit(FinancialStatementScope scope = FinancialStatementScope.Unspecified) =>
        new("سود خالص", "netProfit", MeasureCanonicalizationStatus.Canonicalized, ["netProfit"], MeasureConceptKind.FinancialStatementItem,
            BusinessMeasureDimension.CurrencyAmount, new(StatementScope: scope), SemanticUnit.Unspecified, null, EffectiveUnitEvidence.Unresolved);

    private static CanonicalSemanticIntent Intent(AnalyticalOperation operation, ComparisonIntent comparison, EntityScope scope,
        RankingIntent? ranking, RequestedOutputKind output, FinancialStatementScope statementScope = FinancialStatementScope.Unspecified) =>
        new(2, [NetProfit(statementScope)], operation, comparison, new(ReportingPeriodKind.Unspecified), scope, [], [], null, null, ranking,
            new(output, []), [], new(false, [], []));

    private static CanonicalSemanticIntent Compare(IReadOnlyList<string> companies,
        FinancialStatementScope scope = FinancialStatementScope.Unspecified) =>
        Intent(new(AnalyticalOperationKind.Compare), new(ComparisonMode.MultiCompany), new(EntityScopeKind.NamedCompanies, companies),
            null, RequestedOutputKind.Comparison, scope);

    private static CanonicalSemanticIntent Retrieve(IReadOnlyList<string> companies) =>
        Intent(new(AnalyticalOperationKind.Retrieve), new(ComparisonMode.None), new(EntityScopeKind.NamedCompanies, companies),
            null, RequestedOutputKind.Scalar);

    private static CanonicalSemanticIntent Rank(IReadOnlyList<string> companies, string direction, int? limit = null) =>
        Intent(new(AnalyticalOperationKind.Rank, direction), new(ComparisonMode.MultiCompany), new(EntityScopeKind.NamedCompanies, companies),
            new(direction, "netProfit", limit), RequestedOutputKind.RankedList);

    private static MeasureThresholdFilter Threshold(string concept, decimal value, SemanticUnit unit) =>
        new(concept, new NumericThreshold(value, unit, NumericComparisonOperator.GreaterThan));

    private static FilterGroup And(params FilterExpression[] children) => new(FilterLogicalOperator.And, children);

    private static CanonicalSemanticIntent FilterPopulation(FilterExpression filter, bool withEps = false) =>
        Intent(new(AnalyticalOperationKind.DiscoverPopulation), new(ComparisonMode.None), new(EntityScopeKind.UnboundCompanyPopulation, []),
            null, RequestedOutputKind.Table) with
        {
            Measures = withEps
                ? [NetProfit(), new("EPS", "earningsPerShare", MeasureCanonicalizationStatus.Canonicalized, ["earningsPerShare"],
                    MeasureConceptKind.FinancialStatementItem, BusinessMeasureDimension.CurrencyPerShare, new(), SemanticUnit.IRRPerShare, null,
                    EffectiveUnitEvidence.Unresolved)]
                : [NetProfit()],
            Filters = filter
        };

    private static CanonicalSemanticIntent RankPopulation(string direction, int? limit = null) =>
        Intent(new(AnalyticalOperationKind.Rank, direction), new(ComparisonMode.None), new(EntityScopeKind.UnboundCompanyPopulation, []),
            new(direction, "netProfit", limit), RequestedOutputKind.RankedList);

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "specs", "141-schema-aware-semantic-query-engine");
    }

    private static (JsonCatalogNavigationHierarchyProvider Provider, StructuredQueryCatalogSnapshot Snapshot) Catalog()
    {
        var hierarchy = Path.Combine(Root(), "artifacts", "navigation-hierarchy.yaml");
        var catalog = Path.Combine(Root(), "SemanticCatalog");
        return (new JsonCatalogNavigationHierarchyProvider(hierarchy, catalog), new StructuredQueryCatalogSnapshotBuilder().Build(catalog, hierarchy));
    }

    private static async Task<IReadOnlyList<FinancialValueCatalogReference>> CatalogReferences()
    {
        var (provider, snapshot) = Catalog();
        return await provider.GetFinancialSourceReferencesAsync(snapshot.CatalogVersion, snapshot.CatalogHash, CancellationToken.None);
    }

    private static async Task<(FinancialSourceTitleResolution Resolution, StructuredQueryCatalogSnapshot Snapshot)> NetProfitResolution()
    {
        var (provider, snapshot) = Catalog();
        var references = await provider.GetFinancialSourceReferencesAsync(snapshot.CatalogVersion, snapshot.CatalogHash, CancellationToken.None);
        var canonical = CanonicalSourceItemBindingResolver.Resolve(NetProfit(), references, Provider);
        var bound = Assert.IsType<FinancialValueCatalogReference>(canonical.Reference);
        return (new FinancialSourceTitleResolution(FinancialSourceTitleResolutionStatus.Resolved, canonical.DiagnosticCode, bound, [bound],
            new Dictionary<string, string?>(), bound.UnitClassification, bound.QueryAuthorizationStatus ?? "authorized"), snapshot);
    }

    private static async Task<(StructuredQueryPlan Plan, StructuredQueryCatalogSnapshot Snapshot)> PlanAsync(CanonicalSemanticIntent intent,
        bool scopeConfigured = true, string? unknownCompany = null, IReadOnlyList<FinancialValueCatalogReference>? catalogReferences = null)
    {
        var (provider, snapshot) = Catalog();
        var (resolution, _) = await NetProfitResolution();
        var source = snapshot.DirectSources.Single(x => x.CatalogId == resolution.Reference!.CatalogId);
        var handoff = FinancialSourcePlannerHandoff.CreateCrossCompany(intent, resolution, snapshot.CatalogVersion, snapshot.CatalogHash, source,
            catalogReferences);
        var requirements = handoff.Binding!.PlannerRequirements;
        var required = requirements.DimensionIds.Concat(requirements.RelationshipIds)
            .Concat(requirements.FinancialValueReferences!.Select(r => r.CatalogId)).ToArray();
        var seed = await provider.GetContextAsync(new SchemaContextExpansionRequest(1, snapshot.CatalogVersion, snapshot.CatalogHash, [], 0,
            RequiredCatalogIds: required), CancellationToken.None);
        var navigation = new SchemaNavigationResult(1, snapshot.CatalogVersion, snapshot.CatalogHash, [], seed!.NodeIds, [],
            requirements.DimensionIds, requirements.EntityKinds, requirements.RelationshipIds, requirements.SourcePolicies,
            requirements.PeriodPolicies, [], []);
        var planner = new OfflineStructuredQueryPlanningService(new FixedExtractor(requirements), new FixedNavigator(navigation), provider,
            new SchemaContextCompletenessValidator(), new StructuredQueryPlanBuilder(), new StructuredQueryPlanValidator(executionEnabled: true),
            new LabelCompanyBinder(unknownCompany),
            companyDiscoveryScopeResolver: new AuthorizedCompanyDiscoveryScopeResolver(scopeConfigured
                ? [new AuthorizedCompanyDiscoveryScope(TenantId, "unit-scope", "unit-approval", true)] : []));
        var planning = await planner.PlanAsync(new SchemaNavigationRequest(1, "unit", snapshot.CatalogVersion, snapshot.CatalogHash,
            TenantId: TenantId), requirements, CancellationToken.None);
        return (planning.Plan, snapshot);
    }

    private sealed class FixedExtractor(QuestionSchemaRequirements requirements) : IQuestionSchemaRequirementsExtractor
    {
        public Task<QuestionSchemaRequirements> ExtractAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(requirements);
    }

    private sealed class FixedNavigator(SchemaNavigationResult result) : IVectorlessSchemaNavigator
    {
        public Task<SchemaNavigationResult> NavigateQuestionAsync(SchemaNavigationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(result);
        public Task<SchemaNavigationResult> NavigateAsync(SchemaNavigationRequest request, QuestionSchemaRequirements requirements,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    /// <summary>Binds every label to a deterministic company id except the configured unknown label.</summary>
    private sealed class LabelCompanyBinder(string? unknown) : IStructuredQueryCompanyBinder
    {
        public Task<StructuredCompanyBinding> ResolveAsync(string? mention, Guid tenantId, CancellationToken cancellationToken)
        {
            if (mention == unknown)
                return Task.FromResult(new StructuredCompanyBinding(StructuredCompanyBindingStatus.NotFound, null, [], null, "company_not_found"));
            var id = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(mention ?? string.Empty)));
            return Task.FromResult(new StructuredCompanyBinding(StructuredCompanyBindingStatus.Found, id, [id], "unit-test-binding", null));
        }
    }
}
