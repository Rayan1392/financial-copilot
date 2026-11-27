namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;

/// <summary>
/// Provider line keys of the Feature 148 income statements (<c>cyclicalwaves.IncomeStatementLineDefinitions</c>) that
/// mean revenue and net profit. Different company templates print their top line under different keys, so revenue is
/// the first key in <see cref="RevenueInPriority"/> that has a discrete-quarter value. This is the order the research
/// behind change «valuation-gauge-forward-ratio» used (<c>REV_LINES</c> in
/// <c>tools/research/forward-pe-reverse-engineering/lib.py</c>).
/// </summary>
internal static class CyclicalWavesV2IncomeStatementLines
{
    public const int Sales = 44;               // فروش
    public const int ServiceRevenue = 46;      // درآمد ارائه خدمات
    public const int TotalRevenue = 124;       // جمع درآمدها
    public const int InsuranceIncome = 412;    // درآمدهای بیمه‌ای
    public const int TotalIncome = 599;        // جمع درآمدها
    public const int NetIncome = 66;           // سود (زیان) خالص

    public static readonly IReadOnlyList<int> RevenueInPriority = [Sales, ServiceRevenue, TotalRevenue, InsuranceIncome, TotalIncome];
}
