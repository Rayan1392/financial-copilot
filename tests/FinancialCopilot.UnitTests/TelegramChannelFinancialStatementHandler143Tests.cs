using System.Reflection;
using System.Text.Json.Nodes;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramChannelFinancialStatementHandler143Tests
{
    [Fact]
    public async Task Telegram_delivery_schedules_fetch_until_configured_delay_and_suppresses_duplicates()
    {
        using var f = new Fixture(enabled: true);
        f.StatementOptions.InitialDelayMinutes = 7;

        Assert.Equal(TelegramAssistantResultStatus.Accepted, (await f.Call())?.Status);
        var checkpoint = f.Auth.TelegramProcessedUpdates.Single();
        Assert.Equal("Feature143:Scheduled", checkpoint.Status);
        Assert.Equal(0, f.ResolveCalls);
        Assert.Empty(f.Calls);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, (await f.Call())?.Status);
        Assert.False(await f.Handler.ProcessScheduledAsync(checkpoint.Id, default));

        f.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.False(await f.Handler.ProcessScheduledAsync(checkpoint.Id, default));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await f.Handler.ProcessScheduledAsync(checkpoint.Id, default));
        Assert.Equal(4, f.Calls.Count);
        Assert.False(await f.Handler.ProcessScheduledAsync(checkpoint.Id, default));
    }

    [Fact]
    public async Task Disabled_feature_records_skip_without_resolving_company_or_calling_provider()
    {
        using var f = new Fixture(enabled: false);
        var result = await f.Call();
        Assert.Equal(TelegramAssistantResultStatus.Accepted, result?.Status);
        Assert.Equal("Feature143:Disabled", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.Equal(0, f.ResolveCalls);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Annual_announcement_calls_each_existing_category_service_and_replays_completed_post()
    {
        using var f = new Fixture(enabled: true);
        var result = await f.CallAndFetch();
        var replay = await f.Call();
        Assert.Equal(TelegramAssistantResultStatus.Accepted, result?.Status);
        Assert.Equal(TelegramAssistantResultStatus.Replayed, replay?.Status);
        Assert.Equal(4, f.Calls.Count);
        Assert.Equal(new[]
        {
            ManualNoavaranFinancialStatementType.BalanceSheet,
            ManualNoavaranFinancialStatementType.IncomeStatement,
            ManualNoavaranFinancialStatementType.CashFlow,
            ManualNoavaranFinancialStatementType.FinancialRatios
        }, f.Calls.Select(x => x.StatementType));
        Assert.All(f.Calls, x => Assert.Equal(("19", 1403, 12), (x.ExternalCompanyId, x.FiscalYearEnding, x.PeriodMonths)));
        Assert.Equal("Feature143:Completed", f.Auth.TelegramProcessedUpdates.Single().Status);
    }

    [Fact]
    public async Task Partial_retry_reuses_successful_category_checkpoints_and_retries_only_failure()
    {
        using var f = new Fixture(enabled: true);
        f.FailIncomeOnce = true;
        await f.CallAndFetch();
        Assert.Equal("Feature143:Partial", f.Auth.TelegramProcessedUpdates.Single().Status);
        await f.Call();
        Assert.Equal(5, f.Calls.Count);
        Assert.Equal(2, f.Calls.Count(x => x.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement));
        Assert.Equal(1, f.Calls.Count(x => x.StatementType == ManualNoavaranFinancialStatementType.BalanceSheet));
        Assert.Equal("Feature143:Completed", f.Auth.TelegramProcessedUpdates.Single().Status);
    }

    [Fact]
    public async Task Six_month_income_with_fewer_approved_items_than_matching_three_month_report_is_not_completed()
    {
        using var f = new Fixture(enabled: true, interim: true, resolveInterimFiscal: true);
        f.SeedComparableThreeMonthIncome();
        f.PartialIncomeFirst = true;

        await f.CallAndFetch();

        Assert.Equal("Feature143:Partial", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.Equal(1, f.Calls.Count(x => x.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement));
    }

    [Fact]
    public async Task Reconciliation_refetches_fuller_income_once_and_completes_same_checkpoint()
    {
        using var f = new Fixture(enabled: true, interim: true, resolveInterimFiscal: true);
        f.SeedComparableThreeMonthIncome();
        f.PartialIncomeFirst = true;
        await f.CallAndFetch();
        var checkpoint = f.Auth.TelegramProcessedUpdates.Single();

        Assert.False(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));
        f.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));

        Assert.Equal("Feature143:Completed", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.Equal(2, f.Calls.Count(x => x.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement));
        var income = f.Financial.FinancialStatements.Single(x => x.ExternalStatementId == "partial-income");
        var lines = f.Financial.FinancialStatementLineItems.Where(x => x.FinancialStatementId == income.Id).ToArray();
        Assert.Equal(15, lines.Length);
        Assert.Contains(lines, x => x.Value == 0);
        Assert.False(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));
    }

    [Fact]
    public async Task Reconciliation_stops_for_review_after_two_still_sparse_refetches()
    {
        using var f = new Fixture(enabled: true, interim: true, resolveInterimFiscal: true);
        f.SeedComparableThreeMonthIncome();
        f.PartialIncomeFirst = true;
        f.KeepPartialIncome = true;
        await f.CallAndFetch();
        var checkpoint = f.Auth.TelegramProcessedUpdates.Single();

        f.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));
        Assert.Equal("Feature143:Partial", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.False(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));
        f.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));

        Assert.Equal("Feature143:ReviewRequired", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.Equal(3, f.Calls.Count(x => x.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement));
        Assert.False(await f.Handler.ReconcilePendingAsync(checkpoint.Id, default));
    }

    [Fact]
    public async Task Interim_announcement_without_exact_fiscal_evidence_is_skipped()
    {
        using var f = new Fixture(enabled: true, interim: true);
        var result = await f.CallAndFetch();
        Assert.Equal(TelegramAssistantResultStatus.Accepted, result?.Status);
        Assert.Equal("Feature143:Unresolved", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Interim_announcement_uses_persisted_fiscal_year_end_evidence()
    {
        using var f = new Fixture(enabled: true, interim: true, resolveInterimFiscal: true);
        await f.CallAndFetch();
        Assert.Equal(4, f.Calls.Count);
        Assert.All(f.Calls, x => Assert.Equal((1405, 6), (x.FiscalYearEnding, x.PeriodMonths)));
    }

    [Fact]
    public async Task Thamid_nine_month_announcement_uses_prior_three_and_six_month_fiscal_evidence()
    {
        using var f = new Fixture(enabled: true, interim: true, seedEarlierStatements: true);
        f.SetText("""
            توسعه وعمران امید(#ثامید)
            اطلاعات و صورت‌های مالی میاندوره‌ای دوره 9 ماهه منتهی به 1405/06/31 (حسابرسی نشده)

            @NoavaranAmin

            ✅ افزایش 18 درصدی درآمدهای عملیاتی 9 ماهه 1405 در مقایسه با دوره مشابه

            تحلیل بیشتر در:
            @NoavaranCodal
            """, 9);

        await f.CallAndFetch();

        Assert.Equal(4, f.Calls.Count);
        Assert.All(f.Calls, call => Assert.Equal(("19", 1405, 9),
            (call.ExternalCompanyId, call.FiscalYearEnding, call.PeriodMonths)));
        var reportingEnd = Jalali(1405, 6, 31);
        Assert.Equal(3, await f.Financial.FinancialStatements.CountAsync(x =>
            x.ExternalCompanyId == "19" && x.PeriodEnd == reportingEnd && x.PeriodType == "NineMonths"));
        Assert.Equal("Feature143:Completed", f.Auth.TelegramProcessedUpdates.Single().Status);

        await f.Call();
        Assert.Equal(4, f.Calls.Count);
    }

    [Fact]
    public async Task Targeted_recovery_reprocesses_only_a_stored_unresolved_announcement()
    {
        using var f = new Fixture(enabled: true, interim: true);
        await f.CallAndFetch();
        Assert.Equal("Feature143:Unresolved", f.Auth.TelegramProcessedUpdates.Single().Status);
        Assert.Empty(f.Calls);
        await f.Call();
        Assert.Empty(f.Calls);

        var skipped = f.Auth.TelegramProcessedUpdates.Single();
        var checkpointJson = JsonNode.Parse(skipped.ResponseJson)!;
        checkpointJson["BalanceSheet"] = JsonNode.Parse("""{"outcome":"Persisted","processedRecords":2,"attempts":1}""");
        skipped.ResponseJson = checkpointJson.ToJsonString();
        f.Auth.SaveChanges();

        var end = Jalali(1405, 3, 31);
        f.Financial.FinancialStatements.Add(new NormalizedFinancialStatementRow
        {
            Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName,
            ExternalCompanyId = "19", ExternalStatementId = "recovery-evidence",
            StatementType = "BalanceSheet", PeriodType = "SixMonths", PeriodStart = end.AddDays(-180), PeriodEnd = end,
            WarningsJson = "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]", SourcePayloadChecksum = "recovery"
        });
        f.Financial.SaveChanges();

        var recovery = await f.Handler.RecoverSkippedAsync(-100123, 20, default);

        Assert.Equal("Recovered", recovery.Code);
        Assert.Equal("Feature143:Completed", recovery.Status);
        Assert.Equal(3, f.Calls.Count);
        Assert.DoesNotContain(f.Calls, x => x.StatementType == ManualNoavaranFinancialStatementType.BalanceSheet);
    }

    [Fact]
    public async Task Targeted_recovery_never_replays_completed_or_in_flight_checkpoints()
    {
        using var f = new Fixture(enabled: true);
        await f.CallAndFetch();
        var completed = await f.Handler.RecoverSkippedAsync(-100123, 20, default);
        Assert.Equal("CheckpointNotEligible", completed.Code);
        Assert.Equal(4, f.Calls.Count);

        f.Auth.TelegramProcessedUpdates.Single().Status = "Feature143:Processing";
        f.Auth.SaveChanges();
        var inFlight = await f.Handler.RecoverSkippedAsync(-100123, 20, default);
        Assert.Equal("CheckpointNotEligible", inFlight.Code);
        Assert.Equal(4, f.Calls.Count);
    }

    [Fact]
    public void Fiscal_year_resolver_uses_only_candidates_applicable_to_the_target_period()
    {
        var result = NoavaranFiscalYearEvidenceResolver.Resolve(Jalali(1405, 6, 31), null,
        [
            "[{\"jalaliFiscalYearEnd\":\"1404/12/29\"}]",
            "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]"
        ]);
        Assert.Equal("1405/12/29", result);
    }

    [Fact]
    public void Fiscal_year_resolver_rejects_ambiguous_or_missing_evidence()
    {
        Assert.Null(NoavaranFiscalYearEvidenceResolver.Resolve(Jalali(1405, 6, 31), null,
        [
            "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]",
            "[{\"jalaliFiscalYearEnd\":\"1405/09/30\"}]"
        ]));
        Assert.Null(NoavaranFiscalYearEvidenceResolver.Resolve(Jalali(1405, 6, 31), null, []));
    }

    [Fact]
    public void Fiscal_year_resolver_supports_non_esfand_ends_and_persian_leap_years()
    {
        Assert.Equal("1405/06/31", NoavaranFiscalYearEvidenceResolver.Resolve(
            Jalali(1405, 3, 31), null, ["[{\"jalaliFiscalYearEnd\":\"1405/06/31\"}] "]));
        Assert.Equal("1403/12/30", NoavaranFiscalYearEvidenceResolver.Resolve(
            Jalali(1403, 12, 30), null, ["[{\"jalaliFiscalYearEnd\":\"1403/12/30\"}] "]));
    }

    private static DateOnly Jalali(int year, int month, int day) =>
        DateOnly.FromDateTime(new System.Globalization.PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0));

    [Fact]
    public async Task Unauthorized_channel_is_rejected_before_durable_claim_or_company_lookup()
    {
        using var f = new Fixture(enabled: true);
        f.ChannelOptions.AllowedChannelIds = ["-100999"];
        var result = await f.Call();
        Assert.Equal(TelegramAssistantResultStatus.Unsupported, result?.Status);
        Assert.Empty(f.Auth.TelegramProcessedUpdates);
        Assert.Equal(0, f.ResolveCalls);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Empty_channel_allowlist_keeps_financial_ingestion_closed()
    {
        using var f = new Fixture(enabled: true);
        f.ChannelOptions.AllowedChannelIds = [];
        var result = await f.Call();
        Assert.Equal(TelegramAssistantResultStatus.Unsupported, result?.Status);
        Assert.Empty(f.Auth.TelegramProcessedUpdates);
        Assert.Empty(f.Calls);
    }

    [Fact]
    public async Task Malformed_mixed_announcement_is_consumed_without_monthly_or_financial_work()
    {
        using var f = new Fixture(enabled: true);
        f.SetText("#نماد #فعالیت_ماهانه\nصورت های مالی دوره 6 ماهه منتهی به 1405/06/31 و 1405/09/30");
        var result = await f.Call();
        Assert.Equal(TelegramAssistantResultStatus.Accepted, result?.Status);
        Assert.Empty(f.Auth.TelegramProcessedUpdates);
        Assert.Equal(0, f.ResolveCalls);
        Assert.Empty(f.Calls);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public AuthDbContext Auth { get; }
        private readonly FinancialIngestionDbContext financial = new(new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly CurrentActor actor = new(ActorType.ApiClient, Guid.NewGuid(), Guid.NewGuid(), AuthenticationMode.ApiClient, ApiClientId: Guid.NewGuid());
        private TelegramAssistantUpdate update;
        private readonly bool interim;
        private int interimPeriodMonths = 6;
        public int ResolveCalls;
        public TelegramChannelMonthlyReportOptions ChannelOptions { get; } = new()
        {
            AutoBackfillEnabled = true,
            AllowedChannelIds = ["-100123"]
        };
        public bool FailIncomeOnce;
        public bool PartialIncomeFirst;
        public bool KeepPartialIncome;
        public AdjustableTimeProvider Clock { get; } = new();
        public List<ManualNoavaranFinancialStatementBackfillRequest> Calls { get; } = [];
        private readonly TelegramChannelFinancialStatementHandler handler;
        public TelegramChannelFinancialStatementOptions StatementOptions { get; }
        public TelegramChannelFinancialStatementHandler Handler => handler;

        public Fixture(bool enabled, bool interim = false, bool resolveInterimFiscal = false, bool seedEarlierStatements = false)
        {
            this.interim = interim;
            connection.Open();
            Auth = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>().UseSqlite(connection).Options);
            Auth.Database.EnsureCreated();
            Auth.Tenants.Add(new TenantRow { Id = actor.TenantId, Name = "test" });
            Auth.SaveChanges();
            financial.NoavaranEligibleCompanies.Add(new NoavaranEligibleCompanyRow
                { Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalCompanyId = "19", Name = "sample" });
            if (resolveInterimFiscal)
            {
                var reportEnd = DateOnly.FromDateTime(new System.Globalization.PersianCalendar().ToDateTime(1405, 6, 31, 0, 0, 0, 0));
                financial.FinancialStatements.Add(new NormalizedFinancialStatementRow
                {
                    Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalCompanyId = "19",
                    ExternalStatementId = "existing", StatementType = "BalanceSheet", PeriodType = "SixMonths",
                    PeriodStart = reportEnd.AddDays(-180), PeriodEnd = reportEnd, WarningsJson = "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]"
                });
            }
            if (seedEarlierStatements)
            {
                var calendar = new System.Globalization.PersianCalendar();
                foreach (var (year, month, day, months) in new[] { (1404, 12, 29, 3), (1405, 3, 31, 6) })
                {
                    var end = DateOnly.FromDateTime(calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
                    financial.FinancialStatements.Add(new NormalizedFinancialStatementRow
                    {
                        Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName,
                        ExternalCompanyId = "19", ExternalStatementId = $"thamid-{months}m",
                        StatementType = "BalanceSheet", PeriodType = months == 3 ? "ThreeMonths" : "SixMonths",
                        PeriodStart = end.AddDays(-90 * months), PeriodEnd = end,
                        IsAudited = false, IsComposing = false,
                        WarningsJson = "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]",
                        SourcePayloadChecksum = $"thamid-{months}m-checksum", LastSynchronizedAt = DateTimeOffset.UtcNow
                    });
                }
            }
            financial.SaveChanges();
            update = new TelegramAssistantUpdate(10, TelegramAssistantUpdateKind.ChannelPost, 0, -100123, null, 20,
                null, null, interim
                    ? "شرکت(#نماد)\nاطلاعات و صورت های مالی میاندوره ای دوره 6 ماهه منتهی به 1405/06/31 (حسابرسی نشده)"
                    : "قند لرستان(#قلرست)\nصورت های مالی سال مالی منتهی به 1403/12/30 (حسابرسی نشده)",
                "fa-IR", DateTimeOffset.UtcNow, "test-correlation", "channel");
            var companies = Stub<ICompanyResolverService>(_ =>
            {
                ResolveCalls++;
                return Task.FromResult<ResolvedCompany?>(new(Guid.NewGuid(), "19", "قلرست", null, null, null, null, "قلرست"));
            });
            var backfill = Stub<IManualNoavaranFinancialStatementBackfillService>(args =>
            {
                var request = (ManualNoavaranFinancialStatementBackfillRequest)args[0]!;
                Calls.Add(request);
                if (FailIncomeOnce && request.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement)
                {
                    FailIncomeOnce = false;
                    return Task.FromException<ManualNoavaranFinancialStatementBackfillResult>(new InvalidOperationException("temporary provider failure"));
                }
                if (PartialIncomeFirst && request.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement)
                {
                    var end = Jalali(1405, 6, 31);
                    var firstResponse = KeepPartialIncome ||
                        Calls.Count(x => x.StatementType == ManualNoavaranFinancialStatementType.IncomeStatement) == 1;
                    var statement = financial.FinancialStatements.SingleOrDefault(x => x.ExternalStatementId == "partial-income");
                    if (statement is null)
                    {
                        statement = new NormalizedFinancialStatementRow
                        {
                            Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName,
                            ExternalCompanyId = "19", ExternalStatementId = "partial-income",
                            StatementType = "IncomeStatement", PeriodType = "SixMonths",
                            PeriodStart = Jalali(1405, 1, 1), PeriodEnd = end,
                            IsAudited = false, IsComposing = false,
                            WarningsJson = "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]",
                            SourcePayloadChecksum = "partial-income", LastSynchronizedAt = DateTimeOffset.UtcNow
                        };
                        financial.FinancialStatements.Add(statement);
                    }
                    statement.SourcePayloadChecksum = firstResponse ? "partial-income" : "full-income";
                    foreach (var mapping in QuarterlyFinancialReportsNormalizer.ApprovedMappings.Where(x =>
                                 !firstResponse || x.SourceItemId is 15 or 1 or 5 or 39 or 12 or 40 or 334 or 278 or 153))
                    {
                        var catalogId = Guid.Parse(mapping.CatalogId);
                        if (financial.FinancialStatementLineItems.Any(x => x.FinancialStatementId == statement.Id && x.SourceItemCatalogId == catalogId)) continue;
                        financial.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                        {
                            Id = Guid.NewGuid(), FinancialStatementId = statement.Id,
                            SourceItemCatalogId = catalogId, Value = mapping.SourceItemId == 153 ? 0 : 1
                        });
                    }
                    var partialRun = Guid.NewGuid();
                    financial.SyncRuns.Add(new DataSyncRunRow
                    {
                        Id = partialRun, IdempotencyKey = partialRun.ToString(), Dataset = "FinancialStatements",
                        ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalReference = "19",
                        Status = "Completed", ProcessedRecords = 1, SourcePayloadChecksum = statement.SourcePayloadChecksum,
                        RequestedAt = DateTimeOffset.UtcNow
                    });
                    financial.SaveChanges();
                    return Task.FromResult(new ManualNoavaranFinancialStatementBackfillResult(partialRun, "Persisted", 1));
                }
                var periodEnd = interim
                    ? DateOnly.FromDateTime(new System.Globalization.PersianCalendar().ToDateTime(1405, 6, 31, 0, 0, 0, 0))
                    : DateOnly.FromDateTime(new System.Globalization.PersianCalendar().ToDateTime(1403, 12, 30, 0, 0, 0, 0));
                var fiscalYearEndText = interim ? "1405/12/29" : "1403/12/30";
                if (request.StatementType == ManualNoavaranFinancialStatementType.FinancialRatios)
                    financial.DerivedMetrics.Add(new DerivedMetricRow
                    {
                        Id = Guid.NewGuid(), ExternalCompanyId = "19", MetricCode = "EPS", MetricVersion = "v1",
                        CalculationPolicyVersion = "test", PeriodType = interim ? interimPeriodMonths switch { 3 => "ThreeMonths", 6 => "SixMonths", 9 => "NineMonths", _ => "TwelveMonths" } : "TwelveMonths", PeriodStart = periodEnd.AddDays(-365),
                        PeriodEnd = periodEnd, Unit = "Rial", ObservedAt = DateTimeOffset.UtcNow,
                        LastSynchronizedAt = DateTimeOffset.UtcNow, SourceEvidenceJson = $"[{{\"isAudited\":false,\"jalaliFiscalYearEnd\":\"{fiscalYearEndText}\"}}]"
                    });
                else
                    financial.FinancialStatements.Add(new NormalizedFinancialStatementRow
                    {
                        Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalCompanyId = "19",
                        ExternalStatementId = Guid.NewGuid().ToString(), StatementType = request.StatementType.ToString(),
                        PeriodType = interim ? interimPeriodMonths switch { 3 => "ThreeMonths", 6 => "SixMonths", 9 => "NineMonths", _ => "TwelveMonths" } : "TwelveMonths",
                        PeriodStart = periodEnd.AddDays(-365), PeriodEnd = periodEnd,
                        IsAudited = false, IsComposing = false, WarningsJson = $"[{{\"jalaliFiscalYearEnd\":\"{fiscalYearEndText}\"}}]", SourcePayloadChecksum = "x",
                        LastSynchronizedAt = DateTimeOffset.UtcNow
                    });
                var runId = Guid.NewGuid();
                financial.SyncRuns.Add(new DataSyncRunRow
                {
                    Id = runId, IdempotencyKey = runId.ToString(),
                    Dataset = request.StatementType == ManualNoavaranFinancialStatementType.FinancialRatios ? "FundamentalIndexes" : "FinancialStatements",
                    ProviderName = ProviderSources.NoavaranCurrentApiName, ExternalReference = "19", Status = "Completed",
                    ProcessedRecords = 2, SourcePayloadChecksum = "x", RequestedAt = DateTimeOffset.UtcNow
                });
                financial.SaveChanges();
                return Task.FromResult(new ManualNoavaranFinancialStatementBackfillResult(runId, "Persisted", 2));
            });
            var logs = new List<string>();
            StatementOptions = new TelegramChannelFinancialStatementOptions { AutoBackfillEnabled = enabled };
            handler = new TelegramChannelFinancialStatementHandler(Auth, financial, companies, backfill,
                new TelegramFinancialStatementRecognizer(),
                Options.Create(ChannelOptions),
                Options.Create(StatementOptions),
                Clock, new RecordingLogger(logs));
        }

        public FinancialIngestionDbContext Financial => financial;
        public void SeedComparableThreeMonthIncome()
        {
            var prior = new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(), ProviderName = ProviderSources.NoavaranCurrentApiName,
                ExternalCompanyId = "19", ExternalStatementId = "prior-income",
                StatementType = "IncomeStatement", PeriodType = "ThreeMonths",
                PeriodStart = Jalali(1405, 1, 1), PeriodEnd = Jalali(1405, 3, 31),
                IsAudited = false, IsComposing = false,
                WarningsJson = "[{\"jalaliFiscalYearEnd\":\"1405/12/29\"}]",
                SourcePayloadChecksum = "prior-income", LastSynchronizedAt = DateTimeOffset.UtcNow
            };
            financial.FinancialStatements.Add(prior);
            foreach (var mapping in QuarterlyFinancialReportsNormalizer.ApprovedMappings)
            {
                var catalogId = Guid.Parse(mapping.CatalogId);
                financial.FinancialStatementSourceItems.Add(new FinancialStatementSourceItemCatalogRow
                {
                    Id = catalogId, ProviderName = ProviderSources.NoavaranCurrentApiName,
                    StatementType = "IncomeStatement", SourceItemId = mapping.SourceItemId
                });
                financial.FinancialStatementLineItems.Add(new NormalizedFinancialStatementLineItemRow
                {
                    Id = Guid.NewGuid(), FinancialStatementId = prior.Id,
                    SourceItemCatalogId = catalogId, Value = mapping.SourceItemId == 153 ? 0 : 1
                });
            }
            financial.SaveChanges();
        }
        public Task<TelegramAssistantResult?> Call() => handler.HandleIfRecognizedAsync(update, actor, default);
        public async Task<TelegramAssistantResult?> CallAndFetch()
        {
            var result = await Call();
            if (Auth.TelegramProcessedUpdates.SingleOrDefault()?.Status == "Feature143:Scheduled")
            {
                Clock.Advance(TimeSpan.FromMinutes(StatementOptions.InitialDelayMinutes));
                Assert.True(await handler.ProcessScheduledAsync(Auth.TelegramProcessedUpdates.Single().Id, default));
            }
            return result;
        }
        public void SetText(string text, int? periodMonths = null)
        {
            update = update with { Text = text };
            if (periodMonths is not null) interimPeriodMonths = periodMonths.Value;
        }
        public void Dispose() { Auth.Dispose(); financial.Dispose(); connection.Dispose(); }
    }

    public class ServiceStub : DispatchProxy
    {
        public Func<object?[], object?> InvokeMethod { get; set; } = _ => throw new InvalidOperationException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(args ?? []);
    }

    private static T Stub<T>(Func<object?[], object?> func) where T : class
    {
        var stub = DispatchProxy.Create<T, ServiceStub>();
        ((ServiceStub)(object)stub).InvokeMethod = func;
        return stub;
    }

    private sealed class RecordingLogger(List<string> logs) : ILogger<TelegramChannelFinancialStatementHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => logs.Add(formatter(state, exception));
    }

    public sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
