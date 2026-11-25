using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.InAppMessages;

namespace FinancialCopilot.IntegrationTests;

public sealed class InAppMessage165EndpointTests(AdminManagementApiFactory factory)
    : IClassFixture<AdminManagementApiFactory>
{
    [Fact]
    public async Task Send_and_support_require_distinct_permissions_and_api_keys_are_denied()
    {
        await factory.ResetAsync();
        const string queue = "/api/v1/admin/in-app-messages/support";
        const string inbox = "/api/v1/in-app-messages/me/summary";
        var start = $"/api/v1/admin/users/{AdminManagementApiFactory.ManagedUserId:D}/in-app-messages";
        var content = new { Subject = "Subject", Body = "Body", RepliesEnabled = true, IdempotencyKey = Guid.NewGuid() };
        using var sender = Client(FinancialCopilotPermissions.AdminUsersRead, FinancialCopilotPermissions.AdminInAppMessagesSend);
        using var operatorClient = Client(FinancialCopilotPermissions.AdminUsersRead, FinancialCopilotPermissions.AdminInAppMessagesSupport);
        using var reader = Client(FinancialCopilotPermissions.AdminUsersRead);
        using var apiKey = factory.CreateClient();
        apiKey.DefaultRequestHeaders.Add("X-Api-Key", AuthenticationApiFactory.ApiKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(start, content)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync(start, content)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sender.GetAsync(queue)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apiKey.GetAsync(inbox)).StatusCode);
        using var apiKeyStart = await apiKey.PostAsJsonAsync("/api/v1/in-app-messages/me", content);
        Assert.Equal(HttpStatusCode.Forbidden, apiKeyStart.StatusCode);
        using var invalidSupport = await reader.PostAsJsonAsync("/api/v1/in-app-messages/me", content with { Subject = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSupport.StatusCode);
        using var requested = await reader.PostAsJsonAsync("/api/v1/in-app-messages/me",
            new { Subject = "Help", Body = "Request", IdempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        Assert.True(requested.Headers.CacheControl?.NoStore);
        var request = await requested.Content.ReadFromJsonAsync<MessageReceipt>();
        Assert.NotNull(request);
        using var queued = await operatorClient.GetAsync($"{queue}/{request.ConversationId:D}");
        Assert.Equal(HttpStatusCode.OK, queued.StatusCode);
        var pending = await queued.Content.ReadFromJsonAsync<ConversationDetail>();
        Assert.NotNull(pending);
        Assert.Equal(AdminManagementApiFactory.UserId, pending.Conversation.RecipientUserId);
        Assert.False(Assert.Single(pending.Messages.Items).FromStaff);

        using var sent = await sender.PostAsJsonAsync(start, content);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.True(sent.Headers.CacheControl?.NoStore);
        var receipt = await sent.Content.ReadFromJsonAsync<MessageReceipt>();
        Assert.NotNull(receipt);
        using var support = await operatorClient.GetAsync($"{queue}/{receipt.ConversationId:D}");
        Assert.Equal(HttpStatusCode.OK, support.StatusCode);
        Assert.True(support.Headers.CacheControl?.NoStore);
        using var denied = await sender.GetAsync($"/api/v1/in-app-messages/me/{receipt.ConversationId:D}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var invalid = await sender.PostAsJsonAsync(start, content with { Body = "    " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private HttpClient Client(params string[] permissions)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateWebAppToken(includeTenant: true, permissions: permissions));
        return client;
    }
}
