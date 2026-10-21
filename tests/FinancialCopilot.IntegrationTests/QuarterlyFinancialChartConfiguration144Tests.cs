using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.IntegrationTests;

public sealed class QuarterlyFinancialChartConfiguration144Tests
{
    [Fact]
    public void ApiJsonDefault_IsEnabled_AndEnvironmentOverrideDisablesChart()
    {
        var apiSettingsPath = FindApiSettingsPath();
        const string overrideName = "FEATURE144_TEST_QuarterlyFinancialTable__ShowChart";
        var original = Environment.GetEnvironmentVariable(overrideName);
        try
        {
            Environment.SetEnvironmentVariable(overrideName, null);
            var json = new ConfigurationBuilder().AddJsonFile(apiSettingsPath).Build();
            Assert.True(json.GetValue<bool>("QuarterlyFinancialTable:ShowChart"));

            Environment.SetEnvironmentVariable(overrideName, "false");
            var overridden = new ConfigurationBuilder()
                .AddJsonFile(apiSettingsPath)
                .AddEnvironmentVariables("FEATURE144_TEST_")
                .Build();
            Assert.False(overridden.GetValue<bool>("QuarterlyFinancialTable:ShowChart"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(overrideName, original);
        }
    }

    private static string FindApiSettingsPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "backend", "FinancialCopilot.API", "appsettings.json");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Could not locate the API appsettings.json from the test output directory.");
    }
}
