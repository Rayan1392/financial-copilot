using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Domain.Financial.FundPortfolio;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class FundPortfolioSignalGenerationOptions
{
    public const string SectionName = "FundPortfolio:Analytics:Signals";
    public decimal MinimumActivityAmount { get; set; }
    public decimal MinimumWeightChangePercentagePoints { get; set; } = 1m;
    public decimal MinimumSectorChangePercentagePoints { get; set; } = 1m;
    public decimal MinimumConcentrationIncreasePercentagePoints { get; set; } = 1m;
    public decimal MinimumHedgeCoverageChangePercentagePoints { get; set; } = 5m;
    public decimal MinimumUnrealizedIncomeConcentrationPercentage { get; set; } = 25m;
    public decimal MinimumValuationAdjustmentExposurePercentage { get; set; } = 5m;
    public string Version { get; set; } = "fund-portfolio-signals-v1";

    public FundSignalGenerationRules ToRules() => new(
        MinimumActivityAmount,
        MinimumWeightChangePercentagePoints,
        MinimumSectorChangePercentagePoints,
        MinimumConcentrationIncreasePercentagePoints,
        MinimumHedgeCoverageChangePercentagePoints,
        MinimumUnrealizedIncomeConcentrationPercentage,
        MinimumValuationAdjustmentExposurePercentage,
        Version);
}

public sealed class FundPortfolioSignalGenerationService(
    IFundPortfolioSignalGenerator generator,
    IOptions<FundPortfolioSignalGenerationOptions> options)
{
    public IReadOnlyList<FundPortfolioSignal> Generate(FundPortfolioSignalGenerationInput input) =>
        generator.Generate(input with { Rules = options.Value.ToRules() });
}
