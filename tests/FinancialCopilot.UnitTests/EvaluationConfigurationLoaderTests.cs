public sealed class EvaluationConfigurationLoaderTests
{
    [Fact]
    public void Build_UsesApiConfigurationPrecedenceIncludingDevelopmentUserSecrets()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write("appsettings.json", """
            {"AiModelProviders":{"Providers":[{"ProviderKey":"OpenAI","ModelKey":"base","ApiKey":"base-key"}]}}
            """);
        fixture.Write("appsettings.Development.json", """
            {"AiModelProviders":{"Providers":[{"ProviderKey":"OpenAI","ModelKey":"development","ApiKey":"development-key"}]}}
            """);
        fixture.Write("secrets.json", """
            {"AiModelProviders:Providers:0:ApiKey":"user-secret-key"}
            """);

        var configuration = EvaluationConfigurationLoader.Build(
            fixture.DirectoryPath,
            "Development",
            environmentOverrides: new Dictionary<string, string?>
            {
                ["AiModelProviders__Providers__0__ModelKey"] = "environment-model"
            },
            userSecretsFileOverride: fixture.PathFor("secrets.json"));

        Assert.Equal("environment-model", configuration["AiModelProviders:Providers:0:ModelKey"]);
        Assert.Equal("user-secret-key", configuration["AiModelProviders:Providers:0:ApiKey"]);
        var source = EvaluationConfigurationLoader.GetCredentialSource(
            configuration,
            configuration.GetSection("AiModelProviders:Providers:0"));
        Assert.True(source.IsConfigured);
        Assert.Equal("UserSecrets", source.Source);
    }

    [Fact]
    public void Build_UsesProductionSettingsAndDoesNotLoadDevelopmentSecrets()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write("appsettings.json", """
            {"AiModelProviders":{"Providers":[{"ModelKey":"base"}]}}
            """);
        fixture.Write("appsettings.Production.json", """
            {"AiModelProviders":{"Providers":[{"ModelKey":"production"}]}}
            """);
        fixture.Write("secrets.json", """
            {"AiModelProviders:Providers:0:ApiKey":"development-secret"}
            """);

        var configuration = EvaluationConfigurationLoader.Build(
            fixture.DirectoryPath,
            "Production",
            userSecretsFileOverride: fixture.PathFor("secrets.json"));

        Assert.Equal("production", configuration["AiModelProviders:Providers:0:ModelKey"]);
        Assert.Null(configuration["AiModelProviders:Providers:0:ApiKey"]);
    }

    [Fact]
    public void ResolveEnvironmentName_UsesProcessEnvironmentThenApiLaunchProfile()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write("Properties/launchSettings.json", """
            {"profiles":{"http":{"commandName":"Project","environmentVariables":{"ASPNETCORE_ENVIRONMENT":"Development"}}}}
            """);

        Assert.Equal("Development", EvaluationConfigurationLoader.ResolveEnvironmentName(fixture.DirectoryPath));
        Assert.Equal("Staging", EvaluationConfigurationLoader.ResolveEnvironmentName(
            fixture.DirectoryPath,
            environmentVariables: new Dictionary<string, string?> { ["DOTNET_ENVIRONMENT"] = "Staging" }));
        Assert.Equal("Test", EvaluationConfigurationLoader.ResolveEnvironmentName(
            fixture.DirectoryPath,
            requestedEnvironment: "Test"));
    }

    [Fact]
    public void MissingCredentialIsReportedAsMissingWithoutExposingAnyValue()
    {
        using var fixture = new ConfigurationFixture();
        fixture.Write("appsettings.json", """
            {"AiModelProviders":{"Providers":[{"ProviderKey":"OpenAI","ModelKey":"test"}]}}
            """);

        var configuration = EvaluationConfigurationLoader.Build(fixture.DirectoryPath, "Production");
        var source = EvaluationConfigurationLoader.GetCredentialSource(
            configuration,
            configuration.GetSection("AiModelProviders:Providers:0"));

        Assert.False(source.IsConfigured);
        Assert.Equal("Missing", source.Source);
        Assert.DoesNotContain("secret", source.Source, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"semantic-catalog-config-{Guid.NewGuid():N}");

        public ConfigurationFixture() => Directory.CreateDirectory(DirectoryPath);

        public string PathFor(string relativePath) => Path.Combine(DirectoryPath, relativePath);

        public void Write(string relativePath, string content)
        {
            var path = PathFor(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
