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
        ExtensionData.TryGetValue(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record NadpcoCompanyScopedRequest(string CompanyId);

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

public sealed record NadpcoMonthlyActivityEnvelope(
    string ProductSales,
    string ServiceSales);

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
