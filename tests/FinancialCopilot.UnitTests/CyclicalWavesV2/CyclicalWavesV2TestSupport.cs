using System.Net;
using System.Text;
using System.Text.Json;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

internal static class CyclicalWavesV2Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CyclicalWavesV2", "Fixtures", name), Encoding.UTF8);

    public static JsonDocument Parse(string name) => JsonDocument.Parse(Read(name));

    /// <summary>An unsigned JWT whose payload carries only <c>exp</c>; enough for expiry timing.</summary>
    public static string Jwt(DateTimeOffset expiresAt)
    {
        static string Encode(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode($"{{\"exp\":{expiresAt.ToUnixTimeSeconds()}}}")}.sig";
    }
}

/// <summary>Controllable clock; <see cref="Advance"/> also completes pending delays that are due.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset value) => _now = value;
}

/// <summary>Records every request and answers from a queue or a function.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _count;
    public List<(HttpMethod Method, string Uri, string? Body, Dictionary<string, string> Headers)> Requests { get; } = [];

    public int Count => Volatile.Read(ref _count);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        int index;
        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body, headers));
            index = Interlocked.Increment(ref _count) - 1;
        }

        return respond(request, index);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

internal sealed class SingleClientFactory(HttpMessageHandler handler, string baseAddress) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };
}
