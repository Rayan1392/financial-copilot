using System.Text.Json;
using FinancialCopilot.API.Middleware;
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
}
