using System.Text.RegularExpressions;
using FinancialCopilot.Domain.Identity.TahlilApp;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// TahlilApp partner API settings. The client secret is read from the configured environment variable
/// first, with a configuration fallback for local development and testing.
/// </summary>
public sealed class TahlilAppOptions
{
    public const string SectionName = "Authentication:TahlilApp";

    /// <summary>Feature flag. When false, the launch endpoint returns 404 and no exchange is attempted.</summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute HTTPS origin of the TahlilApp backend. Required when enabled.</summary>
    public string BaseUrl { get; set; } = "https://api.tahlilapp.com/api/";

    /// <summary>Relative path of the official partner exchange endpoint.</summary>
    public string ExchangePath { get; set; } = "partners/exchange";

    public string ClientId { get; set; } = "financial-copilot";

    /// <summary>Environment variable holding the HTTP Basic Auth client secret.</summary>
    public string ClientSecretEnvironmentVariable { get; set; } = "TAHLILAPP_CLIENT_SECRET";

    /// <summary>Optional local-development fallback when the configured environment variable is absent.</summary>
    public string? ClientSecret { get; set; }

    // Kept as a source-compatible configuration alias for existing local deployments.
    public string ApiKeyEnvironmentVariable
    {
        get => ClientSecretEnvironmentVariable;
        set => ClientSecretEnvironmentVariable = value;
    }

    public int ExchangeTimeoutSeconds { get; set; } = 5;

    /// <summary>Upper bound for the exchange response body (Design §13.2: 8 KB).</summary>
    public int MaximumResponseBytes { get; set; } = 8192;

    /// <summary>External plan name → Sapino plan code. Targets must be canonical Billing plans.</summary>
    public Dictionary<string, string> PlanMap { get; set; } = new(StringComparer.Ordinal)
    {
        ["sapio_plus"] = "Plus",
        ["sapio_pro"] = "Pro"
    };

    /// <summary>Key ring location for Data Protection (Design §17.1). Required when the feature is enabled.</summary>
    public string DataProtectionKeyRingPath { get; set; } = string.Empty;

    /// <summary>
    /// Relative path of the official entitlement endpoint; <c>{externalUserId}</c> is the escaped TahlilApp user id.
    /// </summary>
    public string StatusPathTemplate { get; set; } = "partners/users/{externalUserId}/status";

    /// <summary>Single bounded status call budget (Design §11: 3 s). No retry.</summary>
    public int StatusTimeoutSeconds { get; set; } = 3;

    public TahlilAppRevalidationOptions Revalidation { get; set; } = new();
}

/// <summary>Background and login-path entitlement refresh settings (Design §11). The worker also needs <see cref="Enabled"/>.</summary>
public sealed class TahlilAppRevalidationOptions
{
    /// <summary>Starts the background revalidation worker. Off by default.</summary>
    public bool Enabled { get; set; }

    public int PollSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 100;

    public int LeaseSeconds { get; set; } = 90;

    /// <summary>Status TTL: a paid identity is due again this long after a successful sync (Design §11: 15 min).</summary>
    public int StatusTtlMinutes { get; set; } = 15;

    /// <summary>Maximum age of a successful sync before a last-known paid plan lapses to Free (Design §11: 6 h).</summary>
    public int StaleGraceHours { get; set; } = 6;
}

public sealed class TahlilAppOptionsValidator : IValidateOptions<TahlilAppOptions>
{
    private static readonly HashSet<string> CanonicalPlans = new(StringComparer.Ordinal) { "Free", "Plus", "Pro" };
    private static readonly Regex EnvironmentVariableName = new("^[A-Z_][A-Z0-9_]{0,127}$", RegexOptions.CultureInvariant);

    public ValidateOptionsResult Validate(string? name, TahlilAppOptions options)
    {
        var failures = new List<string>();

        if (options.PlanMap.Count == 0)
        {
            failures.Add("PlanMap must contain at least one entry.");
        }

        if (!IsValidStatusPathTemplate(options.StatusPathTemplate))
        {
            failures.Add("StatusPathTemplate must be a safe relative path containing exactly one {externalUserId} placeholder.");
        }

        if (options.StatusTimeoutSeconds is < 1 or > 10)
        {
            failures.Add("StatusTimeoutSeconds must be between 1 and 10.");
        }

        var revalidation = options.Revalidation;
        if (revalidation.PollSeconds is < 5 or > 3600)
        {
            failures.Add("Revalidation:PollSeconds must be between 5 and 3600.");
        }

        if (revalidation.BatchSize is < 1 or > 100)
        {
            failures.Add("Revalidation:BatchSize must be between 1 and 100.");
        }

        if (revalidation.LeaseSeconds is < 30 or > 900)
        {
            failures.Add("Revalidation:LeaseSeconds must be between 30 and 900.");
        }

        if (revalidation.StatusTtlMinutes is < 1 or > 60)
        {
            failures.Add("Revalidation:StatusTtlMinutes must be between 1 and 60.");
        }

        if (revalidation.StaleGraceHours is < 1 or > 24)
        {
            failures.Add("Revalidation:StaleGraceHours must be between 1 and 24.");
        }

        foreach (var (externalPlan, sapinoPlan) in options.PlanMap)
        {
            if (string.IsNullOrWhiteSpace(externalPlan) || externalPlan.Length > 64)
            {
                failures.Add("PlanMap keys must be non-empty and at most 64 characters.");
            }

            if (!CanonicalPlans.Contains(sapinoPlan))
            {
                failures.Add($"PlanMap target '{sapinoPlan}' is not a canonical Billing plan (Free, Plus, Pro).");
            }
        }

        if (!options.Enabled)
        {
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            failures.Add("BaseUrl must be an absolute HTTPS URL without query or fragment.");
        }

        if (string.IsNullOrWhiteSpace(options.ExchangePath) ||
            options.ExchangePath.StartsWith('/') ||
            options.ExchangePath.Contains("..", StringComparison.Ordinal))
        {
            failures.Add("ExchangePath must be a safe relative path under BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) || options.ClientId != "financial-copilot")
        {
            failures.Add("ClientId must be financial-copilot.");
        }

        if (!EnvironmentVariableName.IsMatch(options.ClientSecretEnvironmentVariable ?? string.Empty))
        {
            failures.Add("ClientSecretEnvironmentVariable must be a valid environment variable name.");
        }

        if (options.ExchangeTimeoutSeconds is < 1 or > 120)
        {
            failures.Add("ExchangeTimeoutSeconds must be between 1 and 120.");
        }

        if (options.MaximumResponseBytes is < 1024 or > 65536)
        {
            failures.Add("MaximumResponseBytes must be between 1024 and 65536.");
        }

        if (string.IsNullOrWhiteSpace(options.DataProtectionKeyRingPath))
        {
            failures.Add("DataProtection:KeyRingPath is required when TahlilApp identity is enabled (persistent key ring).");
        }
        else if (!IsWritableDirectory(options.DataProtectionKeyRingPath))
        {
            // Never fall back to an ephemeral ring: that would make stored PII unreadable after a restart (Design §17.1).
            failures.Add("DataProtection:KeyRingPath must be an existing, writable directory.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidStatusPathTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template) ||
            template.StartsWith('/') ||
            template.Contains("..", StringComparison.Ordinal) ||
            template.IndexOfAny(['?', '#']) >= 0)
        {
            return false;
        }

        const string placeholder = "{externalUserId}";
        var first = template.IndexOf(placeholder, StringComparison.Ordinal);
        return first >= 0 && template.IndexOf(placeholder, first + placeholder.Length, StringComparison.Ordinal) < 0;
    }

    private static bool IsWritableDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var probe = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
