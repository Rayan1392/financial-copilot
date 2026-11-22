using FinancialCopilot.Application.InAppMessages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinancialCopilot.API.Controllers;

internal sealed class InAppMessageProblemsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not InAppMessageException exception)
            return;

        var status = exception.Failure switch
        {
            MessageFailure.Invalid => StatusCodes.Status400BadRequest,
            MessageFailure.Forbidden => StatusCodes.Status403Forbidden,
            MessageFailure.NotFound => StatusCodes.Status404NotFound,
            MessageFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = exception.Code,
            Type = $"https://financialcopilot/errors/{exception.Code}"
        };
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        context.Result = new ObjectResult(problem) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
