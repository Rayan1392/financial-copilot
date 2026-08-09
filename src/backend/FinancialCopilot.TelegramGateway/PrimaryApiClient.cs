using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinancialCopilot.Application.Telegram;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.TelegramGateway;

public sealed class PrimaryApiClient(IHttpClientFactory factory, IOptions<TelegramGatewaySettings> options)
{
    private readonly TelegramGatewaySettings settings = options.Value;

    public async Task<TelegramAssistantResult?> HandleUpdateAsync(TelegramAssistantUpdateRequest request, CancellationToken cancellationToken)
    {
        using var client = CreateClient("TelegramGateway.PrimaryApi");
        using var response = await client.PostAsJsonAsync("api/v1/telegram/assistant/updates", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TelegramAssistantResult>(cancellationToken: cancellationToken);
    }

    public async Task<bool> ConfirmLinkAsync(TelegramLinkConfirmRequest request, CancellationToken cancellationToken)
    {
        using var client = CreateClient("TelegramGateway.PrimaryApi");
        using var response = await client.PostAsJsonAsync("api/v1/telegram/link/confirm", request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private HttpClient CreateClient(string name)
    {
        var client = factory.CreateClient(name);
        client.BaseAddress = new Uri(settings.PrimaryApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.RequestTimeoutSeconds, 5, 120));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", settings.PrimaryApiKey);
        return client;
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
