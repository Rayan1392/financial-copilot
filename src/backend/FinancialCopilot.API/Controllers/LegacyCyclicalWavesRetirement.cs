using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Controllers;

/// <summary>Feature 148: legacy CyclicalWaves ingestion triggers answer 410 before any service runs.</summary>
internal static class LegacyCyclicalWavesRetirement
{
    public const string Code = "ProviderRetired";

    public const string Detail =
        "The legacy CyclicalWaves integration was retired by Feature 148. Use the CyclicalWaves v2 operations " +
        "under /api/v1/admin/cyclicalwaves-v2.";

    public static ObjectResult Gone(ControllerBase controller) =>
        controller.Problem(statusCode: StatusCodes.Status410Gone, title: Code, detail: Detail, type: Code);
}
