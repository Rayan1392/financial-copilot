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

    [Fact]
    public void BankingIndustryGroupId_IsReadFromApiJson_AndCanBeOverriddenByEnvironment()
    {
        var apiSettingsPath = FindApiSettingsPath();
        const string overrideName = "FEATURE144_TEST_QuarterlyFinancialReports__BankingIndustryGroupId";
        const string defaultValue = "c96520f9-1d2e-444a-ab46-5bdffd3797d2";
        const string overrideValue = "11111111-2222-3333-4444-555555555555";
        var original = Environment.GetEnvironmentVariable(overrideName);
        try
        {
            Environment.SetEnvironmentVariable(overrideName, null);
            var json = new ConfigurationBuilder().AddJsonFile(apiSettingsPath).Build();
            Assert.Equal(defaultValue, json["QuarterlyFinancialReports:BankingIndustryGroupId"]);

            Environment.SetEnvironmentVariable(overrideName, overrideValue);
            var overridden = new ConfigurationBuilder()
                .AddJsonFile(apiSettingsPath)
                .AddEnvironmentVariables("FEATURE144_TEST_")
                .Build();
            Assert.Equal(overrideValue, overridden["QuarterlyFinancialReports:BankingIndustryGroupId"]);
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
