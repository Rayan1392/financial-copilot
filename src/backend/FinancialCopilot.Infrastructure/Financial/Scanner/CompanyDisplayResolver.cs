namespace FinancialCopilot.Infrastructure.Financial.Scanner;

internal static class CompanyDisplayResolver
{
    public static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
