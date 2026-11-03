using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FinancialCopilot.IntegrationTests;

public sealed class NoavaranFinancialStatementBackfillEndpointTests : IClassFixture<AuthenticationApiFactory>
{
    private const string Route = "/api/v1/admin/noavaran-current/financial-statement-backfill/single-company-period";
    private const string InventoryRoute = "/api/v1/admin/noavaran-current/financial-statements/inventory?ticker=%DA%A9%DA%AF%D9%84";
    private readonly AuthenticationApiFactory _factory;

    public NoavaranFinancialStatementBackfillEndpointTests(AuthenticationApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ManualBackfill_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.PostAsJsonAsync(
            Route,
            new { symbol = "کگل", fiscalYearEnding = 1404, periodMonths = 12, statementType = "IncomeStatement" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Inventory_AsNormalUser_ReturnsForbidden()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _factory.CreateWebAppToken(includeTenant: true));

        using var response = await client.GetAsync(InventoryRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(1404, 1, "IncomeStatement")]
    [InlineData(1404, 15, "BalanceSheet")]
    [InlineData(1404, 6, "Unknown")]
    [InlineData(1399, 6, "CashFlow")]
    public async Task ManualBackfill_DataAdminRejectsInvalidScope(int year, int period, string statementType)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.CreateWebAppToken(includeTenant: true, dataAdmin: true));

        using var response = await client.PostAsJsonAsync(
            Route,
            new { symbol = "کگل", fiscalYearEnding = year, periodMonths = period, statementType },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
