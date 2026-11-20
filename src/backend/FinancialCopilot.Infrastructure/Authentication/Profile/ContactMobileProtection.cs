using Microsoft.AspNetCore.DataProtection;

namespace FinancialCopilot.Infrastructure.Authentication.Profile;

/// <summary>
/// The single owner of the data-protection purpose for a user's self-service contact mobile (edit profile), shared by
/// the profile store and the admin conversation-log user search (Feature 164).
/// </summary>
public static class ContactMobileProtection
{
    public const string Purpose = "FinancialCopilot.OwnProfile.ContactMobile.v1";

    public static IDataProtector CreateProtector(IDataProtectionProvider dataProtection) =>
        dataProtection.CreateProtector(Purpose);
}
