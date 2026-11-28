using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

/// <summary>
/// Streamed operations: the response body is read row by row instead of being loaded into an
/// XmlDocument and a DataSet. Used for large responses (derivative instruments, order book).
/// </summary>
public sealed partial class TsetmcWebServiceClient : ITsetmcOptionWebServiceClient
{
    private const string DiffgramNs = "urn:schemas-microsoft-com:xml-diffgram-v1";

    public Task<IReadOnlyList<TsetmcOptionRecord>> GetOptionContractsAsync(CancellationToken cancellationToken) =>
        InvokeStreamedAsync("Option", BuildEnvelope("Option", string.Empty), MapOption, cancellationToken);

    public Task<IReadOnlyList<TsetmcBestLimitRecord>> GetBestLimitsAsync(byte flow, CancellationToken cancellationToken) =>
        InvokeStreamedAsync("BestLimitsAllIns", BuildEnvelope("BestLimitsAllIns", $"<Flow>{flow}</Flow>"),
            MapBestLimit, cancellationToken);

    /// <summary>
    /// Instruments of a derivative flow. The provider returns every contract ever listed (tens of
    /// megabytes, mostly expired), so only currently valid listed option contracts are kept, and
    /// the Persian trading symbol is used as the symbol because the Latin mnemonic of a contract
    /// is not what traders or the daily trade feed call it.
    /// </summary>
    private Task<IReadOnlyList<TsetmcInstrumentRecord>> GetDerivativeInstrumentsAsync(
        byte flow,
        CancellationToken cancellationToken) =>
        InvokeStreamedAsync("Instrument", BuildEnvelope("Instrument", $"<Flow>{flow}</Flow>"),
            MapListedOptionInstrument, cancellationToken);

    internal static TsetmcOptionRecord? MapOption(IReadOnlyDictionary<string, string> row)
    {
        var insCode = Long(row, "InsCode");
        if (insCode == 0) return null;

        return new TsetmcOptionRecord(
            InsCode: insCode,
            InstrumentId: Text(row, "InstrumentID"),
            OpenPositions: Long(row, "BuyOP"),
            YesterdayOpenPositions: Long(row, "YesterdayOP"),
            ContractSize: (int)Math.Clamp(Long(row, "ContractSize"), int.MinValue, int.MaxValue),
            StrikePrice: Decimal(row, "StrikePrice"),
            UnderlyingInsCode: Long(row, "UAInsCode"),
            BeginDate: Date(row, "BeginDate"),
            EndDate: Date(row, "EndDate"));
    }

    internal static TsetmcBestLimitRecord? MapBestLimit(IReadOnlyDictionary<string, string> row)
    {
        var insCode = Long(row, "InsCode");
        if (insCode == 0) return null;

        return new TsetmcBestLimitRecord(
            InsCode: insCode,
            Level: (int)Math.Clamp(Long(row, "number"), 0, 255),
            BidVolume: Long(row, "QTitMeDem"),
            BidOrderCount: (int)Math.Clamp(Long(row, "ZOrdMeDem"), 0, int.MaxValue),
            BidPrice: Decimal(row, "PMeDem"),
            AskPrice: Decimal(row, "PMeOf"),
            AskOrderCount: (int)Math.Clamp(Long(row, "ZOrdMeOf"), 0, int.MaxValue),
            AskVolume: Long(row, "QTitMeOf"),
            Time: Time(row, "HEven"));
    }

    internal static TsetmcInstrumentRecord? MapListedOptionInstrument(IReadOnlyDictionary<string, string> row)
    {
        var insCode = Long(row, "InsCode");
        var isin = Text(row, "InstrumentID");
        var persianSymbol = Text(row, "LVal18AFC");
        if (insCode == 0 || Text(row, "Valid") != "1" || persianSymbol.Length == 0 ||
            TsetmcOptionIsin.SideOf(isin) is null)
            return null;

        return new TsetmcInstrumentRecord(
            InsCode: insCode,
            InstrumentId: isin,
            Symbol: persianSymbol,
            SymbolFull: persianSymbol,
            CompanyName: Text(row, "LVal30") is { Length: > 0 } contractName ? contractName : Text(row, "LSoc30"),
            MarketCode: Text(row, "YMarNSC"),
            InstrumentKind: Text(row, "CGdSVal"),
            InstrumentGroupCode: Text(row, "CGrValCot"),
            Valid: true,
            ListingDate: Date(row, "DInMar"),
            TotalShares: Decimal(row, "ZTitad"));
    }

    private async Task<IReadOnlyList<T>> InvokeStreamedAsync<T>(
        string action,
        string soapEnvelope,
        Func<IReadOnlyDictionary<string, string>, T?> map,
        CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, httpClient.BaseAddress)
                {
                    Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml")
                };
                request.Content.Headers.ContentType!.CharSet = "utf-8";
                request.Content.Headers.Add("SOAPAction", $"{TsetmcNs}{action}");

                using var response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await ReadRowsAsync(body, action, map, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
            catch (TaskCanceledException ex)
            {
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.Timeout,
                    $"TSETMC {action} timed out.",
                    ex);
            }
            catch (Exception ex)
            {
                // Includes the provider's HTML error page (HTTP 403 or a body that is not SOAP):
                // a failure, never an empty result.
                throw new FinancialProviderException(
                    FinancialProviderErrorCode.RemoteUnavailable,
                    $"TSETMC {action} failed after {_options.RetryCount} attempts.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Reads DataSet diffgram rows from a SOAP response. A row is an element carrying a
    /// diffgr:id attribute; its child elements are the columns.
    /// </summary>
    internal static async Task<IReadOnlyList<T>> ReadRowsAsync<T>(
        Stream body,
        string action,
        Func<IReadOnlyDictionary<string, string>, T?> map,
        CancellationToken cancellationToken) where T : class
    {
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = true
        };
        using var reader = XmlReader.Create(body, settings);

        var result = new List<T>();
        var sawResult = false;
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasNode = await reader.ReadAsync();
        while (hasNode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (!sawResult)
                {
                    sawResult = reader.LocalName == $"{action}Result" && reader.NamespaceURI == TsetmcNs;
                }
                else if (reader.GetAttribute("id", DiffgramNs) is not null)
                {
                    // ReadFromAsync consumes the whole row and leaves the reader on the node after
                    // it, so the loop must not advance again.
                    var element = (XElement)await XNode.ReadFromAsync(reader, cancellationToken);
                    row.Clear();
                    foreach (var column in element.Elements())
                        row[column.Name.LocalName] = column.Value.Trim();
                    if (map(row) is { } mapped) result.Add(mapped);
                    hasNode = !reader.EOF;
                    continue;
                }
            }

            hasNode = await reader.ReadAsync();
        }

        if (!sawResult)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.RemoteUnavailable,
                $"TSETMC {action}: result node not found in response.");
        }

        return result;
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) ? value : string.Empty;

    private static long Long(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) &&
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static decimal Decimal(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) &&
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static DateOnly Date(IReadOnlyDictionary<string, string> row, string column) =>
        row.TryGetValue(column, out var value) &&
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.MinValue;

    private static TimeOnly Time(IReadOnlyDictionary<string, string> row, string column)
    {
        if (!row.TryGetValue(column, out var value) ||
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return TimeOnly.MinValue;

        var (hour, minute, second) = (n / 10000, n % 10000 / 100, n % 100);
        return hour is >= 0 and < 24 && minute is >= 0 and < 60 && second is >= 0 and < 60
            ? new TimeOnly(hour, minute, second)
            : TimeOnly.MinValue;
    }
}

/// <summary>
/// Reads the option side from a TSETMC instrument ISIN. Listed calls start with IRO9 (TSE) or
/// IROA (IFB); listed puts with IROF (TSE) or IROB (IFB). Verified against the symbol prefix
/// (ض call, ط put) on all 1,794 listed contracts on 2026-11-28.
/// </summary>
public static class TsetmcOptionIsin
{
    public static Application.FinancialData.Options.OptionSide? SideOf(string? isin)
    {
        var value = isin?.Trim().ToUpperInvariant() ?? string.Empty;
        if (value.StartsWith("IRO9", StringComparison.Ordinal) || value.StartsWith("IROA", StringComparison.Ordinal))
            return Application.FinancialData.Options.OptionSide.Call;
        if (value.StartsWith("IROF", StringComparison.Ordinal) || value.StartsWith("IROB", StringComparison.Ordinal))
            return Application.FinancialData.Options.OptionSide.Put;
        return null;
    }
}
