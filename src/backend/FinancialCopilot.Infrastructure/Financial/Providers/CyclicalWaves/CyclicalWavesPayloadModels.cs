using System.Text.Json.Serialization;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

internal sealed record CyclicalWavesAuthResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

internal sealed record CyclicalWavesTickerDetailResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] CyclicalWavesTickerData Data);

internal sealed record CyclicalWavesTickerData(
    [property: JsonPropertyName("_id")] string Id,
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("enticker")] string Enticker,
    [property: JsonPropertyName("last_quarter_sale")] decimal? LastQuarterSale,
    [property: JsonPropertyName("penultimate_quarter_sale")] decimal? PenultimateQuarterSale,
    [property: JsonPropertyName("last_year_same_quarter_sale")] decimal? LastYearSameQuarterSale,
    [property: JsonPropertyName("last_quarter_net_profit")] decimal? LastQuarterNetProfit,
    [property: JsonPropertyName("penultimate_quarter_net_profit")] decimal? PenultimateQuarterNetProfit,
    [property: JsonPropertyName("last_year_same_quarter_net_profit")] decimal? LastYearSameQuarterNetProfit,
    [property: JsonPropertyName("last_quarter_gross_profit")] decimal? LastQuarterGrossProfit,
    [property: JsonPropertyName("penultimate_quarter_gross_profit")] decimal? PenultimateQuarterGrossProfit,
    [property: JsonPropertyName("last_year_same_quarter_gross_profit")] decimal? LastYearSameQuarterGrossProfit,
    [property: JsonPropertyName("last_quarter_operating_profit")] decimal? LastQuarterOperatingProfit,
    [property: JsonPropertyName("penultimate_quarter_operating_profit")] decimal? PenultimateQuarterOperatingProfit,
    [property: JsonPropertyName("last_year_same_quarter_operating_profit")] decimal? LastYearSameQuarterOperatingProfit,
    [property: JsonPropertyName("last_quarter_net_profit_margin")] decimal? LastQuarterNetProfitMargin,
    [property: JsonPropertyName("penultimate_quarter_net_profit_margin")] decimal? PenultimateQuarterNetProfitMargin,
    [property: JsonPropertyName("last_year_same_quarter_net_profit_margin")] decimal? LastYearSameQuarterNetProfitMargin,
    [property: JsonPropertyName("last_quarter_gross_profit_margin")] decimal? LastQuarterGrossProfitMargin,
    [property: JsonPropertyName("penultimate_quarter_gross_profit_margin")] decimal? PenultimateQuarterGrossProfitMargin,
    [property: JsonPropertyName("last_year_same_quarter_gross_profit_margin")] decimal? LastYearSameQuarterGrossProfitMargin,
    [property: JsonPropertyName("last_quarter_operating_profit_margin")] decimal? LastQuarterOperatingProfitMargin,
    [property: JsonPropertyName("penultimate_quarter_operating_profit_margin")] decimal? PenultimateQuarterOperatingProfitMargin,
    [property: JsonPropertyName("last_year_same_quarter_operating_profit_margin")] decimal? LastYearSameQuarterOperatingProfitMargin,
    [property: JsonPropertyName("average_4_quarter_sale")] decimal? Average4QuarterSale,
    [property: JsonPropertyName("last_month_sale")] decimal? LastMonthSale,
    [property: JsonPropertyName("penultimate_month_sale")] decimal? PenultimateMonthSale,
    [property: JsonPropertyName("last_year_same_month_sale")] decimal? LastYearSameMonthSale,
    [property: JsonPropertyName("average_12_month_sale")] decimal? Average12MonthSale,
    [property: JsonPropertyName("pe")] decimal? Pe,
    [property: JsonPropertyName("ps")] decimal? Ps,
    [property: JsonPropertyName("last_quarter_date")] string? LastQuarterDate,
    [property: JsonPropertyName("last_month_sale_date")] string? LastMonthSaleDate);
