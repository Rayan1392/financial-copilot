namespace FinancialCopilot.Domain.Identity.TahlilApp;

/// <summary>
/// Masked display of profile identifiers (Design §10.2). Masked values are the default for every audience;
/// only the owner's explicit reveal returns a full value.
/// </summary>
public static class ProfileMasking
{
    private const string Mask = "•••";

    /// <summary>National id shows only the last four digits, e.g. <c>••••••5678</c>.</summary>
    public static string? MaskNationalId(string? last4) =>
        string.IsNullOrEmpty(last4) ? null : "••••••" + last4;

    /// <summary>Mobile shows the local prefix and the last four digits, e.g. <c>0912•••4567</c>.</summary>
    public static string? MaskMobile(string? e164, string? last4)
    {
        if (string.IsNullOrEmpty(e164) || string.IsNullOrEmpty(last4) || e164.Length < 13)
        {
            return null;
        }

        // +989xxxxxxxxx → 09xx prefix
        return "0" + e164[3..6] + Mask + last4;
    }
}
