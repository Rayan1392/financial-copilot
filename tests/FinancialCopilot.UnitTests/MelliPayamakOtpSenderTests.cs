using System.Net;
using System.Net.Http.Json;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class MelliPayamakOtpSenderTests
{
    [Fact]
    public async Task SendAsync_PostsExpectedMobileAndReturnsProviderCode()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { code = "3741437414", status = "" })
        });
        using var client = new HttpClient(handler);
        var sender = CreateSender(client);

        var code = await sender.SendAsync("09123456789", CancellationToken.None);

        Assert.Equal("3741437414", code);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.EndsWith("/api/send/otp/unit-test-token-only", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("09123456789", handler.Payload!.Value.GetProperty("to").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "{\"code\":\"3741437414\",\"status\":\"\"}")]
    [InlineData(HttpStatusCode.OK, "{\"code\":\"3741437414\",\"status\":\"provider error\"}")]
    [InlineData(HttpStatusCode.OK, "{\"code\":\"\",\"status\":\"\"}")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    public async Task SendAsync_ProviderFailureThrowsSafeException(HttpStatusCode status, string body)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        });
        using var client = new HttpClient(handler);
        var sender = CreateSender(client);

        var exception = await Assert.ThrowsAsync<SmsDeliveryException>(
            () => sender.SendAsync("09123456789", CancellationToken.None));

        Assert.Equal("SMS verification is unavailable.", exception.Message);
    }

    private static MelliPayamakOtpSender CreateSender(HttpClient client)
    {
        var options = Options.Create(new SmsOtpOptions
        {
            Enabled = true,
            BaseAddress = "https://console.melipayamak.com",
            OtpPathToken = "unit-test-token-only",
            TimeoutSeconds = 2
        });
        return new MelliPayamakOtpSender(options, NullLogger<MelliPayamakOtpSender>.Instance, client);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public System.Text.Json.JsonElement? Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Payload = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
            return responseFactory(request);
        }
    }
}
