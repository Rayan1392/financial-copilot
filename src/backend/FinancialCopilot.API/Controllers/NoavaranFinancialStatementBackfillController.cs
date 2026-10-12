using FinancialCopilot.API.Contracts;
using FinancialCopilot.API.Security;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/v1/admin/noavaran-current/financial-statement-backfill")]
[Authorize(Policy = AuthorizationPolicies.NoavaranMonthlyBackfill)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class NoavaranFinancialStatementBackfillController(
    FinancialIngestionDbContext dbContext,
    IManualNoavaranFinancialStatementBackfillService backfillService) : ControllerBase
{
    private static readonly int[] AllowedPeriods = [3, 6, 9, 12];
    private const int InventoryPageSize = 15;
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpGet("/api/v1/admin/noavaran-current/financial-statements/inventory")]
    public async Task<ActionResult<FinancialStatementInventoryResponse>> GetInventory(
        [FromQuery] string ticker,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1)
    {
        if (string.IsNullOrWhiteSpace(ticker) || ticker.Trim().Length > 64)
            return BadRequest(new { message = "A ticker of 1 to 64 characters is required." });
        if (page < 1)
            return BadRequest(new { message = "Page must be a positive 1-based page number." });

        var normalizedTicker = ticker.Trim();
        var company = await dbContext.NoavaranEligibleCompanies.AsNoTracking()
            .Where(row => row.TseSymbol == normalizedTicker)
            .Select(row => new { row.ExternalCompanyId, Ticker = row.TseSymbol })
            .FirstOrDefaultAsync(cancellationToken);

        if (company is null)
            return NotFound(new { message = $"No eligible company was found for ticker '{normalizedTicker}'." });

        var result = dbContext.Database.IsRelational()
            ? await GetRelationalInventoryAsync(company.ExternalCompanyId, page, cancellationToken)
            : dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"
                ? await GetInventoryForNonRelationalTestsAsync(company.ExternalCompanyId, page, cancellationToken)
                : throw new NotSupportedException("Financial statement inventory pagination requires a relational database provider.");

        var totalPages = result.TotalCount == 0 ? 0 : (int)Math.Ceiling(result.TotalCount / (double)InventoryPageSize);
        return Ok(new FinancialStatementInventoryResponse(
            company.Ticker ?? normalizedTicker, result.Items, page, InventoryPageSize, result.TotalCount, totalPages));
    }

    private async Task<InventoryPageResult> GetRelationalInventoryAsync(
        string externalCompanyId, int page, CancellationToken cancellationToken)
    {
        // The UNION is the inventory's logical report set. Indicator source evidence is expanded
        // and deduplicated by report identity in PostgreSQL before either COUNT or LIMIT/OFFSET.
        // SqlQueryRaw formats {0}/{1} placeholders, so regex quantifiers in this SQL must have doubled braces.
        const string inventoryCte = """
            WITH inventory AS (
                SELECT s."Id" AS "ReportId", s."ExternalStatementId" AS "VersionId", s."StatementType",
                    evidence.value->>'jalaliFiscalYearEnd' AS "FiscalYearEndSource",
                    CASE WHEN evidence.value->>'jalaliFiscalYearEnd' ~ '^[0-9]{{4}}/[0-9]{{1,2}}/[0-9]{{1,2}}$'
                         THEN split_part(evidence.value->>'jalaliFiscalYearEnd', '/', 1)::integer END AS "FiscalYearSort",
                    s."PeriodEnd" AS "PeriodEnd", s."PeriodType",
                    CASE WHEN evidence.value ? 'isAudited' THEN (evidence.value->>'isAudited')::boolean END AS "IsAudited",
                    CASE WHEN evidence.value ? 'isRepresented' THEN (evidence.value->>'isRepresented')::boolean END AS "IsRestated",
                    CASE WHEN evidence.value ? 'isComposing' THEN (evidence.value->>'isComposing')::boolean END AS "IsConsolidated"
                FROM "FinancialStatements" s
                LEFT JOIN LATERAL (
                    SELECT item.value FROM jsonb_array_elements(COALESCE(NULLIF(s."WarningsJson", '')::jsonb, '[]'::jsonb)) AS item(value) LIMIT 1
                ) evidence(value) ON true
                WHERE s."ExternalCompanyId" = {0}
                  AND s."ProviderName" IN ({1}, 'NadpcoApi')
                UNION ALL
                SELECT NULL::uuid AS "ReportId", grouped."ComBSID" AS "VersionId", 'FinancialIndicators' AS "StatementType",
                    grouped."FiscalYearEndSource",
                    CASE WHEN grouped."FiscalYearEndSource" ~ '^[0-9]{{4}}/[0-9]{{1,2}}/[0-9]{{1,2}}$'
                         THEN split_part(grouped."FiscalYearEndSource", '/', 1)::integer END AS "FiscalYearSort",
                    grouped."PeriodEnd", grouped."PeriodType", grouped."IsAudited", grouped."IsRestated", grouped."IsConsolidated"
                FROM (
                    SELECT DISTINCT evidence.value->>'comBSID' AS "ComBSID", m."PeriodEnd", m."PeriodType",
                        evidence.value->>'jalaliFiscalYearEnd' AS "FiscalYearEndSource",
                        CASE WHEN evidence.value ? 'isAudited' THEN (evidence.value->>'isAudited')::boolean END AS "IsAudited",
                        CASE WHEN evidence.value ? 'isRepresented' THEN (evidence.value->>'isRepresented')::boolean END AS "IsRestated",
                        CASE WHEN evidence.value ? 'isComposing' THEN (evidence.value->>'isComposing')::boolean END AS "IsConsolidated"
                    FROM "DerivedMetrics" m
                    CROSS JOIN LATERAL jsonb_array_elements(COALESCE(NULLIF(m."SourceEvidenceJson", '')::jsonb, '[]'::jsonb)) evidence(value)
                    WHERE m."ExternalCompanyId" = {0}
                      AND evidence.value->>'source' IN ({1}, 'NadpcoApi')
                      AND NULLIF(evidence.value->>'comBSID', '') IS NOT NULL
                ) grouped
            )
            """;

        var countSql = $"{inventoryCte} SELECT COUNT(*)::integer AS \"Value\" FROM inventory";
        var totalCount = await dbContext.Database.SqlQueryRaw<int>(countSql,
                externalCompanyId, ProviderSources.NoavaranCurrentApiName)
            .SingleAsync(cancellationToken);
        var offset = ((long)page - 1) * InventoryPageSize;
        var itemsSql = $"""
            {inventoryCte}
            SELECT "ReportId", "VersionId", "StatementType", "FiscalYearEndSource", "PeriodEnd", "PeriodType",
                   "IsAudited", "IsRestated", "IsConsolidated"
            FROM inventory
            ORDER BY "FiscalYearSort" DESC NULLS LAST, "PeriodEnd" DESC NULLS LAST,
                     "StatementType" ASC, "VersionId" ASC, "ReportId" ASC,
                     "IsAudited" ASC NULLS FIRST, "IsRestated" ASC NULLS FIRST, "IsConsolidated" ASC NULLS FIRST
            LIMIT {InventoryPageSize} OFFSET {offset}
            """;
        var rows = await dbContext.Database.SqlQueryRaw<InventorySqlRow>(itemsSql,
                externalCompanyId, ProviderSources.NoavaranCurrentApiName)
            .ToListAsync(cancellationToken);
        return new InventoryPageResult(rows.Select(row => new FinancialStatementInventoryItem(
            row.ReportId, row.VersionId, row.StatementType, ParseFiscalYearEnd(row.FiscalYearEndSource),
            FormatJalali(DateOnly.FromDateTime(row.PeriodEnd)), ParsePeriodMonths(row.PeriodType),
            row.IsAudited, row.IsRestated, row.IsConsolidated)).ToArray(), totalCount);
    }

    // The controller tests use EF InMemory, which cannot execute the PostgreSQL CTE above. This
    // branch exists only for those tests; deployed relational providers always use the SQL path.
    private async Task<InventoryPageResult> GetInventoryForNonRelationalTestsAsync(
        string externalCompanyId, int page, CancellationToken cancellationToken)
    {
        var statements = await dbContext.FinancialStatements.AsNoTracking()
            .Where(row => row.ExternalCompanyId == externalCompanyId &&
                (row.ProviderName == ProviderSources.NoavaranCurrentApiName || row.ProviderName == "NadpcoApi"))
            .Select(row => new InventoryStatementProjection(
                row.Id, row.ExternalStatementId, row.StatementType, row.PeriodType, row.PeriodEnd, row.WarningsJson))
            .ToListAsync(cancellationToken);
        var items = statements.Select(row =>
        {
            var evidence = ReadEvidence(row.WarningsJson);
            return new FinancialStatementInventoryItem(row.Id, row.ExternalStatementId, row.StatementType,
                ParseFiscalYearEnd(evidence?.JalaliFiscalYearEnd), FormatJalali(row.PeriodEnd), ParsePeriodMonths(row.PeriodType),
                evidence?.IsAudited, evidence?.IsRepresented, evidence?.IsComposing);
        }).ToList();
        var metrics = await dbContext.DerivedMetrics.AsNoTracking()
            .Where(row => row.ExternalCompanyId == externalCompanyId &&
                (row.SourceEvidenceJson.Contains(ProviderSources.NoavaranCurrentApiName) || row.SourceEvidenceJson.Contains("NadpcoApi")))
            .Select(row => new InventoryMetricProjection(row.PeriodType, row.PeriodEnd, row.SourceEvidenceJson))
            .ToListAsync(cancellationToken);
        items.AddRange(metrics.Select(row => (Row: row, Evidence: ReadEvidence(row.SourceEvidenceJson)))
            .Where(item => item.Evidence is { ComBSID: not null, Source: var source } &&
                (source == ProviderSources.NoavaranCurrentApiName || source == "NadpcoApi"))
            .GroupBy(item => new { item.Evidence!.ComBSID, item.Row.PeriodEnd, item.Row.PeriodType,
                item.Evidence.JalaliFiscalYearEnd, item.Evidence.IsAudited, item.Evidence.IsRepresented, item.Evidence.IsComposing })
            .Select(group =>
            {
                var first = group.First();
                return new FinancialStatementInventoryItem(null, first.Evidence!.ComBSID!.Value.ToString(CultureInfo.InvariantCulture),
                    "FinancialIndicators", ParseFiscalYearEnd(first.Evidence.JalaliFiscalYearEnd), FormatJalali(first.Row.PeriodEnd),
                    ParsePeriodMonths(first.Row.PeriodType), first.Evidence.IsAudited, first.Evidence.IsRepresented, first.Evidence.IsComposing);
            }));
        var ordered = items.OrderByDescending(item => item.FiscalYearEndJalali).ThenByDescending(item => item.PeriodEndJalali, StringComparer.Ordinal)
            .ThenBy(item => item.StatementType, StringComparer.Ordinal).ThenBy(item => item.VersionId, StringComparer.Ordinal)
            .ThenBy(item => item.ReportId).ToArray();
        var offset = ((long)page - 1) * InventoryPageSize;
        var pageItems = offset >= ordered.Length ? [] : ordered.Skip((int)offset).Take(InventoryPageSize).ToArray();
        return new InventoryPageResult(pageItems, ordered.Length);
    }

    [HttpPost("single-company-period")]
    public async Task<ActionResult<AdminManualNoavaranFinancialStatementBackfillResponse>> Run(
        [FromBody] AdminManualNoavaranFinancialStatementBackfillRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Symbol))
            ModelState.AddModelError(nameof(request.Symbol), "Symbol is required.");
        if (request.FiscalYearEnding is < 1400 or > 1500)
            ModelState.AddModelError(nameof(request.FiscalYearEnding), "FiscalYearEnding must be between 1400 and 1500.");
        if (!AllowedPeriods.Contains(request.PeriodMonths))
            ModelState.AddModelError(nameof(request.PeriodMonths), "PeriodMonths must be one of 3, 6, 9, or 12.");
        if (!Enum.TryParse<ManualNoavaranFinancialStatementType>(request.StatementType, ignoreCase: false, out var type) ||
            !Enum.IsDefined(type))
            ModelState.AddModelError(nameof(request.StatementType), "StatementType is not supported.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var externalCompanyId = await dbContext.NoavaranEligibleCompanies.AsNoTracking()
            .Where(company => company.TseSymbol == request.Symbol.Trim())
            .Select(company => company.ExternalCompanyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (externalCompanyId is null)
            return NotFound(new { outcome = "CompanyNotFound", message = $"No eligible company was found for symbol '{request.Symbol}'." });
        if (!int.TryParse(externalCompanyId, out var providerCompanyId) || providerCompanyId <= 0)
            return Problem("The resolved ExternalCompanyId is not a valid positive integer.");

        try
        {
            var result = await backfillService.ExecuteAsync(
                new ManualNoavaranFinancialStatementBackfillRequest(
                    externalCompanyId,
                    request.FiscalYearEnding,
                    request.PeriodMonths,
                    type),
                cancellationToken);
            var response = new AdminManualNoavaranFinancialStatementBackfillResponse(
                result.RunId, result.Outcome, result.ProcessedRecords, result.Message);
            return result.Outcome switch
            {
                "Persisted" => Ok(response),
                "AlreadyExists" => Ok(response),
                "Empty" => Ok(response),
                "PersistenceFailed" => StatusCode(StatusCodes.Status500InternalServerError, response),
                _ => Problem("The manual backfill returned an unknown outcome.")
            };
        }
        catch (FinancialProviderException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new AdminManualNoavaranFinancialStatementBackfillResponse(null, "UpstreamFailed", 0, exception.Message));
        }

    }

    private static NadpcoInventoryEvidence? ReadEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<NadpcoInventoryEvidence[]>(json, EvidenceJsonOptions)?.FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ParseFiscalYearEnd(string? value)
    {
        var parts = value?.Split('/', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 3 } ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
            return null;

        try
        {
            _ = new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0);
            return year;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int? ParsePeriodMonths(string? value) => value switch
    {
        "ThreeMonths" or "3" => 3,
        "SixMonths" or "6" => 6,
        "NineMonths" or "9" => 9,
        "TwelveMonths" or "12" => 12,
        _ => null
    };

    private static string FormatJalali(DateOnly value)
    {
        var calendar = new PersianCalendar();
        var date = value.ToDateTime(TimeOnly.MinValue);
        return string.Create(CultureInfo.InvariantCulture,
            $"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}");
    }

    private sealed record InventoryStatementProjection(
        Guid Id, string ExternalStatementId, string StatementType, string PeriodType, DateOnly PeriodEnd, string WarningsJson);
    private sealed record InventoryMetricProjection(string PeriodType, DateOnly PeriodEnd, string SourceEvidenceJson);
    private sealed record InventoryPageResult(IReadOnlyList<FinancialStatementInventoryItem> Items, int TotalCount);
    private sealed class InventorySqlRow
    {
        public Guid? ReportId { get; set; }
        public string? VersionId { get; set; }
        public string StatementType { get; set; } = "";
        public string? FiscalYearEndSource { get; set; }
        public DateTime PeriodEnd { get; set; }
        public string PeriodType { get; set; } = "";
        public bool? IsAudited { get; set; }
        public bool? IsRestated { get; set; }
        public bool? IsConsolidated { get; set; }
    }
    private sealed class NadpcoInventoryEvidence
    {
        public string? Source { get; set; }
        public long? ComBSID { get; set; }
        public string? JalaliFiscalYearEnd { get; set; }
        public bool? IsAudited { get; set; }
        public bool? IsRepresented { get; set; }
        public bool? IsComposing { get; set; }
    }
}
