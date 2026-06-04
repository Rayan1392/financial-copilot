using System.Text.Json;
using FinancialCopilot.API.Middleware;
using FinancialCopilot.Billing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.IntegrationTests;

public sealed class MiddlewareTests
{
    [Fact]
    public async Task GlobalExceptionMiddleware_ReturnsProblemDetails()
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("Sensitive failure details."),
            NullLogger<GlobalExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "correlation-123"
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var document = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("correlation-123", document.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("Sensitive failure details.", document.RootElement.ToString());
    }

    [Fact]
    public async Task GlobalExceptionMiddleware_InsufficientCredit_Returns402WithStableType()
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InsufficientCreditException(),
            NullLogger<GlobalExceptionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var document = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: CancellationToken.None);

        Assert.Equal(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);
        Assert.Equal(
            "https://financialcopilot/errors/insufficient-credit",
            document.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            "Available spending capacity is insufficient.",
            document.RootElement.GetProperty("detail").GetString());
    }
}
