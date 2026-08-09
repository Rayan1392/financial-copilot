using FinancialCopilot.Application.Scanner;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class SalesGrowthScannerOptionsValidator : IValidateOptions<SalesGrowthScannerOptions>
{
    public ValidateOptionsResult Validate(string? name, SalesGrowthScannerOptions options)
    {
        var errors = SalesGrowthScannerOptionsValidation.Validate(options);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
