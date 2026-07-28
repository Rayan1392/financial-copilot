using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public sealed class NadpcoApiTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessTokenSnake { get; init; }

    [JsonPropertyName("accessToken")]
    public string? AccessTokenCamel { get; init; }

    [JsonPropertyName("token")]
    public string? TokenLower { get; init; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresInSnake { get; init; }

    [JsonPropertyName("expiresIn")]
    public int? ExpiresInCamel { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAtCamel { get; init; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? ExpirationLower { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? GetToken() =>
        FirstNonEmpty(AccessTokenSnake, AccessTokenCamel, TokenLower) ??
        TryGetString("access_token") ??
        TryGetString("accessToken") ??
        TryGetString("token");

    public DateTimeOffset GetExpiresAt(DateTimeOffset now, TimeSpan fallbackLifetime)
    {
        if (ExpiresAtCamel is { } expiresAtCamel)
        {
            return expiresAtCamel;
        }

        if (ExpirationLower is { } expirationLower)
        {
            return expirationLower;
        }

        var expiresIn = ExpiresInSnake ?? ExpiresInCamel ?? TryGetInt32("expires_in") ?? TryGetInt32("expiresIn");
        return expiresIn is > 0
            ? now.AddSeconds(expiresIn.Value)
            : now.Add(fallbackLifetime);
    }

    private string? TryGetString(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private int? TryGetInt32(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record NadpcoCompanyScopedRequest(string CompanyId);

public sealed record NadpcoApiMonthlyActivityRequest(
    [property: JsonPropertyName("companyIds")] IReadOnlyCollection<int> CompanyIds,
    [property: JsonPropertyName("fromDate")] string? FromDate,
    [property: JsonPropertyName("toDate")] string? ToDate,
    [property: JsonPropertyName("outputType")] int? OutputType);

public sealed record NadpcoApiStatementRequest(
    [property: JsonPropertyName("companyIds")] IReadOnlyCollection<int> CompanyIds,
    [property: JsonPropertyName("items")] IReadOnlyCollection<int> Items);

public sealed record NadpcoApiFundamentalIndexRequest(
    [property: JsonPropertyName("companyIds")] IReadOnlyCollection<int> CompanyIds,
    [property: JsonPropertyName("companyIndexIds")] IReadOnlyCollection<int> CompanyIndexIds);

public sealed record NadpcoFinancialStatementEnvelope(
    string BalanceSheet,
    string IncomeStatement,
    string CashFlow);

public sealed record NadpcoApiStatementRecord(
    [property: JsonPropertyName("statementID")] long StatementID,
    [property: JsonPropertyName("com_ID")] int ComID,
    [property: JsonPropertyName("bourseSymbol")] string? BourseSymbol,
    [property: JsonPropertyName("fullTitle")] string? FullTitle,
    [property: JsonPropertyName("periodType")] byte PeriodType,
    [property: JsonPropertyName("fiscalYearEnd")] DateTimeOffset FiscalYearEnd,
    [property: JsonPropertyName("jalaliFiscalYearEnd")] string? JalaliFiscalYearEnd,
    [property: JsonPropertyName("periodEnd")] DateTimeOffset PeriodEnd,
    [property: JsonPropertyName("jalaliPeriodEnd")] string? JalaliPeriodEnd,
    [property: JsonPropertyName("anouncementDate")] DateTimeOffset? AnouncementDate,
    [property: JsonPropertyName("jalaliAnouncementDate")] string? JalaliAnouncementDate,
    [property: JsonPropertyName("isAudited")] bool IsAudited,
    [property: JsonPropertyName("isRepresented")] bool IsRepresented,
    [property: JsonPropertyName("isComposing")] bool IsComposing,
    [property: JsonPropertyName("items")] IReadOnlyList<NadpcoApiStatementLineItem> Items);

public sealed record NadpcoApiStatementLineItem(
    [property: JsonPropertyName("itemID")] int ItemID,
    [property: JsonPropertyName("itemTitle")] string? ItemTitle,
    [property: JsonPropertyName("amount")] decimal? Amount,
    [property: JsonPropertyName("amountUnit")] string? AmountUnit);

public sealed record NadpcoApiFundamentalIndexRecord(
    [property: JsonPropertyName("comBS_ID")] long ComBSID,
    [property: JsonPropertyName("comId")] int ComID,
    [property: JsonPropertyName("comTitle")] string? ComTitle,
    [property: JsonPropertyName("periodType")] byte PeriodType,
    [property: JsonPropertyName("jalaliFiscalYearEnd")] string? JalaliFiscalYearEnd,
    [property: JsonPropertyName("jalaliPeriodEnd")] string? JalaliPeriodEnd,
    [property: JsonPropertyName("jalaliAnouncementDate")] string? JalaliAnouncementDate,
    [property: JsonPropertyName("isAudited")] bool IsAudited,
    [property: JsonPropertyName("isRepresented")] bool IsRepresented,
    [property: JsonPropertyName("isComposing")] bool IsComposing,
    [property: JsonPropertyName("indexes")] IReadOnlyList<NadpcoApiFundamentalIndexItem> Indexes);

public sealed record NadpcoApiFundamentalIndexItem(
    [property: JsonPropertyName("companyIndexId")] int CompanyIndexId,
    [property: JsonPropertyName("companyIndexTitle")] string? CompanyIndexTitle,
    [property: JsonPropertyName("companyIndexGroupId")] int? CompanyIndexGroupId,
    [property: JsonPropertyName("companyIndexGroupTitle")] string? CompanyIndexGroupTitle,
    [property: JsonPropertyName("companyIndexValue")] decimal? CompanyIndexValue,
    [property: JsonPropertyName("companyIndexUnit")] string? CompanyIndexUnit);

/// <summary>
/// Current multi-output-type envelope (spec 059). Each <c>ProductSalesType{N}</c> field holds the
/// raw JSON for outputTypeId N (0–4). <c>ServiceSales</c> has no output-type parameter.
/// Null/missing fields mean the fetch for that output type was skipped or failed.
/// </summary>
public sealed record NadpcoMonthlyActivityEnvelope(
    string? ProductSalesType0,
    string? ProductSalesType1,
    string? ProductSalesType2,
    string? ProductSalesType3,
    string? ProductSalesType4,
    string ServiceSales);

/// <summary>
/// Legacy 2-field envelope written before spec 059. Deserialized for backward compatibility so old
/// stored payloads normalize without error. Treated as output-type 0 with <c>OutputType = null</c>.
/// </summary>
public sealed record NadpcoMonthlyActivityLegacyEnvelope(
    string? ProductSales,
    string? ServiceSales);

public sealed class NadpcoApiProductSalesRecord
{
    [JsonPropertyName("activityID")]
    public long? ActivityID { get; init; }

    [JsonPropertyName("monthlyActivityID")]
    public long? MonthlyActivityID { get; init; }

    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("com_ID")]
    public int? ComIDUnderscore { get; init; }

    [JsonPropertyName("comId")]
    public int? ComIdCamel { get; init; }

    [JsonPropertyName("companyId")]
    public int? CompanyId { get; init; }

    [JsonPropertyName("bourseSymbol")]
    public string? BourseSymbol { get; init; }

    [JsonPropertyName("comTitle")]
    public string? ComTitle { get; init; }

    [JsonPropertyName("industryID")]
    public int? IndustryID { get; init; }

    [JsonPropertyName("industryTitle")]
    public string? IndustryTitle { get; init; }

    [JsonPropertyName("tseCode")]
    public string? TseCode { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("month")]
    public byte? Month { get; init; }

    [JsonPropertyName("fiscalYearEnd")]
    public string? FiscalYearEnd { get; init; }

    [JsonPropertyName("jalaliFiscalYearEnd")]
    public string? JalaliFiscalYearEnd { get; init; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; init; }

    [JsonPropertyName("jalaliPublishDate")]
    public string? JalaliPublishDate { get; init; }

    [JsonPropertyName("outputType")]
    public int? OutputType { get; init; }

    [JsonPropertyName("outputTypeTitle")]
    public string? OutputTypeTitle { get; init; }

    [JsonPropertyName("categoryID")]
    public int? CategoryID { get; init; }

    [JsonPropertyName("categoryTitle")]
    public string? CategoryTitle { get; init; }

    [JsonPropertyName("productId")]
    public long? ProductId { get; init; }

    [JsonPropertyName("productCode")]
    public string? ProductCode { get; init; }

    [JsonPropertyName("productTitle")]
    public string? ProductTitle { get; init; }

    [JsonPropertyName("productUnit")]
    public string? ProductUnit { get; init; }

    [JsonPropertyName("productionQuantity")]
    public decimal? ProductionQuantity { get; init; }

    [JsonPropertyName("productProduceAmount")]
    public decimal? ProductProduceAmount { get; init; }

    [JsonPropertyName("salesQuantity")]
    public decimal? SalesQuantity { get; init; }

    [JsonPropertyName("productSaleAmount")]
    public decimal? ProductSaleAmount { get; init; }

    [JsonPropertyName("salesRate")]
    public decimal? SalesRate { get; init; }

    [JsonPropertyName("productSaleRate")]
    public decimal? ProductSaleRate { get; init; }

    [JsonPropertyName("salesValue")]
    public decimal? SalesValue { get; init; }

    [JsonPropertyName("productSaleValue")]
    public decimal? ProductSaleValue { get; init; }

    /// <summary>
    /// Live v2 shape (verified 2026-06-10): one record per company carrying identity fields plus a
    /// nested <c>productSales</c> array whose items hold month/year and the per-product facts.
    /// Items reuse this record type; company identity is merged from the parent during
    /// normalization. Null/empty for the legacy flat shape.
    /// </summary>
    [JsonPropertyName("productSales")]
    public IReadOnlyList<NadpcoApiProductSalesRecord>? ProductSales { get; init; }

    /// <summary>Live v2 field name for the company TSE ticker.</summary>
    [JsonPropertyName("companyTSESymbol")]
    public string? CompanyTSESymbol { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public long? GetActivityId() => ActivityID ?? MonthlyActivityID ?? Id ?? TryGetInt64("monthlyActivityId");

    public int? GetCompanyId() => ComIDUnderscore ?? ComIdCamel ?? CompanyId ?? TryGetInt32("coID");

    public string? GetBourseSymbol() => FirstNonEmpty(BourseSymbol, CompanyTSESymbol);

    public string? GetCompanyTitle() => FirstNonEmpty(ComTitle, TryGetString("companyTitle"));

    public int? GetOutputType() => OutputType ?? TryGetInt32("outputTypeId");

    // Live v2 carries the instrument code as numeric "instCode"; legacy shapes use string "tseCode".
    public string? GetTseCode() =>
        FirstNonEmpty(TseCode, TryGetInt64("instCode")?.ToString(), TryGetString("instCode"));

    // Vendor uses 0 as a "no product id" placeholder (live data); fall through to the natural key.
    public long? GetProductId()
    {
        var id = ProductId ?? TryGetInt64("goodsID") ?? TryGetInt64("product_Id");
        return id is > 0 ? id : null;
    }

    public string? GetProductTitle() => FirstNonEmpty(ProductTitle, TryGetString("goodsTitle"), TryGetString("title"));

    public string? GetProductUnit() => FirstNonEmpty(ProductUnit, TryGetString("unit"), TryGetString("measureUnit"));

    public decimal? GetProductionQuantity() =>
        ProductionQuantity ?? ProductProduceAmount ?? TryGetDecimal("produceAmount") ?? TryGetDecimal("productionAmount");

    public decimal? GetSalesQuantity() =>
        SalesQuantity ?? ProductSaleAmount ?? TryGetDecimal("saleAmount") ?? TryGetDecimal("quantity");

    public decimal? GetSalesRate() => SalesRate ?? ProductSaleRate ?? TryGetDecimal("rate") ?? TryGetDecimal("saleRate");

    public decimal? GetSalesValue() =>
        SalesValue ?? ProductSaleValue ?? TryGetDecimal("value") ?? TryGetDecimal("saleValue") ?? TryGetDecimal("amount");

    public string? GetProductCode() => FirstNonEmpty(ProductCode, GetProductId()?.ToString());

    private string? TryGetString(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private int? TryGetInt32(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private long? TryGetInt64(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;

    private decimal? TryGetDecimal(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result)
            ? result
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed class NadpcoApiServiceSalesRecord
{
    [JsonPropertyName("activityID")]
    public long? ActivityID { get; init; }

    [JsonPropertyName("monthlyActivityID")]
    public long? MonthlyActivityID { get; init; }

    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("com_ID")]
    public int? ComIDUnderscore { get; init; }

    [JsonPropertyName("comId")]
    public int? ComIdCamel { get; init; }

    [JsonPropertyName("companyId")]
    public int? CompanyId { get; init; }

    [JsonPropertyName("bourseSymbol")]
    public string? BourseSymbol { get; init; }

    [JsonPropertyName("comTitle")]
    public string? ComTitle { get; init; }

    [JsonPropertyName("industryID")]
    public int? IndustryID { get; init; }

    [JsonPropertyName("industryTitle")]
    public string? IndustryTitle { get; init; }

    [JsonPropertyName("tseCode")]
    public string? TseCode { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("month")]
    public byte? Month { get; init; }

    [JsonPropertyName("fiscalYearEnd")]
    public string? FiscalYearEnd { get; init; }

    [JsonPropertyName("jalaliFiscalYearEnd")]
    public string? JalaliFiscalYearEnd { get; init; }

    [JsonPropertyName("publishDate")]
    public string? PublishDate { get; init; }

    [JsonPropertyName("jalaliPublishDate")]
    public string? JalaliPublishDate { get; init; }

    [JsonPropertyName("categoryID")]
    public int? CategoryID { get; init; }

    [JsonPropertyName("categoryTitle")]
    public string? CategoryTitle { get; init; }

    [JsonPropertyName("serviceId")]
    public long? ServiceId { get; init; }

    [JsonPropertyName("serviceCode")]
    public string? ServiceCode { get; init; }

    [JsonPropertyName("serviceTitle")]
    public string? ServiceTitle { get; init; }

    [JsonPropertyName("serviceUnit")]
    public string? ServiceUnit { get; init; }

    [JsonPropertyName("salesQuantity")]
    public decimal? SalesQuantity { get; init; }

    [JsonPropertyName("serviceSaleAmount")]
    public decimal? ServiceSaleAmount { get; init; }

    [JsonPropertyName("salesRate")]
    public decimal? SalesRate { get; init; }

    [JsonPropertyName("serviceSaleRate")]
    public decimal? ServiceSaleRate { get; init; }

    [JsonPropertyName("salesValue")]
    public decimal? SalesValue { get; init; }

    [JsonPropertyName("serviceSaleValue")]
    public decimal? ServiceSaleValue { get; init; }

    /// <summary>Live v3 field (verified 2026-06-10): the month's service revenue.</summary>
    [JsonPropertyName("revenueDuringThePeriod")]
    public decimal? RevenueDuringThePeriod { get; init; }

    /// <summary>Live v3 field name for the company TSE ticker.</summary>
    [JsonPropertyName("companyTSESymbol")]
    public string? CompanyTSESymbol { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public long? GetActivityId() => ActivityID ?? MonthlyActivityID ?? Id ?? TryGetInt64("monthlyActivityId");

    public int? GetCompanyId() => ComIDUnderscore ?? ComIdCamel ?? CompanyId ?? TryGetInt32("coID");

    public string? GetBourseSymbol() => FirstNonEmpty(BourseSymbol, CompanyTSESymbol);

    public string? GetTseCode() =>
        FirstNonEmpty(TseCode, TryGetString("instCode"), TryGetInt64("instCode")?.ToString());

    public long? GetServiceId() => ServiceId ?? TryGetInt64("activityServiceID") ?? TryGetInt64("service_Id");

    public string? GetServiceTitle() => FirstNonEmpty(ServiceTitle, TryGetString("title"));

    public string? GetServiceUnit() => FirstNonEmpty(ServiceUnit, TryGetString("unit"), TryGetString("measureUnit"));

    public decimal? GetSalesQuantity() =>
        SalesQuantity ?? ServiceSaleAmount ?? TryGetDecimal("saleAmount") ?? TryGetDecimal("quantity");

    public decimal? GetSalesRate() => SalesRate ?? ServiceSaleRate ?? TryGetDecimal("rate") ?? TryGetDecimal("saleRate");

    public decimal? GetSalesValue() =>
        SalesValue ?? ServiceSaleValue ?? RevenueDuringThePeriod ??
        TryGetDecimal("value") ?? TryGetDecimal("saleValue") ?? TryGetDecimal("amount");

    public string? GetServiceCode() => FirstNonEmpty(ServiceCode, GetServiceId()?.ToString());

    private string? TryGetString(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private int? TryGetInt32(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private long? TryGetInt64(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;

    private decimal? TryGetDecimal(string propertyName) =>
        ExtensionData.TryGetValue(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result)
            ? result
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record NadpcoApiCompanyRecord(
    [property: JsonPropertyName("coID")] int CoID,
    [property: JsonPropertyName("coCode")] string? CoCode,
    [property: JsonPropertyName("coTitle")] string? CoTitle,
    [property: JsonPropertyName("coTitleEnglish")] string? CoTitleEnglish,
    [property: JsonPropertyName("coSymbol")] string? CoSymbol,
    [property: JsonPropertyName("coSymbolEnglish")] string? CoSymbolEnglish,
    [property: JsonPropertyName("floorID")] int? FloorID,
    [property: JsonPropertyName("floorTitle")] string? FloorTitle,
    [property: JsonPropertyName("industryID")] int? IndustryID,
    [property: JsonPropertyName("industryTitle")] string? IndustryTitle,
    [property: JsonPropertyName("tseCode")] string? TseCode,
    [property: JsonPropertyName("tseCIsinCode")] string? TseCIsinCode,
    [property: JsonPropertyName("tseSIsinCode")] string? TseSIsinCode,
    [property: JsonPropertyName("marketID")] int? MarketID,
    [property: JsonPropertyName("marketTitle")] string? MarketTitle,
    [property: JsonPropertyName("precedencyRight")] int? PrecedencyRight,
    [property: JsonPropertyName("acceptionDate")] string? AcceptionDate,
    [property: JsonPropertyName("acceptionDateGre")] string? AcceptionDateGre,
    [property: JsonPropertyName("enlistedDate")] string? EnlistedDate,
    [property: JsonPropertyName("enlistedDateGre")] string? EnlistedDateGre,
    [property: JsonPropertyName("ipoDate")] string? IpoDate,
    [property: JsonPropertyName("ipoDateGre")] string? IpoDateGre,
    [property: JsonPropertyName("fundTypeID")] int? FundTypeID,
    [property: JsonPropertyName("fundTypeTitle")] string? FundTypeTitle,
    [property: JsonPropertyName("coSymbolPinglish")] string? CoSymbolPinglish,
    [property: JsonPropertyName("nationalID")] string? NationalID,
    [property: JsonPropertyName("inExchange")] int? InExchange,
    [property: JsonPropertyName("establishmentDate")] string? EstablishmentDate,
    [property: JsonPropertyName("establishmentDateGre")] string? EstablishmentDateGre,
    [property: JsonPropertyName("businessStartDate")] string? BusinessStartDate,
    [property: JsonPropertyName("businessStartDateGre")] string? BusinessStartDateGre,
    [property: JsonPropertyName("registrationDate")] string? RegistrationDate,
    [property: JsonPropertyName("registrationDateGre")] string? RegistrationDateGre,
    [property: JsonPropertyName("registrationNumber")] string? RegistrationNumber,
    [property: JsonPropertyName("registrationProvince")] string? RegistrationProvince,
    [property: JsonPropertyName("registrationCity")] string? RegistrationCity,
    [property: JsonPropertyName("marketBoard")] string? MarketBoard);
