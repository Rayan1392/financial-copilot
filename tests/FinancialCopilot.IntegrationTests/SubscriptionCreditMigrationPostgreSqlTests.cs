using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class SubscriptionCreditMigrationPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private const string PreLifecycleCorrectionMigration = "20261029150802_AddBillingReservationRequestFingerprint";

    [SkippableFact]
    public async Task LifecycleBackfill_PreservesHistoryAndBalance_RecordsHistoricalFreeAndSnapshotsAllowances()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync(
            "feature139_backfill",
            PreLifecycleCorrectionMigration);
        var accountId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var freePeriodId = Guid.NewGuid();
        var freeGrantId = Guid.NewGuid();
        var paidPeriodId = Guid.NewGuid();
        var paidGrantId = Guid.NewGuid();
        var usageId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var anchor = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var freeExpires = anchor.AddDays(30);
        var reservationExpires = anchor.AddMinutes(30);

        await using (var legacy = database.CreateBillingContext())
        {
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing_subscription_plans
                SET "IncludedCredits" = 25, "PricingPolicyVersion" = 'v1'
                WHERE "Code" = 'Free';
                """);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_customer_accounts
                    ("Id", "TenantId", "UserId", "AccountType", "BillingMode", "SubscriptionRevision", "ExternalSubscriptionManaged")
                VALUES ({accountId}, {tenantId}, {userId}, 'Individual', 'Prepaid', 0, FALSE);
                """);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_wallet_projections ("CustomerAccountId", "Balance", "ReservedAmount", "UpdatedAt", "Revision")
                VALUES ({accountId}, 47, 5, {anchor}, 4);
                """);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_usage_ledger_entries
                    ("Id", "CustomerAccountId", "ActorId", "TenantId", "EntryType", "OperationCode", "CreditsCharged", "PricingPolicyVersion", "IdempotencyKey", "OccurredAt")
                VALUES
                    ({freeGrantId}, {accountId}, {Guid.Empty}, {tenantId}, 'Adjustment', 'Billing.SubscriptionCreditGrant', 25, 'v1', 'legacy-free-grant', {anchor}),
                    ({paidGrantId}, {accountId}, {Guid.Empty}, {tenantId}, 'Adjustment', 'Billing.SubscriptionCreditGrant', 2600, 'legacy-pro-v1', 'legacy-paid-grant', {anchor.AddDays(1)}),
                    ({usageId}, {accountId}, {userId}, {tenantId}, 'Charge', 'AiQuery.Legacy', 18, 'legacy-usage-v1', 'legacy-usage-charge', {anchor.AddDays(2)});
                """);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing_usage_ledger_entries
                SET "SubscriptionCreditsApplied" = 18, "RelatedEntryId" = {freeGrantId}
                WHERE "Id" = {usageId};
                """);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_subscription_credit_periods
                    ("Id", "CustomerAccountId", "PlanCode", "SubscriptionIdentity", "IdempotencyKey", "AnchorAtUtc", "ActivationRevision", "PeriodNumber", "StartsAtUtc", "ExpiresAtUtc", "GrantedCredits", "RemainingCredits", "GrantLedgerEntryId", "Status", "CreatedAtUtc", "Revision")
                VALUES
                    ({freePeriodId}, {accountId}, 'Free', 'legacy:Free:0:1', 'legacy-free-period', {anchor}, 0, 0, {anchor}, {freeExpires}, 25, 7, {freeGrantId}, 'Expired', {anchor}, 1),
                    ({paidPeriodId}, {accountId}, 'Pro', 'legacy:Pro:0:2', 'legacy-paid-period', {anchor.AddDays(1)}, 0, 0, {anchor.AddDays(1)}, {anchor.AddDays(91)}, 2600, 0, {paidGrantId}, 'Expired', {anchor.AddDays(1)}, 1);
                """);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_usage_reservations
                    ("Id", "CustomerAccountId", "IdempotencyKey", "OperationCode", "ReservedCredits", "CreatedAt", "ExpiresAt", "Status")
                VALUES ({reservationId}, {accountId}, 'legacy-pending-reservation', 'AiQuery.LegacyPending', 5, {anchor.AddDays(3)}, {reservationExpires}, 'Reserved');
                """);
        }

        await using (var migration = database.CreateBillingContext())
            await migration.Database.MigrateAsync();

        await using var verify = database.CreateBillingContext();
        var wallet = await verify.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId);
        Assert.Equal(47m, wallet.Balance);
        Assert.Equal(5m, wallet.ReservedAmount);
        Assert.Equal(4, wallet.Revision);

        var freeEvidence = await verify.FreeCreditGrantEvidence.SingleAsync(row => row.UserId == userId);
        Assert.Equal(accountId, freeEvidence.CustomerAccountId);
        Assert.Equal(freeGrantId, freeEvidence.GrantLedgerEntryId);
        Assert.Equal(anchor, freeEvidence.GrantedAtUtc);
        Assert.Equal("historical-period", freeEvidence.EvidenceSource);
        Assert.Null(freeEvidence.AssignedExpiresAtUtc);

        var periods = await verify.SubscriptionCreditPeriods.Where(row => row.CustomerAccountId == accountId)
            .ToDictionaryAsync(row => row.Id);
        Assert.Equal(25m, periods[freePeriodId].GrantedCredits);
        Assert.Equal(7m, periods[freePeriodId].RemainingCredits);
        Assert.Equal(25m, periods[freePeriodId].AllowanceCredits);
        Assert.Equal(2600m, periods[paidPeriodId].AllowanceCredits);
        Assert.Equal(0m, periods[paidPeriodId].RemainingCredits);

        Assert.Equal(250m, await verify.SubscriptionPlans.Where(row => row.Code == "Free")
            .Select(row => row.IncludedCredits).SingleAsync());
        Assert.Equal(3, await verify.UsageLedgerEntries.CountAsync(row => row.CustomerAccountId == accountId));
        Assert.Equal(18m, await verify.UsageLedgerEntries.Where(row => row.Id == usageId)
            .Select(row => row.SubscriptionCreditsApplied).SingleAsync());

        var pending = await verify.UsageReservations.SingleAsync(row => row.Id == reservationId);
        Assert.Equal("Reserved", pending.Status);
        Assert.Equal(5m, pending.ReservedCredits);
        Assert.Empty(await verify.UsageCreditHolds.Where(row => row.UsageReservationId == reservationId).ToListAsync());
        Assert.Equal(0, await verify.UsageLedgerEntries.CountAsync(row =>
            row.CustomerAccountId == accountId && row.OperationCode.Contains("Backfill")));
    }
}
