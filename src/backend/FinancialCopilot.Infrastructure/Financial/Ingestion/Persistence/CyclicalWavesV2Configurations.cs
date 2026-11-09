using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

// Feature 148 mappings. Every table lives in schema "cyclicalwaves" and every foreign key stays inside it.
internal static class CyclicalWavesV2Schema
{
    public const string Name = "cyclicalwaves";
    public const string Amount = "numeric(38,6)";
}

public sealed class CyclicalWavesV2TickerRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2TickerRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2TickerRow> builder)
    {
        builder.ToTable("Tickers", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Ticker).HasMaxLength(64).IsRequired();
        builder.Property(row => row.NormalizedTicker).HasMaxLength(64).IsRequired();
        builder.Property(row => row.Enticker).HasMaxLength(16);
        builder.HasIndex(row => row.Ticker).IsUnique().HasDatabaseName("UX_CyclicalWavesV2Tickers_Ticker");
        builder.HasIndex(row => row.NormalizedTicker).HasDatabaseName("IX_CyclicalWavesV2Tickers_NormalizedTicker");
        builder.HasIndex(row => row.Enticker)
            .IsUnique()
            .HasFilter("\"Enticker\" IS NOT NULL")
            .HasDatabaseName("UX_CyclicalWavesV2Tickers_Enticker");
    }
}

public sealed class CyclicalWavesV2MetricDefinitionRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesV2MetricDefinitionRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2MetricDefinitionRow> builder)
    {
        builder.ToTable(
            "MetricDefinitions",
            CyclicalWavesV2Schema.Name,
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CyclicalWavesV2MetricDefinitions_Unit",
                    "\"Unit\" IN ('Irr', 'Percent', 'Ratio')");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesV2MetricDefinitions_Category",
                    "\"Category\" IN ('MonthlySales', 'QuarterlySales', 'Profit', 'ProfitMargin', 'Valuation')");
            });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.FieldKey).HasMaxLength(64).IsRequired();
        builder.Property(row => row.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(row => row.NameFa).HasMaxLength(200).IsRequired();
        builder.Property(row => row.Category).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Unit).HasMaxLength(16).IsRequired();
        builder.HasIndex(row => row.FieldKey).IsUnique().HasDatabaseName("UX_CyclicalWavesV2MetricDefinitions_FieldKey");
        builder.HasData(ScreenerMetricCatalog.All.Select(definition => new CyclicalWavesV2MetricDefinitionRow
        {
            Id = definition.Id,
            FieldKey = definition.FieldKey,
            NameEn = definition.NameEn,
            NameFa = definition.NameFa,
            Category = definition.Category.ToString(),
            Unit = definition.Unit.ToString(),
            SortOrder = definition.Id
        }));
    }
}

public sealed class CyclicalWavesV2FinancialValueRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2FinancialValueRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2FinancialValueRow> builder)
    {
        builder.ToTable("FinancialValues", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => new { row.TickerId, row.MetricDefinitionId });
        builder.Property(row => row.NumericValue).HasColumnType(CyclicalWavesV2Schema.Amount);
        builder.HasOne<CyclicalWavesV2TickerRow>()
            .WithMany()
            .HasForeignKey(row => row.TickerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CyclicalWavesV2MetricDefinitionRow>()
            .WithMany()
            .HasForeignKey(row => row.MetricDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.MetricDefinitionId).HasDatabaseName("IX_CyclicalWavesV2FinancialValues_Metric");
    }
}

public sealed class CyclicalWavesV2ScreenerMetadataRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesV2ScreenerMetadataRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2ScreenerMetadataRow> builder)
    {
        builder.ToTable("ScreenerMetadata", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => row.TickerId);
        builder.Property(row => row.LastMonthSaleDate).HasColumnType("date");
        builder.Property(row => row.LastQuarterDate).HasColumnType("date");
        builder.HasOne<CyclicalWavesV2TickerRow>()
            .WithOne()
            .HasForeignKey<CyclicalWavesV2ScreenerMetadataRow>(row => row.TickerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CyclicalWavesV2IncomeStatementLineDefinitionRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesV2IncomeStatementLineDefinitionRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2IncomeStatementLineDefinitionRow> builder)
    {
        builder.ToTable("IncomeStatementLineDefinitions", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.NameEn).HasMaxLength(300).IsRequired();
        builder.Property(row => row.NameFa).HasMaxLength(300).IsRequired();
    }
}

public sealed class CyclicalWavesV2IncomeStatementRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesV2IncomeStatementRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2IncomeStatementRow> builder)
    {
        // Live statements carry fiscal-year-change fields, so the month check accepts any calendar month.
        builder.ToTable(
            "IncomeStatements",
            CyclicalWavesV2Schema.Name,
            table => table.HasCheckConstraint(
                "CK_CyclicalWavesV2IncomeStatements_FiscalMonth",
                "\"FiscalMonth\" BETWEEN 1 AND 12"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.PeriodEndingDate).HasColumnType("date");
        builder.Property(row => row.CodalUrl).HasMaxLength(1_000);
        builder.HasOne<CyclicalWavesV2TickerRow>()
            .WithMany()
            .HasForeignKey(row => row.TickerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(row => new { row.TickerId, row.PeriodEndingDate, row.FiscalMonth })
            .IsUnique()
            .HasDatabaseName("UX_CyclicalWavesV2IncomeStatements_Period");
    }
}

public sealed class CyclicalWavesV2IncomeStatementValueRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesV2IncomeStatementValueRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2IncomeStatementValueRow> builder)
    {
        builder.ToTable("IncomeStatementValues", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => new { row.StatementId, row.LineDefinitionId });
        builder.Property(row => row.Quarter).HasColumnType(CyclicalWavesV2Schema.Amount);
        builder.Property(row => row.YearToDate).HasColumnType(CyclicalWavesV2Schema.Amount);
        builder.Property(row => row.TrailingYear).HasColumnType(CyclicalWavesV2Schema.Amount);
        builder.HasOne<CyclicalWavesV2IncomeStatementRow>()
            .WithMany()
            .HasForeignKey(row => row.StatementId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CyclicalWavesV2IncomeStatementLineDefinitionRow>()
            .WithMany()
            .HasForeignKey(row => row.LineDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => row.LineDefinitionId).HasDatabaseName("IX_CyclicalWavesV2IncomeStatementValues_Line");
    }
}

public sealed class CyclicalWavesV2AnalysisRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2AnalysisRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2AnalysisRow> builder)
    {
        builder.ToTable("Analyses", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Title).HasMaxLength(500).IsRequired();
        builder.Property(row => row.Summary).HasColumnType("text");
        builder.Property(row => row.PlainTextSummary).HasColumnType("text");
        builder.Property(row => row.ContentHash).HasColumnType("char(64)").IsRequired();
        builder.HasIndex(row => row.PublishedAt).HasDatabaseName("IX_CyclicalWavesV2Analyses_PublishedAt");
    }
}

public sealed class CyclicalWavesV2AnalysisCategoryRowConfiguration :
    IEntityTypeConfiguration<CyclicalWavesV2AnalysisCategoryRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2AnalysisCategoryRow> builder)
    {
        builder.ToTable("AnalysisCategories", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => new { row.AnalysisId, row.ProviderCategoryId });
        builder.Property(row => row.Title).HasMaxLength(300).IsRequired();
        builder.HasOne<CyclicalWavesV2AnalysisRow>()
            .WithMany()
            .HasForeignKey(row => row.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CyclicalWavesV2AnalysisTagRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2AnalysisTagRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2AnalysisTagRow> builder)
    {
        builder.ToTable("AnalysisTags", CyclicalWavesV2Schema.Name);
        builder.HasKey(row => new { row.AnalysisId, row.ProviderTagId });
        builder.Property(row => row.Name).HasMaxLength(300).IsRequired();
        builder.Property(row => row.NormalizedName).HasMaxLength(300).IsRequired();
        builder.HasOne<CyclicalWavesV2AnalysisRow>()
            .WithMany()
            .HasForeignKey(row => row.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(row => row.NormalizedName).HasDatabaseName("IX_CyclicalWavesV2AnalysisTags_NormalizedName");
    }
}

public sealed class CyclicalWavesV2SyncRunRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2SyncRunRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2SyncRunRow> builder)
    {
        builder.ToTable(
            "SyncRuns",
            CyclicalWavesV2Schema.Name,
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CyclicalWavesV2SyncRuns_Status",
                    "\"Status\" IN ('Requested', 'Running', 'Succeeded', 'CompletedWithErrors', 'Failed', 'SkippedAlreadyRunning')");
                table.HasCheckConstraint(
                    "CK_CyclicalWavesV2SyncRuns_Scope",
                    "\"Scope\" IN ('Catalog', 'Ticker', 'Screener', 'IncomeStatements', 'Analysis', 'GaugeCatalog', 'GaugeDetails', 'EquilibriumCatalog', 'EquilibriumDetails', 'TickerGaugeEquilibrium', 'Full')");
            });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Scope).HasMaxLength(32).IsRequired();
        builder.Property(row => row.TickerFilter).HasMaxLength(64);
        builder.Property(row => row.AnalysisMode).HasMaxLength(16);
        builder.Property(row => row.TriggerSource).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(32).IsRequired();
        builder.Property(row => row.ErrorSummary).HasMaxLength(4_096);
        builder.Property(row => row.LockOwner).HasMaxLength(200);
        builder.Property(row => row.RequestedBy).HasMaxLength(200);
        builder.HasIndex(row => row.Status)
            .IsUnique()
            .HasFilter("\"Status\" = 'Running'")
            .HasDatabaseName("UX_CyclicalWavesV2SyncRuns_SingleRunning");
        builder.HasIndex(row => row.RequestedAt)
            .IsDescending()
            .HasDatabaseName("IX_CyclicalWavesV2SyncRuns_RequestedAt");
    }
}

public sealed class CyclicalWavesV2GaugeAvailabilityRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2GaugeAvailabilityRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2GaugeAvailabilityRow> builder)
    {
        builder.ToTable("GaugeAvailability", CyclicalWavesV2Schema.Name);
        builder.HasKey(x => x.TickerId);
        builder.HasOne<CyclicalWavesV2TickerRow>().WithOne().HasForeignKey<CyclicalWavesV2GaugeAvailabilityRow>(x => x.TickerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.HasPe).HasDatabaseName("IX_CyclicalWavesV2GaugeAvailability_HasPe");
        builder.HasIndex(x => x.HasPs).HasDatabaseName("IX_CyclicalWavesV2GaugeAvailability_HasPs");
    }
}

public sealed class CyclicalWavesV2GaugeDetailRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2GaugeDetailRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2GaugeDetailRow> builder)
    {
        builder.ToTable("GaugeDetails", CyclicalWavesV2Schema.Name, table =>
        {
            table.HasCheckConstraint("CK_CyclicalWavesV2GaugeDetails_Type", "\"GaugeType\" IN ('PE', 'PS')");
            table.HasCheckConstraint("CK_CyclicalWavesV2GaugeDetails_Status", "\"Status\" IS NULL OR \"Status\" BETWEEN 1 AND 4");
            table.HasCheckConstraint("CK_CyclicalWavesV2GaugeDetails_DayCounts", "\"A\" >= 0 AND \"B\" >= 0 AND \"C\" >= 0 AND \"D\" >= 0 AND \"E\" >= 0 AND \"F\" >= 0");
            table.HasCheckConstraint("CK_CyclicalWavesV2GaugeDetails_Scales", "(\"CloseScale\" IS NULL OR \"CloseScale\" BETWEEN 0 AND 16383) AND \"StartScale\" BETWEEN 0 AND 16383 AND \"EndScale\" BETWEEN 0 AND 16383 AND \"MinScale\" BETWEEN 0 AND 16383 AND \"AvgScale\" BETWEEN 0 AND 16383 AND \"MaxScale\" BETWEEN 0 AND 16383");
            table.HasCheckConstraint("CK_CyclicalWavesV2GaugeDetails_Signs", "(\"CloseSign\" IS NULL OR \"CloseSign\" BETWEEN -1 AND 1) AND \"StartSign\" BETWEEN -1 AND 1 AND \"EndSign\" BETWEEN -1 AND 1 AND \"MinSign\" BETWEEN -1 AND 1 AND \"AvgSign\" BETWEEN -1 AND 1 AND \"MaxSign\" BETWEEN -1 AND 1");
            table.HasCheckConstraint("CK_CyclicalWavesV2GaugeDetails_CloseParts", "(\"CloseHigh\" IS NULL) = (\"CloseMiddle\" IS NULL) AND (\"CloseHigh\" IS NULL) = (\"CloseLow\" IS NULL) AND (\"CloseHigh\" IS NULL) = (\"CloseSign\" IS NULL) AND (\"CloseHigh\" IS NULL) = (\"CloseScale\" IS NULL)");
        });
        builder.HasKey(x => new { x.TickerId, x.GaugeType });
        builder.Property(x => x.GaugeType).HasMaxLength(2).IsRequired();
        foreach (var propertyName in new[]
        {
            nameof(CyclicalWavesV2GaugeDetailRow.CloseHigh), nameof(CyclicalWavesV2GaugeDetailRow.CloseMiddle), nameof(CyclicalWavesV2GaugeDetailRow.CloseLow),
            nameof(CyclicalWavesV2GaugeDetailRow.StartHigh), nameof(CyclicalWavesV2GaugeDetailRow.StartMiddle), nameof(CyclicalWavesV2GaugeDetailRow.StartLow),
            nameof(CyclicalWavesV2GaugeDetailRow.EndHigh), nameof(CyclicalWavesV2GaugeDetailRow.EndMiddle), nameof(CyclicalWavesV2GaugeDetailRow.EndLow),
            nameof(CyclicalWavesV2GaugeDetailRow.MinHigh), nameof(CyclicalWavesV2GaugeDetailRow.MinMiddle), nameof(CyclicalWavesV2GaugeDetailRow.MinLow),
            nameof(CyclicalWavesV2GaugeDetailRow.AvgHigh), nameof(CyclicalWavesV2GaugeDetailRow.AvgMiddle), nameof(CyclicalWavesV2GaugeDetailRow.AvgLow),
            nameof(CyclicalWavesV2GaugeDetailRow.MaxHigh), nameof(CyclicalWavesV2GaugeDetailRow.MaxMiddle), nameof(CyclicalWavesV2GaugeDetailRow.MaxLow),
        })
        {
            builder.Property(propertyName).HasColumnType("numeric");
        }
        builder.HasOne<CyclicalWavesV2TickerRow>().WithMany().HasForeignKey(x => x.TickerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.GaugeType, x.Status }).HasDatabaseName("IX_CyclicalWavesV2GaugeDetails_TypeStatus");
    }

}

public sealed class CyclicalWavesV2EquilibriumRangeRowConfiguration : IEntityTypeConfiguration<CyclicalWavesV2EquilibriumRangeRow>
{
    public void Configure(EntityTypeBuilder<CyclicalWavesV2EquilibriumRangeRow> builder)
    {
        builder.ToTable("EquilibriumRanges", CyclicalWavesV2Schema.Name);
        builder.HasKey(x => x.TickerId);
        foreach (var propertyName in new[]
        {
            nameof(CyclicalWavesV2EquilibriumRangeRow.MinHigh), nameof(CyclicalWavesV2EquilibriumRangeRow.MinMiddle), nameof(CyclicalWavesV2EquilibriumRangeRow.MinLow),
            nameof(CyclicalWavesV2EquilibriumRangeRow.AvgHigh), nameof(CyclicalWavesV2EquilibriumRangeRow.AvgMiddle), nameof(CyclicalWavesV2EquilibriumRangeRow.AvgLow),
            nameof(CyclicalWavesV2EquilibriumRangeRow.MaxHigh), nameof(CyclicalWavesV2EquilibriumRangeRow.MaxMiddle), nameof(CyclicalWavesV2EquilibriumRangeRow.MaxLow),
        })
        {
            builder.Property(propertyName).HasColumnType("numeric");
        }
        builder.HasOne<CyclicalWavesV2TickerRow>().WithOne().HasForeignKey<CyclicalWavesV2EquilibriumRangeRow>(x => x.TickerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.HasRange).HasDatabaseName("IX_CyclicalWavesV2EquilibriumRanges_HasRange");
    }
}
