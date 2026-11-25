using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.InAppMessages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[InAppMessageProblems]
[Route("api/v1")]
public sealed class InAppMessagesController(
    InAppMessageUseCases messages, ICurrentActorContext actorContext) : ControllerBase
{
    [HttpPost("admin/users/{userId:guid}/in-app-messages")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    [Authorize(Policy = AuthorizationPolicies.AdminInAppMessagesSend)]
    [EnableRateLimiting(RateLimitPolicies.InAppMessageWrite)]
    public Task<MessageReceipt> Start(Guid userId, StartRequest request, CancellationToken ct) =>
        messages.StartAsync(actorContext.Actor,
            new StartMessage(userId, request.Subject, request.Body, request.RepliesEnabled, request.IdempotencyKey),
            HttpContext.TraceIdentifier, ct);

    [HttpGet("in-app-messages/me/summary")]
    public Task<MessageSummary> Summary(CancellationToken ct) => messages.GetSummaryAsync(actorContext.Actor, ct);

    [HttpGet("in-app-messages/me")]
    public Task<MessagePage<ConversationSummary>> Inbox(
        CancellationToken ct, MessageListFilter filter = MessageListFilter.All, int pageSize = 25, string? cursor = null) =>
        messages.ListAsync(actorContext.Actor, MessageAudience.Recipient, new MessageListRequest(filter, pageSize, cursor), ct);

    [HttpPost("in-app-messages/me")]
    [EnableRateLimiting(RateLimitPolicies.InAppMessageWrite)]
    public Task<MessageReceipt> StartSupport(StartSupportRequest request, CancellationToken ct) =>
        messages.StartSupportAsync(actorContext.Actor, request, ct);

    [HttpGet("in-app-messages/me/{conversationId:guid}")]
    public Task<ConversationDetail> OwnDetail(Guid conversationId, CancellationToken ct, int pageSize = 25, string? cursor = null) =>
        messages.DetailAsync(actorContext.Actor, MessageAudience.Recipient, conversationId, pageSize, cursor, HttpContext.TraceIdentifier, ct);

    [HttpPost("in-app-messages/me/{conversationId:guid}/replies")]
    [EnableRateLimiting(RateLimitPolicies.InAppMessageWrite)]
    public Task<MessageReceipt> Reply(Guid conversationId, UserReplyRequest request, CancellationToken ct) =>
        messages.ReplyAsync(actorContext.Actor,
            new ReplyMessage(conversationId, request.Body, request.IdempotencyKey, MessageAudience.Recipient),
            HttpContext.TraceIdentifier, ct);

    [HttpPut("in-app-messages/me/{conversationId:guid}/read")]
    [EnableRateLimiting(RateLimitPolicies.InAppMessageWrite)]
    public async Task<IActionResult> Read(Guid conversationId, ReadRequest request, CancellationToken ct)
    {
        await messages.AcknowledgeAsync(actorContext.Actor, conversationId, request.MessageIds, ct);
        return NoContent();
    }

    [HttpGet("admin/in-app-messages/support")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    [Authorize(Policy = AuthorizationPolicies.AdminInAppMessagesSupport)]
    public Task<MessagePage<ConversationSummary>> Queue(
        CancellationToken ct, MessageListFilter filter = MessageListFilter.Pending, int pageSize = 25, string? cursor = null) =>
        messages.ListAsync(actorContext.Actor, MessageAudience.Staff, new MessageListRequest(filter, pageSize, cursor), ct);

    [HttpGet("admin/in-app-messages/support/{conversationId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    [Authorize(Policy = AuthorizationPolicies.AdminInAppMessagesSupport)]
    public Task<ConversationDetail> SupportDetail(Guid conversationId, CancellationToken ct, int pageSize = 25, string? cursor = null) =>
        messages.DetailAsync(actorContext.Actor, MessageAudience.Staff, conversationId, pageSize, cursor, HttpContext.TraceIdentifier, ct);

    [HttpPost("admin/in-app-messages/support/{conversationId:guid}/replies")]
    [Authorize(Policy = AuthorizationPolicies.AdminUsersRead)]
    [Authorize(Policy = AuthorizationPolicies.AdminInAppMessagesSupport)]
    [EnableRateLimiting(RateLimitPolicies.InAppMessageWrite)]
    public Task<MessageReceipt> SupportReply(Guid conversationId, StaffReplyRequest request, CancellationToken ct) =>
        messages.ReplyAsync(actorContext.Actor,
            new ReplyMessage(conversationId, request.Body, request.IdempotencyKey, MessageAudience.Staff, request.ExpectedRevision),
            HttpContext.TraceIdentifier, ct);

    public sealed record StartRequest(string Subject, string Body, bool RepliesEnabled, Guid IdempotencyKey);
    public sealed record UserReplyRequest(string Body, Guid IdempotencyKey);
    public sealed record ReadRequest(Guid[] MessageIds);
    public sealed record StaffReplyRequest(string Body, Guid IdempotencyKey, long ExpectedRevision);
}
