using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>Result of canonical serialization: the JSON payload and its SHA-256 checksum (hex).</summary>
public sealed record CodalDbSerializedPayload(string Json, string Checksum);

/// <summary>
/// Serializes a projected CodalDB result set into a <b>canonical</b> JSON string (rows ordered by a
/// stable key; record property order is fixed) and computes the SHA-256 checksum over the UTF-8
/// bytes, so unchanged source data always yields an identical checksum across runs — enabling the
/// existing checksum-dedup in the ingestion pipeline. Callers must also order any nested lists
/// (e.g. statement line items) deterministically.
/// </summary>
public static class CodalDbPayloadSerializer
{
    private static readonly JsonSerializerOptions CanonicalOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static CodalDbSerializedPayload Serialize<T, TKey>(IEnumerable<T> rows, Func<T, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(keySelector);

        var ordered = rows.OrderBy(keySelector).ToList();
        var json = JsonSerializer.Serialize(ordered, CanonicalOptions);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return new CodalDbSerializedPayload(json, checksum);
    }
}
