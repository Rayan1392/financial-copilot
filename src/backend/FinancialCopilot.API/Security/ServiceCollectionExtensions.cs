using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Billing.Persistence;
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
        services.AddOptions<TahlilAppTopUpOptions>()
            .BindConfiguration(TahlilAppTopUpOptions.SectionName)
            .Validate(options => !options.Enabled ||
                !string.IsNullOrWhiteSpace(options.Authentication.Issuer) &&
                options.Authentication.Audience == "sapio-tahlilapp-topup" &&
                options.Authentication.SigningKeys.Count > 0 &&
                options.Authentication.SigningKeys.All(item => !string.IsNullOrWhiteSpace(item.Key) &&
                    !string.IsNullOrWhiteSpace(item.Value) && Encoding.UTF8.GetByteCount(item.Value) >= 32),
                "Enabled TahlilApp top-ups require an issuer, the contract audience, and 32-byte signing keys keyed by key ID.")
            .ValidateOnStart();
        services.AddScoped<ICurrentActorContext, HttpCurrentActorContext>();
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Validate(_ => HasValidSigningConfiguration(configuration),
                "Authentication:JwtBearer must provide a 32-character signing key when Authority is not configured.")
            .ValidateOnStart();
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
            .AddJwtBearer(AuthenticationSchemes.TahlilAppTopUpPartner, options => ConfigureTopUpPartnerJwt(options, configuration))
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                AuthenticationSchemes.ApiKey,
                _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("TahlilAppTopUpPartner", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim("scope", "topup.deliver");
            });
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
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.TrackerReadSelf,
                FinancialCopilotPermissions.TrackerReadSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.TrackerWriteSelf,
                FinancialCopilotPermissions.TrackerWriteSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.RadarReadSelf,
                FinancialCopilotPermissions.RadarReadSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.RadarWriteSelf,
                FinancialCopilotPermissions.RadarWriteSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.NotificationReadSelf,
                FinancialCopilotPermissions.NotificationReadSelf);
            AddActorPermissionPolicy(
                options,
                AuthorizationPolicies.NotificationManageSelf,
                FinancialCopilotPermissions.NotificationManageSelf);
            options.AddPolicy(AuthorizationPolicies.TelegramLinkManageSelf, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.WebAppUser));
                policy.AddRequirements(new PermissionRequirement(FinancialCopilotPermissions.TelegramLinkManageSelf));
            });
            options.AddPolicy(AuthorizationPolicies.TelegramMembershipReadSelf, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.WebAppUser));
                policy.AddRequirements(new PermissionRequirement(FinancialCopilotPermissions.TelegramMembershipReadSelf));
            });
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
            options.AddPolicy(AuthorizationPolicies.NoavaranMonthlyBackfill, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                    HasValidActorContext(context.User) &&
                    IsMode(context.User, AuthenticationMode.WebAppUser) &&
                    (context.User.HasClaim(FinancialCopilotClaimTypes.Permission, FinancialCopilotPermissions.NoavaranMonthlyBackfillExecute) ||
                     context.User.HasClaim(FinancialCopilotClaimTypes.Permission, FinancialCopilotPermissions.DataSyncManage)));
            });
        });

        return services;
    }

    private static void ConfigureTopUpPartnerJwt(JwtBearerOptions options, IConfiguration configuration)
    {
        var section = configuration.GetSection("Billing:TahlilAppTopUps:Authentication");
        var issuer = section["Issuer"];
        var audience = section["Audience"] ?? "sapio-tahlilapp-topup";
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            IssuerSigningKeyResolver = (_, _, keyId, _) =>
            {
                var encodedKey = section.GetSection("SigningKeys")[keyId ?? string.Empty];
                if (string.IsNullOrWhiteSpace(encodedKey)) return Array.Empty<SecurityKey>();
                return new SecurityKey[] { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(encodedKey)) { KeyId = keyId } };
            }
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                var jti = principal?.FindFirst("jti")?.Value;
                var tokenIssuer = principal?.FindFirst("iss")?.Value ?? issuer;
                var expText = principal?.FindFirst("exp")?.Value;
                if (string.IsNullOrWhiteSpace(jti) || jti.Length < 16 || !long.TryParse(expText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var exp))
                {
                    context.Fail("Partner delivery JWT must contain a unique jti and exp claim.");
                    return;
                }
                if (!HttpMethods.IsPost(context.Request.Method) ||
                    !context.Request.Path.Equals("/api/v1/billing/partners/tahlilapp/top-ups", StringComparison.OrdinalIgnoreCase)) return;
                var consumed = await context.HttpContext.RequestServices.GetRequiredService<TahlilAppTopUpService>()
                    .TryConsumeTokenJtiAsync(tokenIssuer ?? string.Empty, jti, DateTimeOffset.FromUnixTimeSeconds(exp), context.HttpContext.RequestAborted);
                if (!consumed) context.Fail("Partner delivery JWT was already consumed or is invalid.");
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                return WritePartnerAuthProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized);
            },
            OnForbidden = context =>
            {
                return WritePartnerAuthProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden);
            }
        };
    }

    private static async Task WritePartnerAuthProblemAsync(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? context.TraceIdentifier;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "urn:financial-copilot:problem:partner-auth-failed",
            title = "partner-auth-failed",
            status,
            code = "partner-auth-failed",
            correlationId
        });
        await context.Response.Body.WriteAsync(payload);
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
        // No fallback signing key is ever installed. Without an authority, startup
        // validation fails before the API can serve requests.

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
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

                var principal = context.Principal;
                var sessionStamp = principal?.FindFirstValue("financial_copilot:security_stamp");
                if (principal is not null && IsMode(principal, AuthenticationMode.WebAppUser) && sessionStamp is not null)
                {
                    var subject = principal?.FindFirstValue("sub");
                    if (!Guid.TryParse(subject, out var userId))
                    {
                        context.Fail("The owned-user session is invalid.");
                        return;
                    }

                    var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<FinancialCopilotUser>>();
                    var currentUser = await userManager.FindByIdAsync(userId.ToString());
                    if (currentUser is null || !string.Equals(currentUser.SecurityStamp, sessionStamp, StringComparison.Ordinal))
                    {
                        context.Fail("The owned-user session has been revoked.");
                    }
                }

            }
        };
    }

    internal static bool HasValidSigningConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Authentication:JwtBearer");
        var authority = section["Authority"];
        var signingKey = section["SigningKey"];
        return !string.IsNullOrWhiteSpace(authority) ||
               !string.IsNullOrWhiteSpace(signingKey) && signingKey.Length >= 32;
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

public sealed class TahlilAppTopUpOptions
{
    public const string SectionName = "Billing:TahlilAppTopUps";
    public bool Enabled { get; set; }
    public TahlilAppTopUpAuthenticationOptions Authentication { get; set; } = new();
}

public sealed class TahlilAppTopUpAuthenticationOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = "sapio-tahlilapp-topup";
    public Dictionary<string, string> SigningKeys { get; set; } = new(StringComparer.Ordinal);
}
