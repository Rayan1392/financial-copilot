using System.Security.Cryptography;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class ManualUploadFundPortfolioReportSource(IReadOnlyList<ManualFundPortfolioUpload> uploads) : IFundPortfolioReportSource
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ManualFundPortfolioUpload> files = new(uploads.Select((upload, index) => (upload, index))
        .ToDictionary(item => $"manual:{item.index}:{Convert.ToHexString(SHA256.HashData(item.upload.Content))}", item => item.upload, StringComparer.Ordinal));

    public string ProviderName => "ManualUpload";
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public FundPortfolioReportSourceDescriptor Register(ManualFundPortfolioUpload upload)
    {
        var token = $"manual:{Guid.NewGuid():N}:{Convert.ToHexString(SHA256.HashData(upload.Content))}";
        files[token] = upload;
        return ToDescriptor(token, upload);
    }

    public Task<FundPortfolioSourcePage> DiscoverAsync(FundPortfolioSourceQuery query, CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(query.MaximumItems, 1, 500);
        var offset = ParseContinuationToken(query.ContinuationToken);
        var all = files.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => ToDescriptor(item.Key, item.Value))
            .ToArray();
        var items = all.Skip(offset).Take(pageSize).ToArray();
        var nextOffset = offset + items.Length;
        return Task.FromResult(new FundPortfolioSourcePage(items, nextOffset < all.Length ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
    }

    public Task<FundPortfolioSourceDownload> DownloadAsync(FundPortfolioReportSourceDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (!files.TryGetValue(descriptor.DownloadToken, out var upload)) throw new FileNotFoundException("Manual upload was not found.");
        return Task.FromResult(new FundPortfolioSourceDownload(new MemoryStream(upload.Content, writable: false), upload.ContentType, upload.Content.LongLength,
            upload.Checksum ?? Convert.ToHexString(SHA256.HashData(upload.Content))));
    }

    private static FundPortfolioReportSourceDescriptor ToDescriptor(string token, ManualFundPortfolioUpload upload) => new(
        "ManualUpload", token, upload.OriginalFileName, upload.PeriodEnd, upload.FundName, null,
        upload.Checksum ?? Convert.ToHexString(SHA256.HashData(upload.Content)), token);

    private static int ParseContinuationToken(string? token) =>
        int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset)
            ? Math.Max(0, offset)
            : 0;
}

public sealed class FundPortfolioLocalSourceOptions
{
    public string ProviderName { get; set; } = "ConfiguredLocalStorage";
    public string RootPath { get; set; } = string.Empty;
    public string AllowedPrefix { get; set; } = string.Empty;
    public int MaximumItemsPerPage { get; set; } = 100;
}

public sealed class ConfiguredLocalFundPortfolioReportSource(IOptions<FundPortfolioLocalSourceOptions> options) : IFundPortfolioReportSource
{
    public string ProviderName => options.Value.ProviderName;
    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.RootPath) && Directory.Exists(options.Value.RootPath);
    public string? UnavailableReason => IsAvailable ? null : "No approved local/object-storage source is configured.";

    public Task<FundPortfolioSourcePage> DiscoverAsync(FundPortfolioSourceQuery query, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var root = GetAllowedRoot();
        var pageSize = Math.Min(Math.Clamp(query.MaximumItems, 1, 500), Math.Max(1, options.Value.MaximumItemsPerPage));
        var offset = ParseContinuationToken(query.ContinuationToken);
        var files = Directory.EnumerateFiles(root, "*.xlsx", SearchOption.AllDirectories)
            .Where(path => !query.ModifiedAfterUtc.HasValue || File.GetLastWriteTimeUtc(path) > query.ModifiedAfterUtc.Value.UtcDateTime)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Skip(offset)
            .Take(pageSize + 1)
            .ToArray();
        var hasMore = files.Length > pageSize;
        var pageFiles = files.Take(pageSize)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                var info = new FileInfo(path);
                return new FundPortfolioReportSourceDescriptor(ProviderName, relative, info.Name, null, null, info.LastWriteTimeUtc,
                    null, relative);
            }).ToArray();
        return Task.FromResult(new FundPortfolioSourcePage(pageFiles, hasMore
            ? (offset + pageFiles.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null));
    }

    public Task<FundPortfolioSourceDownload> DownloadAsync(FundPortfolioReportSourceDescriptor descriptor, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        if (!string.Equals(descriptor.ProviderName, ProviderName, StringComparison.Ordinal)) throw new InvalidOperationException("Source provider mismatch.");
        var root = GetAllowedRoot();
        var candidate = Path.GetFullPath(Path.Combine(root, descriptor.DownloadToken.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source token escapes the approved prefix.");
        if (!File.Exists(candidate)) throw new FileNotFoundException("Source workbook was not found.");
        var info = new FileInfo(candidate);
        return Task.FromResult(new FundPortfolioSourceDownload(File.OpenRead(candidate), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", info.Length, descriptor.Checksum));
    }

    private string GetAllowedRoot()
    {
        var root = Path.GetFullPath(options.Value.RootPath);
        var prefix = options.Value.AllowedPrefix?.Trim('/','\\') ?? string.Empty;
        var allowed = Path.GetFullPath(Path.Combine(root, prefix));
        if (!allowed.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !string.Equals(allowed, root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Configured source prefix escapes the source root.");
        Directory.CreateDirectory(allowed);
        return allowed;
    }

    private void EnsureAvailable() { if (!IsAvailable) throw new InvalidOperationException(UnavailableReason); }

    private static int ParseContinuationToken(string? token) =>
        int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var offset)
            ? Math.Max(0, offset)
            : 0;
}

public sealed class UnavailableFundPortfolioReportSource(string providerName = "Unconfigured") : IFundPortfolioReportSource
{
    public string ProviderName => providerName;
    public bool IsAvailable => false;
    public string UnavailableReason => "No verified fund portfolio report source adapter is configured.";
    public Task<FundPortfolioSourcePage> DiscoverAsync(FundPortfolioSourceQuery query, CancellationToken cancellationToken) => throw new InvalidOperationException(UnavailableReason);
    public Task<FundPortfolioSourceDownload> DownloadAsync(FundPortfolioReportSourceDescriptor descriptor, CancellationToken cancellationToken) => throw new InvalidOperationException(UnavailableReason);
}
