using System.Text.Json;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Invariants of the authoritative Feature 141 evaluation suite (engine-e2e-suite-v1.json): stable
/// unique IDs, exact Persian questions, a typed expected outcome per case, an explicit blocker for every
/// non-executable case, evidence for every PASS, and agreement with the runner's executable cases.
/// </summary>
public sealed class Feature141EngineE2ESuiteTests
{
    private static readonly string[] Outcomes = ["Answered", "Ambiguous", "Unsupported", "Incomplete", "Unauthorized"];
    private static readonly string[] Statuses = ["PASS", "BLOCKED", "NOT_RUN", "FAIL"];

    [Fact]
    public void SuiteIsAuthoritativeConsistentAndNeverMarksUnexecutedCasesAsPassed()
    {
        var evaluation = EvaluationDirectory();
        using var suite = JsonDocument.Parse(File.ReadAllText(Path.Combine(evaluation, "engine-e2e-suite-v1.json")));
        var root = suite.RootElement;
        Assert.True(root.GetProperty("authoritative").GetBoolean());
        Assert.True(File.Exists(Path.Combine(evaluation, root.GetProperty("fixture").GetString()!)));
        var blockers = root.GetProperty("blockerVocabulary").EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        // The suite only grows: the original 20 design cases plus cases added by later slices (summary.totalCases).
        var totalCases = root.GetProperty("summary").GetProperty("totalCases").GetInt32();
        Assert.True(totalCases >= 20, "the suite never drops below the original 20 cases");
        Assert.Equal(totalCases, cases.Length);
        Assert.Equal(totalCases, cases.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        var pass = 0; var blocked = 0;
        foreach (var item in cases)
        {
            var id = item.GetProperty("id").GetString()!;
            Assert.Matches("^F141-E[0-9]{2}$", id);
            Assert.Contains(item.GetProperty("question").GetString()!, c => c is >= '؀' and <= 'ۿ');
            Assert.Contains(item.GetProperty("expectedOutcome").GetString(), Outcomes);
            var status = item.GetProperty("status").GetString();
            Assert.Contains(status, Statuses);
            var executable = item.GetProperty("executable").GetBoolean();
            var blocker = item.GetProperty("blocker");
            var evidence = item.GetProperty("evidence").EnumerateArray().Select(e => e.GetString()!).ToArray();
            if (!executable)
            {
                Assert.Equal(JsonValueKind.Object, blocker.ValueKind);
                var code = blocker.GetProperty("code").GetString() ?? string.Empty;
                Assert.True(blockers.Contains(code), $"{id} has unknown blocker code '{code}'");
                Assert.Equal("BLOCKED", status);
            }
            if (status == "PASS")
            {
                Assert.True(executable, $"{id} is PASS but not executable");
                Assert.NotEmpty(evidence);
                Assert.True(item.TryGetProperty("lastRun", out _), $"{id} PASS without lastRun");
                foreach (var file in evidence.Where(e => e.StartsWith("artifacts/", StringComparison.Ordinal)))
                    Assert.True(File.Exists(Path.Combine(evaluation, file.Replace('/', Path.DirectorySeparatorChar))), $"{id} evidence missing: {file}");
                pass++;
            }
            if (status == "BLOCKED") blocked++;
        }
        var summary = root.GetProperty("summary");
        Assert.Equal(pass, summary.GetProperty("pass").GetInt32());
        Assert.Equal(blocked, summary.GetProperty("blocked").GetInt32());
        Assert.Equal(cases.Count(item => item.GetProperty("status").GetString() == "FAIL"), summary.GetProperty("fail").GetInt32());

        // Every runner case is a suite case with the same question and typed expectation.
        foreach (var runnerCase in Feature141VerticalSliceCases.All)
        {
            var suiteCase = Assert.Single(cases, item => item.GetProperty("id").GetString() == runnerCase.CaseId);
            Assert.Equal(runnerCase.Question, suiteCase.GetProperty("question").GetString());
            Assert.Equal(runnerCase.ExpectedOutcome.ToString(), suiteCase.GetProperty("expectedOutcome").GetString());
            Assert.True(suiteCase.GetProperty("executable").GetBoolean());
            Assert.Equal(runnerCase.TenantScopeMissing ? "missing" : "authenticated", suiteCase.GetProperty("tenantScope").GetString());
            if (runnerCase.ExpectedValue is { } value)
            {
                var expected = suiteCase.GetProperty("expected");
                Assert.Equal(value, expected.GetProperty("value").GetDecimal());
                Assert.Equal(runnerCase.ExpectedUnit, expected.GetProperty("unit").GetString());
                Assert.Equal(runnerCase.ExpectedPeriodEnd!.Value.ToString("yyyy-MM-dd"), expected.GetProperty("periodEnd").GetString());
                Assert.Equal(runnerCase.ExpectedPersianPeriodEnd, expected.GetProperty("persianPeriodEnd").GetString());
            }
        }
    }

    private static string EvaluationDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "specs", "141-schema-aware-semantic-query-engine", "evaluation");
    }
}
