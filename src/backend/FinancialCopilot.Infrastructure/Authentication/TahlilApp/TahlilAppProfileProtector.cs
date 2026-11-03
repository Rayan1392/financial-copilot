using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>
/// Field-level encryption for TahlilApp profile PII with ASP.NET Core Data Protection (Design §10.2, §17.1).
/// No custom cryptography and no lookup keys: ciphertext is never used for matching.
/// </summary>
public sealed class TahlilAppProfileProtector(IDataProtectionProvider protectionProvider)
{
    public const string NationalIdPurpose = "FinancialCopilot.Profile.NationalId.v1";
    public const string MobilePurpose = "FinancialCopilot.Profile.Mobile.v1";

    public string Protect(string purpose, string plaintext) =>
        protectionProvider.CreateProtector(purpose).Protect(plaintext);

    /// <returns>The plaintext, or null when the ciphertext cannot be unprotected (for example after key loss).</returns>
    public string? TryUnprotect(string purpose, string ciphertext)
    {
        try
        {
            return protectionProvider.CreateProtector(purpose).Unprotect(ciphertext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
