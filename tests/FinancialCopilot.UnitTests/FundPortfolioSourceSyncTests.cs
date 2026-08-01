using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioSourceSyncTests
{
    [Fact]
    public async Task ManualSource_ProvidesStableDescriptorAndDownloadStream()
    {
        var source = new ManualUploadFundPortfolioReportSource([
            new ManualFundPortfolioUpload("report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1, 2, 3], "Fund A")]);

        var page = await source.DiscoverAsync(new("ManualUpload", MaximumItems: 10), CancellationToken.None);
        var descriptor = Assert.Single(page.Items);
        var download = await source.DownloadAsync(descriptor, CancellationToken.None);
        using var reader = new MemoryStream();
        await download.Content.CopyToAsync(reader);

        Assert.Equal("Fund A", descriptor.ObservedFundName);
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ToArray());
    }

    [Fact]
    public async Task ManualSource_HonorsContinuationTokenAcrossBoundedPages()
    {
        var source = new ManualUploadFundPortfolioReportSource([
            new("a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [1], "Fund A"),
            new("b.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [2], "Fund B")]);

        var first = await source.DiscoverAsync(new("ManualUpload", MaximumItems: 1), CancellationToken.None);
        var second = await source.DiscoverAsync(new("ManualUpload", MaximumItems: 1, ContinuationToken: first.ContinuationToken), CancellationToken.None);

        Assert.Single(first.Items);
        Assert.Equal("1", first.ContinuationToken);
        Assert.Single(second.Items);
        Assert.Null(second.ContinuationToken);
        Assert.NotEqual(first.Items[0].StableSourceObjectId, second.Items[0].StableSourceObjectId);
    }

    [Fact]
    public async Task UnavailableSource_ExplicitlyRejectsDiscovery()
    {
        var source = new UnavailableFundPortfolioReportSource("Unconfigured");

        Assert.False(source.IsAvailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.DiscoverAsync(new("Unconfigured"), CancellationToken.None));
    }
}
