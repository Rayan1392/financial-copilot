using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.TelegramGateway;

public sealed class PrimaryApiClient(IHttpClientFactory factory, IOptions<TelegramGatewaySettings> options) : IHealthCheck
{
    private readonly TelegramGatewaySettings settings = options.Value;
    private int authenticationFailed;

    public async Task<TelegramAssistantResult?> HandleUpdateAsync(TelegramAssistantUpdateRequest request, CancellationToken cancellationToken)
    {
        using var client = CreateClient("TelegramGateway.PrimaryApi");
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/telegram/assistant/updates")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.TryAddWithoutValidation("X-Correlation-Id", request.CorrelationId);
        using var response = await client.SendAsync(message, cancellationToken);
        ObserveAuthentication(response.StatusCode);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelegramAssistantResult>(cancellationToken: cancellationToken);
    }

    public async Task<bool> ConfirmLinkAsync(TelegramLinkConfirmRequest request, CancellationToken cancellationToken)
    {
        using var client = CreateClient("TelegramGateway.PrimaryApi");
        using var response = await client.PostAsJsonAsync("api/v1/telegram/link/confirm", request, cancellationToken);
        ObserveAuthentication(response.StatusCode);
        return response.IsSuccessStatusCode;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Volatile.Read(ref authenticationFailed) == 0
            ? HealthCheckResult.Healthy("Primary API authentication has not failed.")
            : HealthCheckResult.Unhealthy("Primary API rejected the configured service credential."));

    private HttpClient CreateClient(string name)
    {
        var client = factory.CreateClient(name);
        client.BaseAddress = new Uri(settings.PrimaryApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 5, 120));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", settings.PrimaryApiKey);
        return client;
    }

    private void ObserveAuthentication(System.Net.HttpStatusCode statusCode)
    {
        if (statusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            Volatile.Write(ref authenticationFailed, 1);
        }
        else if ((int)statusCode is >= 200 and < 300)
        {
            Volatile.Write(ref authenticationFailed, 0);
        }
    }
}

public sealed record TelegramAssistantUpdateRequest(
    long TelegramUpdateId,
    TelegramAssistantUpdateKind Kind,
    long TelegramUserId,
    long TelegramChatId,
    int? MessageThreadId,
    long? TelegramMessageId,
    string? CallbackQueryId,
    string? CallbackData,
    string? Text,
    string Locale,
    DateTimeOffset ReceivedAtUtc,
    string CorrelationId);

public sealed record TelegramLinkConfirmRequest(
    string StartParameter,
    long TelegramUserId,
    long TelegramChatId,
    string? Username,
    long TelegramUpdateId);
