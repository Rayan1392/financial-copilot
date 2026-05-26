using System.Security.Claims;
using System.Text;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace FinancialCopilot.API.Security;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFinancialCopilotSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentActorContext, HttpCurrentActorContext>();
        services.Configure<ApiKeyAuthenticationOptions>(
            configuration.GetSection(ApiKeyAuthenticationOptions.SectionName));

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = AuthenticationSchemes.Dynamic;
                options.DefaultChallengeScheme = AuthenticationSchemes.Dynamic;
            })
            .AddPolicyScheme(AuthenticationSchemes.Dynamic, AuthenticationSchemes.Dynamic, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                        ? AuthenticationSchemes.ApiKey
                        : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options => ConfigureJwtBearer(options, configuration))
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                AuthenticationSchemes.ApiKey,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.AiFacade, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasValidActorContext(context.User));
            });

            options.AddPolicy(AuthorizationPolicies.ApiClientOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.ApiClient));
            });
        });

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection("Authentication:JwtBearer");
        var authority = section["Authority"];
        var issuer = section["Issuer"];
        var audience = section["Audience"];
        var signingKey = section["SigningKey"];

        options.MapInboundClaims = false;
        options.Authority = authority;
        options.RequireHttpsMetadata = section.GetValue("RequireHttpsMetadata", true);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(audience),
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            NameClaimType = "sub"
        };

        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            options.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        }
        else if (string.IsNullOrWhiteSpace(authority))
        {
            options.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("unconfigured-jwt-signing-key-rejects-tokens"));
        }

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity &&
                    !identity.HasClaim(claim =>
                        claim.Type == FinancialCopilotClaimTypes.AuthenticationMode))
                {
                    identity.AddClaim(new Claim(
                        FinancialCopilotClaimTypes.AuthenticationMode,
                        AuthenticationMode.WebAppUser.ToString()));
                }

                return Task.CompletedTask;
            }
        };
    }

    private static bool HasValidActorContext(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue(FinancialCopilotClaimTypes.TenantId), out _))
        {
            return false;
        }

        return IsMode(principal, AuthenticationMode.WebAppUser)
            ? Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out _)
            : IsMode(principal, AuthenticationMode.ApiClient) &&
              Guid.TryParse(principal.FindFirstValue(FinancialCopilotClaimTypes.ApiClientId), out _);
    }

    private static bool IsMode(ClaimsPrincipal principal, AuthenticationMode mode) =>
        string.Equals(
            principal.FindFirstValue(FinancialCopilotClaimTypes.AuthenticationMode),
            mode.ToString(),
            StringComparison.OrdinalIgnoreCase);
}
