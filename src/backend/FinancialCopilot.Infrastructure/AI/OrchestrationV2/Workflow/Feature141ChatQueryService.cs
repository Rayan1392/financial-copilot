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
    ILogger<Feature141ChatQueryService> logger)
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
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(request))
            return "این قابلیت برای درخواست احراز هویت‌شدهٔ فعلی فعال نیست.";

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
                return "برای تعیین دقیق قلم مالی، شرکت، دوره و نوع گزارش به توضیح بیشتری نیاز دارم.";

            var intent = extraction.Intent;
            if (!string.IsNullOrWhiteSpace(validatedCompanySymbol) &&
                intent.EntityScope.Kind == EntityScopeKind.NamedCompanies)
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
                return RenderClarification(intent.Ambiguity.Questions,
                    "درخواست مالی برای اجرای دقیق نیاز به شفاف‌سازی دارد.");
            if (intent.Measures.Count != 1)
                return "لطفاً در هر پرسش یک قلم مالی مشخص را درخواست کنید تا منبع و واحد آن بدون حدس انتخاب شود.";

            var measure = intent.Measures[0];
            var navigationRequest = new SchemaNavigationRequest(
                1,
                request.Message,
                catalogSnapshot.CatalogVersion,
                catalogSnapshot.CatalogHash,
                TenantId: request.TenantId,
                CorrelationId: request.CorrelationId);
            var requiresGeneralPlanner = measure.Kind != MeasureConceptKind.FinancialStatementItem
                || intent.Operation.Kind != AnalyticalOperationKind.Retrieve
                || intent.Comparison.Mode != ComparisonMode.None
                || intent.Period.Kind is not (ReportingPeriodKind.LatestAvailable or ReportingPeriodKind.Unspecified)
                || intent.Products.Count != 0 || intent.Categories.Count != 0 || intent.Filters is not null
                || intent.Aggregation is not null || intent.Ranking is not null || intent.Constraints.Count != 0
                || intent.Output.RequestedFields.Count != 0;
            if (requiresGeneralPlanner)
            {
                var generalPlan = await planningService.PlanAsync(navigationRequest, cancellationToken).ConfigureAwait(false);
                return await ExecutePlannedAsync(request, generalPlan, cancellationToken).ConfigureAwait(false);
            }

            var references = await hierarchyProvider.GetFinancialSourceReferencesAsync(
                catalogSnapshot.CatalogVersion, catalogSnapshot.CatalogHash, cancellationToken).ConfigureAwait(false);
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
                return labels.Length == 0
                    ? "چند قلم مالی با این عبارت سازگار است؛ لطفاً عنوان دقیق قلم صورت مالی را مشخص کنید."
                    : $"چند قلم مالی با این عبارت سازگار است؛ لطفاً یکی را دقیق مشخص کنید: {string.Join("، ", labels)}.";
            }
            if (resolution.Status != FinancialSourceTitleResolutionStatus.Resolved || resolution.Reference is null)
                return "قلم مالی در فهرست ثبت‌شده به‌صورت یکتا پیدا نشد؛ لطفاً عنوان دقیق قلم صورت مالی را بنویسید.";

            var directSource = catalogSnapshot.DirectSources.SingleOrDefault(source =>
                string.Equals(source.CatalogId, resolution.Reference.CatalogId, StringComparison.Ordinal));
            var handoff = FinancialSourcePlannerHandoff.Create(
                intent, resolution, catalogSnapshot.CatalogVersion, catalogSnapshot.CatalogHash, directSource);
            if (handoff.Status != FinancialSourcePlannerHandoffStatus.ReadyForOfflineValidation
                || handoff.Binding is null)
                return RenderHandoffFailure(handoff.DiagnosticCode);

            var planned = await planningService.PlanAsync(
                navigationRequest, handoff.Binding.PlannerRequirements, cancellationToken).ConfigureAwait(false);
            return await ExecutePlannedAsync(request, planned, cancellationToken).ConfigureAwait(false);
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
            return "اجرای امن پرس‌وجوی مالی کامل نشد؛ هیچ عددی گزارش نمی‌کنم. لطفاً عنوان قلم، شرکت و دوره را دقیق‌تر بنویسید.";
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

    private static string RenderClarification(IReadOnlyList<string> questions, string fallback) =>
        questions.Count == 0 ? fallback : string.Join(" ", questions);

    private static string RenderHandoffFailure(string diagnosticCode) => diagnosticCode switch
    {
        "statement_scope_evidence_unavailable" =>
            "لطفاً مشخص کنید گزارش مستقل شرکت را می‌خواهید یا گزارش تلفیقی؛ این دو دامنه با هم ادغام نمی‌شوند.",
        "period_intent_not_supported_by_planner_slice" =>
            "این دوره در مسیر امن فعلی به‌صورت یکتا قابل انتخاب نیست؛ لطفاً دورهٔ گزارش را دقیق‌تر مشخص کنید.",
        "requested_result_unit_conversion_not_supported" or "effective_source_unit_unresolved" =>
            "واحد درخواستی با واحد مستند منبع سازگار نیست؛ لطفاً واحد موردنظر را مشخص کنید.",
        "operation_or_comparison_not_supported_by_planner_slice" =>
            "این نوع مقایسه برای قلم انتخاب‌شده هنوز شواهد کافی ندارد؛ مقدار یک دورهٔ مشخص را درخواست کنید.",
        "source_identity_ambiguous" =>
            "چند قلم مالی ممکن است منظور شما باشد؛ لطفاً عنوان دقیق قلم صورت مالی را مشخص کنید.",
        _ => "این درخواست بدون حدس دربارهٔ مفهوم، واحد، دوره یا دامنهٔ گزارش قابل اجرا نیست؛ لطفاً آن را دقیق‌تر بیان کنید."
    };
}
