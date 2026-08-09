using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.TelegramGateway;

public sealed class GatewayRequestAuthenticator(
    IOptions<TelegramGatewaySettings> options,
    GatewayReplayNonceStore replayNonces)
{
    private readonly TelegramGatewaySettings settings = options.Value;

    public bool IsValid(HttpRequest request, string body)
    {
        if (!request.Headers.TryGetValue("X-Gateway-Id", out var id) || id != settings.ServiceId ||
            !request.Headers.TryGetValue("X-Gateway-Timestamp", out var timestamp) ||
            !long.TryParse(timestamp, out var unix) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) > settings.MaximumClockSkewSeconds ||
            !request.Headers.TryGetValue("X-Gateway-Nonce", out var nonce) || string.IsNullOrWhiteSpace(nonce) ||
            !request.Headers.TryGetValue("X-Gateway-Signature", out var signature))
            return false;

        var expected = TelegramGatewaySignature.Sign(request.Method, request.Path, timestamp!, nonce!, body, settings.ServiceSecret);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature!)) &&
            replayNonces.TryAccept(id!, nonce!, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(settings.MaximumClockSkewSeconds));
    }
}

public static class TelegramGatewaySignature
{
    public static string Sign(string method, string path, string timestamp, string nonce, string body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{method}\n{path}\n{timestamp}\n{nonce}\n{body}")));
}
