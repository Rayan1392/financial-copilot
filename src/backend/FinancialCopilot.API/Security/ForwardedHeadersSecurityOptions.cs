using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FinancialCopilot.API.Security;

/// <summary>
/// Explicit trust configuration for proxy-provided client identity headers.
/// Forwarded headers remain disabled unless a deployment opts in and supplies a
/// concrete proxy/network allow-list.
/// </summary>
public sealed class ForwardedHeadersSecurityOptions
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; init; }
    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
    public int ForwardLimit { get; init; } = 1;

    public bool IsValid(out string? error)
    {
        if (!Enabled)
        {
            error = null;
            return true;
        }

        if (ForwardLimit is < 1 or > 10)
        {
            error = "ForwardedHeaders:ForwardLimit must be between 1 and 10.";
            return false;
        }

        if (KnownProxies.Length == 0 && KnownNetworks.Length == 0)
        {
            error = "Forwarded headers cannot be enabled without KnownProxies or KnownNetworks.";
            return false;
        }

        foreach (var value in KnownProxies)
        {
            if (!IPAddress.TryParse(value, out _))
            {
                error = $"ForwardedHeaders:KnownProxies contains an invalid IP address: {value}.";
                return false;
            }
        }

        foreach (var value in KnownNetworks)
        {
            if (!TryParseNetwork(value, out _, out _))
            {
                error = $"ForwardedHeaders:KnownNetworks contains an invalid CIDR network: {value}.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public static bool TryParseNetwork(string value, out IPAddress address, out int prefixLength)
    {
        address = IPAddress.None;
        prefixLength = 0;
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !IPAddress.TryParse(parts[0], out var parsedAddress) ||
            !int.TryParse(parts[1], out prefixLength))
        {
            return false;
        }

        address = parsedAddress;

        var maximumPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        // A zero-length network would trust every address and is equivalent to
        // globally trusting client-supplied forwarding headers.
        return prefixLength > 0 && prefixLength <= maximumPrefix;
    }

    public void ApplyTo(ForwardedHeadersOptions options)
    {
        if (!Enabled) return;

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var value in KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(value));
        }

        foreach (var value in KnownNetworks)
        {
            if (TryParseNetwork(value, out var address, out var prefixLength))
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefixLength));
            }
        }
    }
}
