namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class ComprehensiveAnalysisRow
{
    public long Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string PersianCreatedAt { get; set; } = string.Empty;

    public int AuthorId { get; set; }

    public string AuthorName { get; set; } = string.Empty;

    public DateTimeOffset SyncedAt { get; set; }
}

public sealed class ComprehensiveAnalysisTagRow
{
    public long AnalysisId { get; set; }

    public int TagId { get; set; }

    public string TagName { get; set; } = string.Empty;

    public string TagSlug { get; set; } = string.Empty;

    public int TagTypeId { get; set; }

    public bool IsAnalytic { get; set; }
}

public sealed class ComprehensiveAnalysisCategoryRow
{
    public long AnalysisId { get; set; }

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;
}

public sealed class ComprehensiveAnalysisSyncLogRow
{
    public int Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public int PagesTotal { get; set; }

    public int ItemsSynced { get; set; }

    public string? ErrorMessage { get; set; }
}
