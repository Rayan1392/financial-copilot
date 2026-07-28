using System.Net;
using System.Security.Cryptography;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Worker;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramDevPollingWorkerPhotoTests
{
    [Fact]
    public async Task SendPhotoAsync_posts_png_caption_and_parse_mode_as_multipart()
    {
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.test/bot/") };
        var media = new TelegramAssistantMediaAttachment(
            "photo",
            "image/png",
            "monthly-trend.png",
            Convert.ToBase64String(png),
            Convert.ToHexStringLower(SHA256.HashData(png)),
            "monthly-trend-chart-v1");

        await TelegramDevPollingWorker.SendPhotoAsync(
            client,
            123456,
            "روند فروش ماهانه",
            "MarkdownV2",
            null,
            media,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/bot/sendPhoto", handler.RequestPath);
        Assert.Equal("123456", handler.TextParts["chat_id"]);
        Assert.Equal("روند فروش ماهانه", handler.TextParts["caption"]);
        Assert.Equal("MarkdownV2", handler.TextParts["parse_mode"]);
        Assert.Equal(png, handler.BinaryParts["photo"]);
        Assert.Equal("image/png", handler.BinaryContentTypes["photo"]);
        Assert.Equal("monthly-trend.png", handler.FileNames["photo"]);
    }

    [Fact]
    public async Task SendPhotoAsync_rejects_a_changed_attachment_before_network_delivery()
    {
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.test/bot/") };
        var media = new TelegramAssistantMediaAttachment(
            "photo",
            "image/png",
            "monthly-trend.png",
            Convert.ToBase64String([1, 2, 3]),
            "incorrect-hash",
            "monthly-trend-chart-v1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TelegramDevPollingWorker.SendPhotoAsync(
                client,
                123456,
                "caption",
                "MarkdownV2",
                null,
                media,
                CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? RequestPath { get; private set; }
        public Dictionary<string, string> TextParts { get; } = [];
        public Dictionary<string, byte[]> BinaryParts { get; } = [];
        public Dictionary<string, string?> BinaryContentTypes { get; } = [];
        public Dictionary<string, string?> FileNames { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestPath = request.RequestUri?.AbsolutePath;
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            foreach (var part in multipart)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"')
                    ?? throw new InvalidOperationException("Multipart part name is required.");
                var fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                if (fileName is null)
                {
                    TextParts[name] = await part.ReadAsStringAsync(cancellationToken);
                }
                else
                {
                    BinaryParts[name] = await part.ReadAsByteArrayAsync(cancellationToken);
                    BinaryContentTypes[name] = part.Headers.ContentType?.MediaType;
                    FileNames[name] = fileName;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            };
        }
    }
}
