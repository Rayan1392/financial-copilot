using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public sealed class IndustryRelativeValuationSourceOptions
{
    public const string SectionName = "IndustryRelativeValuation:SourceIngestion";
    public bool Enabled { get; init; }
    public string CanonicalProviderName { get; init; } = ProviderSources.NoavaranCurrentApiName;
    public int MaximumCompaniesPerRun { get; init; } = 5000;
    public int MaximumConcurrency { get; init; } = 4;
    public int LeaseMinutes { get; init; } = 120;
}

public sealed record IndustryRelativeValuationSourceRunRequest(
    string? CorrelationId = null,
    Guid? CompanyId = null,
    int? MaximumCompanies = null);

public sealed record IndustryRelativeValuationSourceRunResult(
    string CorrelationId,
    int CompaniesConsidered,
    int FactsPersisted,
    int FactsUnchanged,
    int Failures,
    bool LeaseContended);

public interface IIndustryRelativeValuationSourceIngestionService
{
    Task<IndustryRelativeValuationSourceRunResult> RunAsync(
        IndustryRelativeValuationSourceRunRequest request,
        CancellationToken cancellationToken);
}

public sealed class IndustryRelativeValuationSourceIngestionService(
    FinancialIngestionDbContext db,
    ICyclicalWavesRelativeValuationProviderClient provider,
    IOptions<IndustryRelativeValuationSourceOptions> options,
    TimeProvider clock,
    ILogger<IndustryRelativeValuationSourceIngestionService> logger)
    : IIndustryRelativeValuationSourceIngestionService
{
    private const string LeaseName = "IndustryRelativeValuationSourceIngestion";
    private const string SourceProviderName = "CyclicalWaves";
    private readonly IndustryRelativeValuationSourceOptions settings = options.Value;

    public async Task<IndustryRelativeValuationSourceRunResult> RunAsync(
        IndustryRelativeValuationSourceRunRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();

        if (!settings.Enabled)
            return new(correlationId, 0, 0, 0, 0, false);

        if (!await TryAcquireLeaseAsync(correlationId, cancellationToken))
            return new(correlationId, 0, 0, 0, 0, true);

        try
        {
            var limit = Math.Clamp(
                request.MaximumCompanies ?? settings.MaximumCompaniesPerRun,
                1,
                settings.MaximumCompaniesPerRun);

            var companies = await db.Companies.AsNoTracking()
                .Where(row => row.ProviderName == settings.CanonicalProviderName &&
                              row.IndustryId != null &&
                              row.SymbolIsin != null &&
                              (request.CompanyId == null || row.Id == request.CompanyId))
                .OrderBy(row => row.Id)
                .Take(limit)
                .Select(row => new SourceCompany(row.Id, row.SymbolIsin!))
                .ToArrayAsync(cancellationToken);

            var persisted = 0;
            var unchanged = 0;
            var failures = 0;
            // EF Core DbContext is intentionally scoped to this orchestration run. Persist each
            // company serially; provider request concurrency can be introduced later behind a
            // DbContextFactory without compromising the immutable write boundary.
            foreach (var company in companies)
            {
                try
                {
                    var result = await ProcessCompanyAsync(company, cancellationToken);
                    persisted += result.Persisted;
                    unchanged += result.Unchanged;
                    failures += result.Failed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures++;
                    logger.LogWarning(exception,
                        "Feature 125 source ingestion failed for canonical company {CompanyId}.",
                        company.CompanyId);
                }
            }
            return new(correlationId, companies.Length, persisted, unchanged, failures, false);
        }
        finally
        {
            await ReleaseLeaseAsync(correlationId, CancellationToken.None);
        }
    }

    private async Task<(int Persisted, int Unchanged, int Failed)> ProcessCompanyAsync(
        SourceCompany company,
        CancellationToken cancellationToken)
    {
        var peTask = provider.GetPeGaugeAsync(company.SymbolIsin, cancellationToken);
        var equilibriumTask = provider.GetEquilibriumGaugeAsync(company.SymbolIsin, cancellationToken);
        await Task.WhenAll(peTask, equilibriumTask);

        var persisted = 0;
        var unchanged = 0;
        var failed = 0;
        foreach (var result in new[] { await peTask, await equilibriumTask })
        {
            if (await PersistAsync(company.CompanyId, SourceProviderName, result, cancellationToken)) persisted++;
            else unchanged++;
            if (!result.IsSuccess) failed++;
        }

        var ps = await db.CompanyPsGaugeSnapshots.AsNoTracking()
            .Where(row => row.CompanyId == company.CompanyId && row.ProviderName == "CyclicalWaves")
            .OrderByDescending(row => row.ObservationDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (ps is not null && ps.GaugeClose > 0m && ps.GaugeAverage > 0m)
        {
            var projection = PsRelativeValuationFactProjection.FromGauge(
                company.CompanyId,
                "CyclicalWaves",
                $"{ps.Id:D}:{ps.GaugePayloadHash}",
                company.SymbolIsin,
                new PsGaugeDistribution(
                    ps.BucketA, ps.BucketB, ps.BucketC, ps.BucketD, ps.BucketE, ps.BucketF,
                    ps.GaugeClose, ps.GaugeAverage, ps.BoundaryStart, ps.BoundaryMin,
                    ps.BoundaryAverage, ps.BoundaryMax, ps.BoundaryEnd),
                ps.GaugeFetchedAtUtc,
                ps.GaugePayloadHash,
                "{\"projection\":\"Feature114.PSGauge\"}");
            var psResult = new RelativeValuationProviderResult(
                RelativeValuationSourceKind.PSGauge,
                projection.CurrentPS,
                projection.HistoricalAveragePS,
                projection.SourceObservationId,
                projection.SourceEndpoint,
                projection.IdentityEvidence,
                RelativeValuationFactReadiness.Ready,
                "Valid",
                projection.PayloadHash,
                projection.RawPayload,
                projection.FetchedAtUtc);
            if (await PersistAsync(company.CompanyId, projection.ProviderName, psResult, cancellationToken)) persisted++;
            else unchanged++;
        }

        return (persisted, unchanged, failed);
    }

    private async Task<bool> PersistAsync(
        Guid companyId,
        string providerName,
        RelativeValuationProviderResult result,
        CancellationToken cancellationToken)
    {
        var existing = await db.IndustryRelativeValuationSourceFacts.SingleOrDefaultAsync(
            row => row.ProviderName == providerName &&
                   row.SourceKind == result.SourceKind.ToString() &&
                   row.SourceObservationId == result.SourceObservationId,
            cancellationToken);
        if (existing is not null) return false;

        var now = clock.GetUtcNow();
        var sourceWatermark = BuildSourceWatermark(
            providerName,
            result.SourceEndpoint,
            companyId,
            result.SourceObservationId,
            result.PayloadHash);
        db.IndustryRelativeValuationSourceFacts.Add(new IndustryRelativeValuationSourceFactRow
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProviderName = providerName,
            SourceKind = result.SourceKind.ToString(),
            SourceObservationId = result.SourceObservationId,
            CurrentValue = result.CurrentValue,
            ReferenceValue = result.ReferenceValue,
            FetchedAtUtc = result.FetchedAtUtc ?? now,
            PersistedAtUtc = now,
            SourceEndpoint = result.SourceEndpoint,
            SourceWatermark = sourceWatermark,
            PayloadHash = result.PayloadHash,
            Readiness = result.Readiness.ToString(),
            QualityCode = result.QualityCode,
            IdentityEvidence = result.IdentityEvidence,
            RawPayload = result.RawPayload
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryAcquireLeaseAsync(string owner, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lease = await db.IndustryRelativeValuationSourceLeases.SingleOrDefaultAsync(
            row => row.LeaseName == LeaseName, cancellationToken);
        if (lease is not null && lease.ExpiresAtUtc > now && lease.Owner != owner) return false;
        if (lease is null)
        {
            db.IndustryRelativeValuationSourceLeases.Add(new IndustryRelativeValuationSourceLeaseRow
            {
                LeaseName = LeaseName,
                Owner = owner,
                UpdatedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(settings.LeaseMinutes)
            });
        }
        else
        {
            lease.Owner = owner;
            lease.UpdatedAtUtc = now;
            lease.ExpiresAtUtc = now.AddMinutes(settings.LeaseMinutes);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task ReleaseLeaseAsync(string owner, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var lease = await db.IndustryRelativeValuationSourceLeases.SingleOrDefaultAsync(
            row => row.LeaseName == LeaseName && row.Owner == owner,
            cancellationToken);
        if (lease is null) return;
        lease.ExpiresAtUtc = now;
        lease.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSourceWatermark(
        string providerName,
        string endpoint,
        Guid companyId,
        string observationId,
        string payloadHash) =>
        $"{providerName}|{endpoint}|{companyId:D}|{observationId}|{payloadHash}";

    private sealed record SourceCompany(Guid CompanyId, string SymbolIsin);
}
