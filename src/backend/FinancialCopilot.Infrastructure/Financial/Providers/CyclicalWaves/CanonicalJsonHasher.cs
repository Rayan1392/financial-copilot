using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CanonicalJsonHasher : ICanonicalJsonHasher
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    public string ComputeHash(string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);

        using var document = JsonDocument.Parse(rawJson, DocumentOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = false,
                   SkipValidation = false
               }))
        {
            WriteCanonical(document.RootElement, writer);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();

                for (var index = 1; index < properties.Length; index++)
                {
                    if (string.Equals(
                            properties[index - 1].Name,
                            properties[index].Name,
                            StringComparison.Ordinal))
                    {
                        throw new JsonException(
                            $"Duplicate JSON property '{properties[index].Name}' is not canonicalizable.");
                    }
                }

                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                return;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                return;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                return;

            case JsonValueKind.Number:
                WriteCanonicalNumber(element, writer);
                return;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                return;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                return;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                return;

            default:
                throw new JsonException($"Unsupported JSON value kind '{element.ValueKind}'.");
        }
    }

    private static void WriteCanonicalNumber(JsonElement element, Utf8JsonWriter writer)
    {
        if (!element.TryGetDecimal(out var value))
        {
            throw new JsonException("JSON number is outside the supported finite decimal range.");
        }

        var canonical = value == decimal.Zero
            ? "0"
            : value.ToString("G29", CultureInfo.InvariantCulture);

        writer.WriteRawValue(canonical, skipInputValidation: false);
    }
}
