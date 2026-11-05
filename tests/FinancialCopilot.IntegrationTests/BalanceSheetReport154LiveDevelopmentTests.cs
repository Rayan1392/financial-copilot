using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

/// <summary>
/// Feature 154 live Development verification (AC-S01, M03, M04, C02, C03, C06, C13). Runs the normal V2 endpoint path
/// with the financial-data context on the read-only Development role (default_transaction_read_only=on) and every
/// other context off Development (in-memory harness contexts; the remaining Npgsql contexts on a fresh disposable
/// PostgreSQL database). Expected values come from independent SQL on a separate read-only connection.
/// Gated by FEATURE154_LIVE_DEV_READONLY_CONNECTION_STRING; never writes to Development.
/// </summary>
[Collection(FinancialStatementValueSearch131PostgreSqlCollection.Name)]
public sealed class BalanceSheetReport154LiveDevelopmentTests(FinancialStatementValueSearch131PostgreSqlFixture fixture)
{
    private static readonly string[] LivePredicateMarkets =
    [
        "037c69ad-f519-419f-ae62-59003b6b2428", "a3ccb30a-caed-4f26-a84a-ac0eb8c78c76",
        "86c05022-632c-44cd-96c9-5c4f58c51ef5", "78ea1ed8-3242-4081-9e09-e6dbf34090e5"
    ];

    private static NpgsqlConnectionStringBuilder? ReadOnlyConnection()
    {
        var configured = Environment.GetEnvironmentVariable("FEATURE154_LIVE_DEV_READONLY_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured)) return null;
        var builder = new NpgsqlConnectionStringBuilder(configured) { Pooling = false, Options = "-c default_transaction_read_only=on" };
        return builder.Host is "localhost" or "127.0.0.1" or "::1" &&
               string.Equals(builder.Database, "financial_copilot", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(builder.Username, "feature141_app_reader", StringComparison.Ordinal)
            ? builder
            : null;
    }

    [SkippableFact]
    public async Task LiveDevelopment_NormalPath_MatchesStoredRecords()
    {
        var readOnly = ReadOnlyConnection();
        Skip.If(readOnly is null, "Set FEATURE154_LIVE_DEV_READONLY_CONNECTION_STRING to the local read-only feature141_app_reader connection.");
        await using var disposable = await fixture.CreateDatabaseAsync();
        await using var factory = new BalanceSheetReport154LiveApiFactory(readOnly!.ConnectionString, disposable.ConnectionString);
        factory.EnsureBillingSeeded();
        await using var sql = new NpgsqlConnection(readOnly.ConnectionString);
        await sql.OpenAsync();
        var report = new JsonObject { ["executedAtUtc"] = DateTimeOffset.UtcNow.ToString("O") };
        var results = new JsonArray();

        // Safety: any write through the financial-data context fails on the read-only Development connection.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialIngestionDbContext>();
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync("UPDATE \"Companies\" SET \"Name\" = \"Name\" WHERE false"));
            report["writeAttempt"] = $"{exception.SqlState}: {exception.MessageText}";
            Assert.Equal("25006", exception.SqlState); // read_only_sql_transaction
            await using var check = new NpgsqlCommand("SHOW default_transaction_read_only", sql);
            report["sessionReadOnly"] = (string?)await check.ExecuteScalarAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        async Task<JsonElement> Ask(string message)
        {
            using var response = await client.PostAsJsonAsync("/api/ai/v1/query", new { message }, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        // ---- AC-S01 / AC-M03: وبملت default report.
        var webMellat = await Ask("ترازنامه وبملت");
        results.Add(await CheckDefault(sql, webMellat, "ترازنامه وبملت", "452", "BankingBalanceSheet", "AC-S01+AC-M03"));
        // ---- AC-M04: General template for شگل and فاذر on their latest Development snapshots.
        results.Add(await CheckDefault(sql, await Ask("ترازنامه شگل"), "ترازنامه شگل", "167", "GeneralBalanceSheet", "AC-M04"));
        results.Add(await CheckDefault(sql, await Ask("ترازنامه فاذر"), "ترازنامه فاذر", "290", "GeneralBalanceSheet", "AC-M04"));

        // ---- Comparisons.
        foreach (var (ac, message, company, kind, baselineDate, periodType) in new (string, string, string, string, DateOnly?, string?)[]
                 {
                     ("AC-C02", "ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن", "452", "fiscal_year_end", new DateOnly(2026, 3, 20), null),
                     ("AC-C03", "ترازنامه شگل را با پایان سال مالی قبل مقایسه کن", "167", "fiscal_year_end", new DateOnly(2025, 12, 21), null),
                     ("AC-C06", "ترازنامه وبملت در مقایسه با مدت مشابه سال قبل", "452", "same_stage_previous_year", new DateOnly(2025, 9, 22), "SixMonths"),
                     ("AC-C06", "ترازنامه شگل در مقایسه با مدت مشابه سال قبل", "167", "same_stage_previous_year", new DateOnly(2025, 3, 20), "ThreeMonths"),
                     // S-6 transitional issuer (fiscal-year end moved 10/30 -> 09/30): same stage is unavailable.
                     ("AC-C06", "ترازنامه بکهنوج در مقایسه با مدت مشابه سال قبل", "13097", "same_stage_previous_year", null, null),
                     ("AC-C13", "ترازنامه وبملت را با فصل قبل مقایسه کن", "452", "previous_fiscal_quarter_end", new DateOnly(2026, 6, 21), null),
                     ("AC-C13", "ترازنامه شگل را با فصل قبل مقایسه کن", "167", "previous_fiscal_quarter_end", new DateOnly(2025, 12, 21), null),
                     ("AC-C13", "ترازنامه بکهنوج را با فصل قبل مقایسه کن", "13097", "previous_fiscal_quarter_end", null, null),
                     // S-9 shape: ومعین 6-month anchor 1405/03/31 has no 3-month snapshot at 1404/12/29 (previous fiscal quarter-end).
                     ("AC-C13", "ترازنامه ومعین را با فصل قبل مقایسه کن", "12548", "previous_fiscal_quarter_end", null, null),
                     // F154-V-05, ساربیل (non-Esfand, year end 09/30), 9-month anchor 1405/06/31:
                     // fiscal-year end 1404/09/30 is neither 1404/12/29 nor the previous report (388181 at 1404/12/29).
                     ("AC-C03", "ترازنامه ساربیل را با پایان سال مالی قبل مقایسه کن", "236", "fiscal_year_end", new DateOnly(2025, 12, 21), null),
                     // Contrast for AC-C03: the previous report is a different statement (388181, 3M at 1404/12/29).
                     ("AC-C03 (contrast)", "ترازنامه ساربیل را با گزارش قبلی مقایسه کن", "236", "previous_report", new DateOnly(2026, 3, 20), "ThreeMonths"),
                     // Same stage, exact AC-C06 dates: 9M 1405/06/31 -> 9M 1404/06/31 (382074).
                     ("AC-C06", "ترازنامه ساربیل در مقایسه با مدت مشابه سال قبل", "236", "same_stage_previous_year", new DateOnly(2025, 9, 22), "NineMonths"),
                     // S-9 shape on the same issuer: no 6-month snapshot at 1405/03/31; «گزارش قبلی (۱۴۰۴/۱۲/۲۹)» offered.
                     ("AC-C13", "ترازنامه ساربیل را با فصل قبل مقایسه کن", "236", "previous_fiscal_quarter_end", null, null),
                     // AC-C06 missing match with an unchanged calendar: منجیل has no 9-month snapshot at 1404/06/31.
                     ("AC-C06", "ترازنامه منجیل در مقایسه با مدت مشابه سال قبل", "15763", "same_stage_previous_year", null, null)
                 })
            results.Add(await CheckComparison(sql, await Ask(message), message, company, kind, baselineDate, periodType, ac));

        report["results"] = results;
        // The checked-in evidence JSON is rewritten only on request; by default the run writes to the temp folder.
        var output = Path.Combine(Path.GetTempPath(), "feature154-live-development-verification.json");
        if (Environment.GetEnvironmentVariable("FEATURE154_LIVE_DEV_WRITE_EVIDENCE") == "1")
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "specs"))) directory = directory.Parent;
            output = Path.Combine(directory!.FullName, "specs", "154-ai-balance-sheet-report", "evidence", "live-development-verification.json");
        }

        await File.WriteAllTextAsync(output, report.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));
        Assert.All(results, item => Assert.NotEqual("FAIL-live", item!["result"]!.GetValue<string>()));
    }

    // ---------------------------------------------------------------- independent SQL (§5.4 ranking)

    private const string EligibleStandalone = """
        s."ProviderName" = 'NoavaranCurrentApi' AND s."StatementType" = 'BalanceSheet' AND s."IsComposing" = false
        AND s."PeriodType" IN ('ThreeMonths','SixMonths','NineMonths','TwelveMonths')
        AND (s."WarningsJson"::jsonb -> 0 ->> 'assumedScale') = 'MillionRials'
        AND COALESCE((s."WarningsJson"::jsonb -> 0 ->> 'isComposing')::boolean, false) = false
        """;

    private const string Ranking = """
        ORDER BY s."PeriodEnd" DESC, s."IsAudited" DESC, s."IsRepresented" DESC, s."PublishedAt" DESC NULLS LAST,
                 s."ExternalStatementId"::bigint DESC, s."Id" DESC
        LIMIT 1
        """;

    private static async Task<(Guid Id, string ExternalId, DateOnly PeriodEnd, string PeriodType)?> SelectSnapshot(
        NpgsqlConnection sql, string company, DateOnly? periodEnd = null, string? periodType = null)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT s."Id", s."ExternalStatementId", s."PeriodEnd", s."PeriodType" FROM "FinancialStatements" s
            WHERE {EligibleStandalone} AND s."ExternalCompanyId" = @company
              AND (@periodEnd::date IS NULL OR s."PeriodEnd" = @periodEnd::date)
              AND (@periodType::text IS NULL OR s."PeriodType" = @periodType::text)
            {Ranking}
            """, sql);
        command.Parameters.AddWithValue("company", company);
        command.Parameters.Add(new NpgsqlParameter("periodEnd", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)periodEnd ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("periodType", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)periodType ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetGuid(0), reader.GetString(1), DateOnly.FromDateTime(reader.GetDateTime(2)), reader.GetString(3));
    }

    private static async Task<Dictionary<int, decimal?>> StoredValues(NpgsqlConnection sql, Guid statementId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT c."SourceItemId", l."Value" FROM "FinancialStatementLineItems" l
            JOIN "FinancialStatementSourceItems" c ON c."Id" = l."SourceItemCatalogId"
            WHERE l."FinancialStatementId" = @id AND c."ProviderName" = 'NoavaranCurrentApi' AND c."StatementType" = 'BalanceSheet'
            """, sql);
        command.Parameters.AddWithValue("id", statementId);
        var values = new Dictionary<int, decimal?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
        return values;
    }

    // ---------------------------------------------------------------- payload helpers

    private static readonly Dictionary<string, int> RowSource = new()
    {
        ["cash"] = 1, ["bank_receivables_central_bank"] = 2, ["bank_receivables_banks"] = 4, ["bank_loans_granted"] = 291,
        ["bank_deposits"] = 223, ["bank_due_to_banks"] = 39, ["bank_treasury_shares"] = 170, ["current_assets_total"] = 135,
        ["non_current_assets_total"] = 137, ["total_assets"] = 138, ["current_liabilities_total"] = 139,
        ["non_current_liabilities_total"] = 140, ["total_liabilities"] = 143, ["share_capital"] = 57, ["retained_earnings"] = 71,
        ["total_equity"] = 147, ["total_liabilities_and_equity"] = 148, ["trade_receivables"] = 306, ["inventories"] = 18,
        ["orders_prepayments"] = 19, ["ppe"] = 21, ["trade_payables"] = 307, ["advances_received"] = 45,
        ["short_term_borrowings"] = 47, ["long_term_borrowings"] = 51, ["capital_increase_funds"] = 58, ["revaluation_surplus"] = 64,
        ["net_working_capital"] = 145, ["interest_bearing_liabilities"] = 144
    };

    private static IEnumerable<(JsonElement Row, string Group)> Rows(JsonElement snapshot)
    {
        foreach (var section in snapshot.GetProperty("sections").EnumerateArray())
        {
            foreach (var group in section.GetProperty("groups").EnumerateArray())
            {
                var groupId = $"{section.GetProperty("sectionId").GetString()}:{group.GetProperty("groupId").GetString()}";
                foreach (var row in group.GetProperty("rows").EnumerateArray()) yield return (row, groupId);
                if (group.GetProperty("subtotal").ValueKind == JsonValueKind.Object) yield return (group.GetProperty("subtotal"), groupId);
            }

            if (section.GetProperty("total").ValueKind == JsonValueKind.Object) yield return (section.GetProperty("total"), section.GetProperty("sectionId").GetString()!);
        }

        yield return (snapshot.GetProperty("finalTotals").GetProperty("totalAssets"), "final");
        yield return (snapshot.GetProperty("finalTotals").GetProperty("totalLiabilitiesAndEquity"), "final");
        foreach (var row in snapshot.GetProperty("analyticalItems").EnumerateArray()) yield return (row, "analytical");
    }

    private static decimal? CellValue(JsonElement row)
    {
        var cell = row.GetProperty("cell");
        return cell.GetProperty("state").GetString() == "available" && cell.GetProperty("value").ValueKind == JsonValueKind.Number
            ? cell.GetProperty("value").GetDecimal()
            : null;
    }

    private static string Text(JsonElement root) => root.GetProperty("textAnswer").GetString() ?? string.Empty;

    private static async Task<JsonObject> CheckDefault(NpgsqlConnection sql, JsonElement root, string message, string company,
        string template, string ac)
    {
        var failures = new List<string>();
        var bs = root.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        var expected = await SelectSnapshot(sql, company);
        var anchorId = bs.GetProperty("anchorStatementId").GetString();
        var record = new JsonObject
        {
            ["ac"] = ac, ["request"] = message, ["company"] = company,
            ["outcome"] = bs.GetProperty("outcome").GetString(), ["templateId"] = bs.GetProperty("templateId").GetString(),
            ["expectedAnchor"] = expected is null ? null : $"{expected.Value.ExternalId} {expected.Value.PeriodEnd:yyyy-MM-dd} {expected.Value.PeriodType} ({expected.Value.Id})",
            ["actualAnchorStatementId"] = anchorId
        };
        if (bs.GetProperty("outcome").GetString() != "available" || expected is null || anchorId is null)
        {
            record["result"] = "FAIL-live";
            record["failures"] = new JsonArray("report not available or no expected snapshot");
            return record;
        }

        if (!string.Equals(anchorId, expected.Value.Id.ToString("D"), StringComparison.Ordinal)) failures.Add($"anchor {anchorId} != expected {expected.Value.Id}");
        // F154-V-04: template selection rests on the classification substitute (LivePredicateClassification), so it is
        // labelled separately from the live values, placement and text.
        var templateMatches = bs.GetProperty("templateId").GetString() == template;
        if (!templateMatches) failures.Add($"template {bs.GetProperty("templateId").GetString()}");
        record["templateSelection"] = templateMatches ? "PASS-live (classification substituted)" : "FAIL-live";
        var snapshot = bs.GetProperty("snapshots").GetProperty(anchorId);
        var stored = await StoredValues(sql, expected.Value.Id);
        var rows = new JsonObject();
        foreach (var (row, group) in Rows(snapshot))
        {
            var rowId = row.GetProperty("rowId").GetString()!;
            var actual = CellValue(row);
            var storedValue = stored.TryGetValue(RowSource[rowId], out var value) ? value : null;
            rows[rowId] = new JsonObject { ["group"] = group, ["stored"] = storedValue, ["payload"] = actual };
            if (actual != storedValue) failures.Add($"{rowId}: payload {actual} != stored {storedValue}");
        }

        // PD-22 placement: only 47 (current) and 51 (non-current) are General subgroup details.
        foreach (var (row, group) in Rows(snapshot).Where(item => item.Group.EndsWith(":current", StringComparison.Ordinal) ||
                                                                 item.Group.EndsWith(":non_current", StringComparison.Ordinal)))
        {
            var rowId = row.GetProperty("rowId").GetString()!;
            if (row.GetProperty("kind").GetString() == "detail" && rowId is not ("short_term_borrowings" or "long_term_borrowings"))
                failures.Add($"{rowId} placed in {group}");
        }

        var assets = stored.GetValueOrDefault(138);
        var equity = stored.GetValueOrDefault(147);
        decimal? ratio = assets is > 0m && equity is { } e ? e / assets.Value * 100m : null;
        var payloadRatio = snapshot.GetProperty("ratio").GetProperty("value").ValueKind == JsonValueKind.Number
            ? snapshot.GetProperty("ratio").GetProperty("value").GetDecimal()
            : (decimal?)null;
        if (ratio is { } r && (payloadRatio is null || Math.Abs(r - payloadRatio.Value) > 0.000000000001m)) failures.Add($"ratio {payloadRatio} != {r}");
        var text = Text(root);
        if (assets is { } totalAssets && !text.Contains(ToPersianAmount(totalAssets), StringComparison.Ordinal)) failures.Add("total assets missing from the text answer");
        record["rows"] = rows;
        record["expectedRatio"] = ratio;
        record["payloadRatio"] = payloadRatio;
        record["textExcerpt"] = text.Length > 400 ? text[..400] : text;
        record["result"] = failures.Count == 0 ? "PASS-live" : "FAIL-live";
        record["failures"] = new JsonArray(failures.Select(item => (JsonNode)item).ToArray());
        return record;
    }

    // S-9 shape: the unavailable baseline names its own date and the previous report is offered with its date, not selected.
    private static readonly Dictionary<string, string[]> ExpectedUnavailableText = new(StringComparer.Ordinal)
    {
        ["ترازنامه ومعین را با فصل قبل مقایسه کن"] = ["«پایان فصل قبل (۱۴۰۴/۱۲/۲۹)» در دسترس نیست", "گزارش قبلی (۱۴۰۴/۰۹/۳۰)"],
        ["ترازنامه ساربیل را با فصل قبل مقایسه کن"] = ["«پایان فصل قبل (۱۴۰۵/۰۳/۳۱)» در دسترس نیست", "گزارش قبلی (۱۴۰۴/۱۲/۲۹)"],
        ["ترازنامه منجیل در مقایسه با مدت مشابه سال قبل"] = ["«مدت مشابه سال مالی قبل (۱۴۰۴/۰۶/۳۱)» در دسترس نیست"]
    };

    private static readonly Dictionary<string, string> ExpectedUnavailableReason = new(StringComparer.Ordinal)
    {
        ["ترازنامه بکهنوج در مقایسه با مدت مشابه سال قبل"] = "fiscal_calendar_changed",
        ["ترازنامه بکهنوج را با فصل قبل مقایسه کن"] = "fiscal_calendar_changed",
        ["ترازنامه ومعین را با فصل قبل مقایسه کن"] = "no_snapshot_at_date",
        ["ترازنامه ساربیل را با فصل قبل مقایسه کن"] = "no_snapshot_at_date",
        ["ترازنامه منجیل در مقایسه با مدت مشابه سال قبل"] = "no_matching_stage"
    };

    private static readonly HashSet<string> DistinctFromPreviousReport = new(StringComparer.Ordinal)
    {
        "ترازنامه ساربیل را با پایان سال مالی قبل مقایسه کن"
    };

    private static async Task<JsonObject> CheckComparison(NpgsqlConnection sql, JsonElement root, string message, string company,
        string kind, DateOnly? baselineDate, string? periodType, string ac)
    {
        var failures = new List<string>();
        var bs = root.GetProperty("financialStatementTableResult").GetProperty("balanceSheetReport");
        var record = new JsonObject { ["ac"] = ac, ["request"] = message, ["company"] = company, ["baselineKind"] = kind };
        if (bs.GetProperty("outcome").GetString() != "available")
        {
            record["result"] = "FAIL-live";
            record["failures"] = new JsonArray($"outcome {bs.GetProperty("outcome").GetString()}");
            return record;
        }

        var anchorId = bs.GetProperty("anchorStatementId").GetString()!;
        var expectedAnchor = await SelectSnapshot(sql, company);
        if (expectedAnchor is null || anchorId != expectedAnchor.Value.Id.ToString("D")) failures.Add($"anchor {anchorId} != expected {expectedAnchor?.Id}");
        if (bs.GetProperty("comparison").GetProperty("selectedBaselineKind").GetString() != kind) failures.Add("selected baseline kind differs");
        var candidate = bs.GetProperty("comparison").GetProperty("candidates").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == kind);
        record["candidateState"] = candidate.GetProperty("state").GetString();
        record["candidateReason"] = candidate.GetProperty("reason").ValueKind == JsonValueKind.String ? candidate.GetProperty("reason").GetString() : null;
        record["candidateDate"] = candidate.GetProperty("baselineDate").ValueKind == JsonValueKind.String ? candidate.GetProperty("baselineDate").GetString() : null;
        record["alternatives"] = new JsonArray(candidate.GetProperty("alternatives").EnumerateArray().Select(item => (JsonNode)item.GetString()!).ToArray());
        record["anchor"] = $"{expectedAnchor?.ExternalId} {expectedAnchor?.PeriodEnd:yyyy-MM-dd} {expectedAnchor?.PeriodType}";
        record["candidates"] = new JsonArray(bs.GetProperty("comparison").GetProperty("candidates").EnumerateArray()
            .Select(item => (JsonNode)($"{item.GetProperty("kind").GetString()}={item.GetProperty("state").GetString()}" +
                                       (item.GetProperty("baselineDate").ValueKind == JsonValueKind.String ? $"@{item.GetProperty("baselineDate").GetString()}" : "") +
                                       (item.GetProperty("reason").ValueKind == JsonValueKind.String ? $" ({item.GetProperty("reason").GetString()})" : ""))!)
            .ToArray());
        if (ExpectedUnavailableReason.TryGetValue(message, out var expectedReason) && record["candidateReason"]?.GetValue<string>() != expectedReason)
            failures.Add($"reason {record["candidateReason"]} != {expectedReason}");
        if (DistinctFromPreviousReport.Contains(message))
        {
            // AC-C03: the fiscal-year-end baseline is neither the Esfand date nor the previous report.
            var previous = bs.GetProperty("comparison").GetProperty("candidates").EnumerateArray()
                .Single(item => item.GetProperty("kind").GetString() == "previous_report");
            var previousDate = previous.GetProperty("baselineDate").GetString();
            record["previousReportDate"] = previousDate;
            if (previousDate == record["candidateDate"]?.GetValue<string>()) failures.Add("fiscal-year-end baseline equals the previous report");
            if (record["candidateDate"]?.GetValue<string>() == "2026-03-20") failures.Add("fiscal-year-end baseline is the Esfand date");
            if (previous.GetProperty("statementId").ValueKind == JsonValueKind.String &&
                previous.GetProperty("statementId").GetString() == candidate.GetProperty("statementId").GetString())
                failures.Add("fiscal-year-end baseline is the previous report's statement");
        }
        var text = Text(root);
        record["textExcerpt"] = text.Length > 300 ? text[..300] : text;
        if (text.Contains("فصل دوم", StringComparison.Ordinal) || text.Contains("عملکرد فصل", StringComparison.Ordinal) || text.Contains("به فصل قبل", StringComparison.Ordinal))
            failures.Add("quarter wording on a difference");

        if (baselineDate is null)
        {
            // Expected unavailable (transitional year): previous_report is offered as an alternative, never selected silently.
            if (candidate.GetProperty("state").GetString() != "unavailable") failures.Add("expected unavailable baseline");
            // Spec §8.2 / design §5: alternatives are the other available kinds; their order is not specified.
            if (!candidate.GetProperty("alternatives").EnumerateArray().Any(item => item.GetString() == "previous_report")) failures.Add("previous_report not offered");
            if (!text.Contains("گزارش قبلی", StringComparison.Ordinal)) failures.Add("text does not offer «گزارش قبلی»");
            foreach (var fragment in ExpectedUnavailableText.GetValueOrDefault(message, []))
                if (!text.Contains(fragment, StringComparison.Ordinal)) failures.Add($"text lacks «{fragment}»");
            record["textExcerpt"] = text.Length > 500 ? text[..500] : text;
            record["result"] = failures.Count == 0 ? "PASS-live" : "FAIL-live";
            record["failures"] = new JsonArray(failures.Select(item => (JsonNode)item).ToArray());
            return record;
        }

        var expectedBaseline = await SelectSnapshot(sql, company, baselineDate, periodType);
        if (expectedBaseline is null) failures.Add($"no stored snapshot at {baselineDate:yyyy-MM-dd}");
        if (candidate.GetProperty("state").GetString() != "available") failures.Add($"baseline {candidate.GetProperty("state").GetString()} ({record["candidateReason"]})");
        var baselineId = candidate.GetProperty("statementId").ValueKind == JsonValueKind.String ? candidate.GetProperty("statementId").GetString() : null;
        record["expectedBaseline"] = expectedBaseline is null ? null : $"{expectedBaseline.Value.ExternalId} {expectedBaseline.Value.PeriodEnd:yyyy-MM-dd} {expectedBaseline.Value.PeriodType} ({expectedBaseline.Value.Id})";
        record["actualBaselineStatementId"] = baselineId;
        if (expectedBaseline is { } eb && baselineId != eb.Id.ToString("D")) failures.Add($"baseline {baselineId} != expected {eb.Id}");
        if (expectedAnchor is { } ea && expectedBaseline is { } ebv && failures.Count == 0)
        {
            var anchorStored = await StoredValues(sql, ea.Id);
            var baselineStored = await StoredValues(sql, ebv.Id);
            var anchorSnapshot = bs.GetProperty("snapshots").GetProperty(anchorId);
            var baselineSnapshot = bs.GetProperty("snapshots").GetProperty(baselineId!);
            var baselineRows = Rows(baselineSnapshot).ToDictionary(item => item.Row.GetProperty("rowId").GetString()!, item => item.Row);
            var changes = candidate.GetProperty("rowChanges");
            var checkedRows = new JsonObject();
            foreach (var (row, _) in Rows(anchorSnapshot))
            {
                var rowId = row.GetProperty("rowId").GetString()!;
                var source = RowSource[rowId];
                decimal? a = anchorStored.GetValueOrDefault(source);
                decimal? b = baselineStored.GetValueOrDefault(source);
                var payloadA = CellValue(row);
                var payloadB = baselineRows.TryGetValue(rowId, out var baselineRow) ? CellValue(baselineRow) : null;
                if (payloadA != a) failures.Add($"{rowId}: anchor payload {payloadA} != stored {a}");
                if (baselineRows.ContainsKey(rowId) && payloadB != b) failures.Add($"{rowId}: baseline payload {payloadB} != stored {b}");
                var change = changes.GetProperty(rowId);
                decimal? amount = change.GetProperty("amount").ValueKind == JsonValueKind.Number ? change.GetProperty("amount").GetDecimal() : null;
                decimal? percent = change.GetProperty("percent").ValueKind == JsonValueKind.Number ? change.GetProperty("percent").GetDecimal() : null;
                if (payloadA is { } av && payloadB is { } bv)
                {
                    if (amount != av - bv) failures.Add($"{rowId}: change {amount} != {av - bv}");
                    if (bv > 0m && (percent is null || Math.Abs(percent.Value - (av - bv) / bv * 100m) > 0.0000000001m)) failures.Add($"{rowId}: percent {percent}");
                    if (bv <= 0m && percent is not null) failures.Add($"{rowId}: percent shown for non-positive baseline");
                }
                else if (amount is not null) failures.Add($"{rowId}: change without both values");
                checkedRows[rowId] = new JsonObject { ["anchor"] = a, ["baseline"] = b, ["amount"] = amount, ["percent"] = percent };
            }

            decimal? Ratio(Dictionary<int, decimal?> values) =>
                values.GetValueOrDefault(138) is > 0m && values.GetValueOrDefault(147) is { } e ? e / values[138]!.Value * 100m : null;
            var expectedPp = Ratio(anchorStored) - Ratio(baselineStored);
            var payloadPp = candidate.GetProperty("ratioChange").GetProperty("ppDifference").ValueKind == JsonValueKind.Number
                ? candidate.GetProperty("ratioChange").GetProperty("ppDifference").GetDecimal()
                : (decimal?)null;
            if (expectedPp is { } pp && (payloadPp is null || Math.Abs(pp - payloadPp.Value) > 0.0000000001m)) failures.Add($"pp {payloadPp} != {pp}");
            record["rows"] = checkedRows;
            record["expectedPp"] = expectedPp;
            record["payloadPp"] = payloadPp;
        }

        record["result"] = failures.Count == 0 ? "PASS-live" : "FAIL-live";
        record["failures"] = new JsonArray(failures.Select(item => (JsonNode)item).ToArray());
        return record;
    }

    private static string ToPersianAmount(decimal value)
    {
        var text = Math.Abs(value).ToString("#,##0.##", CultureInfo.InvariantCulture);
        var persian = string.Concat(text.Select(ch => ch is >= '0' and <= '9' ? (char)('۰' + ch - '0') : ch)).Replace(',', '٬');
        return value < 0 ? $"({persian})" : persian;
    }

    /// <summary>
    /// Test seam (documented in live-development-verification.md): the read-only role has no SELECT on the
    /// NoavaranEligibleCompanies view or IndustryGroups/Industries. Classification is read from "Companies" with the
    /// live deployed view predicate (4 MarketIds); the view's INNER JOINs are represented by non-null GroupId/IndustryId,
    /// which FK_Companies_IndustryGroups_GroupId and FK_Companies_Industries_IndustryId guarantee. Everything else is live.
    /// </summary>
    internal sealed class LivePredicateClassification(FinancialIngestionDbContext db, IFinancialStatementTableRepository tables)
        : IQuarterlyFinancialReportsRepository
    {
        public Task<IReadOnlyList<QuarterlyReportObservation>> GetIncomeStatementReportsAsync(QuarterlyFinancialReportSelection selection,
            CancellationToken cancellationToken = default) =>
            ((IQuarterlyFinancialReportsRepository)tables).GetIncomeStatementReportsAsync(selection, cancellationToken);

        public async Task<IReadOnlyList<Guid?>> GetEligibleCompanyGroupsAsync(Guid canonicalCompanyId, string providerName,
            string externalCompanyId, CancellationToken cancellationToken = default)
        {
            var markets = LivePredicateMarkets.Select(Guid.Parse).ToArray();
            return await db.Companies.AsNoTracking()
                .Where(row => row.Id == canonicalCompanyId && row.ProviderName == providerName && row.ExternalCompanyId == externalCompanyId &&
                              row.PrecedencyRight == 0 && row.MarketId != null && markets.Contains(row.MarketId.Value) &&
                              row.GroupId != null && row.IndustryId != null)
                .Select(row => row.GroupId)
                .ToListAsync(cancellationToken);
        }
    }
}

public sealed class BalanceSheetReport154LiveApiFactory(string readOnlyDevelopment, string disposable) : AiFacadeApiFactory
{
    public V2MonthlySalesRoutingFakeAiModelClient Fake { get; } = new();

    protected override bool ForceV1Orchestration => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiOrchestration:Mode"] = "MicrosoftAgentFrameworkV2",
            // Any Npgsql context not replaced below points at the disposable database, never at Development.
            ["ConnectionStrings:FinancialCopilot"] = disposable,
            ["RateLimiting:AuthenticatedActor:PermitLimit"] = "1000"
        }));
        builder.ConfigureTestServices(services =>
        {
            // Financial data: the read-only Development role, every session read-only.
            services.RemoveAll<FinancialIngestionDbContext>();
            services.RemoveAll<DbContextOptions<FinancialIngestionDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinancialIngestionDbContext>>();
            services.AddDbContext<FinancialIngestionDbContext>(options => options.UseNpgsql(readOnlyDevelopment));
            // Writable Npgsql contexts the harness does not already replace with in-memory stores: the disposable database.
            Replace<AuthDbContext>(services, disposable);
            Replace<SemanticCatalogDbContext>(services, disposable);
            Replace<FinancialCopilot.Infrastructure.Financial.Providers.Persistence.FinancialProviderDbContext>(services, disposable);
            services.RemoveAll<IQuarterlyFinancialReportsRepository>();
            services.AddScoped<IQuarterlyFinancialReportsRepository>(provider => new BalanceSheetReport154LiveDevelopmentTests.LivePredicateClassification(
                provider.GetRequiredService<FinancialIngestionDbContext>(), provider.GetRequiredService<IFinancialStatementTableRepository>()));
            services.RemoveAll<IAiModelClient>();
            services.AddSingleton<IAiModelClient>(Fake);
        });
    }

    private static void Replace<TContext>(IServiceCollection services, string connectionString) where TContext : DbContext
    {
        services.RemoveAll<TContext>();
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.AddDbContext<TContext>(options => options.UseNpgsql(connectionString));
    }
}
