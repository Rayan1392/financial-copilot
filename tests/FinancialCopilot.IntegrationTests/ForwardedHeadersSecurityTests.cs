using System.Net;
using FinancialCopilot.API.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.IntegrationTests;

public sealed class ForwardedHeadersSecurityTests
{
    [Fact]
    public async Task TrustedProxy_RewritesRemoteIpFromForwardedFor()
    {
        var remoteIp = await ProcessAsync(
            new ForwardedHeadersSecurityOptions
            {
                Enabled = true,
                KnownProxies = ["10.0.0.1"],
                ForwardLimit = 1
            },
            "10.0.0.1",
            "203.0.113.10");

        Assert.Equal(IPAddress.Parse("203.0.113.10"), remoteIp);
    }

    [Fact]
    public async Task UntrustedProxy_IgnoresForwardedFor()
    {
        var remoteIp = await ProcessAsync(
            new ForwardedHeadersSecurityOptions
            {
                Enabled = true,
                KnownProxies = ["10.0.0.1"],
                ForwardLimit = 1
            },
            "10.0.0.2",
            "203.0.113.10");

        Assert.Equal(IPAddress.Parse("10.0.0.2"), remoteIp);
    }

    [Fact]
    public async Task ForgedForwardedFor_FromUntrustedClientCannotChangeLimiterIdentity()
    {
        var security = new ForwardedHeadersSecurityOptions
        {
            Enabled = true,
            KnownProxies = ["10.0.0.1"],
            ForwardLimit = 1
        };
        var remoteIp = await ProcessAsync(security, "198.51.100.20", "203.0.113.10");
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;

        var partition = RateLimitPolicies.PublicIp(context, new PublicAuthenticationRateLimitOptions());

        Assert.Equal(IPAddress.Parse("198.51.100.20").MapToIPv6().ToString(), partition.PartitionKey);
    }

    [Fact]
    public async Task MultipleTrustedHops_AreProcessedOnlyWithinConfiguredLimit()
    {
        var remoteIp = await ProcessAsync(
            new ForwardedHeadersSecurityOptions
            {
                Enabled = true,
                KnownProxies = ["10.0.0.1", "10.0.0.2"],
                ForwardLimit = 2
            },
            "10.0.0.1",
            "203.0.113.10, 10.0.0.2");

        Assert.Equal(IPAddress.Parse("203.0.113.10"), remoteIp);
    }

    [Fact]
    public void EnabledConfigurationRequiresExplicitTrustedAddress()
    {
        var options = new ForwardedHeadersSecurityOptions { Enabled = true };

        Assert.False(options.IsValid(out var error));
        Assert.Contains("KnownProxies", error, StringComparison.Ordinal);
    }

    private static async Task<IPAddress?> ProcessAsync(
        ForwardedHeadersSecurityOptions security,
        string remoteIp,
        string forwardedFor)
    {
        var options = new ForwardedHeadersOptions();
        security.ApplyTo(options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        IPAddress? observed = null;
        RequestDelegate next = httpContext =>
        {
            observed = httpContext.Connection.RemoteIpAddress;
            return Task.CompletedTask;
        };
        var middleware = new ForwardedHeadersMiddleware(next, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);
        return observed;
    }
}
