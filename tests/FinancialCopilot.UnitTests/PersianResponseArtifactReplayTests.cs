using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.UnitTests;

public sealed class PersianResponseArtifactReplayTests
{
    [Fact]
    public async Task HistoricalPostgreSqlObservations_AreRenderedWithoutRequeryOrNumericSubstitution()
    {
        var root = FindRepositoryRoot();
        var inputPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            "monthly-production-sales-live-llm-postgresql-eight-cases-v17-2026-10-09.jsonl");
        var outputPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            "monthly-production-sales-persian-response-replay-v1-2026-10-09.jsonl");
        var generator = new PersianStructuredQueryResponseGenerator();
        var generated = new List<string>();

        foreach (var line in await File.ReadAllLinesAsync(inputPath))
        {
            using var document = JsonDocument.Parse(line);
            var rootElement = document.RootElement;
            var caseId = rootElement.GetProperty("caseId").GetString()!;
            var question = rootElement.GetProperty("question").GetString()!;
            var pipelineStatus = rootElement.GetProperty("status").GetString();
            PersianStructuredQueryResponse response;
            StructuredQueryPlan? plan = null;
            if (rootElement.TryGetProperty("plan", out var planJson) && planJson.ValueKind == JsonValueKind.Object)
                plan = JsonSerializer.Deserialize<StructuredQueryPlan>(planJson.GetRawText());

            if (string.Equals(pipelineStatus, "succeeded", StringComparison.Ordinal)
                && rootElement.TryGetProperty("observedRows", out var rowsJson)
                && rowsJson.ValueKind == JsonValueKind.Array && plan is not null)
            {
                var rows = rowsJson.EnumerateArray().Select(ParseRow).Cast<IReadOnlyDictionary<string, object?>>().ToArray();
                var unit = plan.Sources.FirstOrDefault()?.CatalogId == "metric_monthly_sales"
                    ? "MillionIRR" : rows.Select(row => row.GetValueOrDefault("SourceUnit") as string)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "ton";
                var execution = new StructuredQueryExecutionResult(StructuredQueryExecutionStatus.Succeeded,
                    rows, plan.Sources.Select(source => source.CatalogId).ToArray(), plan.CatalogVersion, plan.CatalogHash,
                    rootElement.TryGetProperty("queryFingerprint", out var fingerprint) ? fingerprint.GetString() : "historical-postgresql-artifact",
                    null, null, "historical-isolated-postgresql-fixture", [], null, unit, "fixture-catalog", "catalog-unit");
                response = generator.Generate(new(plan, execution));
                var quantityComparisonMissingBaselineUnit = plan.Sources.Any(source =>
                        source.CatalogId is "metric_monthly_production_quantity" or "metric_monthly_sales_quantity")
                    && rows.Any(row => row.GetValueOrDefault("PreviousValue") is not null
                        && string.IsNullOrWhiteSpace(Convert.ToString(row.GetValueOrDefault("PreviousSourceUnit"))));
                Assert.Equal(quantityComparisonMissingBaselineUnit
                    ? StructuredAnswerStatus.Ambiguous : StructuredAnswerStatus.Answered, response.Status);
                if (!quantityComparisonMissingBaselineUnit)
                    AssertActualValuesAreInAnswer(caseId, rows, response.TextAnswer);
            }
            else
            {
                var diagnostics = rootElement.TryGetProperty("validationDiagnostics", out var diagnosticJson)
                    && diagnosticJson.ValueKind == JsonValueKind.Array
                    ? diagnosticJson.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                    : [];
                response = generator.Generate(new(null, null, "planning_validation", diagnostics));
                Assert.Equal(StructuredAnswerStatus.Unsupported, response.Status);
            }

            generated.Add(JsonSerializer.Serialize(new
            {
                caseId, question, executionMode = "deterministic response replay + persisted PostgreSQL result artifact",
                originalPostgreSqlArtifact = Path.GetFileName(inputPath),
                originalTrace = rootElement.Clone(), responseStatus = response.Status.ToString(),
                finalPersianAnswer = response.TextAnswer, response.Evidence, response.TableRows,
                noDatabaseRequery = true, fixtureOnly = true, developmentDatabaseQueried = false
            }));
        }

        Assert.Equal(8, generated.Count);
        await File.WriteAllLinesAsync(outputPath, generated);
    }

    [Fact]
    public async Task CrossCompanyPostgreSqlObservations_RenderSafeAnswersAndRejectMisalignedYoY()
    {
        var root = FindRepositoryRoot();
        var inputPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            "cross-company-postgresql-examples-v2-2026-10-09.jsonl");
        var outputPath = Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts",
            "cross-company-persian-response-replay-v1-2026-10-09.jsonl");
        var generator = new PersianStructuredQueryResponseGenerator();
        var generated = new List<string>();

        foreach (var line in await File.ReadAllLinesAsync(inputPath))
        {
            using var document = JsonDocument.Parse(line);
            var item = document.RootElement;
            var plan = JsonSerializer.Deserialize<StructuredQueryPlan>(item.GetProperty("queryPlan").GetRawText())!;
            var caseRows = item.GetProperty("actualRows").ValueKind == JsonValueKind.Array
                ? item.GetProperty("actualRows").EnumerateArray().Select(ParseRow).Cast<IReadOnlyDictionary<string, object?>>().ToArray()
                : item.GetProperty("actualRows").ValueKind == JsonValueKind.Object
                    ? new[] { ParseRow(item.GetProperty("actualRows")) }
                    : [];
            var postgresStatus = item.GetProperty("postgresStatus").GetString();
            var isMisalignedYoy = plan.FinancialOperation.ComparisonMode == PlanComparisonMode.YearOverYear;
            var status = isMisalignedYoy ? StructuredQueryExecutionStatus.Ambiguous
                : Enum.Parse<StructuredQueryExecutionStatus>(postgresStatus!, ignoreCase: true);
            if (isMisalignedYoy)
            {
                var expected = item.GetProperty("expected");
                var baseline = expected.GetProperty("basePeriod").GetString()!.Split("..", StringSplitOptions.None);
                var current = expected.GetProperty("comparisonPeriod").GetString()!.Split("..", StringSplitOptions.None);
                Assert.Equal(2, baseline.Length);
                Assert.Equal(2, current.Length);
                Assert.NotEqual(DateOnly.Parse(baseline[1]).AddYears(1), DateOnly.Parse(current[1]));
            }
            var rows = caseRows;
            var unit = plan.Sources.FirstOrDefault()?.CatalogId == "metric_monthly_sales" ? "MillionIRR" : "ton";
            var execution = new StructuredQueryExecutionResult(status, rows,
                plan.Sources.Select(source => source.CatalogId).ToArray(), plan.CatalogVersion, plan.CatalogHash,
                "historical-cross-company-postgresql-artifact", null, null, "historical-isolated-postgresql-fixture", [],
                isMisalignedYoy ? "monthly_comparison_reporting_windows_not_comparable" : null,
                unit, "fixture-catalog", "catalog-unit");
            var response = generator.Generate(new(plan, execution));
            if (isMisalignedYoy)
            {
                Assert.Equal(StructuredAnswerStatus.Ambiguous, response.Status);
                Assert.DoesNotContain("۲۳۳", response.TextAnswer);
            }
            else if (postgresStatus == "NoData")
                Assert.Equal(StructuredAnswerStatus.NoData, response.Status);
            else
                Assert.Equal(StructuredAnswerStatus.Answered, response.Status);

            generated.Add(JsonSerializer.Serialize(new
            {
                question = item.GetProperty("question").GetString(),
                executionMode = "deterministic response replay + persisted PostgreSQL result artifact",
                originalPostgreSqlArtifact = Path.GetFileName(inputPath), originalTrace = item.Clone(),
                responseStatus = response.Status.ToString(), finalPersianAnswer = response.TextAnswer,
                response.Evidence, response.TableRows, noDatabaseRequery = true,
                yoyComparabilityPolicyApplied = isMisalignedYoy, fixtureOnly = true,
                developmentDatabaseQueried = false
            }));
        }
        Assert.Equal(4, generated.Count);
        await File.WriteAllLinesAsync(outputPath, generated);
    }

    private static IReadOnlyDictionary<string, object?> ParseRow(JsonElement row)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in row.EnumerateObject())
        {
            object? value = property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String when property.Name is "PeriodStart" or "PeriodEnd" or "PreviousPeriodStart" or "PreviousPeriodEnd"
                    && DateOnly.TryParse(property.Value.GetString(), out var date) => date,
                JsonValueKind.String when property.Name == "CompanyId" && Guid.TryParse(property.Value.GetString(), out var id) => id,
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetDecimal(out var number) => number,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => property.Value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                    ? item.GetString() : item.ToString()).Cast<object?>().ToArray(),
                _ => property.Value.ToString()
            };
            result[property.Name] = value;
        }
        return result;
    }

    private static void AssertActualValuesAreInAnswer(string caseId,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string answer)
    {
        var values = rows.SelectMany(row => new[] { "Value", "GrowthPercentage", "PreviousValue" }
            .Where(row.ContainsKey).Select(key => row[key]))
            .Where(value => value is decimal).Cast<decimal>().Distinct().ToArray();
        if (caseId == "monthly-04-production-top-products-growth")
            values = rows.Where(row => row.GetValueOrDefault("GrowthPercentage") is decimal)
                .Select(row => (decimal)row["GrowthPercentage"]!).Distinct().ToArray();
        foreach (var value in values)
        {
            var formatted = value.ToString("#,##0.############################", System.Globalization.CultureInfo.InvariantCulture)
                .Replace(',', '٬').Replace('.', '٫');
            Assert.Contains(ConvertDigits(formatted), answer);
        }
        if (caseId == "monthly-08-sales-six-month-trend")
            Assert.All(new[] { "۱۲۰", "۱۵۰", "۲۰۰", "۲۵۰", "۳۰۰", "۸۵۰" }, value => Assert.Contains(value, answer));
    }

    private static string ConvertDigits(string value) => new(value.Select(character => character is >= '0' and <= '9'
        ? (char)('۰' + character - '0') : character).ToArray());

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
