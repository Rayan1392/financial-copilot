using System.Security.Cryptography;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class ProductRevenueMixTelegramImageTests
{
    [Fact]
    public void Product_revenue_mix_is_sent_as_png_with_company_and_symbol_in_caption()
    {
        var result = new ProductRevenueMixResponse(
            "داترا",
            "آترا زیست آرای",
            1405,
            5,
            163_520_000m,
            "NoavaranCurrentApi",
            [
                new ProductRevenueMixProductItem("گروه دیستون", 1343.959m, 82.2m, 1, true, null, null, null),
                new ProductRevenueMixProductItem("گروه مزو", 273.827m, 16.7m, 2, false, null, null, null)
            ]);
        var response = new AiQueryResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DetectedIntent.ProductRevenueMix,
            null, null, null, null, null, null, false, null,
            new UsageAccountingResult("AiQuery.ProductRevenueMix", "Completed", 1, 99, "v1", false),
            ProductRevenueMixResult: result);

        var message = Assert.Single(new TelegramAssistantResponseRenderer(
            new TelegramMonthlyTrendChartRenderer(),
            NullLogger<TelegramAssistantResponseRenderer>.Instance).Render(response, "fa-IR"));

        Assert.Contains("آترا زیست آرای \\(داترا\\)", message.Text);
        Assert.Contains("۱۴۰۵/۰۵", message.Text);
        Assert.DoesNotContain("ردیف", message.Text);
        var media = Assert.IsType<TelegramAssistantMediaAttachment>(message.Media);
        Assert.Equal("photo", media.Kind);
        Assert.Equal("image/png", media.ContentType);
        Assert.Equal("product-revenue-mix-table-v1", media.RenderVersion);
        var bytes = Convert.FromBase64String(media.ContentBase64);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), media.Sha256);
    }
}
