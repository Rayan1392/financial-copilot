using System.Globalization;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Per-capability Chat data source (Feature 159, ADR 0001). Chat keeps one base provider
/// (<see cref="FinancialDataOptions.ChatProvider"/>); <c>FinancialData:CapabilitySources</c> may assign another provider
/// to a capability in <see cref="OverridableRoutes"/> only. An empty map means every capability uses the base provider.
/// There is no fallback between providers.
/// </summary>
public sealed class ChatCapabilitySourceMap
{
    /// <summary>The only capabilities whose source may be overridden. Steps 2 and 3 extend it through their own features.</summary>
    public static readonly IReadOnlySet<string> OverridableRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        CapabilityExecutionRoutes.ComprehensiveAnalysis
    };

    private readonly Dictionary<string, FinancialDataChatProvider> _overrides;

    private ChatCapabilitySourceMap(Dictionary<string, FinancialDataChatProvider> overrides) => _overrides = overrides;

    public static ChatCapabilitySourceMap Empty { get; } = new(new Dictionary<string, FinancialDataChatProvider>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, FinancialDataChatProvider> Overrides => _overrides;

    public bool TryGetSource(string executionRoute, out FinancialDataChatProvider provider) =>
        _overrides.TryGetValue(executionRoute, out provider);

    /// <summary>Builds the map or throws <see cref="InvalidOperationException"/> listing every invalid entry.</summary>
    public static ChatCapabilitySourceMap Create(FinancialDataOptions options)
    {
        var errors = Validate(options, out var map);
        return errors.Count == 0
            ? map
            : throw new InvalidOperationException(
                "FinancialData:CapabilitySources is invalid: " + string.Join(" ", errors));
    }

    /// <summary>Returns the validation errors (empty when valid) and the map built from the valid entries.</summary>
    public static IReadOnlyList<string> Validate(FinancialDataOptions options, out ChatCapabilitySourceMap map)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();
        var overrides = new Dictionary<string, FinancialDataChatProvider>(StringComparer.Ordinal);
        foreach (var (key, value) in options.CapabilitySources ?? [])
        {
            // An empty value is how docker compose leaves a capability unmapped.
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var route = OverridableRoutes.FirstOrDefault(candidate =>
                string.Equals(candidate, key?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (route is null)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"'{key}' cannot be overridden; allowed: {string.Join(", ", OverridableRoutes)}."));
                continue;
            }

            if (!TryParseProvider(value, out var provider))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture,
                    $"'{key}' has unknown provider '{value}'; allowed: {string.Join(", ", Enum.GetNames<FinancialDataChatProvider>())}."));
                continue;
            }

            overrides[route] = provider;
        }

        if (options.ChatProvider == FinancialDataChatProvider.CyclicalWaves && overrides.Count > 0)
        {
            errors.Add("Capability overrides are allowed only while FinancialData:ChatProvider is NoavaranAmin.");
        }

        map = errors.Count == 0 && overrides.Count > 0 ? new ChatCapabilitySourceMap(overrides) : Empty;
        return errors;
    }

    private static bool TryParseProvider(string value, out FinancialDataChatProvider provider)
    {
        provider = default;
        var trimmed = value.Trim();
        return !trimmed.All(char.IsAsciiDigit) &&
               Enum.TryParse(trimmed, ignoreCase: true, out provider) &&
               Enum.IsDefined(provider);
    }
}
