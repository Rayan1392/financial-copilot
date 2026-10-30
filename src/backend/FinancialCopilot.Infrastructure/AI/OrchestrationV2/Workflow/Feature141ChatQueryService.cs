using System.Text.RegularExpressions;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

/// <summary>Compatibility entry point; authorization is now the general feature flag plus normal authentication.</summary>
public static class Feature141DeveloperUatAuthorizationPolicy
{
    public static bool IsAuthorized(IConfiguration configuration,
        Microsoft.Extensions.Hosting.IHostEnvironment hostEnvironment, AiQueryRequest request)
    {
        _ = hostEnvironment;
        if (!configuration.GetValue<bool>("Feature141:Enabled")
            || request.TenantId == Guid.Empty || request.ActorId == Guid.Empty) return false;
        return request.ActorType == ActorType.User
               && request.AuthenticationMode == AuthenticationMode.WebAppUser
               && request.UserId == request.ActorId
            || request.ActorType == ActorType.ApiClient
               && request.AuthenticationMode == AuthenticationMode.ApiClient
               && request.ApiClientId == request.ActorId;
    }
}

/// <summary>
/// A Feature 141 chat answer. <paramref name="Definitive"/> is true when the governed engine owned the question: a plan
/// was produced and answered or refused by the engine, or a material constraint was refused with a typed outcome. A
/// non-definitive answer (extraction failure, clarification, unbound measure) never replaces another route's answer.
/// <paramref name="Refused"/> marks the typed Unsupported outcome (a material constraint the engine cannot honor).
/// <paramref name="Answered"/> marks a result with verified rows (a value, comparison, ranking including a disclosed partial
/// ranking, or a discovery list); NoData, Ambiguous, Unsupported, refused and failed results are not answers.
/// </summary>
internal sealed record Feature141ChatAnswer(string Text, bool Definitive, bool Refused = false, bool Answered = false)
{
    /// <summary>Billing completion status of the turn: one credit for an answer, nothing for any other result.</summary>
    public string CompletionStatus => Answered ? "Completed" : Definitive ? "ValidationFailed" : "ClarificationRequired";

    /// <summary>The chat outcome a definitive result is presented with (the engine's refusal is the Unsupported outcome).</summary>
    public DialogueOutcome Outcome => Refused ? DialogueOutcome.Unsupported : DialogueOutcome.Answered;
}

/// <summary>
/// A named statement period («دوره شش‌ماهه منتهی به شهریور ۱۴۰۵»): the Jalali month it ends in and, when stated, its
/// length in months. The verified statement retrieval reads the latest reported period only, so a named period is
/// answered only when the returned latest statement is exactly that period; any other statement is refused.
/// </summary>
internal sealed record StatementPeriodRequirement(DateOnly EndMonthStart, DateOnly EndMonthEnd, int? Months)
{
    private static readonly Regex Duration = new(@"(سه|شش|نه|دوازده|۳|۶|۹|۱۲|3|6|9|12)\s*‌?\s*ماهه",
        RegexOptions.CultureInvariant);

    /// <summary>A single-company statement intent naming a period, rewritten to the latest period plus the period to verify.</summary>
    internal static (CanonicalSemanticIntent Intent, StatementPeriodRequirement Requirement)? TryCreate(
        CanonicalSemanticIntent intent, string question)
    {
        if (intent.Measures.Count == 0 || intent.Measures.Any(measure => measure.Kind != MeasureConceptKind.FinancialStatementItem)
            || intent.EntityScope is not { Kind: EntityScopeKind.NamedCompanies, CompanyLabels.Count: 1 })
            return null;
        var end = intent.Period.Kind switch
        {
            ReportingPeriodKind.ExplicitMonth => PersianReportingPeriodNormalizer.Normalize(intent.Period),
            ReportingPeriodKind.DateRange => PersianReportingPeriodNormalizer.NormalizeRange(intent.Period)?.Last,
            _ => null
        };
        if (end is not { Status: PersianReportingPeriodStatus.Normalized, Start: { } start, End: { } finish }) return null;
        var months = Duration.Match(question) is { Success: true } match ? MonthsOf(match.Groups[1].Value) : null;
        return (intent with
        {
            Period = new ReportingPeriodIntent(ReportingPeriodKind.LatestAvailable),
            // A period length the model restated as a constraint is verified on the result, not executed.
            Constraints = intent.Constraints
                .Where(constraint => !constraint.Name.Contains("period", StringComparison.OrdinalIgnoreCase)).ToArray()
        }, new(start, finish, months));
    }

    /// <summary>True when every returned statement ends in the named month and, when stated, covers the named length.</summary>
    internal bool IsSatisfiedBy(IReadOnlyList<StructuredAnswerEvidence> evidence) => evidence.Count > 0 && evidence.All(item =>
        PersianReportingPeriodText.Parse(item.PeriodEnd) is { } periodEnd && periodEnd >= EndMonthStart && periodEnd <= EndMonthEnd
        && (Months is null || MonthsOf(item.Facts.GetValueOrDefault("PeriodType")) == Months));

    internal static string MismatchText(IReadOnlyList<StructuredAnswerEvidence> evidence)
    {
        var latest = evidence.Count == 0 ? null : PersianReportingPeriodText.StatementPeriod(
            evidence[0].Facts.GetValueOrDefault("PeriodType"), PersianReportingPeriodText.Parse(evidence[0].PeriodStart),
            PersianReportingPeriodText.Parse(evidence[0].PeriodEnd));
        return "صورت مالی دورهٔ درخواستی، آخرین دورهٔ گزارش‌شدهٔ این شرکت نیست" +
               (latest is null ? "" : $" (آخرین دورهٔ موجود: {latest})") +
               "؛ در حال حاضر فقط آخرین دورهٔ گزارش‌شده پاسخ داده می‌شود و عددی برای دورهٔ دیگر گزارش نمی‌کنم.";
    }

    private static int? MonthsOf(string? value) => value switch
    {
        "سه" or "۳" or "3" or "ThreeMonths" => 3,
        "شش" or "۶" or "6" or "SixMonths" => 6,
        "نه" or "۹" or "9" or "NineMonths" => 9,
        "دوازده" or "۱۲" or "12" or "TwelveMonths" => 12,
        _ => null
    };
}

/// <summary>
/// Executes the catalog-governed Feature 141 pipeline for an authenticated chat request. SQL is
/// never accepted from the model: the model produces typed intent, while the deterministic
/// planner/compiler and the read-only executor own all executable structure.
/// </summary>
internal sealed class Feature141ChatQueryService(
    IConfiguration configuration,
    IAiModelExecutionService modelExecutionService,
    JsonCatalogNavigationHierarchyProvider hierarchyProvider,
    IOfflineStructuredQueryPlanningService planningService,
    IStructuredQueryCompiler compiler,
    StructuredQueryCatalogSnapshot catalogSnapshot,
    IStructuredQueryAuthorizer authorizer,
    IReadOnlyStructuredQueryExecutor executor,
    IPersianStructuredQueryResponseGenerator responseGenerator,
    ILogger<Feature141ChatQueryService> logger,
    IQuestionSchemaRequirementsExtractor requirementsExtractor)
{
    private const string ProviderName = "NoavaranCurrentApi";

    public bool IsEnabled(AiQueryRequest request)
    {
        if (!configuration.GetValue<bool>("Feature141:Enabled")
            || request.TenantId == Guid.Empty || request.ActorId == Guid.Empty)
            return false;

        return request.ActorType == ActorType.User
               && request.AuthenticationMode == AuthenticationMode.WebAppUser
               && request.UserId is { } userId && userId != Guid.Empty && request.ActorId == userId
            || request.ActorType == ActorType.ApiClient
               && request.AuthenticationMode == AuthenticationMode.ApiClient
               && request.ApiClientId is { } clientId && clientId != Guid.Empty && request.ActorId == clientId;
    }

    public Task<string> ExecuteAsync(AiQueryRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(request, null, cancellationToken);

    public async Task<string> ExecuteAsync(
        AiQueryRequest request,
        string? validatedCompanySymbol,
        CancellationToken cancellationToken) =>
        (await AnswerAsync(request, validatedCompanySymbol, cancellationToken).ConfigureAwait(false)).Text;

    public async Task<Feature141ChatAnswer> AnswerAsync(
        AiQueryRequest request,
        string? validatedCompanySymbol,
        CancellationToken cancellationToken)
    {
        static Feature141ChatAnswer Pending(string text) => new(text, false);
        if (!IsEnabled(request))
            return Pending("این قابلیت برای درخواست احراز هویت‌شدهٔ فعلی فعال نیست.");

        logger.LogInformation(
            "Feature141 registered financial source route selected. Route={Route} CorrelationId={CorrelationId}",
            "query_registered_financial_source", request.CorrelationId);
        try
        {
            var extractor = new LlmCanonicalSemanticIntentExtractor(
                modelExecutionService,
                new CanonicalSemanticIntentValidator(),
                configuredDeadline: TimeSpan.FromSeconds(25),
                maximumOutputTokens: 900,
                maximumQuestionCharacters: 4000,
                tenantId: request.TenantId,
                correlationId: request.CorrelationId);
            var extraction = await extractor.ExtractAsync(request.Message, cancellationToken).ConfigureAwait(false);
            if (!extraction.Succeeded || extraction.Intent is null)
            {
                logger.LogInformation(
                    "Feature141 intent extraction failed. FailureCategory={FailureCategory} Diagnostic={Diagnostic} CorrelationId={CorrelationId}",
                    extraction.FailureCategory, extraction.SanitizedProviderDiagnostic, request.CorrelationId);
                return Pending("برای تعیین دقیق قلم مالی، شرکت، دوره و نوع گزارش به توضیح بیشتری نیاز دارم.");
            }

            var intent = extraction.Intent;
            // A router-validated symbol replaces the extracted label of a single-company request only;
            // replacing the labels of a multi-company request would silently drop the other companies.
            if (!string.IsNullOrWhiteSpace(validatedCompanySymbol) &&
                intent.EntityScope.Kind == EntityScopeKind.NamedCompanies &&
                intent.EntityScope.CompanyLabels.Count <= 1)
            {
                intent = intent with
                {
                    EntityScope = intent.EntityScope with { CompanyLabels = [validatedCompanySymbol] }
                };
            }
            intent = WithDefaultStatementBasis(intent, request.Message);
            logger.LogInformation(
                "Feature141 intent extracted. Route={Route} CompanySymbols={CompanySymbols} Products={Products} RequestedMeasures={RequestedMeasures} PeriodKind={PeriodKind} PeriodCount={PeriodCount} ComparisonMode={ComparisonMode} Presentation={Presentation}",
                "query_registered_financial_source",
                string.Join(',', intent.EntityScope.CompanyLabels),
                string.Join(',', intent.Products.Select(product => product.UserLabel)),
                string.Join(',', intent.Measures.Select(measure => measure.ConceptId ?? measure.Kind.ToString())),
                intent.Period.Kind, intent.Period.NumberOfPeriods, intent.Comparison.Mode,
                intent.Output.Kind);
            if (intent.Ambiguity.RequiresClarification)
                return Pending(RenderClarification(intent.Ambiguity.Questions,
                    "درخواست مالی برای اجرای دقیق نیاز به شفاف‌سازی دارد."));
            // A named statement period is answered through the verified latest-period retrieval and then verified.
            var statementPeriod = StatementPeriodRequirement.TryCreate(intent, request.Message);
            if (statementPeriod is { } named)
            {
                intent = named.Intent;
                logger.LogInformation(
                    "Feature141 named statement period. EndMonthStart={EndMonthStart} EndMonthEnd={EndMonthEnd} Months={Months} CorrelationId={CorrelationId}",
                    named.Requirement.EndMonthStart, named.Requirement.EndMonthEnd, named.Requirement.Months, request.CorrelationId);
            }
            // Cross-company comparison, ranking and threshold filtering of statement items use the verified
            // cross-company handoff (StatementQueryShapeClassifier owns which intents are representable).
            var shapeResult = intent.Measures.Count > 0
                && intent.Measures.All(item => item.Kind == MeasureConceptKind.FinancialStatementItem)
                    ? StatementQueryShapeClassifier.Classify(intent)
                    : null;
            var statementShape = shapeResult?.Shape;
            logger.LogInformation(
                "Feature141 statement shape classified. Shape={Shape} Diagnostic={Diagnostic} Operation={Operation} Ranking={Ranking} Constraints={Constraints} RequestedFields={RequestedFields} Filters={Filters} CorrelationId={CorrelationId}",
                statementShape?.ToString() ?? "none", shapeResult?.Diagnostic ?? "none", intent.Operation.Kind,
                intent.Ranking is null ? "none" : $"{intent.Ranking.Direction}:{intent.Ranking.MeasureConcept}:{intent.Ranking.Limit}",
                string.Join(',', intent.Constraints.Select(constraint => $"{constraint.Name}={constraint.Value}/{constraint.Provenance}")),
                string.Join(',', intent.Output.RequestedFields), intent.Filters is null ? "none" : intent.Filters.GetType().Name,
                request.CorrelationId);
            var crossCompany = statementShape is StatementQueryShape.MultiCompanyComparison
                or StatementQueryShape.Ranking or StatementQueryShape.Filter;
            if (intent.Measures.Count != 1 && !crossCompany)
                return Pending("لطفاً در هر پرسش یک قلم مالی مشخص را درخواست کنید تا منبع و واحد آن بدون حدس انتخاب شود.");

            var measure = intent.Measures[0];
            var navigationRequest = new SchemaNavigationRequest(
                1,
                request.Message,
                catalogSnapshot.CatalogVersion,
                catalogSnapshot.CatalogHash,
                TenantId: request.TenantId,
                CorrelationId: request.CorrelationId);
            var requiresGeneralPlanner = !crossCompany && (measure.Kind != MeasureConceptKind.FinancialStatementItem
                || intent.Operation.Kind != AnalyticalOperationKind.Retrieve
                || intent.Comparison.Mode != ComparisonMode.None
                || intent.Period.Kind is not (ReportingPeriodKind.LatestAvailable or ReportingPeriodKind.Unspecified)
                || intent.Products.Count != 0 || intent.Categories.Count != 0 || intent.Filters is not null
                || intent.Aggregation is not null || intent.Ranking is not null || intent.Constraints.Count != 0
                || intent.Output.RequestedFields.Count != 0);
            if (requiresGeneralPlanner)
            {
                // The general planner path has no numeric-threshold representation (monthly production or
                // sales filters among them); executing it would answer the question without the threshold.
                if (DropsMaterialThreshold(intent))
                {
                    logger.LogInformation(
                        "Feature141 typed outcome. Outcome={Outcome} Diagnostic={Diagnostic} CorrelationId={CorrelationId}",
                        "Unsupported", "threshold_filter_not_supported_on_general_planner_path", request.CorrelationId);
                    return new("شرط عددی (مانند «بالای ۵۰۰ تن» یا «بیشتر از مبلغی مشخص») روی آمار تولید و فروش ماهانه هنوز پشتیبانی نمی‌شود؛ برای جلوگیری از پاسخ نادرست، پرس‌وجو بدون این شرط اجرا نشد. می‌توانید مقدار ماهانهٔ یک شرکت و محصول مشخص را بپرسید.", true, true);
                }
                // Monthly measures read single-month reports only (catalog OutputType 0); a cumulative/fiscal-year-to-date
                // request would otherwise be answered with one month's value.
                if (RequestsCumulativeMonthlyScope(intent))
                {
                    logger.LogInformation(
                        "Feature141 typed outcome. Outcome={Outcome} Diagnostic={Diagnostic} CorrelationId={CorrelationId}",
                        "Unsupported", "monthly_cumulative_reporting_scope_not_supported", request.CorrelationId);
                    return new("آمار تجمعی (از ابتدای سال مالی) تولید و فروش هنوز پشتیبانی نمی‌شود و به‌جای آن عدد یک ماه گزارش نمی‌شود. می‌توانید مقدار یک ماه مشخص را بپرسید.", true, true);
                }
                // The typed monthly measure is always in the requirements outline: the question-ranked outline
                // otherwise omits it when no multi-word alias occurs (e.g. «تولید گندله کگل در شهریور ۱۴۰۵»).
                navigationRequest = navigationRequest with { RequiredCatalogIds = RequiredMonthlyMeasureIds(intent) };
                // The model names the Persian year and month; the calendar bounds are computed here so
                // model-generated Gregorian dates never become authoritative.
                var requirements = await requirementsExtractor.ExtractAsync(navigationRequest, cancellationToken).ConfigureAwait(false);
                if (intent.Period.Kind == ReportingPeriodKind.ExplicitMonth)
                {
                    var period = PersianReportingPeriodNormalizer.Normalize(intent.Period);
                    if (period.Status != PersianReportingPeriodStatus.Normalized)
                    {
                        logger.LogInformation(
                            "Feature141 Persian month could not be normalized. CorrelationId={CorrelationId} Diagnostic={Diagnostic}",
                            request.CorrelationId, period.DiagnosticCode);
                        return Pending("ماه شمسی درخواستی قابل تشخیص نیست؛ لطفاً نام ماه و سال شمسی را دقیق بنویسید (مثلاً شهریور ۱۴۰۵).");
                    }
                    requirements = ReportingPeriodRequirementsNormalizer.Apply(requirements, period);
                }
                // A month range («از فروردین تا شهریور ۱۴۰۵») is bounded deterministically in the same way; a range
                // that cannot be normalized keeps the extracted requirements and the planner validates them.
                else if (intent.Period.Kind == ReportingPeriodKind.DateRange
                    && PersianReportingPeriodNormalizer.NormalizeRange(intent.Period) is { } range)
                    requirements = ReportingPeriodRequirementsNormalizer.Apply(requirements, range.First, range.Last);
                var generalPlan = await planningService.PlanAsync(navigationRequest, requirements, cancellationToken).ConfigureAwait(false);
                return await ExecutePlannedAsync(request, generalPlan, cancellationToken).ConfigureAwait(false);
            }

            var references = await hierarchyProvider.GetFinancialSourceReferencesAsync(
                catalogSnapshot.CatalogVersion, catalogSnapshot.CatalogHash, cancellationToken).ConfigureAwait(false);
            if (crossCompany)
            {
                // Same typed gate as the verified engine path: unrepresentable units, operators or bindings stop here.
                var typedBinding = CanonicalIntentCatalogBinding.Bind(intent, references);
                if (typedBinding.Status != CanonicalCatalogBindingStatus.Bound)
                {
                    logger.LogInformation(
                        "Feature141 typed outcome. Outcome={Outcome} Diagnostic={Diagnostic} CorrelationId={CorrelationId}",
                        typedBinding.Status, string.Join(',', typedBinding.Diagnostics), request.CorrelationId);
                    var refused = typedBinding.Status == CanonicalCatalogBindingStatus.Unsupported;
                    return new(RenderHandoffFailure(typedBinding.Diagnostics.LastOrDefault() ?? string.Empty), refused, refused);
                }
            }
            var canonical = CanonicalSourceItemBindingResolver.Resolve(measure, references, ProviderName);
            var resolution = canonical.Status == CanonicalSourceItemBindingStatus.Bound
                             && canonical.Reference is { } bound
                ? new FinancialSourceTitleResolution(
                    FinancialSourceTitleResolutionStatus.Resolved,
                    canonical.DiagnosticCode,
                    bound,
                    [bound],
                    new Dictionary<string, string?>(),
                    bound.UnitClassification,
                    bound.QueryAuthorizationStatus ?? (bound.Queryable ? "authorized" : "not-authorized"))
                : FinancialSourceItemTitleResolver.Resolve(
                    new FinancialSourceTitleResolutionRequest(measure.UserPhrase, ProviderName), references);

            if (resolution.Status == FinancialSourceTitleResolutionStatus.Ambiguous)
            {
                var labels = (resolution.Candidates ?? []).Select(item => item.TitleFa)
                    .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).Take(5).ToArray();
                return Pending(labels.Length == 0
                    ? "چند قلم مالی با این عبارت سازگار است؛ لطفاً عنوان دقیق قلم صورت مالی را مشخص کنید."
                    : $"چند قلم مالی با این عبارت سازگار است؛ لطفاً یکی را دقیق مشخص کنید: {string.Join("، ", labels)}.");
            }
            if (resolution.Status != FinancialSourceTitleResolutionStatus.Resolved || resolution.Reference is null)
                return Pending("قلم مالی در فهرست ثبت‌شده به‌صورت یکتا پیدا نشد؛ لطفاً عنوان دقیق قلم صورت مالی را بنویسید.");

            var directSource = catalogSnapshot.DirectSources.SingleOrDefault(source =>
                string.Equals(source.CatalogId, resolution.Reference.CatalogId, StringComparison.Ordinal));
            var handoff = crossCompany
                ? FinancialSourcePlannerHandoff.CreateCrossCompany(
                    intent, resolution, catalogSnapshot.CatalogVersion, catalogSnapshot.CatalogHash, directSource, references)
                : FinancialSourcePlannerHandoff.Create(
                    intent, resolution, catalogSnapshot.CatalogVersion, catalogSnapshot.CatalogHash, directSource);
            if (handoff.Status != FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation
                || handoff.Binding is null)
                return Pending(RenderHandoffFailure(handoff.DiagnosticCode));

            var planned = await planningService.PlanAsync(
                navigationRequest, handoff.Binding.PlannerRequirements, cancellationToken).ConfigureAwait(false);
            return await ExecutePlannedAsync(request, planned, cancellationToken, statementPeriod?.Requirement).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Feature141 chat execution failed without exposing internal details. CorrelationId={CorrelationId}",
                request.CorrelationId);
            return Pending("اجرای امن پرس‌وجوی مالی کامل نشد؛ هیچ عددی گزارش نمی‌کنم. لطفاً عنوان قلم، شرکت و دوره را دقیق‌تر بنویسید.");
        }
    }

    /// <summary>Executes an engine-owned plan; every result is definitive, and only one with verified rows is an answer.</summary>
    private async Task<Feature141ChatAnswer> ExecutePlannedAsync(AiQueryRequest request,
        OfflineStructuredQueryPlanningResult planned, CancellationToken cancellationToken,
        StatementPeriodRequirement? requiredStatementPeriod = null)
    {
            if (!planned.Validation.ExecutionEligible)
            {
                logger.LogInformation(
                    "Feature141 plan was not execution eligible. CorrelationId={CorrelationId} Diagnostics={Diagnostics} Operation={Operation} OperationKind={OperationKind} Presentation={Presentation} PeriodPolicies={PeriodPolicies} PeriodStart={PeriodStart} PeriodEnd={PeriodEnd} MaximumPeriods={MaximumPeriods} RequirementsUncertain={RequirementsUncertain} Uncertainty={Uncertainty}",
                    request.CorrelationId, string.Join(',', planned.Validation.Diagnostics),
                    planned.Plan.FinancialOperation.Operation, planned.Plan.FinancialOperation.Kind, planned.Plan.Presentation,
                    string.Join(',', planned.Plan.Period.Policies), planned.Plan.Period.ReportingPeriodStart,
                    planned.Plan.Period.ReportingPeriodEnd, planned.Plan.Period.MaximumPeriods,
                    planned.Requirements.HasUnknownOrAmbiguousRequirements, planned.Requirements.UncertaintyReason);
                return Definitive(responseGenerator.Generate(new StructuredQueryAnswerRequest(
                    planned.Plan, null, "planning_validation", planned.Validation.Diagnostics)));
            }

            var compilation = compiler.Compile(planned.Plan, catalogSnapshot);
            if (!compilation.Success || compilation.Query is null)
            {
                logger.LogWarning(
                    "Feature141 compilation failed. CorrelationId={CorrelationId} Diagnostics={Diagnostics}",
                    request.CorrelationId, string.Join(',', compilation.Diagnostics));
                return Definitive(responseGenerator.Generate(new StructuredQueryAnswerRequest(
                    planned.Plan, null, "sql_compilation", compilation.Diagnostics)));
            }

            var grant = authorizer.Authorize(compilation.Query, request.TenantId);
            if (grant is null)
            {
                logger.LogWarning(
                    "Feature141 catalog authorization rejected a compiled query. CorrelationId={CorrelationId} Fingerprint={Fingerprint}",
                    request.CorrelationId, compilation.Query.QueryFingerprint);
                return new("این درخواست با فهرست منابع مجاز یا دامنهٔ شرکت سازگار نیست؛ هیچ داده‌ای خوانده نشد.", true);
            }

            var execution = await executor.ExecuteAsync(compilation.Query, grant, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Feature141 query completed. CorrelationId={CorrelationId} Fingerprint={Fingerprint} Status={Status} Rows={RowCount} ElapsedMs={ElapsedMs}",
                request.CorrelationId, compilation.Query.QueryFingerprint, execution.Status,
                execution.Rows.Count, execution.Elapsed?.TotalMilliseconds);
            var response = responseGenerator.Generate(new StructuredQueryAnswerRequest(
                planned.Plan, execution, "postgresql_execution",
                planned.Validation.Diagnostics.Concat(compilation.Diagnostics).Distinct(StringComparer.Ordinal).ToArray()));
            var answer = Definitive(response);
            if (requiredStatementPeriod is null || !answer.Answered || requiredStatementPeriod.IsSatisfiedBy(response.Evidence))
                return answer;
            logger.LogInformation(
                "Feature141 typed outcome. Outcome={Outcome} Diagnostic={Diagnostic} CorrelationId={CorrelationId}",
                "NoData", "named_statement_period_is_not_the_latest_reported_period", request.CorrelationId);
            return new(StatementPeriodRequirement.MismatchText(response.Evidence), true);
    }

    /// <summary>
    /// A rendered engine result. Only Answered rows, or the Incomplete rows of a disclosed partial ranking or discovery
    /// list, answer the question; NoData, Ambiguous, Unsupported, Failed and row-less results do not.
    /// </summary>
    internal static Feature141ChatAnswer Definitive(PersianStructuredQueryResponse response) =>
        new(response.TextAnswer, true, Answered: response.Status is StructuredAnswerStatus.Answered or StructuredAnswerStatus.Incomplete
            && response.TableRows.Count > 0);

    /// <summary>
    /// True when the intent carries a numeric threshold the general planner path cannot honor. "Which companies produced X"
    /// is discovery of positive recorded production, so a single "production &gt; 0" threshold restates that selection rule
    /// and is not dropped; every other threshold is.
    /// </summary>
    internal static bool DropsMaterialThreshold(CanonicalSemanticIntent intent) =>
        ContainsThreshold(intent.Filters)
        && !(intent.Operation.Kind == AnalyticalOperationKind.DiscoverPopulation
             && intent.Filters is MeasureThresholdFilter
             {
                 MeasureConcept: CanonicalBusinessMeasureConceptRegistry.ProductionQuantity,
                 Threshold: { Operator: NumericComparisonOperator.GreaterThan, Value: 0m }
             });

    /// <summary>True when a monthly production or sales measure is requested on a cumulative (fiscal-year-to-date) basis.</summary>
    internal static bool RequestsCumulativeMonthlyScope(CanonicalSemanticIntent intent) =>
        intent.Measures.Any(measure => measure.Qualifiers.ReportingBasis == MeasureReportingBasis.MonthlyCumulative
            && measure.Kind is MeasureConceptKind.ProductionQuantity or MeasureConceptKind.SalesQuantity
                or MeasureConceptKind.SalesAmount or MeasureConceptKind.SalesRate);

    /// <summary>
    /// The catalog monthly measure of a single typed monthly measure for named companies; nothing for other intents.
    /// Unbound producer discovery keeps its deterministic planner rewrite, which applies only when the model did not
    /// select the measure itself.
    /// </summary>
    internal static IReadOnlyList<string> RequiredMonthlyMeasureIds(CanonicalSemanticIntent intent) => intent.Measures.Count == 1
        && intent.EntityScope is { Kind: EntityScopeKind.NamedCompanies, CompanyLabels.Count: > 0 }
        ? intent.Measures[0].Kind switch
        {
            MeasureConceptKind.ProductionQuantity => ["metric_monthly_production_quantity"],
            MeasureConceptKind.SalesQuantity => ["metric_monthly_sales_quantity"],
            MeasureConceptKind.SalesAmount => ["metric_monthly_sales"],
            _ => []
        }
        : [];

    /// <summary>
    /// The default statement basis: a statement-item question that names no basis is answered on the standalone statements
    /// (غیرتلفیقی). The compiled query filters that basis, so a missing standalone statement is NoData, never a consolidated
    /// substitute. A question with consolidated wording keeps the model's basis, so a consolidated request is never replaced.
    /// </summary>
    internal static CanonicalSemanticIntent WithDefaultStatementBasis(CanonicalSemanticIntent intent, string question)
    {
        if (intent.Measures.Count == 0 || intent.Measures.Any(measure =>
                measure.Kind != MeasureConceptKind.FinancialStatementItem
                || measure.Qualifiers.StatementScope != FinancialStatementScope.Unspecified))
            return intent;
        if (question.Contains("تلفیقی", StringComparison.Ordinal) || question.Contains("consolidat", StringComparison.OrdinalIgnoreCase))
            return intent;
        return intent with
        {
            Measures = intent.Measures.Select(measure => measure with
            {
                Qualifiers = measure.Qualifiers with { StatementScope = FinancialStatementScope.Standalone }
            }).ToArray()
        };
    }

    private static bool ContainsThreshold(FilterExpression? filter) => filter switch
    {
        MeasureThresholdFilter => true,
        FilterGroup group => (group.Children ?? []).Any(ContainsThreshold),
        _ => false
    };

    private static string RenderClarification(IReadOnlyList<string> questions, string fallback) =>
        questions.Count == 0 ? fallback : string.Join(" ", questions);

    private static string RenderHandoffFailure(string diagnosticCode) => diagnosticCode switch
    {
        "statement_scope_evidence_unavailable" =>
            "لطفاً مشخص کنید گزارش مستقل شرکت را می‌خواهید یا گزارش تلفیقی؛ این دو دامنه با هم ادغام نمی‌شوند.",
        "period_intent_not_supported_by_planner_slice" =>
            "این دوره در مسیر امن فعلی به‌صورت یکتا قابل انتخاب نیست؛ لطفاً دورهٔ گزارش را دقیق‌تر مشخص کنید.",
        "requested_result_unit_conversion_not_supported" or "effective_source_unit_unresolved"
            or "statement_filter_threshold_unit_conversion_not_supported" or "statement_filter_threshold_unit_unspecified" =>
            "واحد درخواستی با واحد مستند منبع سازگار نیست؛ لطفاً واحد موردنظر را مشخص کنید.",
        "operation_or_comparison_not_supported_by_planner_slice" =>
            "این نوع مقایسه برای قلم انتخاب‌شده هنوز شواهد کافی ندارد؛ مقدار یک دورهٔ مشخص را درخواست کنید.",
        "statement_filter_expression_not_supported" =>
            "ترکیب «یا»، نفی یا بیش از دو شرط عددی پشتیبانی نمی‌شود؛ حداکثر دو شرط را با «و» بیان کنید.",
        "statement_filter_items_on_different_statements" =>
            "شرط‌ها باید روی اقلام یک صورت مالی باشند تا برای هر شرکت در یک دورهٔ گزارش بررسی شوند.",
        "source_identity_ambiguous" =>
            "چند قلم مالی ممکن است منظور شما باشد؛ لطفاً عنوان دقیق قلم صورت مالی را مشخص کنید.",
        _ => "این درخواست بدون حدس دربارهٔ مفهوم، واحد، دوره یا دامنهٔ گزارش قابل اجرا نیست؛ لطفاً آن را دقیق‌تر بیان کنید."
    };
}
