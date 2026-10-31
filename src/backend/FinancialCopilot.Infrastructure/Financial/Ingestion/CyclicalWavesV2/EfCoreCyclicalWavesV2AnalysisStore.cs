using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Comprehensive-analysis articles keyed by provider id. The content hash only detects changes; an
/// unchanged article is not written at all, a changed one replaces its categories and tags, and no
/// article is ever deleted.
/// </summary>
public sealed class EfCoreCyclicalWavesV2AnalysisStore(
    FinancialIngestionDbContext db,
    IHtmlTextStripper htmlTextStripper) : ICyclicalWavesV2AnalysisStore
{
    private const int MaximumTitleLength = 500;
    private const int MaximumNameLength = 300;

    public async Task<CyclicalWavesV2AnalysisApplyResult> ApplyAnalysesPageAsync(
        IReadOnlyCollection<CyclicalWavesV2AnalysisArticle> articles,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var incoming = articles
            .GroupBy(article => article.Id)
            .Select(group => group.Last())
            .ToArray();
        var ids = incoming.Select(article => article.Id).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var stored = await db.CyclicalWavesV2Analyses
            .Where(row => ids.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        int inserted = 0, updated = 0, unchanged = 0;
        var replaced = new List<long>();

        foreach (var article in incoming)
        {
            var hash = ComputeContentHash(article);
            if (stored.TryGetValue(article.Id, out var row))
            {
                if (row.ContentHash == hash)
                {
                    unchanged++;
                    continue;
                }

                Apply(row, article, hash, now);
                replaced.Add(article.Id);
                updated++;
                continue;
            }

            row = new CyclicalWavesV2AnalysisRow { Id = article.Id, FirstSyncedAt = now };
            Apply(row, article, hash, now);
            db.CyclicalWavesV2Analyses.Add(row);
            AddChildren(article);
            inserted++;
        }

        if (replaced.Count > 0)
        {
            await db.CyclicalWavesV2AnalysisCategories
                .Where(row => replaced.Contains(row.AnalysisId))
                .ExecuteDeleteAsync(cancellationToken);
            await db.CyclicalWavesV2AnalysisTags
                .Where(row => replaced.Contains(row.AnalysisId))
                .ExecuteDeleteAsync(cancellationToken);
            foreach (var article in incoming.Where(article => replaced.Contains(article.Id)))
            {
                AddChildren(article);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return new CyclicalWavesV2AnalysisApplyResult(inserted, updated, unchanged);
    }

    public async Task<DateTimeOffset?> GetMaxPublishedAtAsync(CancellationToken cancellationToken) =>
        await db.CyclicalWavesV2Analyses
            .AsNoTracking()
            .OrderByDescending(row => row.PublishedAt)
            .Select(row => (DateTimeOffset?)row.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>SHA-256 over title, summary, publication instant and the sorted category and tag identities.</summary>
    public static string ComputeContentHash(CyclicalWavesV2AnalysisArticle article)
    {
        var builder = new StringBuilder();
        builder.Append(article.Title).Append('\u001f');
        builder.Append(article.Summary ?? string.Empty).Append('\u001f');
        builder.Append(article.PublishedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append('\u001f');
        foreach (var category in article.Categories.OrderBy(category => category.Id))
        {
            builder.Append(category.Id.ToString(CultureInfo.InvariantCulture)).Append(':').Append(category.Title).Append('\u001e');
        }

        builder.Append('\u001f');
        foreach (var tag in article.Tags.OrderBy(tag => tag.Id))
        {
            builder.Append(tag.Id.ToString(CultureInfo.InvariantCulture)).Append(':').Append(tag.Name).Append('\u001e');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private void Apply(CyclicalWavesV2AnalysisRow row, CyclicalWavesV2AnalysisArticle article, string hash, DateTimeOffset now)
    {
        row.Title = Truncate(article.Title, MaximumTitleLength);
        row.Summary = string.IsNullOrWhiteSpace(article.Summary) ? null : article.Summary;
        row.PlainTextSummary = row.Summary is null ? null : htmlTextStripper.Strip(row.Summary);
        row.PublishedAt = article.PublishedAt.ToUniversalTime();
        row.ContentHash = hash;
        row.LastSyncedAt = now;
        row.UpdatedAt = now;
    }

    private void AddChildren(CyclicalWavesV2AnalysisArticle article)
    {
        foreach (var category in article.Categories.GroupBy(category => category.Id).Select(group => group.First()))
        {
            db.CyclicalWavesV2AnalysisCategories.Add(new CyclicalWavesV2AnalysisCategoryRow
            {
                AnalysisId = article.Id,
                ProviderCategoryId = category.Id,
                Title = Truncate(category.Title, MaximumNameLength)
            });
        }

        foreach (var tag in article.Tags.GroupBy(tag => tag.Id).Select(group => group.First()))
        {
            db.CyclicalWavesV2AnalysisTags.Add(new CyclicalWavesV2AnalysisTagRow
            {
                AnalysisId = article.Id,
                ProviderTagId = tag.Id,
                Name = Truncate(tag.Name, MaximumNameLength),
                NormalizedName = Truncate(PersianSymbolNormalizer.Normalize(tag.Name), MaximumNameLength)
            });
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
