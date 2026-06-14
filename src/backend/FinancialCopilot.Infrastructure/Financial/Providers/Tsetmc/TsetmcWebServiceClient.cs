using System.Data;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

/// <summary>
/// SOAP client for the TSETMC TsePublicV2 ASMX web service.
/// Endpoint: http://service.tsetmc.com/WebService/TsePublicV2.asmx
/// Uses raw HTTP + SOAP 1.1 envelopes, no WCF dependency.
/// </summary>
public sealed class TsetmcWebServiceClient(
    HttpClient httpClient,
    IOptions<TsetmcWebServiceOptions> options,
    ILogger<TsetmcWebServiceClient> logger) : ITsetmcWebServiceClient
{
    private const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";
    private const string TsetmcNs = "http://tsetmc.com/";

    private readonly TsetmcWebServiceOptions _options = options.Value;

    public async Task<IReadOnlyList<TsetmcInstrumentRecord>> GetInstrumentsAsync(
        byte flow,
        CancellationToken cancellationToken)
    {
        var xml = BuildEnvelope("Instrument", $"<Flow>{flow}</Flow>");
        var ds = await InvokeAsync("Instrument", xml, cancellationToken);
        return ParseInstruments(ds);
    }

    public async Task<IReadOnlyList<TsetmcIntradayTradeRecord>> GetIntradayTradesAsync(
        byte flow,
        CancellationToken cancellationToken)
    {
        var xml = BuildEnvelope("TradeLastDay", $"<Flow>{flow}</Flow>");
        var ds = await InvokeAsync("TradeLastDay", xml, cancellationToken);
        return ParseIntradayTrades(ds);
    }

    public async Task<IReadOnlyList<TsetmcDailyTradeRecord>> GetDailyTradesAsync(
        DateOnly date,
        byte flow,
        CancellationToken cancellationToken)
    {
        var selDate = date.ToString("yyyyMMdd");
        var xml = BuildEnvelope("TradeOneDay", $"<SelDate>{selDate}</SelDate><Flow>{flow}</Flow>");
        var ds = await InvokeAsync("TradeOneDay", xml, cancellationToken);
        return ParseDailyTrades(ds, date);
    }

    public async Task<IReadOnlyList<TsetmcDailyIndexRecord>> GetDailyIndicesAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var dEven = date.ToString("yyyyMMdd");
        var xml = BuildEnvelope("IndexB2", $"<DEven>{dEven}</DEven>");
        var ds = await InvokeAsync("IndexB2", xml, cancellationToken);
        return ParseDailyIndices(ds, date);
    }

    public async Task<IReadOnlyList<TsetmcIntradayIndexRecord>> GetIntradayIndicesAsync(
        byte flow,
        CancellationToken cancellationToken)
    {
        var xml = BuildEnvelope("IndexB1LastDayLastData", $"<Flow>{flow}</Flow>");
        var ds = await InvokeAsync("IndexB1LastDayLastData", xml, cancellationToken);
        return ParseIntradayIndices(ds);
    }

    // --- private helpers ---

    private string BuildEnvelope(string methodName, string bodyContent)
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="{SoapNs}">
              <soap:Body>
                <{methodName} xmlns="{TsetmcNs}">
                  <UserName>{SecurityElement(Escape(_options.UserName))}</UserName>
                  <Password>{SecurityElement(Escape(_options.Password))}</Password>
                  {bodyContent}
                </{methodName}>
              </soap:Body>
            </soap:Envelope>
            """;
    }

    private async Task<DataSet> InvokeAsync(
        string action,
        string soapEnvelope,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
                content.Headers.ContentType!.CharSet = "utf-8";
                content.Headers.Add("SOAPAction", $"{TsetmcNs}{action}");

                using var response = await httpClient.PostAsync(httpClient.BaseAddress, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
                return ExtractDataSet(responseXml, action);
            }
            catch (FinancialProviderException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.RetryCount)
            {
                logger.LogWarning(
                    ex,
                    "TSETMC {Action} attempt {Attempt}/{Max} failed; retrying.",
                    action, attempt, _options.RetryCount);
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.Timeout,
                    $"TSETMC {action} timed out.",
                    ex);
            }
            catch (Exception ex)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.RemoteUnavailable,
                    $"TSETMC {action} failed after {_options.RetryCount} attempts.",
                    ex);
            }
        }
    }

    private static DataSet ExtractDataSet(string responseXml, string action)
    {
        var doc = new XmlDocument();
        doc.LoadXml(responseXml);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("soap", SoapNs);
        ns.AddNamespace("tns", TsetmcNs);

        var resultNode = doc.SelectSingleNode($"//tns:{action}Result", ns);
        if (resultNode is null)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable,
                $"TSETMC {action}: result node not found in response.");
        }

        var ds = new DataSet();
        ds.ReadXml(new XmlNodeReader(resultNode));
        return ds;
    }

    private static IReadOnlyList<TsetmcInstrumentRecord> ParseInstruments(DataSet ds)
    {
        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return [];

        var result = new List<TsetmcInstrumentRecord>(ds.Tables[0].Rows.Count);
        foreach (DataRow r in ds.Tables[0].Rows)
        {
            var insCode = ParseLong(r, "InsCode");
            if (insCode == 0) continue;

            result.Add(new TsetmcInstrumentRecord(
                InsCode: insCode,
                InstrumentId: ParseString(r, "InstrumentID"),
                Symbol: ParseString(r, "CValMne"),
                SymbolFull: ParseString(r, "LVal18AFC"),
                CompanyName: ParseString(r, "LSoc30"),
                MarketCode: ParseString(r, "YMarNSC"),
                InstrumentKind: ParseString(r, "CGdSVal"),
                InstrumentGroupCode: ParseString(r, "CGrValCot"),
                Valid: ParseString(r, "Valid") == "1",
                ListingDate: ParseDateInt(r, "DInMar"),
                TotalShares: ParseDecimal(r, "ZTitad")));
        }
        return result;
    }

    private static IReadOnlyList<TsetmcIntradayTradeRecord> ParseIntradayTrades(DataSet ds)
    {
        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return [];

        var result = new List<TsetmcIntradayTradeRecord>(ds.Tables[0].Rows.Count);
        foreach (DataRow r in ds.Tables[0].Rows)
        {
            var insCode = ParseLong(r, "InsCode");
            if (insCode == 0) continue;

            result.Add(new TsetmcIntradayTradeRecord(
                InsCode: insCode,
                TradingDate: ParseDateInt(r, "DEven"),
                TradingTime: ParseTimeInt(r, "HEven"),
                TotalTransactions: ParseDecimal(r, "ZTotTran"),
                Volume: ParseDecimal(r, "QTotTran5J"),
                TotalCapital: ParseDecimal(r, "QTotCap"),
                ClosingPrice: ParseDecimal(r, "PClosing"),
                LastTradedPrice: ParseDecimal(r, "PDrCotVal"),
                PriceChange: ParseDecimal(r, "PriceChange"),
                PriceMin: ParseDecimal(r, "PriceMin"),
                PriceMax: ParseDecimal(r, "PriceMax"),
                PriceFirst: ParseDecimal(r, "PriceFirst"),
                PriceYesterday: ParseDecimal(r, "PriceYesterday")));
        }
        return result;
    }

    private static IReadOnlyList<TsetmcDailyTradeRecord> ParseDailyTrades(DataSet ds, DateOnly date)
    {
        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return [];

        var result = new List<TsetmcDailyTradeRecord>(ds.Tables[0].Rows.Count);
        foreach (DataRow r in ds.Tables[0].Rows)
        {
            var insCode = ParseLong(r, "InsCode");
            if (insCode == 0) continue;

            result.Add(new TsetmcDailyTradeRecord(
                InsCode: insCode,
                Symbol: ParseString(r, "LVal18AFC"),
                TradingDate: date,
                ClosingPrice: ParseDecimal(r, "PClosing"),
                LastTradedPrice: ParseDecimal(r, "PDrCotVal"),
                PriceYesterday: ParseDecimal(r, "PriceYesterday"),
                PriceFirst: ParseDecimal(r, "PriceFirst"),
                PriceMin: ParseDecimal(r, "PriceMin"),
                PriceMax: ParseDecimal(r, "PriceMax"),
                PriceChange: ParseDecimal(r, "PriceChange"),
                TotalTransactions: ParseDecimal(r, "ZTotTran"),
                Volume: ParseDecimal(r, "QTotTran5J"),
                TotalCapital: ParseDecimal(r, "QTotCap")));
        }
        return result;
    }

    private static IReadOnlyList<TsetmcDailyIndexRecord> ParseDailyIndices(DataSet ds, DateOnly date)
    {
        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return [];

        var result = new List<TsetmcDailyIndexRecord>(ds.Tables[0].Rows.Count);
        foreach (DataRow r in ds.Tables[0].Rows)
        {
            var insCode = ParseLong(r, "InsCode");
            if (insCode == 0) continue;

            result.Add(new TsetmcDailyIndexRecord(
                InsCode: insCode,
                IndexDate: date,
                Value: ParseDecimal(r, "xNivInuClMresIbs"),
                High: ParseNullableDecimal(r, "xNivInuPhMresIbs"),
                Low: ParseNullableDecimal(r, "xNivInuPbMresIbs"),
                ChangePercent: ParseNullableDecimal(r, "XVarDrInuClV")));
        }
        return result;
    }

    private static IReadOnlyList<TsetmcIntradayIndexRecord> ParseIntradayIndices(DataSet ds)
    {
        if (ds.Tables.Count == 0 || ds.Tables[0].Rows.Count == 0)
            return [];

        var result = new List<TsetmcIntradayIndexRecord>(ds.Tables[0].Rows.Count);
        foreach (DataRow r in ds.Tables[0].Rows)
        {
            var insCode = ParseLong(r, "insCode"); // lowercase in IndexB1 response
            if (insCode == 0) continue;

            result.Add(new TsetmcIntradayIndexRecord(
                InsCode: insCode,
                IndexDate: ParseDateInt(r, "DEven"),
                IndexTime: ParseTimeInt(r, "HEven"),
                Value: ParseDecimal(r, "XDrNivJIdx004"),
                ChangePercent: ParseNullableDecimal(r, "XVarIdxJ")));
        }
        return result;
    }

    // --- field parsing helpers ---

    private static string ParseString(DataRow r, string col) =>
        r.Table.Columns.Contains(col) ? r[col]?.ToString() ?? string.Empty : string.Empty;

    private static long ParseLong(DataRow r, string col)
    {
        if (!r.Table.Columns.Contains(col)) return 0;
        return long.TryParse(r[col]?.ToString(), out var v) ? v : 0;
    }

    private static decimal ParseDecimal(DataRow r, string col)
    {
        if (!r.Table.Columns.Contains(col)) return 0;
        return decimal.TryParse(r[col]?.ToString(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static decimal? ParseNullableDecimal(DataRow r, string col)
    {
        if (!r.Table.Columns.Contains(col)) return null;
        var s = r[col]?.ToString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static DateOnly ParseDateInt(DataRow r, string col)
    {
        var s = r.Table.Columns.Contains(col) ? r[col]?.ToString() : null;
        if (s is null || s.Length < 8) return DateOnly.MinValue;
        if (int.TryParse(s, out var n))
        {
            var y = n / 10000;
            var m = (n % 10000) / 100;
            var d = n % 100;
            if (y > 1000 && m >= 1 && m <= 12 && d >= 1 && d <= 31)
                return new DateOnly(y, m, d);
        }
        return DateOnly.MinValue;
    }

    private static TimeOnly ParseTimeInt(DataRow r, string col)
    {
        var s = r.Table.Columns.Contains(col) ? r[col]?.ToString() : null;
        if (s is null) return TimeOnly.MinValue;
        if (int.TryParse(s, out var n))
        {
            var h = n / 10000;
            var min = (n % 10000) / 100;
            var sec = n % 100;
            if (h >= 0 && h < 24 && min >= 0 && min < 60 && sec >= 0 && sec < 60)
                return new TimeOnly(h, min, sec);
        }
        return TimeOnly.MinValue;
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    // Prevent XML injection via credentials by returning the already-escaped value as a string.
    private static string SecurityElement(string escaped) => escaped;
}
