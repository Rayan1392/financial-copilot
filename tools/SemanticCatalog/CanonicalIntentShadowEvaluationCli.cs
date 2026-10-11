using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Semantics.Intent;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class CanonicalIntentShadowEvaluationCli
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    static CanonicalIntentShadowEvaluationCli()
    {
        // Keep shadow artifacts readable and stable across enum reordering. The scorer also
        // accepts numeric enum values from earlier smoke runs for backwards compatibility.
        JsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }

    public static async Task RunAsync(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run --project tools/SemanticCatalog -- evaluate-canonical-intent --tenant-id <guid> [--dataset <json>] [--output <jsonl>] [--limit <count>] [--case-ids <id,id>] [--environment <name>] [--launch-profile <name>]");
            Console.WriteLine("Diagnostic modes: --preflight-only | --smoke-structured | --smoke-canonical --question <text> [--capture-canonical-output]. They never bind a catalog or execute a query.");
            Console.WriteLine("Runs the legacy requirements extractor and Option B extractor in shadow only. It never invokes catalog binding, planning, query execution, or chat routing.");
            return;
        }

        var root = FindRepositoryRoot();
        var tenantId = Guid.Parse(GetRequiredOption(args, "--tenant-id"));
        var datasetPath = FullPath(root, GetOption(args, "--dataset") ?? "specs/141-schema-aware-semantic-query-engine/evaluation/canonical-intent-benchmark-v3.json");
        var outputPath = FullPath(root, GetOption(args, "--output") ?? $"specs/141-schema-aware-semantic-query-engine/artifacts/canonical-intent-shadow-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        var limit = int.TryParse(GetOption(args, "--limit"), out var maximum) ? Math.Clamp(maximum, 1, 10_000) : int.MaxValue;
        using var dataset = JsonDocument.Parse(await File.ReadAllTextAsync(datasetPath));
        var allCases = dataset.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var requestedIds = GetOption(args, "--case-ids")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var selected = allCases.Where(item => requestedIds is null || requestedIds.Contains(item.GetProperty("id").GetString()!)).Take(limit).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("No benchmark cases selected.");

        var apiConfig = Path.Combine(root, "src", "backend", "FinancialCopilot.API");
        var hierarchyPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml");
        var catalogPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
        var environmentName = EvaluationConfigurationLoader.ResolveEnvironmentName(
            apiConfig, GetOption(args, "--environment"), launchProfile: GetOption(args, "--launch-profile"));
        var configuration = EvaluationConfigurationLoader.Build(apiConfig, environmentName, configurationOverrides: new Dictionary<string, string?>
        {
            ["Feature141:SchemaNavigator:HierarchyPath"] = hierarchyPath,
            ["Feature141:SchemaNavigator:CatalogPath"] = catalogPath
        });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddFinancialCopilotInfrastructure(configuration);
        services.AddSingleton(CanonicalBusinessMeasureConceptRegistry.Default);
        services.AddSingleton<CanonicalSemanticIntentCanonicalizer>();
        services.AddSingleton<CanonicalSemanticIntentValidator>();
        var captureCanonicalOutput = args.Contains("--capture-canonical-output", StringComparer.OrdinalIgnoreCase);
        StructuredOutputCapturingExecutionService? structuredOutputCapture = null;
        services.AddSingleton<ICanonicalSemanticIntentExtractor>(provider => new LlmCanonicalSemanticIntentExtractor(
            captureCanonicalOutput
                ? structuredOutputCapture = new StructuredOutputCapturingExecutionService(
                    provider.GetRequiredService<IAiModelExecutionService>(),
                    args.Contains("--smoke-canonical", StringComparer.OrdinalIgnoreCase))
                : provider.GetRequiredService<IAiModelExecutionService>(),
            provider.GetRequiredService<CanonicalSemanticIntentValidator>(),
            configuredDeadline: TimeSpan.FromSeconds(25), tenantId: tenantId,
            canonicalizer: provider.GetRequiredService<CanonicalSemanticIntentCanonicalizer>()));
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IAiProviderCapabilityRegistry>();
        var eligibleProviders = registry.GetAvailableProviders(tenantId)
            .Where(item => (item.Capabilities & AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)) ==
                AiWorkloadCapabilities.RequiredFor(AiWorkloadKind.SchemaNavigation)).ToArray();
        var selectedProvider = eligibleProviders.FirstOrDefault();
        var selectedRegistration = configuration.GetSection("AiModelProviders:Providers").GetChildren()
            .FirstOrDefault(section => string.Equals(section["ProviderKey"], selectedProvider?.ProviderKey, StringComparison.OrdinalIgnoreCase));
        var credential = selectedRegistration is null
            ? (Source: "Missing", IsConfigured: false)
            : EvaluationConfigurationLoader.GetCredentialSource(configuration, selectedRegistration);
        var optionB = provider.GetRequiredService<ICanonicalSemanticIntentExtractor>();
        Console.WriteLine($"Shadow run: dataset={Path.GetFileName(datasetPath)}; cases={selected.Length}; environment={environmentName}; provider={selectedProvider?.ProviderKey ?? "none"}; model={selectedProvider?.ModelKey ?? "none"}; credentialSource={credential.Source}; credentialConfigured={credential.IsConfigured}; no catalog binding or query execution.");
        if (args.Contains("--preflight-only", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(eligibleProviders.Length > 0
                ? "Preflight passed: at least one eligible structured-output provider is configured; no model requests were sent."
                : "Preflight unavailable: no eligible structured-output provider is configured; no model requests were sent.");
            return;
        }

        if (args.Contains("--smoke-structured", StringComparer.OrdinalIgnoreCase))
        {
            await RunStructuredOutputSmokeAsync(provider.GetRequiredService<IAiModelExecutionService>(), tenantId,
                selectedProvider, CancellationToken.None);
            return;
        }

        if (args.Contains("--smoke-canonical", StringComparer.OrdinalIgnoreCase))
        {
            var question = GetOption(args, "--question") ?? throw new ArgumentException("--smoke-canonical requires --question <text>.");
            await RunCanonicalIntentSmokeAsync(optionB, question);
            return;
        }

        string catalogVersion = "unavailable";
        string catalogHash = "unavailable";
        if (File.Exists(hierarchyPath))
        {
            using var hierarchy = JsonDocument.Parse(await File.ReadAllTextAsync(hierarchyPath));
            catalogVersion = hierarchy.RootElement.GetProperty("catalogVersion").GetString() ?? "unknown";
            catalogHash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
        }
        var legacy = provider.GetRequiredService<IQuestionSchemaRequirementsExtractor>();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await using var stream = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));

        if (eligibleProviders.Length == 0)
        {
            foreach (var benchmarkCase in selected)
            {
                var caseId = benchmarkCase.GetProperty("id").GetString()!;
                var question = benchmarkCase.GetProperty("question").GetString()!;
                var attemptId = Guid.NewGuid().ToString("N");
                await WriteRow(writer, new { eventType = "attempt_started", caseId, attemptId, startedAtUtc = DateTimeOffset.UtcNow, secretValuesLogged = false });
                await WriteRow(writer, new
                {
                    eventType = "attempt_terminal", caseId, attemptId, question,
                    goldStatus = ReadString(benchmarkCase, "goldStatus"),
                    expected = benchmarkCase.GetProperty("expected"),
                    expectedFields = benchmarkCase.GetProperty("expectedFields"),
                    legacy = new { status = "failed", failureCategory = "provider_failure_or_unavailable", extractedIntent = (object?)null, modelCalls = 0, inputTokens = (int?)null, outputTokens = (int?)null, latencyMs = 0L },
                    optionB = new { status = "failed", failureCategory = "provider_failure", diagnostic = "no_eligible_structured_output_provider", intent = (object?)null, modelCalls = 0, inputTokens = (int?)null, outputTokens = (int?)null, latencyMs = 0L },
                    completedAtUtc = DateTimeOffset.UtcNow, secretValuesLogged = false
                });
            }
            Console.WriteLine("No configured structured-output provider is eligible; terminal no-provider records were written without model requests.");
            Console.WriteLine($"Terminal shadow rows written: {outputPath}");
            return;
        }

        foreach (var benchmarkCase in selected)
        {
            var caseId = benchmarkCase.GetProperty("id").GetString()!;
            var question = benchmarkCase.GetProperty("question").GetString()!;
            var attemptId = Guid.NewGuid().ToString("N");
            var startedAt = DateTimeOffset.UtcNow;
            if (captureCanonicalOutput) structuredOutputCapture!.Reset();
            await WriteRow(writer, new { eventType = "attempt_started", benchmarkVersion = dataset.RootElement.GetProperty("benchmarkVersion").GetInt32(), caseId, attemptId, startedAtUtc = startedAt, secretValuesLogged = false });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var legacyTask = RunLegacyAsync(legacy, new SchemaNavigationRequest(1, question, catalogVersion, catalogHash,
                TenantId: tenantId, CorrelationId: $"intent-shadow-{caseId}-{attemptId}"), timeout.Token);
            var optionBTask = RunOptionBAsync(optionB, question, timeout.Token);
            await Task.WhenAll(legacyTask, optionBTask);
            var legacyResult = await legacyTask;
            var optionBResult = await optionBTask;
            await WriteRow(writer, new
            {
                eventType = "attempt_terminal",
                benchmarkVersion = dataset.RootElement.GetProperty("benchmarkVersion").GetInt32(),
                caseId,
                attemptId,
                question,
                goldStatus = ReadString(benchmarkCase, "goldStatus"),
                expected = benchmarkCase.GetProperty("expected"),
                expectedFields = benchmarkCase.GetProperty("expectedFields"),
                legacy = legacyResult,
                optionB = new
                {
                    status = optionBResult.Succeeded ? "completed" : "failed",
                    failureCategory = optionBResult.FailureCategory,
                    diagnostic = optionBResult.SanitizedProviderDiagnostic,
                    intent = optionBResult.Intent,
                    modelCalls = optionBResult.ModelCalls,
                    inputTokens = optionBResult.InputTokens,
                    outputTokens = optionBResult.OutputTokens,
                    latencyMs = (long)optionBResult.Latency.TotalMilliseconds,
                    rawStructuredOutput = captureCanonicalOutput ? structuredOutputCapture?.LastStructuredJson : null
                },
                completedAtUtc = DateTimeOffset.UtcNow,
                secretValuesLogged = false
            });
            Console.WriteLine($"{caseId}: legacy={legacyResult.status} ({legacyResult.latencyMs}ms), optionB={(optionBResult.Succeeded ? "completed" : optionBResult.FailureCategory)} ({optionBResult.Latency.TotalMilliseconds:F0}ms)");
        }

        Console.WriteLine($"Terminal shadow rows written: {outputPath}");
    }

    private static async Task<LegacyExtractionResult> RunLegacyAsync(
        IQuestionSchemaRequirementsExtractor extractor, SchemaNavigationRequest request, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var requirements = await extractor.ExtractAsync(request, cancellationToken);
            timer.Stop();
            var shape = requirements.StructuredShape;
            return new("completed", null, new
            {
                measureIds = requirements.MeasureIds,
                entityKinds = requirements.EntityKinds,
                entityReferences = requirements.EntityReferences,
                productReferences = requirements.ProductReferences,
                populationDiscoveryRequested = requirements.PopulationDiscoveryRequested,
                operation = shape?.FinancialOperationKind.ToString() ?? requirements.RequestedFinancialValueOperation,
                comparison = shape?.ComparisonMode.ToString(),
                maximumPeriods = shape?.MaximumPeriods,
                periodPolicies = requirements.PeriodPolicies,
                hasUnknownOrAmbiguousRequirements = requirements.HasUnknownOrAmbiguousRequirements,
                uncertaintyReason = requirements.UncertaintyReason
            }, requirements.ModelCalls, requirements.InputTokens, requirements.OutputTokens,
                (long)(requirements.ModelLatency ?? timer.Elapsed).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timer.Stop();
            return new("failed", "evaluation_timeout", null, 0, null, null, (long)timer.Elapsed.TotalMilliseconds);
        }
        catch (AiModelProviderException exception)
        {
            timer.Stop();
            var category = exception.Status == AiExecutionStatus.TimedOut ? "provider_timeout_or_cancellation" : "provider_failure_or_unavailable";
            return new("failed", category, null, exception.EvaluationModelCalls ?? 1,
                exception.AccumulatedProviderInputTokens ?? exception.ProviderReportedInputTokens,
                exception.AccumulatedProviderOutputTokens ?? exception.ProviderReportedOutputTokens,
                (long)(exception.EvaluationLatency ?? timer.Elapsed).TotalMilliseconds);
        }
        catch (Exception exception)
        {
            timer.Stop();
            return new("failed", "internal_error", new { exceptionType = exception.GetType().Name }, 0, null, null, (long)timer.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<CanonicalIntentExtractionOutcome> RunOptionBAsync(
        ICanonicalSemanticIntentExtractor extractor, string question, CancellationToken cancellationToken)
    {
        try
        {
            return await extractor.ExtractAsync(question, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new(null, "evaluation_timeout", "shadow_case_deadline_exceeded", 0, null, null, TimeSpan.Zero);
        }
        catch (Exception exception)
        {
            return new(null, "internal_error", exception.GetType().Name, 0, null, null, TimeSpan.Zero);
        }
    }

    private static async Task RunStructuredOutputSmokeAsync(
        IAiModelExecutionService execution,
        Guid tenantId,
        AiModelProviderDescriptor? selectedProvider,
        CancellationToken cancellationToken)
    {
        if (selectedProvider is null)
        {
            Console.WriteLine("{\"stage\":\"simple_structured_output\",\"category\":\"provider_not_configured\",\"secretValuesLogged\":false}");
            return;
        }

        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"ok\":{\"type\":\"boolean\"}},\"required\":[\"ok\"]}";
        var correlationId = $"canonical-intent-smoke-{Guid.NewGuid():N}";
        var request = new AiModelRequest(correlationId, tenantId, AiWorkloadKind.SchemaNavigation,
            [new(AiMessageRole.System, "Return only the requested JSON object with ok set to true."),
             new(AiMessageRole.User, "Simple structured-output connectivity smoke test.")],
            new AiStructuredOutputContract("canonical_intent_provider_smoke", ["ok"], schema),
            MaxOutputTokens: 64);
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await execution.ExecuteAsync(
                new AiModelSelectionRequest(tenantId, AiWorkloadKind.SchemaNavigation,
                    AiModelCapability.ChatCompletion | AiModelCapability.StructuredOutput, correlationId),
                request, cancellationToken);
            timer.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                stage = "simple_structured_output",
                status = "completed",
                provider = result.Usage.ProviderKey,
                model = result.Usage.ModelKey,
                inputTokens = result.Usage.InputTokens,
                outputTokens = result.Usage.OutputTokens,
                latencyMs = (long)timer.Elapsed.TotalMilliseconds,
                secretValuesLogged = false
            }, JsonOptions));
        }
        catch (AiModelProviderException exception)
        {
            timer.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                stage = "simple_structured_output",
                status = "failed",
                category = ClassifyProviderFailure(exception),
                errorCode = exception.Code,
                httpStatus = exception.HttpStatusCode,
                providerErrorCode = exception.ProviderErrorCode,
                requestHost = exception.RequestHost,
                exceptionType = exception.InnerException?.GetType().Name,
                latencyMs = (long)timer.Elapsed.TotalMilliseconds,
                secretValuesLogged = false
            }, JsonOptions));
        }
        catch (Exception exception)
        {
            timer.Stop();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                stage = "simple_structured_output",
                status = "failed",
                category = "internal_error",
                exceptionType = exception.GetType().Name,
                latencyMs = (long)timer.Elapsed.TotalMilliseconds,
                secretValuesLogged = false
            }, JsonOptions));
        }
    }

    private static async Task RunCanonicalIntentSmokeAsync(ICanonicalSemanticIntentExtractor extractor, string question)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var outcome = await extractor.ExtractAsync(question, timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            stage = "canonical_intent_extraction",
            status = outcome.Succeeded ? "completed" : "failed",
            failureCategory = outcome.FailureCategory,
            diagnostic = outcome.SanitizedProviderDiagnostic,
            modelCalls = outcome.ModelCalls,
            inputTokens = outcome.InputTokens,
            outputTokens = outcome.OutputTokens,
            latencyMs = (long)outcome.Latency.TotalMilliseconds,
            intent = outcome.Intent is null ? null : new
            {
                operation = outcome.Intent.Operation.Kind.ToString(),
                measureConcepts = outcome.Intent.Measures.Select(measure => measure.ConceptId).ToArray(),
                dimensions = outcome.Intent.Measures.Select(measure => measure.Dimension.ToString()).ToArray(),
                requestedUnits = outcome.Intent.Measures.Select(measure => measure.RequestedUnit.ToString()).ToArray(),
                period = outcome.Intent.Period.Kind.ToString(),
                entityScope = outcome.Intent.EntityScope.Kind.ToString(),
                requiresClarification = outcome.Intent.Ambiguity.RequiresClarification
            },
            secretValuesLogged = false
        }, JsonOptions));
    }

    private static string ClassifyProviderFailure(AiModelProviderException exception) => exception.Code switch
    {
        "hosted_request_serialization_failed" => "request_serialization_failure",
        "hosted_response_deserialization_failed" => "response_deserialization_failure",
        "hosted_provider_transport_failed" => "provider_transport_failure",
        "hosted_provider_authentication_failed" => "provider_authentication_failure",
        "hosted_provider_quota_exceeded" => "provider_quota_failure",
        "hosted_provider_rate_limited" => "provider_rate_limited",
        "hosted_client_internal_error" => "internal_error",
        _ when exception.Status == AiExecutionStatus.TimedOut => "provider_timeout",
        _ when exception.HttpStatusCode is 400 or 422 => "invalid_request_schema",
        _ when exception.Status == AiExecutionStatus.InvalidStructuredOutput => "invalid_structured_output",
        _ => "provider_failure"
    };

    private static async Task WriteRow(StreamWriter writer, object row)
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(row, JsonOptions));
        await writer.FlushAsync();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static string SanitizeStructuredOutput(string value)
    {
        var sanitized = Regex.Replace(value, @"(?i)\bsk-(?:proj-)?[A-Za-z0-9_-]{12,}\b", "[REDACTED_API_KEY]");
        return Regex.Replace(sanitized,
            @"(?i)(api[_-]?key|authorization|access[_-]?token|secret)(\s*[:=]\s*)(""[^""]*""|'[^']*'|[^\s,}]+)",
            "$1$2[REDACTED]");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "README.md"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string FullPath(string root, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));

    private static string GetRequiredOption(string[] args, string name) =>
        GetOption(args, name) ?? throw new ArgumentException($"Missing required option {name}.");

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record LegacyExtractionResult(
        string status,
        string? failureCategory,
        object? extractedIntent,
        int modelCalls,
        int? inputTokens,
        int? outputTokens,
        long latencyMs);

    private sealed class StructuredOutputCapturingExecutionService(IAiModelExecutionService inner, bool printRawOutput)
        : IAiModelExecutionService
    {
        public string? LastStructuredJson { get; private set; }

        public void Reset() => LastStructuredJson = null;

        public async Task<AiModelResult> ExecuteAsync(AiModelSelectionRequest selection, AiModelRequest request,
            CancellationToken cancellationToken)
        {
            var result = await inner.ExecuteAsync(selection, request, cancellationToken);
            if (request.StructuredOutput?.SchemaName == "canonical_semantic_intent_v2" &&
                result.StructuredJson is { } structuredJson)
            {
                LastStructuredJson = SanitizeStructuredOutput(structuredJson);
                if (printRawOutput) Console.WriteLine($"capturedStructuredOutput={LastStructuredJson}");
            }
            return result;
        }
    }
}
