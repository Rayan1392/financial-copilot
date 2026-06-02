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
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, AdminAuthorizationResultHandler>();
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
                policy.AddRequirements(new PermissionRequirement(
                    FinancialCopilotPermissions.AiQuery,
                    AllowApiClient: true));
            });

            options.AddPolicy(AuthorizationPolicies.ApiClientOnly, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.ApiClient));
            });

            options.AddPolicy(AuthorizationPolicies.BillingAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.WebAppUser));
                policy.AddRequirements(new PermissionRequirement(FinancialCopilotPermissions.BillingManage));
            });

            options.AddPolicy(AuthorizationPolicies.DataAdmin, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.WebAppUser));
                policy.AddRequirements(new PermissionRequirement(FinancialCopilotPermissions.DataSyncManage));
            });

            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.UsageReadSelf,
                FinancialCopilotPermissions.UsageReadSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.WatchlistReadSelf,
                FinancialCopilotPermissions.WatchlistReadSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.WatchlistWriteSelf,
                FinancialCopilotPermissions.WatchlistWriteSelf);
            options.AddPolicy(AuthorizationPolicies.MarketSummaryRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context => HasValidActorContext(context.User));
            });
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminUsersRead, FinancialCopilotPermissions.AdminUsersRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminUsersManage, FinancialCopilotPermissions.AdminUsersManage);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminRolesRead, FinancialCopilotPermissions.AdminRolesRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminRolesManage, FinancialCopilotPermissions.AdminRolesManage);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminPermissionsRead, FinancialCopilotPermissions.AdminPermissionsRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminPermissionsManage, FinancialCopilotPermissions.AdminPermissionsManage);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminTenantsRead, FinancialCopilotPermissions.AdminTenantsRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminTenantsManage, FinancialCopilotPermissions.AdminTenantsManage);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminPlansRead, FinancialCopilotPermissions.AdminPlansRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminPlansManage, FinancialCopilotPermissions.AdminPlansManage);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminSubscriptionsRead, FinancialCopilotPermissions.AdminSubscriptionsRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminSubscriptionsManage, FinancialCopilotPermissions.AdminSubscriptionsManage);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminUsageLedgerRead, FinancialCopilotPermissions.AdminUsageLedgerRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminCreditsAdjust, FinancialCopilotPermissions.AdminCreditsAdjust);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminBillingAuditRead, FinancialCopilotPermissions.AdminBillingAuditRead);
            AddWebAdminPermissionPolicy(options, AuthorizationPolicies.AdminSecurityAuditRead, FinancialCopilotPermissions.AdminSecurityAuditRead);
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

                AddLegacyPermissionClaims(context.Principal);

                return Task.CompletedTask;
            }
        };
    }

    private static void AddLegacyPermissionClaims(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity ||
            !IsMode(principal, AuthenticationMode.WebAppUser))
        {
            return;
        }

        foreach (var permission in FinancialCopilotPermissions.WebUserDefaults)
        {
            AddPermissionIfMissing(identity, permission);
        }
        if (principal.HasClaim("role", "DataAdmin"))
        {
            AddPermissionIfMissing(identity, FinancialCopilotPermissions.DataSyncManage);
        }
        if (principal.HasClaim("role", "BillingAdmin"))
        {
            AddPermissionIfMissing(identity, FinancialCopilotPermissions.BillingManage);
        }
    }

    private static void AddPermissionIfMissing(ClaimsIdentity identity, string permission)
    {
        if (!identity.HasClaim(FinancialCopilotClaimTypes.Permission, permission))
        {
            identity.AddClaim(new Claim(FinancialCopilotClaimTypes.Permission, permission));
        }
    }

    private static void AddActorPermissionPolicy(
        AuthorizationOptions options,
        string policyName,
        string permission)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context => HasValidActorContext(context.User));
            policy.AddRequirements(new PermissionRequirement(permission, AllowApiClient: true));
        });
    }

    private static void AddWebAdminPermissionPolicy(
        AuthorizationOptions options,
        string policyName,
        string permission)
    {
        options.AddPolicy(policyName, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireAssertion(context =>
                HasValidActorContext(context.User) &&
                IsMode(context.User, AuthenticationMode.WebAppUser));
            policy.AddRequirements(new PermissionRequirement(permission));
        });
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
