using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisBlogClient(
    HttpClient httpClient,
    IOptions<ComprehensiveAnalysisBlogOptions> options,
    ILogger<ComprehensiveAnalysisBlogClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ComprehensiveAnalysisPagedResponse?> GetPageAsync(
        int page,
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken cancellationToken)
    {
        var pageSize = options.Value.PageSize;
        var url = BuildUrl(page, pageSize, fromDate, toDate);

        var response = await httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "ComprehensiveAnalysis blog API returned {Status} for page {Page}.",
                (int)response.StatusCode,
                page);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ComprehensiveAnalysisPagedResponse>(
            JsonOptions,
            cancellationToken);
    }

    private static string BuildUrl(int page, int pageSize, DateOnly? fromDate, DateOnly? toDate)
    {
        var url = $"blog/getComprehensiveAnalysis?page={page}&paginate={pageSize}";

        if (fromDate.HasValue)
        {
            url += $"&filter[from_date]={fromDate.Value:yyyy-MM-dd}";
        }

        if (toDate.HasValue)
        {
            url += $"&filter[to_date]={toDate.Value:yyyy-MM-dd}";
        }

        return url;
    }
}
