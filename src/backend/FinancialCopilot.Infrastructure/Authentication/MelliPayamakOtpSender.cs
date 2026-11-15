using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class SmsOtpOptions
{
    public const string SectionName = "Authentication:Sms";
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "MelliPayamak";
    public string BaseAddress { get; init; } = "https://console.melipayamak.com";
    public string OtpPathToken { get; init; } = "";
    public string CodeHashSecret { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 10;
    public int CodeLifetimeMinutes { get; init; } = 5;
    public int CooldownSeconds { get; init; } = 60;
    public int MaximumRequestsPerHour { get; init; } = 5;
    public int MaximumCodeAttempts { get; init; } = 5;
}

public interface ISmsOtpSender
{
    Task<string> SendAsync(string normalizedMobile, CancellationToken cancellationToken);
}

/// <summary>
/// Sends one OTP using MelliPayamak's path-token endpoint. The token-bearing URI and provider payloads are
/// deliberately excluded from logs. The provider returns the OTP in `code`; callers must persist only its digest.
/// </summary>
public sealed class MelliPayamakOtpSender : ISmsOtpSender
{
    private readonly HttpClient _client;
    private readonly SmsOtpOptions _settings;
    private readonly ILogger<MelliPayamakOtpSender> _logger;

    public MelliPayamakOtpSender(IOptions<SmsOtpOptions> options, ILogger<MelliPayamakOtpSender> logger)
        : this(options, logger, new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) }))
    {
    }

    internal MelliPayamakOtpSender(IOptions<SmsOtpOptions> options, ILogger<MelliPayamakOtpSender> logger, HttpClient client)
    {
        _settings = options.Value;
        _logger = logger;
        _client = client;
    }

    public async Task<string> SendAsync(string normalizedMobile, CancellationToken cancellationToken)
    {
        if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.OtpPathToken))
            throw new SmsDeliveryException("SMS verification is unavailable.");

        var endpoint = new Uri($"{_settings.BaseAddress.TrimEnd('/')}/api/send/otp/{_settings.OtpPathToken}", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { to = normalizedMobile })
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SMS provider rejected OTP request. StatusCode: {StatusCode}", (int)response.StatusCode);
                throw new SmsDeliveryException("SMS verification is unavailable.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var buffer = new byte[1024];
            while (body.Length <= 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                body.Write(buffer, 0, read);
            }
            if (body.Length > 16 * 1024) throw new SmsDeliveryException("SMS verification is unavailable.");
            body.Position = 0;
            var result = await JsonSerializer.DeserializeAsync<OtpResponse>(body, cancellationToken: timeout.Token);
            if (result is null || !string.IsNullOrEmpty(result.Status) ||
                string.IsNullOrWhiteSpace(result.Code) || result.Code.Length is < 4 or > 10 || result.Code.Any(character => !char.IsAsciiDigit(character)))
            {
                _logger.LogWarning("SMS provider returned an unsuccessful OTP result.");
                throw new SmsDeliveryException("SMS verification is unavailable.");
            }
            return result.Code;
        }
        catch (SmsDeliveryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            _logger.LogWarning("SMS OTP request failed. FailureType: {FailureType}", exception.GetType().Name);
            throw new SmsDeliveryException("SMS verification is unavailable.");
        }
    }

    private sealed record OtpResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("code")] string? Code,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] string? Status);
}

public sealed class SmsDeliveryException(string message) : Exception(message);
