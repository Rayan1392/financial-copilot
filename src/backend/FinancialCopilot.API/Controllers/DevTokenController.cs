using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.API.Controllers;

#if DEBUG

[ApiController]
[Route("dev")]
public sealed class DevTokenController(IConfiguration configuration, IHostEnvironment env) : ControllerBase
{
    public sealed record TokenRequest(
        Guid UserId,
        Guid TenantId,
        string[] Roles,
        int ExpiresInMinutes = 60);

    public sealed record TokenResponse(string Token, DateTimeOffset ExpiresAt);

    [HttpPost("token")]
    public IActionResult GenerateToken([FromBody] TokenRequest request)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var signingKey = configuration["Authentication:JwtBearer:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
            return Problem("Authentication:JwtBearer:SigningKey is not configured.");

        var issuer   = configuration["Authentication:JwtBearer:Issuer"];
        var audience = configuration["Authentication:JwtBearer:Audience"];

        var claims = new List<Claim>
        {
            new("sub", request.UserId.ToString()),
            new("financial_copilot:tenant_id", request.TenantId.ToString()),
        };
        foreach (var role in request.Roles)
            claims.Add(new Claim("role", role));

        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt   = DateTimeOffset.UtcNow.AddMinutes(request.ExpiresInMinutes);

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return Ok(new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt));
    }
}

#endif
