using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

public static class EvaluationConfigurationLoader
{
    public static string ResolveEnvironmentName(
        string apiProjectDirectory,
        string? requestedEnvironment = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        string? launchProfile = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedEnvironment)) return requestedEnvironment;

        string? GetEnvironment(string name) => environmentVariables is null
            ? Environment.GetEnvironmentVariable(name)
            : environmentVariables.GetValueOrDefault(name);

        var dotnetEnvironment = GetEnvironment("DOTNET_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(dotnetEnvironment)) return dotnetEnvironment;
        var aspnetEnvironment = GetEnvironment("ASPNETCORE_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(aspnetEnvironment)) return aspnetEnvironment;

        var launchSettingsPath = Path.Combine(apiProjectDirectory, "Properties", "launchSettings.json");
        if (File.Exists(launchSettingsPath))
        {
            using var launchSettings = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            if (launchSettings.RootElement.TryGetProperty("profiles", out var profiles))
            {
                var selected = !string.IsNullOrWhiteSpace(launchProfile) && profiles.TryGetProperty(launchProfile, out var namedProfile)
                    ? namedProfile
                    : profiles.EnumerateObject()
                        .Select(profile => profile.Value)
                        .FirstOrDefault(profile =>
                            profile.TryGetProperty("commandName", out var commandName) &&
                            string.Equals(commandName.GetString(), "Project", StringComparison.OrdinalIgnoreCase));

                if (selected.ValueKind == JsonValueKind.Object &&
                    selected.TryGetProperty("environmentVariables", out var variables))
                {
                    foreach (var name in new[] { "DOTNET_ENVIRONMENT", "ASPNETCORE_ENVIRONMENT" })
                    {
                        if (variables.TryGetProperty(name, out var value) &&
                            value.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(value.GetString()))
                            return value.GetString()!;
                    }
                }
            }
        }

        return "Production";
    }

    public static IConfigurationRoot Build(
        string apiProjectDirectory,
        string environmentName,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        string? userSecretsFileOverride = null,
        string? userSecretsIdOverride = null)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiProjectDirectory, "appsettings.json"), optional: false, reloadOnChange: false)
            .AddJsonFile(Path.Combine(apiProjectDirectory, $"appsettings.{environmentName}.json"), optional: true, reloadOnChange: false);

        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(userSecretsFileOverride))
            {
                builder.AddJsonFile(userSecretsFileOverride, optional: true, reloadOnChange: false);
            }
            else
            {
                var userSecretsId = userSecretsIdOverride ?? ReadApiUserSecretsId(apiProjectDirectory);
                if (!string.IsNullOrWhiteSpace(userSecretsId))
                    builder.AddJsonFile(ResolveUserSecretsPath(userSecretsId), optional: true, reloadOnChange: false);
            }
        }

        builder.AddInMemoryCollection(NormalizeEnvironmentVariables(environmentOverrides ?? ReadEnvironmentVariables()));
        if (configurationOverrides is not null)
            builder.AddInMemoryCollection(configurationOverrides);
        return builder.Build();
    }

    public static (string Source, bool IsConfigured) GetCredentialSource(
        IConfigurationRoot configuration,
        IConfigurationSection providerRegistration)
    {
        var credentialReference = providerRegistration["CredentialSecretReference"];
        if (!string.IsNullOrWhiteSpace(credentialReference))
        {
            var referencedCredential = Environment.GetEnvironmentVariable(credentialReference);
            if (!string.IsNullOrWhiteSpace(referencedCredential))
                return ($"EnvironmentVariable:{credentialReference}", true);
        }

        var key = $"{providerRegistration.Path}:ApiKey";
        var value = configuration[key];
        var sourceProvider = configuration.Providers.Reverse()
            .FirstOrDefault(provider => provider.TryGet(key, out var configured) && !string.IsNullOrWhiteSpace(configured));
        var sourceDescription = sourceProvider?.ToString() ?? string.Empty;
        var source = sourceDescription.Contains("secrets.json", StringComparison.OrdinalIgnoreCase)
            ? "UserSecrets"
            : sourceProvider?.GetType().Name.Contains("EnvironmentVariables", StringComparison.Ordinal) == true
                ? "EnvironmentVariables"
                : string.IsNullOrWhiteSpace(value) ? "Missing" : "ApplicationConfiguration";
        return (source, !string.IsNullOrWhiteSpace(value));
    }

    private static string? ReadApiUserSecretsId(string apiProjectDirectory)
    {
        var projectPath = Path.Combine(apiProjectDirectory, "FinancialCopilot.API.csproj");
        if (!File.Exists(projectPath)) return null;

        var document = XDocument.Load(projectPath);
        return document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "UserSecretsId")?
            .Value.Trim();
    }

    private static string ResolveUserSecretsPath(string userSecretsId)
    {
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft");
        return OperatingSystem.IsWindows()
            ? Path.Combine(root, "Microsoft", "UserSecrets", userSecretsId, "secrets.json")
            : Path.Combine(root, "usersecrets", userSecretsId, "secrets.json");
    }

    private static IReadOnlyDictionary<string, string?> ReadEnvironmentVariables()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            if (item.Key is string key && item.Value is string value)
                values[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }
        return values;
    }

    private static IReadOnlyDictionary<string, string?> NormalizeEnvironmentVariables(
        IReadOnlyDictionary<string, string?> environmentVariables) =>
        environmentVariables.ToDictionary(
            item => item.Key.Replace("__", ":", StringComparison.Ordinal),
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
}
