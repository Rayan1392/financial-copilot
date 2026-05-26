using FinancialCopilot.API.Security;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FinancialCopilot.API.Controllers;

[ApiController]
[Route("api/ai/v1")]
[Authorize(Policy = AuthorizationPolicies.AiFacade)]
[EnableRateLimiting(RateLimitPolicies.AuthenticatedActor)]
public sealed class AiFacadeController(ICurrentActorContext actorContext) : ControllerBase
{
    [HttpPost("query")]
    public IActionResult Query() => NotImplementedResponse("AI Query Orchestrator");

    [HttpGet("conversations")]
    public IActionResult Conversations() => NotImplementedResponse("Conversation history");

    [HttpGet("conversations/{conversationId:guid}")]
    public IActionResult Conversation(Guid conversationId) =>
        NotImplementedResponse($"Conversation {conversationId}");

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public IActionResult Messages(Guid conversationId) =>
        NotImplementedResponse($"Messages for conversation {conversationId}");

    private IActionResult NotImplementedResponse(string capability)
    {
        var actor = actorContext.Actor;
        var details = new ProblemDetails
        {
            Type = "https://financialcopilot/errors/not-implemented",
            Title = "Capability is not implemented.",
            Status = StatusCodes.Status501NotImplemented,
            Detail = $"{capability} will be implemented in a subsequent story."
        };

        details.Extensions["authenticationMode"] = actor.AuthenticationMode.ToString();

        return StatusCode(StatusCodes.Status501NotImplemented, details);
    }
}
