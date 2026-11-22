using FinancialCopilot.Application.AI.GroundedAnalysis;
using Microsoft.Extensions.Configuration;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// BUG-041: the production image has no appsettings.json (docker/api.Dockerfile removes it), so Grounded Analysis
/// reaches production only through docker/appsettings.Production.json and the server .env (documented in
/// .env.example). These tests build the configuration the api container sees and check that the feature is on,
/// priced with the approved amounts, and allowed to use web search.
/// </summary>
public sealed class GroundedAnalysisProductionConfigurationTests
{
    [Fact]
    public void Production_configuration_turns_grounded_analysis_on_with_valid_options()
    {
        var configuration = BuildProductionConfiguration();

        Assert.True(configuration.GetValue<bool>("GroundedAnalysis:Enabled"));
        var options = configuration.GetSection(GroundedAnalysisOptions.SectionName).Get<GroundedAnalysisOptions>()
            ?? new GroundedAnalysisOptions();
        options.Validate();
    }

    [Fact]
    public void Production_prices_are_valid_and_equal_the_approved_amounts_in_appsettings()
    {
        var production = BindPricing(BuildProductionConfiguration());
        var approved = BindPricing(new ConfigurationBuilder()
            .AddJsonFile(RepositoryPath("src", "backend", "FinancialCopilot.API", "appsettings.json"), optional: false)
            .Build());

        production.Validate();
        Assert.Equal(
            (approved.Credits.SimpleNoWeb, approved.Credits.SimpleWeb, approved.Credits.DeepNoWeb, approved.Credits.DeepWeb),
            (production.Credits.SimpleNoWeb, production.Credits.SimpleWeb, production.Credits.DeepNoWeb, production.Credits.DeepWeb));
    }

    /// <summary>
    /// Change «local-company-data-context» (AC-9): the input budgets, the hard ceiling and the data pack reach production
    /// with the same values as appsettings.json (through code defaults or the production files), and match OQ-123.
    /// </summary>
    [Fact]
    public void Production_input_budgets_and_data_pack_equal_appsettings_and_the_approved_values()
    {
        var production = BindAnalysis(BuildProductionConfiguration());
        var approved = BindAnalysis(new ConfigurationBuilder()
            .AddJsonFile(RepositoryPath("src", "backend", "FinancialCopilot.API", "appsettings.json"), optional: false)
            .Build());

        foreach (var options in new[] { production, approved })
        {
            Assert.Equal(24000, options.Profiles.Simple.InputTokenTarget);
            Assert.Equal(40000, options.Profiles.Deep.InputTokenTarget);
            Assert.Equal(120000, options.MaxInputTokens);
            Assert.True(options.DataPack.Enabled);
            Assert.Equal(3, options.DataPack.SectionTimeoutSeconds);
            Assert.Equal(8, options.DataPack.TotalTimeoutSeconds);
            options.Validate();
        }
    }

    [Fact]
    public void Production_grounded_analysis_provider_has_the_web_search_capability()
    {
        var withEnvironment = BuildProductionConfiguration();
        var imageOnly = new ConfigurationBuilder()
            .AddJsonFile(RepositoryPath("docker", "appsettings.Production.json"), optional: false)
            .Build();

        Assert.Contains("WebSearch", ProviderCapabilities(withEnvironment));
        Assert.Contains("WebSearch", ProviderCapabilities(imageOnly));
    }

    [Fact]
    public void Compose_default_openai_capabilities_include_web_search()
    {
        var line = File.ReadLines(RepositoryPath("docker-compose.yml"))
            .Single(item => item.TrimStart().StartsWith("AiModelProviders__Providers__0__Capabilities:", StringComparison.Ordinal));

        var fallback = line[(line.IndexOf(":-", StringComparison.Ordinal) + 2)..line.LastIndexOf('}')];
        Assert.Contains("WebSearch", fallback.Split(',', StringSplitOptions.TrimEntries));
    }

    /// <summary>The image's appsettings.Production.json plus every configuration key (with <c>__</c>) of .env.example,
    /// which the api container receives through env_file.</summary>
    private static IConfiguration BuildProductionConfiguration()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(RepositoryPath(".env.example")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (!key.Contains("__", StringComparison.Ordinal)) continue;
            environment[key.Replace("__", ":", StringComparison.Ordinal)] = line[(separator + 1)..].Trim();
        }

        return new ConfigurationBuilder()
            .AddJsonFile(RepositoryPath("docker", "appsettings.Production.json"), optional: false)
            .AddInMemoryCollection(environment)
            .Build();
    }

    private static GroundedAnalysisPricingOptions BindPricing(IConfiguration configuration) =>
        configuration.GetSection(GroundedAnalysisPricingOptions.SectionName).Get<GroundedAnalysisPricingOptions>()
        ?? new GroundedAnalysisPricingOptions();

    private static GroundedAnalysisOptions BindAnalysis(IConfiguration configuration) =>
        configuration.GetSection(GroundedAnalysisOptions.SectionName).Get<GroundedAnalysisOptions>()
        ?? new GroundedAnalysisOptions();

    private static string[] ProviderCapabilities(IConfiguration configuration)
    {
        var providerKey = configuration["GroundedAnalysis:ProviderKey"] ?? new GroundedAnalysisOptions().ProviderKey;
        var provider = configuration.GetSection("AiModelProviders:Providers").GetChildren()
            .Single(item => string.Equals(item["ProviderKey"], providerKey, StringComparison.OrdinalIgnoreCase));
        return (provider["Capabilities"] ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
    }

    private static string RepositoryPath(params string[] parts)
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md")))
                return Path.Combine([current.FullName, .. parts]);
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
