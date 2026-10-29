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
/// </summary>
internal sealed record Feature141ChatAnswer(string Text, bool Definitive, bool Refused = false);

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
                return Pending("برای تعیین دقیق قلم مالی، شرکت، دوره و نوع گزارش به توضیح بیشتری نیاز دارم.");

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
                var generalPlan = await planningService.PlanAsync(navigationRequest, requirements, cancellationToken).ConfigureAwait(false);
                return new(await ExecutePlannedAsync(request, generalPlan, cancellationToken).ConfigureAwait(false), true);
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
            return new(await ExecutePlannedAsync(request, planned, cancellationToken).ConfigureAwait(false), true);
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

    private async Task<string> ExecutePlannedAsync(AiQueryRequest request,
        OfflineStructuredQueryPlanningResult planned, CancellationToken cancellationToken)
    {
            if (!planned.Validation.ExecutionEligible)
            {
                logger.LogInformation(
                    "Feature141 plan was not execution eligible. CorrelationId={CorrelationId} Diagnostics={Diagnostics}",
                    request.CorrelationId, string.Join(',', planned.Validation.Diagnostics));
                return responseGenerator.Generate(new StructuredQueryAnswerRequest(
                    planned.Plan, null, "planning_validation", planned.Validation.Diagnostics)).TextAnswer;
            }

            var compilation = compiler.Compile(planned.Plan, catalogSnapshot);
            if (!compilation.Success || compilation.Query is null)
            {
                logger.LogWarning(
                    "Feature141 compilation failed. CorrelationId={CorrelationId} Diagnostics={Diagnostics}",
                    request.CorrelationId, string.Join(',', compilation.Diagnostics));
                return responseGenerator.Generate(new StructuredQueryAnswerRequest(
                    planned.Plan, null, "sql_compilation", compilation.Diagnostics)).TextAnswer;
            }

            var grant = authorizer.Authorize(compilation.Query, request.TenantId);
            if (grant is null)
            {
                logger.LogWarning(
                    "Feature141 catalog authorization rejected a compiled query. CorrelationId={CorrelationId} Fingerprint={Fingerprint}",
                    request.CorrelationId, compilation.Query.QueryFingerprint);
                return "این درخواست با فهرست منابع مجاز یا دامنهٔ شرکت سازگار نیست؛ هیچ داده‌ای خوانده نشد.";
            }

            var execution = await executor.ExecuteAsync(compilation.Query, grant, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation(
                "Feature141 query completed. CorrelationId={CorrelationId} Fingerprint={Fingerprint} Status={Status} Rows={RowCount} ElapsedMs={ElapsedMs}",
                request.CorrelationId, compilation.Query.QueryFingerprint, execution.Status,
                execution.Rows.Count, execution.Elapsed?.TotalMilliseconds);
            return responseGenerator.Generate(new StructuredQueryAnswerRequest(
                planned.Plan, execution, "postgresql_execution",
                planned.Validation.Diagnostics.Concat(compilation.Diagnostics).Distinct(StringComparer.Ordinal).ToArray()))
                .TextAnswer;
    }

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
