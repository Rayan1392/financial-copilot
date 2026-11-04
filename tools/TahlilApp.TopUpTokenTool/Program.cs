using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

// Local test tool: mints TahlilApp partner JWTs for POST /api/v1/billing/partners/tahlilapp/top-ups.
// Tokens go to stdout (one per line); diagnostics go to stderr so `$(dotnet run ...)` captures only tokens.

if (args.Any(arg => arg is "-h" or "--help" or "/?"))
{
    Console.Error.WriteLine(TokenToolUsage.Text);
    return 0;
}

var switchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["--kid"] = "TopUpTokenTool:KeyId",
    ["--subject"] = "TopUpTokenTool:Subject",
    ["--scope"] = "TopUpTokenTool:Scope",
    ["--minutes"] = "TopUpTokenTool:LifetimeMinutes",
    ["--count"] = "TopUpTokenTool:Count",
    ["--issuer"] = "Billing:TahlilAppTopUps:Authentication:Issuer",
    ["--audience"] = "Billing:TahlilAppTopUps:Authentication:Audience",
};

try
{
    var configuration = new ConfigurationBuilder()
        .AddUserSecrets<TokenSettings>(optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args, switchMappings)
        .Build();

    var settings = TokenSettings.Load(configuration);
    var now = DateTime.UtcNow;

    for (var i = 0; i < settings.Count; i++)
    {
        var token = TopUpTokenFactory.Create(settings, now);
        TopUpTokenFactory.VerifyLikeApi(token, settings);
        Console.WriteLine(token);
    }

    Console.Error.WriteLine(
        $"Minted {settings.Count} single-use token(s): iss={settings.Issuer} aud={settings.Audience} " +
        $"kid={settings.KeyId} sub={settings.Subject} scope={settings.Scope} " +
        $"expires={now.Add(settings.Lifetime):O} (each jti is consumed on first successful POST).");
    return 0;
}
catch (Exception exception) when (exception is InvalidOperationException or FormatException or SecurityTokenException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    Console.Error.WriteLine("Run with --help for usage.");
    return 1;
}

/// <summary>Resolved token parameters. Issuer, audience and keys are read from the API's own config section.</summary>
internal sealed record TokenSettings(
    string Issuer,
    string Audience,
    string KeyId,
    string SigningKey,
    string Subject,
    string Scope,
    TimeSpan Lifetime,
    int Count)
{
    private const string ContractAudience = "sapio-tahlilapp-topup";
    private const int MinimumKeyBytes = 32;

    public static TokenSettings Load(IConfiguration configuration)
    {
        var auth = configuration.GetSection("Billing:TahlilAppTopUps:Authentication");
        var tool = configuration.GetSection("TopUpTokenTool");

        var issuer = Required(auth["Issuer"], "Billing:TahlilAppTopUps:Authentication:Issuer");
        var audience = string.IsNullOrWhiteSpace(auth["Audience"]) ? ContractAudience : auth["Audience"]!;
        if (audience != ContractAudience)
        {
            throw new InvalidOperationException($"Audience must be '{ContractAudience}' (the API rejects any other value), got '{audience}'.");
        }

        var signingKeys = auth.GetSection("SigningKeys").GetChildren().ToList();
        if (signingKeys.Count == 0)
        {
            throw new InvalidOperationException("No signing keys under Billing:TahlilAppTopUps:Authentication:SigningKeys.");
        }

        var keyId = tool["KeyId"];
        if (string.IsNullOrWhiteSpace(keyId))
        {
            if (signingKeys.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Several signing keys exist ({string.Join(", ", signingKeys.Select(k => k.Key))}); choose one with --kid or TopUpTokenTool:KeyId.");
            }
            keyId = signingKeys[0].Key;
        }

        var signingKey = signingKeys.FirstOrDefault(k => string.Equals(k.Key, keyId, StringComparison.Ordinal))?.Value;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                $"Key ID '{keyId}' is not configured. Known key IDs: {string.Join(", ", signingKeys.Select(k => k.Key))}.");
        }
        if (Encoding.UTF8.GetByteCount(signingKey) < MinimumKeyBytes)
        {
            throw new InvalidOperationException($"Signing key '{keyId}' is shorter than {MinimumKeyBytes} UTF-8 bytes; the API refuses it.");
        }

        var minutes = ParseInt(tool["LifetimeMinutes"], fallback: 5, min: 1, max: 60, "LifetimeMinutes (--minutes)");
        var count = ParseInt(tool["Count"], fallback: 1, min: 1, max: 100, "Count (--count)");

        return new TokenSettings(
            issuer,
            audience,
            keyId,
            signingKey,
            NonEmptyOr(tool["Subject"], "partner-topup"),
            NonEmptyOr(tool["Scope"], "topup.deliver"),
            TimeSpan.FromMinutes(minutes),
            count);
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Missing configuration '{key}' (set it in the API user-secrets).")
            : value;

    private static string NonEmptyOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static int ParseInt(string? value, int fallback, int min, int max, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ||
            parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{name} must be an integer between {min} and {max}, got '{value}'.");
        }
        return parsed;
    }
}

internal static class TopUpTokenFactory
{
    public static string Create(TokenSettings settings, DateTime utcNow)
    {
        var claims = new[]
        {
            new Claim("sub", settings.Subject),
            new Claim("scope", settings.Scope),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var credentials = new SigningCredentials(SigningKey(settings), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: utcNow.AddSeconds(-5),
            expires: utcNow.Add(settings.Lifetime),
            signingCredentials: credentials);
        jwt.Header["kid"] = settings.KeyId;
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>
    /// Re-validates the token with the same rules as the API's TahlilAppTopUpPartner scheme,
    /// so a misconfiguration fails here instead of as an opaque 401 from the endpoint.
    /// </summary>
    public static void VerifyLikeApi(string token, TokenSettings settings)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            IssuerSigningKeyResolver = (_, _, keyId, _) =>
                string.Equals(keyId, settings.KeyId, StringComparison.Ordinal)
                    ? new SecurityKey[] { SigningKey(settings) }
                    : Array.Empty<SecurityKey>(),
        };

        var principal = handler.ValidateToken(token, parameters, out _);
        if (!principal.HasClaim("scope", "topup.deliver"))
        {
            Console.Error.WriteLine($"warning: scope '{settings.Scope}' is not 'topup.deliver'; the endpoint will answer 403.");
        }
    }

    private static SymmetricSecurityKey SigningKey(TokenSettings settings) =>
        new(Encoding.UTF8.GetBytes(settings.SigningKey)) { KeyId = settings.KeyId };
}

internal static class TokenToolUsage
{
    public const string Text = """
        TahlilApp top-up partner JWT generator (local testing only)

        Usage:
          dotnet run --project tools/TahlilApp.TopUpTokenTool -- [options]

        Options:
          --kid <id>         Signing key ID from Billing:TahlilAppTopUps:Authentication:SigningKeys
                             (default: TopUpTokenTool:KeyId, or the only configured key)
          --minutes <n>      Token lifetime, 1-60 (default 5)
          --count <n>        Number of tokens, 1-100 (default 1); each jti is single-use
          --subject <sub>    sub claim (default partner-topup)
          --scope <scope>    scope claim (default topup.deliver)
          --issuer <iss>     Override issuer (default: API config)
          --audience <aud>   Override audience (must be sapio-tahlilapp-topup)

        Configuration comes from the API user-secrets (same UserSecretsId), then
        environment variables, then the command line.
        """;
}
