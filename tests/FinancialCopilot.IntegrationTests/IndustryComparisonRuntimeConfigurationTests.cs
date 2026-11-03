using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.IntegrationTests;

public sealed class IndustryComparisonRuntimeConfigurationTests
{
    [Fact]
    public void ApiRuntime_ExecutesIndustryComparisonSemantically_AndUsesLocalMemoryCache()
    {
        var apiSettingsPath = FindApiSettingsPath();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(apiSettingsPath, optional: false)
            .AddJsonFile(Path.Combine(Path.GetDirectoryName(apiSettingsPath)!, "appsettings.Development.json"), optional: false)
            .Build();

        Assert.Equal(
            "SemanticPrimary",
            configuration["SemanticRouting:Capabilities:symbol_vs_industry_relative_valuation"]);
        Assert.False(configuration.GetValue<bool>("ScannerCache:UseRedis"));
    }

    private static string FindApiSettingsPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "backend",
                "FinancialCopilot.API",
                "appsettings.json");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate the API appsettings.json from the test output directory.");
    }
}
