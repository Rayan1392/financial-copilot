using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class NavigatorEvaluationCli
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run --project tools/SemanticCatalog -- evaluate-navigator --tenant-id <guid> [--environment <name>] [--launch-profile <name>] [--dataset <json/yaml>] [--hierarchy <json>] [--output <jsonl>] [--limit <count>] [--case-ids <id,id>] [--retry-failed-from <jsonl>] [--inspect-outline-only]");
            return;
        }

        var root = FindRepositoryRoot();
        var tenantId = Guid.Parse(GetRequiredOption(args, "--tenant-id"));
        var datasetPath = FullPath(root, GetOption(args, "--dataset") ?? "specs/141-schema-aware-semantic-query-engine/evaluation/navigation-golden-v1.yaml");
        var hierarchyPath = FullPath(root, GetOption(args, "--hierarchy") ?? "specs/141-schema-aware-semantic-query-engine/artifacts/navigation-hierarchy.yaml");
        var catalogPath = FullPath(root, GetOption(args, "--catalog") ?? "specs/141-schema-aware-semantic-query-engine/SemanticCatalog");
        var resumePath = GetOption(args, "--retry-failed-from");
        var outputPath = FullPath(root, GetOption(args, "--output") ?? resumePath ?? "artifacts/schema-navigation-predictions.jsonl");
        var limit = int.TryParse(GetOption(args, "--limit"), out var maximum) ? Math.Clamp(maximum, 1, 10_000) : int.MaxValue;

        using var hierarchy = JsonDocument.Parse(await File.ReadAllTextAsync(hierarchyPath));
        var catalogVersion = hierarchy.RootElement.GetProperty("catalogVersion").GetString()!;
        var catalogHash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        var catalogReferences = BuildCatalogReferences(hierarchy.RootElement, catalogPath);
        using var dataset = JsonDocument.Parse(await File.ReadAllTextAsync(datasetPath));
        var allCases = dataset.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var cases = allCases.AsEnumerable();
        var requestedCaseIds = GetOption(args, "--case-ids")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, JsonObject> recordedRows = [];
        if (!string.IsNullOrWhiteSpace(resumePath))
        {
            var resumeFullPath = FullPath(root, resumePath);
            recordedRows = await ReadEvaluationRowsAsync(resumeFullPath);
            var failedCaseIds = recordedRows.Where(pair => !IsCompletedRow(pair.Value)).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
            requestedCaseIds = requestedCaseIds is null ? failedCaseIds : requestedCaseIds.Intersect(failedCaseIds, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            cases = cases.Where(item => requestedCaseIds.Contains(item.GetProperty("id").GetString()!));
        }
        else if (requestedCaseIds is not null)
        {
            cases = cases.Where(item => requestedCaseIds.Contains(item.GetProperty("id").GetString()!));
        }
        cases = cases.Take(limit);
        var caseArray = cases.ToArray();
        cases = caseArray;
        if (caseArray.Length == 0 && requestedCaseIds is null) throw new InvalidOperationException("The evaluation dataset contains no cases.");
        ValidateExpectedCatalogReferences(caseArray, catalogReferences);

        var apiConfig = Path.Combine(root, "src", "backend", "FinancialCopilot.API");
        var inMemory = new Dictionary<string, string?>
        {
            ["Feature141:SchemaNavigator:HierarchyPath"] = hierarchyPath,
            ["Feature141:SchemaNavigator:CatalogPath"] = catalogPath
        };
        var environmentName = EvaluationConfigurationLoader.ResolveEnvironmentName(
            apiConfig, GetOption(args, "--environment"), launchProfile: GetOption(args, "--launch-profile"));
        var configuration = EvaluationConfigurationLoader.Build(
            apiConfig, environmentName, configurationOverrides: inMemory);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var eligibleProviders = provider.GetRequiredService<IAiProviderCapabilityRegistry>()
            .GetAvailableProviders(tenantId)
            .Where(item => (item.Capabilities & AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)) ==
                AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation))
            .ToArray();
        var selectedProvider = eligibleProviders.FirstOrDefault();
        var providerConfiguration = configuration.GetSection("AiModelProviders:Providers").GetChildren()
            .FirstOrDefault(item =>
                string.Equals(item["ProviderKey"], selectedProvider?.ProviderKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item["Adapter"], selectedProvider?.ProviderKey, StringComparison.OrdinalIgnoreCase));
        var credentialSource = providerConfiguration is null
            ? ("Missing", false)
            : EvaluationConfigurationLoader.GetCredentialSource(configuration, providerConfiguration);
        var endpointHost = Uri.TryCreate(providerConfiguration?["Endpoint"], UriKind.Absolute, out var endpoint)
            ? endpoint.Host
            : "unconfigured";
        Console.WriteLine($"AI configuration: environment={environmentName}; provider={selectedProvider?.ProviderKey ?? "none"}; model={selectedProvider?.ModelKey ?? "none"}; endpointHost={endpointHost}; credentialSource={credentialSource.Item1}; credentialConfigured={credentialSource.Item2}.");
        if (eligibleProviders.Length == 0)
        {
            Console.Error.WriteLine("Live evaluation blocked: no enabled tenant-eligible provider supports structured schema-navigation output. No model requests were sent.");
            Environment.ExitCode = 3;
            return;
        }
        var budgets = provider.GetRequiredService<SchemaNavigatorBudgets>();
        if (args.Contains("--inspect-outline-only", StringComparer.OrdinalIgnoreCase))
        {
            var outlineProvider = provider.GetRequiredService<ICatalogNavigationOutlineProvider>();
            var maximumCharacters = Math.Min(Math.Min(budgets.MaximumHierarchyContextCharacters, budgets.MaximumInputCharacters / 2),
                budgets.MaximumCatalogContextTokens * 2);
            foreach (var item in caseArray)
            {
                var caseId = item.GetProperty("id").GetString()!;
                try
                {
                    var outline = await outlineProvider.GetCompactOutlineAsync(catalogVersion, catalogHash,
                        item.GetProperty("question").GetString()!, maximumCharacters, CancellationToken.None);
                    var nodeCount = 0;
                    var sourceCandidateCount = 0;
                    var candidateSummary = new List<object>();
                    foreach (var line in outline.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        using var node = JsonDocument.Parse(line);
                        nodeCount++;
                        if (node.RootElement.TryGetProperty("sourceItemCandidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                        {
                            sourceCandidateCount += candidates.GetArrayLength();
                            candidateSummary.AddRange(candidates.EnumerateArray().Select(candidate => (object)new
                            {
                                catalogId = ReadString(candidate, "catalogId"),
                                providerName = ReadString(candidate, "providerName"),
                                statementType = ReadString(candidate, "statementType"),
                                sourceItemId = ReadInt32(candidate, "sourceItemId"),
                                titleFa = ReadString(candidate, "titleFa"),
                                matchScore = candidate.TryGetProperty("matchScore", out var score) && score.TryGetDouble(out var parsed) ? parsed : 0
                            }));
                        }
                    }
                    Console.WriteLine(JsonSerializer.Serialize(new { caseId, outlineCharacters = outline.Length,
                        outlineCharacterLimit = maximumCharacters, estimatedOutlineTokens = EstimateTokens(outline.Length),
                        hierarchyNodes = nodeCount, selectedSourceItemCandidates = sourceCandidateCount,
                        sourceCandidates = candidateSummary, allCatalogSourceItems = catalogReferences.DirectSourceItems.Count,
                        requestSent = false }));
                }
                catch (SchemaNavigationBudgetException exception)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { caseId, budgetCode = exception.Code,
                        observed = exception.Observed, limit = exception.Limit, requestSent = false }));
                }
            }
            return;
        }
        if (args.Contains("--preflight-only", StringComparer.OrdinalIgnoreCase))
        {
            var identities = string.Join(", ", eligibleProviders.Select(item => $"{item.ProviderKey}/{item.ModelKey}"));
            Console.WriteLine($"Preflight passed: {eligibleProviders.Length} eligible structured-output provider(s): {identities}; no model requests were sent.");
            return;
        }
        var navigator = provider.GetRequiredService<IVectorlessSchemaNavigator>();
        var requirementsExtractor = provider.GetRequiredService<IQuestionSchemaRequirementsExtractor>();
        Console.WriteLine($"Live evaluation budget: {caseArray.Length} cases; at most {budgets.MaximumModelCalls} total calls per case including extraction and retries, {budgets.MaximumNavigationCalls} navigation calls, {budgets.MaximumRetryAttempts} bounded retry, {caseArray.Length * budgets.MaximumInputTokens} input tokens and {caseArray.Length * budgets.MaximumTotalOutputTokens} output tokens across all cases (request-wide caps). Initial per-call output cap {budgets.InitialOutputTokens}; adaptive retry cap {budgets.MaximumOutputTokens}. Provider billing estimates are unavailable unless the provider returns cost metadata.");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var completed = 0;

        foreach (var item in caseArray)
        {
            var caseId = item.GetProperty("id").GetString()!;
            var question = item.GetProperty("question").GetString()!;
            var correlationId = $"feature141-eval-{caseId}-{Guid.NewGuid():N}";
            var stopwatch = Stopwatch.StartNew();
            using var caseTimeout = new CancellationTokenSource(budgets.EffectiveTotalTimeout);
            SchemaNavigationResult? result = null;
            QuestionSchemaRequirements? requirements = null;
            Exception? failure = null;
            var executionPhase = "requirements_extraction";
            try
            {
                var request = new SchemaNavigationRequest(1, question,
                    catalogVersion, catalogHash, TenantId: tenantId, CorrelationId: correlationId);
                requirements = await requirementsExtractor.ExtractAsync(request, caseTimeout.Token);
                executionPhase = "schema_navigation";
                result = await navigator.NavigateAsync(request, requirements, caseTimeout.Token);
            }
            catch (AiModelProviderException exception)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    eventType = "ai_provider_case_failed",
                    exceptionType = exception.GetType().Name,
                    errorCode = exception.Code,
                    provider = selectedProvider?.ProviderKey,
                    model = selectedProvider?.ModelKey,
                    endpointHost = exception.RequestHost ?? endpointHost,
                    httpStatus = exception.HttpStatusCode,
                    providerErrorCode = exception.ProviderErrorCode,
                    providerRequestId = exception.ProviderRequestId,
                    quotaMetadata = exception.ProviderQuotaMetadata,
                    phase = executionPhase,
                    caseId,
                    correlationId,
                    secretValuesLogged = false
                }));
                failure = exception;
            }
            catch (SchemaNavigationOutputException exception)
            {
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    eventType = "schema_navigation_case_failed",
                    exceptionType = exception.GetType().Name,
                    errorCode = exception.Code,
                    provider = selectedProvider?.ProviderKey,
                    model = selectedProvider?.ModelKey,
                    phase = executionPhase,
                    inputTokens = exception.InputTokens,
                    outputTokens = exception.OutputTokens,
                    latencyMs = exception.ModelLatency is { } latency ? (long?)latency.TotalMilliseconds : null,
                    providerReportedCost = exception.ProviderReportedCost,
                    providerReportedCurrency = exception.ProviderReportedCurrency,
                    caseId,
                    correlationId,
                    secretValuesLogged = false
                }));
                failure = exception;
            }
            catch (SchemaNavigationBudgetException exception) { failure = exception; }
            catch (OperationCanceledException exception) { failure = exception; }
            catch (Exception exception) { failure = exception; }
            stopwatch.Stop();

            if (failure is not null)
            {
                var providerFailure = failure as AiModelProviderException;
                var outputFailure = failure as SchemaNavigationOutputException;
                var code = providerFailure?.Code ?? (failure as SchemaNavigationBudgetException)?.Code
                    ?? outputFailure?.Code ?? failure.GetType().Name;
                var modelCalls = providerFailure?.EvaluationModelCalls ?? outputFailure?.ModelCalls
                    ?? (requirements?.ModelCalls ?? (failure is AiModelProviderException ? 1 : 0));
                if (failure is OperationCanceledException && executionPhase == "schema_navigation") modelCalls++;
                var failedRow = JsonSerializer.SerializeToNode(new
                {
                    schemaVersion = 1, caseId, status = "failed", phase = executionPhase,
                    provider = selectedProvider?.ProviderKey, model = selectedProvider?.ModelKey,
                    expectedFinancialValueReferences = GetJsonValue(item, "expectedFinancialValueReferences"),
                    expectedFinancialValueOperation = GetJsonValue(item, "expectedFinancialValueOperation"),
                    predictedFinancialValueReferences = (JsonNode?)null,
                    failureCategory = FailureCategory(failure), failureCode = code,
                    modelCalls, inputTokens = providerFailure?.AccumulatedProviderInputTokens ?? outputFailure?.InputTokens ?? requirements?.InputTokens ?? providerFailure?.ProviderReportedInputTokens,
                    outputTokens = providerFailure?.AccumulatedProviderOutputTokens ?? outputFailure?.OutputTokens ?? requirements?.OutputTokens ?? providerFailure?.ProviderReportedOutputTokens,
                    estimatedInputTokens = outputFailure?.EstimatedInputTokens ?? requirements?.EstimatedInputTokens,
                    estimatedOutputTokens = outputFailure?.EstimatedOutputTokens ?? requirements?.EstimatedOutputTokens,
                    retryAttempts = providerFailure?.EvaluationRetryAttempts ?? outputFailure?.RetryAttempts ?? requirements?.RetryAttempts ?? 0,
                    latencyMs = (long)(providerFailure?.EvaluationLatency?.TotalMilliseconds ?? outputFailure?.ModelLatency?.TotalMilliseconds ?? ((requirements?.ModelLatency ?? TimeSpan.Zero).TotalMilliseconds + (providerFailure?.ProviderLatency?.TotalMilliseconds ?? 0))),
                    wallLatencyMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                    providerResponseId = providerFailure?.ProviderResponseId,
                    providerRequestId = providerFailure?.ProviderRequestId,
                    httpStatus = providerFailure?.HttpStatusCode,
                    providerErrorCode = providerFailure?.ProviderErrorCode,
                    correlationId,
                    providerCompletionReason = providerFailure?.ProviderCompletionReason,
                    observedBudget = (failure as SchemaNavigationBudgetException)?.Observed,
                    configuredBudget = (failure as SchemaNavigationBudgetException)?.Limit,
                    diagnostics = new[] { code, executionPhase }, secretValuesLogged = false
                })!.AsObject();
                await PersistEvaluationRowsAsync(outputPath, recordedRows, caseId, failedRow);
                completed++;
                Environment.ExitCode = 4;
                continue;
            }

            var successfulResult = result!;
            var successfulRequirements = requirements!;
            IReadOnlyList<CatalogFinancialReference> enrichedFinancialReferences;
            try
            {
                enrichedFinancialReferences = EnrichReferences(successfulResult.SelectedFinancialValueReferences, catalogReferences);
            }
            catch (InvalidDataException exception)
            {
                var failedReferenceRow = JsonSerializer.SerializeToNode(new
                {
                    schemaVersion = 1, caseId, status = "failed", phase = executionPhase,
                    provider = selectedProvider?.ProviderKey, model = selectedProvider?.ModelKey,
                    expectedFinancialValueReferences = GetJsonValue(item, "expectedFinancialValueReferences"),
                    expectedFinancialValueOperation = GetJsonValue(item, "expectedFinancialValueOperation"),
                    predictedFinancialValueReferences = successfulResult.SelectedFinancialValueReferences,
                    failureCategory = "invalid_catalog_reference", failureCode = "navigator_selected_unrecognized_financial_reference",
                    failureDetail = exception.Message, modelCalls = successfulResult.ModelCalls,
                    inputTokens = successfulResult.InputTokens, outputTokens = successfulResult.OutputTokens,
                    retryAttempts = successfulResult.RetryAttempts,
                    latencyMs = (long)(successfulResult.ModelLatency?.TotalMilliseconds ?? 0),
                    wallLatencyMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                    providerResponseId = successfulRequirements.ProviderResponseId,
                    correlationId, secretValuesLogged = false
                })!.AsObject();
                await PersistEvaluationRowsAsync(outputPath, recordedRows, caseId, failedReferenceRow);
                completed++;
                Environment.ExitCode = 4;
                Console.Error.WriteLine(JsonSerializer.Serialize(new
                {
                    eventType = "schema_navigation_case_failed", caseId, errorCode = "navigator_selected_unrecognized_financial_reference",
                    model = selectedProvider?.ModelKey, phase = executionPhase, correlationId, detail = exception.Message,
                    secretValuesLogged = false
                }));
                continue;
            }
            var row = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = 1,
                caseId,
                status = "completed",
                expectedFinancialValueReferences = GetJsonValue(item, "expectedFinancialValueReferences"),
                expectedFinancialValueOperation = GetJsonValue(item, "expectedFinancialValueOperation"),
                selectedNodeIds = successfulResult.SelectedNodeIds,
                llmSelectedNodeIds = successfulResult.Candidates.Select(candidate => candidate.NodeId).Distinct(StringComparer.Ordinal).ToArray(),
                finalContextNodeCount = successfulResult.SelectedNodeIds.Count,
                metricIds = successfulResult.SelectedMeasureIds,
                selectedMeasureIds = successfulResult.SelectedMeasureIds,
                candidateMeasureIds = successfulResult.CandidateMeasureIds,
                requiredMeasureIds = successfulResult.RequiredMeasureIds,
                incidentalMeasureIds = (successfulResult.CandidateMeasureIds ?? []).Except(
                    successfulResult.SelectedMeasureIds.Concat(successfulResult.RequiredMeasureIds ?? []), StringComparer.Ordinal).ToArray(),
                dimensionIds = successfulResult.SelectedDimensionIds,
                selectedDimensionIds = successfulResult.SelectedDimensionIds,
                candidateDimensionIds = successfulResult.CandidateDimensionIds,
                requiredDimensionIds = successfulRequirements.DimensionIds,
                incidentalDimensionIds = (successfulResult.CandidateDimensionIds ?? []).Except(
                    successfulResult.SelectedDimensionIds.Concat(successfulRequirements.DimensionIds), StringComparer.Ordinal).ToArray(),
                entityKinds = successfulResult.SelectedEntityKinds,
                relationshipIds = successfulResult.SelectedRelationshipIds,
                selectedRelationshipIds = successfulResult.SelectedRelationshipIds,
                candidateRelationshipIds = successfulResult.CandidateRelationshipIds,
                requiredRelationshipIds = successfulRequirements.RelationshipIds,
                incidentalRelationshipIds = (successfulResult.CandidateRelationshipIds ?? []).Except(
                    successfulResult.SelectedRelationshipIds.Concat(successfulRequirements.RelationshipIds), StringComparer.Ordinal).ToArray(),
                sourcePolicies = successfulResult.SelectedSourcePolicies,
                periodPolicies = successfulResult.SelectedPeriodPolicies,
                comparisonScopes = successfulResult.SelectedComparisonScopes,
                presentationRequirements = successfulResult.SelectedPresentationRequirements,
                statementScopes = successfulResult.SelectedStatementScopes,
                aggregationRequirements = successfulResult.SelectedAggregationRequirements,
                orderingRequirements = successfulResult.SelectedOrderingRequirements,
                entityReferences = successfulResult.SelectedEntityReferences,
                independentRequirements = new
                {
                    measureIds = successfulRequirements.MeasureIds,
                    dimensionIds = successfulRequirements.DimensionIds,
                    entityKinds = successfulRequirements.EntityKinds,
                    relationshipIds = successfulRequirements.RelationshipIds,
                    sourcePolicies = successfulRequirements.SourcePolicies,
                    periodPolicies = successfulRequirements.PeriodPolicies,
                    comparisonScopes = successfulRequirements.ComparisonScopes,
                    presentationRequirements = successfulRequirements.PresentationRequirements,
                    statementScopes = successfulRequirements.StatementScopes,
                    aggregationRequirements = successfulRequirements.AggregationRequirements,
                    orderingRequirements = successfulRequirements.OrderingRequirements,
                    entityReferences = successfulRequirements.EntityReferences,
                    productReferences = successfulRequirements.ProductReferences,
                    financialValueReferences = successfulRequirements.FinancialValueReferences,
                    financialValueOperation = successfulRequirements.RequestedFinancialValueOperation,
                    unknownOrAmbiguous = successfulRequirements.HasUnknownOrAmbiguousRequirements,
                    uncertaintyReason = successfulRequirements.UncertaintyReason,
                    extractionLatencyMs = (long)(successfulRequirements.ModelLatency?.TotalMilliseconds ?? 0)
                },
                financialValueReferences = enrichedFinancialReferences,
                financialValueOperation = successfulResult.FinancialValueOperation,
                requirementsExtractionInputTokens = successfulRequirements.InputTokens,
                requirementsExtractionOutputTokens = successfulRequirements.OutputTokens,
                missingRequirements = successfulResult.MissingContext,
                completenessStatus = successfulResult.CompletenessStatus?.ToString(),
                retrievalComplete = successfulResult.RetrievalComplete,
                semanticReviewApproved = successfulResult.SemanticReviewApproved,
                queryAuthorized = successfulResult.QueryAuthorized,
                executable = successfulResult.RetrievalComplete && successfulResult.SemanticReviewApproved && successfulResult.QueryAuthorized,
                isComplete = successfulResult.CompletenessStatus == SchemaContextCompletenessStatus.Complete,
                outcome = Outcome(successfulResult.CompletenessStatus),
                modelCalls = successfulResult.ModelCalls,
                retryAttempts = successfulResult.RetryAttempts,
                inputTokens = successfulResult.InputTokens,
                outputTokens = successfulResult.OutputTokens,
                estimatedInputTokens = successfulResult.EstimatedInputTokens,
                estimatedOutputTokens = successfulResult.EstimatedOutputTokens,
                latencyMs = (long)(successfulResult.ModelLatency?.TotalMilliseconds ?? stopwatch.Elapsed.TotalMilliseconds),
                wallLatencyMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                providerReportedCost = successfulResult.ProviderReportedCost,
                providerReportedCurrency = successfulResult.ProviderReportedCurrency,
                diagnostics = successfulResult.Diagnostics
            })!.AsObject();
            await PersistEvaluationRowsAsync(outputPath, recordedRows, caseId, row);
            completed++;
        }

        Console.WriteLine($"Recorded or retried {completed} navigator cases to {outputPath}. No database query was executed.");
    }

    private sealed record CatalogFinancialReference(
        [property: JsonPropertyName("referenceType")] string ReferenceType,
        [property: JsonPropertyName("catalogId")] string CatalogId,
        [property: JsonPropertyName("providerName")] string? ProviderName = null,
        [property: JsonPropertyName("statementType")] string? StatementType = null,
        [property: JsonPropertyName("sourceItemId")] int? SourceItemId = null,
        [property: JsonPropertyName("sourceItemCatalogId")] string? SourceItemCatalogId = null,
        [property: JsonPropertyName("metricCode")] string? MetricCode = null,
        [property: JsonPropertyName("unitStatus")] string? SourceUnitStatus = null,
        [property: JsonPropertyName("queryable")] bool Queryable = false);

    private sealed record CatalogReferenceIndex(
        IReadOnlyDictionary<string, CatalogFinancialReference> DirectSourceItems,
        IReadOnlySet<string> GovernedMetrics,
        IReadOnlyDictionary<string, string> GovernedMetricCodes,
        IReadOnlySet<string> ProviderFinancialIndicators,
        IReadOnlyDictionary<string, string> MonthlyMeasureCodes);

    private static CatalogReferenceIndex BuildCatalogReferences(JsonElement hierarchy, string catalogPath)
    {
        var direct = new Dictionary<string, CatalogFinancialReference>(StringComparer.Ordinal);
        var metrics = new HashSet<string>(StringComparer.Ordinal);
        var indicators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in hierarchy.GetProperty("nodes").EnumerateArray())
        {
            var refs = node.GetProperty("catalogReferences");
            foreach (var item in Strings(refs, "directSourceItemIds"))
                if (!direct.ContainsKey(item)) direct[item] = new CatalogFinancialReference(nameof(FinancialValueReferenceType.DirectSourceItem), item);
            foreach (var item in Strings(refs, "metricIds")) metrics.Add(item);
            foreach (var item in Strings(refs, "providerFinancialIndicatorIds")) indicators.Add(item);
            if (!node.TryGetProperty("sourceItemReferences", out var sourceReferences) || sourceReferences.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in sourceReferences.EnumerateArray())
            {
                var id = ReadString(item, "catalogId");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var authorization = item.TryGetProperty("queryAuthorization", out var auth) ? auth : default;
                var queryable = authorization.ValueKind == JsonValueKind.Object
                    && authorization.TryGetProperty("queryable", out var queryableNode)
                    && queryableNode.ValueKind == JsonValueKind.True;
                direct[id] = new CatalogFinancialReference(nameof(FinancialValueReferenceType.DirectSourceItem), id,
                    ReadString(item, "providerName"), ReadString(item, "statementType"), ReadInt32(item, "sourceItemId"),
                    ReadString(item, "sourceItemCatalogId"), ReadString(item, "mappedMetricCode"),
                    ReadString(item, "sourceUnitStatus"), queryable);
            }
        }

        var metricCodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var metricsDirectory = Path.Combine(catalogPath, "metrics");
        foreach (var path in Directory.EnumerateFiles(metricsDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var metric = document.RootElement;
            var id = ReadString(metric, "id");
            if (string.IsNullOrWhiteSpace(id) || !metric.TryGetProperty("sourceDefinition", out var definition)
                || !definition.TryGetProperty("Code", out var code) || !code.TryGetProperty("Value", out var value)) continue;
            metricCodes[id] = value.GetString() ?? string.Empty;
        }

        var monthlyMeasureCodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var monthlyMeasuresPath = Path.Combine(catalogPath, "dictionaries", "monthly-measures.yaml");
        if (File.Exists(monthlyMeasuresPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(monthlyMeasuresPath));
            if (document.RootElement.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                foreach (var entry in entries.EnumerateArray())
                {
                    var id = ReadString(entry, "catalogId");
                    var code = ReadString(entry, "metricCode");
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(code))
                        monthlyMeasureCodes[id] = code;
                }
        }
        return new CatalogReferenceIndex(direct, metrics, metricCodes, indicators, monthlyMeasureCodes);
    }

    private static void ValidateExpectedCatalogReferences(JsonElement[] cases, CatalogReferenceIndex catalog)
    {
        foreach (var item in cases)
        {
            var caseId = ReadString(item, "id") ?? "<missing-id>";
            if (item.TryGetProperty("expectedFinancialValueOperation", out var operation)
                && (operation.ValueKind != JsonValueKind.String || !Enum.TryParse<FinancialValueOperation>(operation.GetString(), out _)))
                throw new InvalidDataException($"Golden case {caseId} has an unknown expected financial operation.");
            if (!item.TryGetProperty("expectedFinancialValueReferences", out var expected) || expected.ValueKind != JsonValueKind.Array) continue;
            foreach (var reference in expected.EnumerateArray())
            {
                var type = ReadString(reference, "referenceType");
                var id = ReadString(reference, "catalogId");
                if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException($"Golden case {caseId} contains a financial reference without referenceType or catalogId.");
                var exists = type switch
                {
                    nameof(FinancialValueReferenceType.DirectSourceItem) => catalog.DirectSourceItems.TryGetValue(id, out var source),
                    nameof(FinancialValueReferenceType.GovernedMetric) => catalog.GovernedMetrics.Contains(id),
                    nameof(FinancialValueReferenceType.ProviderFinancialIndicator) => catalog.ProviderFinancialIndicators.Contains(id),
                    nameof(FinancialValueReferenceType.MonthlyMeasure) => catalog.MonthlyMeasureCodes.ContainsKey(id),
                    _ => false
                };
                if (!exists) throw new InvalidDataException($"Golden case {caseId} contains an unknown financial reference: {type}/{id}.");
                if (type != nameof(FinancialValueReferenceType.DirectSourceItem)) continue;
                var actual = catalog.DirectSourceItems[id];
                ValidateExpectedString(reference, "providerName", actual.ProviderName, caseId);
                ValidateExpectedString(reference, "statementType", actual.StatementType, caseId);
                ValidateExpectedString(reference, "sourceItemCatalogId", actual.SourceItemCatalogId, caseId);
                ValidateExpectedString(reference, "metricCode", actual.MetricCode, caseId);
                ValidateExpectedString(reference, "unitStatus", actual.SourceUnitStatus, caseId);
                if (reference.TryGetProperty("sourceItemId", out var sourceItemId)
                    && (!sourceItemId.TryGetInt32(out var expectedId) || expectedId != actual.SourceItemId))
                    throw new InvalidDataException($"Golden case {caseId} SourceItemId disagrees with the canonical catalog.");
            }
        }
    }

    private static void ValidateExpectedString(JsonElement reference, string property, string? actual, string caseId)
    {
        if (reference.TryGetProperty(property, out var expected) && expected.ValueKind == JsonValueKind.String
            && !string.Equals(expected.GetString(), actual, StringComparison.Ordinal))
            throw new InvalidDataException($"Golden case {caseId} {property} disagrees with the canonical catalog.");
        if (reference.TryGetProperty(property, out expected) && expected.ValueKind == JsonValueKind.Null && actual is not null)
            throw new InvalidDataException($"Golden case {caseId} {property} disagrees with the canonical catalog.");
    }

    private static IReadOnlyList<CatalogFinancialReference> EnrichReferences(
        IReadOnlyList<FinancialValueSelection>? references, CatalogReferenceIndex catalog)
    {
        if (references is null) return [];
        var resolved = new List<CatalogFinancialReference>();
        foreach (var reference in references)
        {
            if (!Enum.TryParse<FinancialValueReferenceType>(reference.ReferenceType, out var type))
                throw new InvalidDataException($"Navigator returned an unknown financial reference type: {reference.ReferenceType}.");
            switch (type)
            {
                case FinancialValueReferenceType.DirectSourceItem when catalog.DirectSourceItems.TryGetValue(reference.CatalogId, out var source):
                    resolved.Add(source);
                    break;
                case FinancialValueReferenceType.GovernedMetric when catalog.GovernedMetrics.Contains(reference.CatalogId):
                    catalog.GovernedMetricCodes.TryGetValue(reference.CatalogId, out var metricCode);
                    resolved.Add(new(nameof(FinancialValueReferenceType.GovernedMetric), reference.CatalogId, MetricCode: metricCode));
                    break;
                case FinancialValueReferenceType.ProviderFinancialIndicator when catalog.ProviderFinancialIndicators.Contains(reference.CatalogId):
                    resolved.Add(new(nameof(FinancialValueReferenceType.ProviderFinancialIndicator), reference.CatalogId));
                    break;
                case FinancialValueReferenceType.MonthlyMeasure when catalog.MonthlyMeasureCodes.TryGetValue(reference.CatalogId, out var monthlyMetricCode):
                    resolved.Add(new(nameof(FinancialValueReferenceType.MonthlyMeasure), reference.CatalogId, MetricCode: monthlyMetricCode));
                    break;
                default:
                    throw new InvalidDataException($"Navigator returned a financial reference not present in the canonical catalog: {reference.CatalogId}.");
            }
        }
        return resolved;
    }

    private static string[] Strings(JsonElement parent, string name) => parent.TryGetProperty(name, out var values)
        && values.ValueKind == JsonValueKind.Array
        ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray()
        : [];

    private static string? ReadString(JsonElement parent, string name) => parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt32(JsonElement parent, string name) => parent.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var number) ? number : null;

    private static string Outcome(SchemaContextCompletenessStatus? status) => status switch
    {
        SchemaContextCompletenessStatus.Complete => "complete",
        SchemaContextCompletenessStatus.ClarificationRequired => "clarification",
        SchemaContextCompletenessStatus.Unsupported => "unsupported",
        SchemaContextCompletenessStatus.CatalogMismatch => "catalog-mismatch",
        _ => "incomplete"
    };

    private static int EstimateTokens(int characters) => (characters + 1) / 2;

    private static object? GetJsonValue(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? JsonNode.Parse(value.GetRawText()) : null;

    private static string FailureCategory(Exception exception) => exception switch
    {
        AiModelProviderException { Code: "ModelOutputTruncated" } => "model_output_truncated",
        AiModelProviderException => "provider_failure",
        SchemaNavigationBudgetException => "context_or_token_budget_exceeded",
        SchemaNavigationOutputException => "invalid_structured_output_or_catalog_reference",
        OperationCanceledException => "timeout_or_cancellation",
        _ => "unexpected_evaluation_failure"
    };

    private static bool IsCompletedRow(JsonObject row)
    {
        var status = row["status"]?.GetValue<string>();
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)) return true;
        return row["failureCategory"] is null;
    }

    private static async Task<Dictionary<string, JsonObject>> ReadEvaluationRowsAsync(string path)
    {
        var rows = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var row = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Evaluation JSONL contains a non-object row.");
            var id = row["caseId"]?.GetValue<string>() ?? row["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Evaluation JSONL contains a row without caseId.");
            rows[id] = row;
        }
        return rows;
    }

    private static async Task PersistEvaluationRowsAsync(string outputPath, Dictionary<string, JsonObject> rows,
        string caseId, JsonObject row)
    {
        rows[caseId] = row;
        var temporary = outputPath + ".tmp";
        await File.WriteAllLinesAsync(temporary, rows.Values.Select(value => value.ToJsonString()));
        File.Move(temporary, outputPath, overwrite: true);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "specs", "141-schema-aware-semantic-query-engine"))
                && Directory.Exists(Path.Combine(current.FullName, "src", "backend"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string FullPath(string root, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        return null;
    }
    private static string GetRequiredOption(string[] args, string name) => GetOption(args, name)
        ?? throw new ArgumentException($"Required option {name} is missing.");
}
