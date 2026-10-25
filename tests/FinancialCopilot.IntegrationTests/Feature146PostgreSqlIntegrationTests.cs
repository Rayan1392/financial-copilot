using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.API.Controllers;
using FinancialCopilot.API.Security;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Administration;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FinancialCopilot.IntegrationTests;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class Feature146PostgreSqlIntegrationTests(PostgreSqlIntegrationFixture fixture)
{
    [SkippableFact]
    public async Task Auth_migration_backfills_unknown_without_inferred_source_and_adds_query_indexes()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using var auth = database.CreateAuthContext();
        var previousMigration = auth.Database.GetMigrations()
            .Last(migration => !migration.EndsWith("_Feature146UserOperationalMetadata", StringComparison.Ordinal));
        await auth.Database.MigrateAsync(previousMigration);

        var tenantId = Guid.NewGuid();
        var user = NewUser("historical@example.test");
        auth.Tenants.Add(new TenantRow { Id = tenantId, Name = "Historical tenant" });
        auth.Users.Add(user);
        auth.UserTenants.Add(new UserTenantRow { TenantId = tenantId, UserId = user.Id, IsDefault = true });
        await auth.SaveChangesAsync();

        await auth.Database.MigrateAsync();
        var metadata = await auth.UserOperationalMetadata.SingleAsync(row => row.UserId == user.Id);
        Assert.Equal(UserOperationalMetadataCodes.Unknown, metadata.AcquisitionSourceCode);
        Assert.Null(metadata.RegisteredAtUtc);
        Assert.Null(metadata.LastSuccessfulLoginAtUtc);
        Assert.Null(metadata.LastLoginProviderCode);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'auth_user_operational_metadata'";
        var indexNames = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) indexNames.Add(reader.GetString(0));
        }
        Assert.Contains(indexNames, name => name.Contains("RegisteredAtUtc_UserId", StringComparison.Ordinal));
        Assert.Contains(indexNames, name => name.Contains("AcquisitionSourceCode_RegisteredAtUtc_UserId", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task Directory_filters_and_keyset_pagination_are_tenant_scoped_and_use_batched_queries()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using (var migrationContext = database.CreateAuthContext())
            await migrationContext.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var registeredAt = DateTimeOffset.UtcNow.AddDays(-1.5);
        var lastLoginAt = DateTimeOffset.UtcNow.AddHours(-4);
        var expectedIds = new List<Guid>();
        await using (var seed = database.CreateAuthContext())
        {
            seed.Tenants.AddRange(
                new TenantRow { Id = tenantId, Name = "Directory tenant" },
                new TenantRow { Id = otherTenantId, Name = "Other tenant" });

            for (var index = 0; index < 24; index++)
            {
                var user = NewUser($"customer-{index:D2}@example.test");
                var enabled = index % 5 != 0;
                AddMember(seed, tenantId, user, UserOperationalMetadataCodes.Native, registeredAt, lastLoginAt, enabled);
                if (index % 2 == 0)
                {
                    var isLinked = index != 2;
                    seed.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow
                    {
                        Id = Guid.NewGuid(),
                        Provider = ExternalUserId.Provider,
                        ExternalUserId = $"feature146-external-{index:D2}",
                        LocalUserId = user.Id,
                        TenantId = tenantId,
                        Status = isLinked ? "Active" : "Revoked",
                        LinkedAtUtc = registeredAt
                    });
                    if (enabled && isLinked) expectedIds.Add(user.Id);
                }
            }

            var otherTenantUser = NewUser("customer-other-tenant@example.test");
            AddMember(seed, otherTenantId, otherTenantUser, UserOperationalMetadataCodes.Native, registeredAt, lastLoginAt);
            seed.TahlilAppExternalIdentities.Add(new TahlilAppExternalIdentityRow
            {
                Id = Guid.NewGuid(),
                Provider = ExternalUserId.Provider,
                ExternalUserId = "feature146-external-other-tenant",
                LocalUserId = otherTenantUser.Id,
                TenantId = otherTenantId,
                Status = "Active",
                LinkedAtUtc = registeredAt
            });
            await seed.SaveChangesAsync();
        }

        Assert.Equal(8, expectedIds.Count);
        var queryCount = new CountingCommandInterceptor();
        await using var auth = database.CreateAuthContext(queryCount);
        await using var billing = database.CreateBillingContext();
        var queries = CreateQueries(auth, billing);
        var filter = new AdminUserDirectoryQuery(
            PageSize: 2,
            Search: "customer-",
            Status: "enabled",
            AcquisitionSource: UserOperationalMetadataCodes.Native,
            LinkedProvider: "tahlilapp",
            ExternalProvider: "tahlilapp",
            ExternalUserId: "feature146-external-",
            RegisteredFromUtc: DateTimeOffset.UtcNow.AddDays(-2),
            RegisteredToUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastLoginFromUtc: DateTimeOffset.UtcNow.AddDays(-1),
            LastLoginToUtc: DateTimeOffset.UtcNow);

        var firstPage = await queries.GetDirectoryAsync(tenantId, filter, false, false, false, CancellationToken.None);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.True(queryCount.ReaderCount <= 4, $"Expected one tenant page query plus batched identity lookups; saw {queryCount.ReaderCount} SQL reader commands.");
        await Assert.ThrowsAsync<AdminManagementException>(() => queries.GetDirectoryAsync(
            tenantId,
            filter with { Status = "disabled", Cursor = firstPage.NextCursor },
            false,
            false,
            false,
            CancellationToken.None));

        var allPages = firstPage.Items.ToList();
        var cursor = firstPage.NextCursor;
        for (var pageNumber = 0; cursor is not null && pageNumber < 10; pageNumber++)
        {
            var page = await queries.GetDirectoryAsync(tenantId, filter with { Cursor = cursor }, false, false, false, CancellationToken.None);
            allPages.AddRange(page.Items);
            cursor = page.NextCursor;
        }

        Assert.Null(cursor);
        Assert.Equal(expectedIds.Count, allPages.Count);
        Assert.Equal(allPages.Count, allPages.Select(item => item.UserId).Distinct().Count());
        Assert.All(allPages, item => Assert.Contains(item.UserId, expectedIds));
        Assert.All(allPages, item =>
        {
            Assert.Equal(UserOperationalMetadataCodes.Native, item.AcquisitionSourceCode);
            Assert.Equal("not_authorized", item.Subscription.Status);
            Assert.Null(item.Subscription.Data);
            Assert.Equal("not_authorized", item.Credits.Status);
            Assert.Null(item.Credits.Data);
        });

        var fullPage = await queries.GetDirectoryAsync(tenantId, filter with { PageSize = 100, Cursor = null }, false, false, false, CancellationToken.None);
        Assert.Equal(fullPage.Items.Select(item => item.UserId), allPages.Select(item => item.UserId));
        var otherTenantPage = await queries.GetDirectoryAsync(otherTenantId, filter with { PageSize = 100, Cursor = null }, false, false, false, CancellationToken.None);
        Assert.Single(otherTenantPage.Items);
        Assert.Equal("not_authorized", otherTenantPage.Items.Single().Subscription.Status);
    }

    [SkippableFact]
    public async Task Customer360_applies_section_permissions_tenant_isolation_and_active_refresh_session_rules()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using (var migrationContext = database.CreateAuthContext())
            await migrationContext.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var user = NewUser("customer-360@example.test");
        var otherTenantUser = NewUser("other-customer@example.test");
        var now = DateTimeOffset.UtcNow;
        var activeSessionId = Guid.NewGuid();
        var rotatedOldId = Guid.NewGuid();
        var rotatedReplacementId = Guid.NewGuid();
        var replayedOldId = Guid.NewGuid();
        var replayedReplacementId = Guid.NewGuid();

        await using (var seed = database.CreateAuthContext())
        {
            seed.Tenants.AddRange(
                new TenantRow { Id = tenantId, Name = "Customer 360 tenant" },
                new TenantRow { Id = otherTenantId, Name = "Other tenant" });
            AddMember(seed, tenantId, user, UserOperationalMetadataCodes.Native, now.AddDays(-30), now.AddMinutes(-5));
            AddMember(seed, otherTenantId, otherTenantUser, UserOperationalMetadataCodes.Unknown, null, null);

            seed.RefreshTokens.AddRange(
                RefreshToken(Guid.NewGuid(), user.Id, tenantId, "active", now.AddDays(-1), now.AddDays(1)),
                RefreshToken(Guid.NewGuid(), user.Id, tenantId, "expired", now.AddDays(-2), now.AddSeconds(-1)),
                RefreshToken(Guid.NewGuid(), user.Id, tenantId, "revoked", now.AddHours(-2), now.AddDays(1), now.AddMinutes(-1)),
                RefreshToken(rotatedOldId, user.Id, tenantId, "rotated-old", now.AddHours(-2), now.AddDays(1), now.AddMinutes(-2), rotatedReplacementId),
                RefreshToken(rotatedReplacementId, user.Id, tenantId, "rotated-replacement", now.AddMinutes(-1), now.AddDays(1)),
                RefreshToken(replayedOldId, user.Id, tenantId, "replayed-old", now.AddHours(-1), now.AddDays(1), now.AddMinutes(-1), replayedReplacementId),
                RefreshToken(replayedReplacementId, user.Id, tenantId, "replayed-replacement", now.AddMinutes(-1), now.AddDays(1), now.AddSeconds(-30)));
            await seed.SaveChangesAsync();
        }

        var unauthorizedBillingCommands = new CountingCommandInterceptor();
        await using (var auth = database.CreateAuthContext())
        await using (var billing = database.CreateBillingContext(unauthorizedBillingCommands))
        {
            var action = typeof(AdminManagementController).GetMethod(nameof(AdminManagementController.GetCustomer360))!;
            Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>(), attribute => attribute.Policy == AuthorizationPolicies.AdminUsersRead);

            var controller = CreateController(
                tenantId,
                user.Id,
                CreateQueries(auth, billing),
                Array.Empty<string>());
            var view = await controller.GetCustomer360(user.Id, CancellationToken.None);
            Assert.Equal(2, view.ActiveRefreshSessions);
            Assert.Equal("not_authorized", view.Subscription.Status);
            Assert.Null(view.Subscription.Data);
            Assert.Equal("not_authorized", view.Credits.Status);
            Assert.Null(view.Credits.Data);
            Assert.Equal("not_authorized", view.Purchases.Status);
            Assert.Null(view.Purchases.Data);
            Assert.Equal(0, unauthorizedBillingCommands.ReaderCount);
        }

        var authorizedBillingCommands = new CountingCommandInterceptor();
        await using (var auth = database.CreateAuthContext())
        await using (var billing = database.CreateBillingContext(authorizedBillingCommands))
        {
            billing.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = user.Id,
                AccountType = "user",
                BillingMode = "wallet"
            });
            await billing.SaveChangesAsync();
            authorizedBillingCommands.Reset();

            var controller = CreateController(
                tenantId,
                user.Id,
                CreateQueries(auth, billing),
                new[] { AuthorizationPolicies.AdminSubscriptionsRead });
            var view = await controller.GetCustomer360(user.Id, CancellationToken.None);
            Assert.Equal("available", view.Subscription.Status);
            Assert.Equal("not_authorized", view.Credits.Status);
            Assert.Equal("not_authorized", view.Purchases.Status);
            Assert.InRange(authorizedBillingCommands.ReaderCount, 1, 2);
        }

        await using (var auth = database.CreateAuthContext())
        await using (var billing = database.CreateBillingContext())
        {
            var queries = CreateQueries(auth, billing);
            await Assert.ThrowsAsync<AdminManagementException>(() => queries.GetCustomer360Async(
                tenantId, otherTenantUser.Id, new AdminCustomer360Permissions(false, false, false), CancellationToken.None));
        }

        await using var duplicateCheck = database.CreateAuthContext();
        duplicateCheck.RefreshTokens.Add(RefreshToken(Guid.NewGuid(), user.Id, tenantId, "active", now, now.AddDays(1)));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateCheck.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task Directory_billing_summary_failure_marks_only_the_affected_permission_section_unavailable()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using (var migrationContext = database.CreateAuthContext())
            await migrationContext.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var user = NewUser("section-failure@example.test");
        await using (var auth = database.CreateAuthContext())
        {
            auth.Tenants.Add(new TenantRow { Id = tenantId, Name = "Section failure tenant" });
            AddMember(auth, tenantId, user, UserOperationalMetadataCodes.Native, DateTimeOffset.UtcNow.AddDays(-1), null);
            await auth.SaveChangesAsync();
        }

        var accountId = Guid.NewGuid();
        await using (var billing = database.CreateBillingContext())
        {
            billing.CustomerAccounts.Add(new CustomerAccountRow
            {
                Id = accountId,
                TenantId = tenantId,
                UserId = user.Id,
                AccountType = "user",
                BillingMode = "wallet",
                SubscriptionPlanCode = "Pro"
            });
            billing.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId,
                Balance = 10,
                ReservedAmount = 2,
                UpdatedAt = DateTimeOffset.UtcNow,
                Revision = 1
            });
            await billing.SaveChangesAsync();
        }

        await using var queryAuth = database.CreateAuthContext();
        await using var queryBilling = database.CreateBillingContext(new FailPlanCapabilityCommandInterceptor());
        var page = await CreateQueries(queryAuth, queryBilling).GetDirectoryAsync(
            tenantId,
            new AdminUserDirectoryQuery(PageSize: 10),
            true,
            true,
            true,
            CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("unavailable", item.Subscription.Status);
        Assert.Equal("billing_read_failed", item.Subscription.Reason);
        Assert.Equal("available", item.Credits.Status);
        Assert.Equal(8m, item.Credits.Data!.SpendableCredits);
    }

    [SkippableFact]
    public async Task Billing_histories_page_without_mutating_wallet_and_ignore_cross_account_financial_details()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using (var migrationContext = database.CreateAuthContext())
            await migrationContext.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var user = NewUser("history-customer@example.test");
        var otherUser = NewUser("other-history-customer@example.test");
        var now = DateTimeOffset.UtcNow;
        await using (var auth = database.CreateAuthContext())
        {
            auth.Tenants.Add(new TenantRow { Id = tenantId, Name = "History tenant" });
            AddMember(auth, tenantId, user, UserOperationalMetadataCodes.Native, now.AddDays(-10), null);
            AddMember(auth, tenantId, otherUser, UserOperationalMetadataCodes.Native, now.AddDays(-10), null);
            await auth.SaveChangesAsync();
        }

        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        var localTransactionId = Guid.NewGuid();
        var crossAccountTransactionId = Guid.NewGuid();
        var ledgerIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var periodIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var productCode = "feature146-local-history";
        await using (var billing = database.CreateBillingContext())
        {
            billing.CustomerAccounts.AddRange(
                new CustomerAccountRow { Id = accountId, TenantId = tenantId, UserId = user.Id, AccountType = "user", BillingMode = "wallet" },
                new CustomerAccountRow { Id = otherAccountId, TenantId = tenantId, UserId = otherUser.Id, AccountType = "user", BillingMode = "wallet" });
            billing.PurchaseProducts.Add(new BillingPurchaseProductRow
            {
                Code = productCode,
                ProductType = "credits",
                Version = "v1",
                DisplayName = "Local credit pack",
                Amount = 12m,
                Currency = "USD",
                Credits = 120m,
                Channel = "web",
                IsActive = true,
                SortOrder = 1,
                CreatedAtUtc = now.AddDays(-1)
            });
            billing.WalletProjections.Add(new WalletProjectionRow
            {
                CustomerAccountId = accountId,
                Balance = 120m,
                ReservedAmount = 5m,
                UpdatedAt = now,
                Revision = 3
            });
            await billing.SaveChangesAsync();

            for (var index = 0; index < ledgerIds.Length; index++)
            {
                billing.UsageLedgerEntries.Add(new UsageLedgerEntryRow
                {
                    Id = ledgerIds[index],
                    CustomerAccountId = accountId,
                    ActorId = user.Id,
                    TenantId = tenantId,
                    EntryType = "charge",
                    OperationCode = "feature146-history-test",
                    CreditsCharged = index + 1,
                    PricingPolicyVersion = "v1",
                    IdempotencyKey = $"feature146-history-ledger-{index}",
                    OccurredAt = now.AddMinutes(-10 + index),
                    CompletionStatus = "completed",
                    AllocationSource = "wallet"
                });
            }

            billing.SubscriptionCreditPeriods.AddRange(
                new SubscriptionCreditPeriodRow
                {
                    Id = periodIds[0], CustomerAccountId = accountId, PlanCode = "Pro", IdempotencyKey = "feature146-period-1",
                    AnchorAtUtc = now.AddDays(-1), ActivationRevision = 1, PeriodNumber = 1, StartsAtUtc = now.AddHours(-1),
                    ExpiresAtUtc = now.AddDays(29), GrantedCredits = 100m, RemainingCredits = 90m,
                    GrantLedgerEntryId = Guid.NewGuid(), Status = "active", CreatedAtUtc = now.AddHours(-1), Revision = 1
                },
                new SubscriptionCreditPeriodRow
                {
                    Id = periodIds[1], CustomerAccountId = accountId, PlanCode = "Pro", IdempotencyKey = "feature146-period-2",
                    AnchorAtUtc = now.AddDays(-30), ActivationRevision = 1, PeriodNumber = 2, StartsAtUtc = now.AddDays(-30),
                    ExpiresAtUtc = now.AddDays(-1), GrantedCredits = 100m, RemainingCredits = 0m,
                    GrantLedgerEntryId = Guid.NewGuid(), Status = "expired", CreatedAtUtc = now.AddDays(-30), Revision = 1
                });
            billing.FinancialTransactions.AddRange(
                new FinancialTransactionRow
                {
                    Id = localTransactionId, CustomerAccountId = accountId, Type = "purchase", Amount = 12m,
                    Currency = "USD", IdempotencyKey = "feature146-transaction-local", OccurredAt = now.AddHours(-2)
                },
                new FinancialTransactionRow
                {
                    Id = crossAccountTransactionId, CustomerAccountId = otherAccountId, Type = "purchase", Amount = 99m,
                    Currency = "USD", IdempotencyKey = "feature146-transaction-other", OccurredAt = now.AddHours(-1)
                });
            await billing.SaveChangesAsync();

            billing.CheckoutIntents.AddRange(
                NewCheckout(tenantId, user.Id, accountId, productCode, "pending", "pending", now.AddMinutes(-3)),
                NewCheckout(tenantId, user.Id, accountId, productCode, "local-fulfilled", "fulfilled", now.AddMinutes(-2), localTransactionId, now.AddMinutes(-2)),
                NewCheckout(tenantId, user.Id, accountId, productCode, "cross-account-link", "fulfilled", now.AddMinutes(-1), crossAccountTransactionId, now.AddMinutes(-1)));
            await billing.SaveChangesAsync();
        }

        await using var queryAuth = database.CreateAuthContext();
        await using var queryBilling = database.CreateBillingContext();
        var queries = CreateQueries(queryAuth, queryBilling);
        var creditPages = new List<AdminCreditHistoryItem>();
        var creditPage = await queries.GetCreditHistoryAsync(tenantId, user.Id, 2, null, CancellationToken.None);
        creditPages.AddRange(creditPage.Items);
        while (creditPage.NextCursor is not null)
        {
            creditPage = await queries.GetCreditHistoryAsync(tenantId, user.Id, 2, creditPage.NextCursor, CancellationToken.None);
            creditPages.AddRange(creditPage.Items);
        }
        Assert.Equal(5, creditPages.Count);
        Assert.Contains(creditPages, item => item.RecordType == "usage_ledger");
        Assert.Contains(creditPages, item => item.RecordType == "subscription_credit_period" && item.Status == "expired");

        var purchasePages = new List<AdminPurchaseHistoryItem>();
        var purchasePage = await queries.GetPurchaseHistoryAsync(tenantId, user.Id, 2, null, CancellationToken.None);
        purchasePages.AddRange(purchasePage.Items);
        while (purchasePage.NextCursor is not null)
        {
            purchasePage = await queries.GetPurchaseHistoryAsync(tenantId, user.Id, 2, purchasePage.NextCursor, CancellationToken.None);
            purchasePages.AddRange(purchasePage.Items);
        }
        Assert.Equal(3, purchasePages.Count);
        Assert.Contains(purchasePages, item => item.FinancialTransactionId == localTransactionId && item.FinancialTransactionType == "purchase");
        var crossAccountItem = Assert.Single(purchasePages, item => item.ProductCode == productCode && item.FinancialTransactionId is null && item.FinancialTransactionType is null && item.Status == "fulfilled");
        Assert.Null(crossAccountItem.ReconciliationState);

        var customer = await queries.GetCustomer360Async(
            tenantId, user.Id, new AdminCustomer360Permissions(false, false, true), CancellationToken.None);
        Assert.Equal(1, customer.Purchases.Data!.CompletedLocalPurchaseCount);
        Assert.Null(customer.Purchases.Data.RecentLocalPurchases.Single(item => item.Status == "fulfilled" && item.FinancialTransactionId is null).ReconciliationState);
        Assert.Equal(120m, (await queryBilling.WalletProjections.SingleAsync(row => row.CustomerAccountId == accountId)).Balance);
        Assert.Equal(3, await queryBilling.UsageLedgerEntries.CountAsync(row => row.CustomerAccountId == accountId));
    }

    [SkippableFact]
    public async Task Operational_metadata_uses_postgresql_optimistic_concurrency_across_connections()
    {
        await using var database = await fixture.CreateBillingDatabaseAsync();
        await using (var migrationContext = database.CreateAuthContext())
            await migrationContext.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var user = NewUser("concurrent-login@example.test");
        var registeredAt = DateTimeOffset.UtcNow.AddDays(-1);
        await using (var seed = database.CreateAuthContext())
        {
            seed.Tenants.Add(new TenantRow { Id = tenantId, Name = "Concurrency tenant" });
            AddMember(seed, tenantId, user, UserOperationalMetadataCodes.Native, registeredAt, null);
            await seed.SaveChangesAsync();
        }

        await using var first = database.CreateAuthContext();
        await using var second = database.CreateAuthContext();
        await first.Database.OpenConnectionAsync();
        await second.Database.OpenConnectionAsync();
        Assert.NotEqual(await BackendPidAsync(first), await BackendPidAsync(second));

        var firstMetadata = await first.UserOperationalMetadata.SingleAsync(row => row.UserId == user.Id);
        var secondMetadata = await second.UserOperationalMetadata.SingleAsync(row => row.UserId == user.Id);
        firstMetadata.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
        firstMetadata.ConcurrencyVersion++;
        secondMetadata.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow;
        secondMetadata.ConcurrencyVersion++;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSave = SaveAfterGateAsync(first, gate.Task);
        var secondSave = SaveAfterGateAsync(second, gate.Task);
        gate.SetResult();
        var results = await Task.WhenAll(firstSave, secondSave);
        Assert.Single(results, succeeded => succeeded);
        Assert.Single(results, succeeded => !succeeded);

        var failedContext = results[0] ? second : first;
        var failedMetadata = results[0] ? secondMetadata : firstMetadata;
        await failedContext.Entry(failedMetadata).ReloadAsync();
        failedMetadata.LastSuccessfulLoginAtUtc = DateTimeOffset.UtcNow.AddSeconds(1);
        failedMetadata.ConcurrencyVersion++;
        await failedContext.SaveChangesAsync();

        await using var verify = database.CreateAuthContext();
        var stored = await verify.UserOperationalMetadata.SingleAsync(row => row.UserId == user.Id);
        Assert.Equal(3, stored.ConcurrencyVersion);
        Assert.True(stored.LastSuccessfulLoginAtUtc > DateTimeOffset.UtcNow.AddSeconds(-2));
    }

    private static EfCoreAdminUserQueries CreateQueries(AuthDbContext auth, BillingDbContext billing) =>
        new(auth, billing, TimeProvider.System, new EphemeralDataProtectionProvider(), NullLogger<EfCoreAdminUserQueries>.Instance);

    private static AdminManagementController CreateController(
        Guid tenantId,
        Guid actorId,
        IAdminUserQueries queries,
        IReadOnlyCollection<string> allowedPolicies)
    {
        var actor = new CurrentActor(ActorType.User, actorId, tenantId, AuthenticationMode.WebAppUser, UserId: actorId);
        var controller = new AdminManagementController(
            new TestActorContext(actor),
            null!,
            queries,
            new TestAuthorizationService(allowedPolicies));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity("Feature146IntegrationTest"))
            }
        };
        return controller;
    }

    private static FinancialCopilotUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        IsEnabled = true
    };

    private static void AddMember(
        AuthDbContext context,
        Guid tenantId,
        FinancialCopilotUser user,
        string source,
        DateTimeOffset? registeredAtUtc,
        DateTimeOffset? lastLoginAtUtc,
        bool enabled = true)
    {
        user.IsEnabled = enabled;
        context.Users.Add(user);
        context.UserTenants.Add(new UserTenantRow { TenantId = tenantId, UserId = user.Id });
        context.UserOperationalMetadata.Add(new UserOperationalMetadataRow
        {
            UserId = user.Id,
            AcquisitionSourceCode = source,
            RegisteredAtUtc = registeredAtUtc,
            LastSuccessfulLoginAtUtc = lastLoginAtUtc,
            LastLoginProviderCode = lastLoginAtUtc is null ? null : "native",
            ConcurrencyVersion = 1
        });
    }

    private static RefreshTokenRow RefreshToken(
        Guid id,
        Guid userId,
        Guid tenantId,
        string tokenKey,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt = null,
        Guid? replacedByTokenId = null) => new()
    {
        Id = id,
        UserId = userId,
        TenantId = tenantId,
        TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenKey))),
        CreatedAt = createdAt,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
        ReplacedByTokenId = replacedByTokenId,
        RevocationReason = revokedAt is null ? null : "Feature 146 test fixture",
        AuthenticationMethod = "pwd"
    };

    private static BillingCheckoutIntentRow NewCheckout(
        Guid tenantId,
        Guid actorId,
        Guid accountId,
        string productCode,
        string idempotencyKey,
        string status,
        DateTimeOffset createdAtUtc,
        Guid? financialTransactionId = null,
        DateTimeOffset? fulfilledAtUtc = null)
    {
        return new BillingCheckoutIntentRow
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorId = actorId,
            CustomerAccountId = accountId,
            ProductType = "credits",
            ProductCode = productCode,
            ProductVersion = "v1",
            ProductDisplayName = "Local credit pack",
            Amount = 12m,
            Currency = "USD",
            Credits = 120m,
            Channel = "web",
            PaymentReference = $"feature146-{idempotencyKey}",
            Status = status,
            IdempotencyKey = $"feature146-checkout-{idempotencyKey}",
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.AddDays(1),
            FulfillmentLedgerEntryId = null,
            FulfillmentFinancialTransactionId = financialTransactionId,
            FulfilledAtUtc = fulfilledAtUtc,
            ConcurrencyToken = Guid.NewGuid(),
            Version = 1
        };
    }

    private static async Task<bool> SaveAfterGateAsync(AuthDbContext context, Task gate)
    {
        await gate;
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static async Task<int> BackendPidAsync(AuthDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        private int readerCount;
        public int ReaderCount => Volatile.Read(ref readerCount);

        public void Reset() => Interlocked.Exchange(ref readerCount, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref readerCount);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestActorContext(CurrentActor actor) : ICurrentActorContext
    {
        public CurrentActor Actor => actor;
    }

    private sealed class TestAuthorizationService(IReadOnlyCollection<string> allowedPolicies) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(allowedPolicies.Contains(policyName)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
    }

    private sealed class FailPlanCapabilityCommandInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("billing_plan_capabilities", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Feature 146 targeted plan capability read failure.");

            return ValueTask.FromResult(result);
        }
    }
}
