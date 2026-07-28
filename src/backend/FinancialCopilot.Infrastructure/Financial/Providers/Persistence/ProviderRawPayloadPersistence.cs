using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Persistence;

public sealed class ProviderRawPayloadRow
{
    public Guid Id { get; set; }

    public string ProviderName { get; set; } = string.Empty;

    public string Dataset { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string ExternalReference { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public string Checksum { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class ProviderRawPayloadRowConfiguration : IEntityTypeConfiguration<ProviderRawPayloadRow>
{
    public void Configure(EntityTypeBuilder<ProviderRawPayloadRow> builder)
    {
        builder.ToTable("ProviderRawPayloads");
        builder.HasKey(row => row.Id);
        builder.HasIndex(row => new { row.ProviderName, row.Checksum }).IsUnique();
        builder.Property(row => row.ProviderName).HasMaxLength(128);
        builder.Property(row => row.Dataset).HasMaxLength(64);
        builder.Property(row => row.Endpoint).HasMaxLength(512);
        builder.Property(row => row.ExternalReference).HasMaxLength(256);
        builder.Property(row => row.Checksum).HasMaxLength(64);
    }
}

public sealed class ProviderRawPayloadStore(FinancialProviderDbContext dbContext) : IProviderRawPayloadStore
{
    public async Task StoreAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ProviderRawPayloads.SingleOrDefaultAsync(
            row => row.ProviderName == payload.ProviderName && row.Checksum == payload.Checksum,
            cancellationToken);

        if (existing is not null)
        {
            return;
        }

        dbContext.ProviderRawPayloads.Add(new ProviderRawPayloadRow
        {
            Id = payload.Id,
            ProviderName = payload.ProviderName,
            Dataset = payload.Dataset.ToString(),
            Endpoint = payload.Endpoint,
            ExternalReference = payload.ExternalReference,
            Payload = payload.Payload,
            Checksum = payload.Checksum,
            ReceivedAt = payload.ReceivedAt
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProviderRawPayload?> FindByChecksumAsync(
        string providerName,
        string checksum,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.ProviderRawPayloads.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ProviderName == providerName && candidate.Checksum == checksum,
            cancellationToken);

        return row is null
            ? null
            : new ProviderRawPayload(
                row.Id,
                row.ProviderName,
                Enum.Parse<ProviderDataset>(row.Dataset),
                row.Endpoint,
                row.ExternalReference,
                row.Payload,
                row.Checksum,
                row.ReceivedAt);
    }
}
