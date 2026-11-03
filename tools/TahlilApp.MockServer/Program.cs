using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException("TahlilApp.MockServer is development-only and refuses non-Development environments.");
var serviceKey = builder.Configuration["Mock:ServiceApiKey"];
if (string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Length < 32)
    throw new InvalidOperationException("Configure a random development API key of at least 32 characters in Mock:ServiceApiKey.");
var sapioOriginText = builder.Configuration["Mock:SapioFrontendOrigin"] ?? "https://localhost:5173";
if (!Uri.TryCreate(sapioOriginText, UriKind.Absolute, out var sapioOrigin) || sapioOrigin.Scheme != Uri.UriSchemeHttps ||
    sapioOrigin.Host is not ("localhost" or "127.0.0.1" or "[::1]") || sapioOrigin.AbsolutePath != "/" ||
    !string.IsNullOrEmpty(sapioOrigin.Query) || !string.IsNullOrEmpty(sapioOrigin.Fragment))
    throw new InvalidOperationException("Mock:SapioFrontendOrigin must be an HTTPS loopback origin.");
var bindUrls = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrWhiteSpace(bindUrls) || bindUrls.Split(';', StringSplitOptions.RemoveEmptyEntries).Any(value =>
        !Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
        endpoint.Host is not ("localhost" or "127.0.0.1" or "[::1]")))
    throw new InvalidOperationException("Bind the mock only to an HTTPS loopback address, such as https://localhost:7443.");

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<MockState>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.MapGet("/", () => Results.Content(MockUi.Html, "text/html; charset=utf-8"));
app.MapGet("/health", () => Results.Ok(new { status = "ok", environment = "Development" }));
app.MapGet("/dev/state", (MockState state) => Results.Ok(state.GetUiState()));

// Fake TahlilApp browser entry point. The selected persona is synthetic and the URL uses the real SapioAI /launch route.
app.MapPost("/api/financial-copilot/launch", (LaunchIssueRequest request, HttpRequest http, MockState state) =>
{
    if (!state.TryGetUser(request.ExternalUserId, out var user))
        return Results.BadRequest(new { error = "unknown_synthetic_user" });
    var code = state.Issue(user, request.Simulation ?? LaunchSimulation.Normal);
    var sapioOrigin = state.SapioFrontendOrigin;
    return Results.Ok(new { launchUrl = $"{sapioOrigin.TrimEnd('/')}/launch?code={Uri.EscapeDataString(code)}" });
});

app.MapPut("/dev/users/{externalUserId}", (string externalUserId, UserUpdateRequest request, MockState state) =>
{
    if (!state.UpdateUser(externalUserId, request)) return Results.NotFound();
    return Results.Ok(new { saved = true });
});

app.MapPut("/dev/failures", (FailureSettings settings, MockState state) =>
{
    state.SetFailures(settings);
    return Results.Ok(new { saved = true });
});

app.MapPost("/api/financial-copilot/session-exchange", async (HttpRequest request, MockState state, CancellationToken cancellationToken) =>
{
    if (!state.HasValidServiceKey(request.Headers["X-Api-Key"].ToString()))
        return Results.Unauthorized();
    if (!HasVersion2(request))
        return Results.StatusCode(StatusCodes.Status406NotAcceptable);

    var behavior = state.GetFailures().Exchange;
    if (behavior.DelayMilliseconds > 0)
        await Task.Delay(behavior.DelayMilliseconds, cancellationToken);
    if (behavior.StatusCode != 200)
        return Results.StatusCode(behavior.StatusCode);

    ExchangeRequest? body;
    try { body = await request.ReadFromJsonAsync<ExchangeRequest>(cancellationToken); }
    catch { return Results.BadRequest(new { error = "invalid_code" }); }
    if (string.IsNullOrWhiteSpace(body?.Code))
        return Results.BadRequest(new { error = "invalid_code" });

    var consumed = state.Consume(body.Code);
    if (consumed is null)
        return Results.BadRequest(new { error = "invalid_code" });
    return Results.Json(state.ExchangeResponse(consumed));
});

app.MapGet("/api/financial-copilot/users/{externalUserId}/status", async (string externalUserId, HttpRequest request, MockState state, CancellationToken cancellationToken) =>
{
    if (!state.HasValidServiceKey(request.Headers["X-Api-Key"].ToString()))
        return Results.Unauthorized();
    if (!HasVersion2(request))
        return Results.StatusCode(StatusCodes.Status406NotAcceptable);

    var behavior = state.GetFailures().Status;
    if (behavior.DelayMilliseconds > 0)
        await Task.Delay(behavior.DelayMilliseconds, cancellationToken);
    if (behavior.StatusCode != 200)
        return Results.StatusCode(behavior.StatusCode);
    if (!state.TryGetUser(externalUserId, out var user))
        return Results.NotFound();
    return Results.Json(state.StatusResponse(user));
});

app.Run();

static bool HasVersion2(HttpRequest request) =>
    request.Headers.TryGetValue("X-SapioAI-Contract-Version", out var version) && version.Count == 1 && version[0] == "2";

public sealed record LaunchIssueRequest(string ExternalUserId, LaunchSimulation? Simulation = null);
public sealed record ExchangeRequest(string Code);
public sealed record UserUpdateRequest(string Plan, bool IsActive, string Status, DateTimeOffset EffectiveFrom, DateTimeOffset? ExpiresAt, long Revision);
public sealed record FailureSettings(FailureBehavior Exchange, FailureBehavior Status);
public sealed record FailureBehavior(int StatusCode = 200, int DelayMilliseconds = 0);
public enum LaunchSimulation { Normal, Invalid, Expired, Reused }

public sealed record SyntheticUser(string ExternalUserId, string FirstName, string LastName, string NationalId, string Mobile,
    string Status, string Plan, bool IsActive, DateTimeOffset EffectiveFrom, DateTimeOffset? ExpiresAt, long Revision);

public sealed class MockState(IConfiguration configuration)
{
    private sealed record LaunchCodeEntry(string Hash, string ExternalUserId, DateTimeOffset ExpiresAt, bool Consumed);
    private readonly ConcurrentDictionary<string, LaunchCodeEntry> codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SyntheticUser> users = new(StringComparer.Ordinal)
    {
        ["dev-user-001"] = Default("dev-user-001", "آزمایشی", "یک"),
        ["dev-user-002"] = Default("dev-user-002", "کاربر", "دو"),
        ["dev-user-003"] = Default("dev-user-003", "نمونه", "سه")
    };
    private readonly object settingsLock = new();
    private FailureSettings failures = new(new(), new());

    public string SapioFrontendOrigin => configuration["Mock:SapioFrontendOrigin"] ?? "https://localhost:5173";

    public bool HasValidServiceKey(string candidate)
    {
        var expected = configuration["Mock:ServiceApiKey"];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(candidate)) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
        return CryptographicOperations.FixedTimeEquals(expectedHash, candidateHash);
    }

    public bool TryGetUser(string id, out SyntheticUser user) => users.TryGetValue(id, out user!);
    public FailureSettings GetFailures() { lock (settingsLock) return failures; }
    public void SetFailures(FailureSettings value)
    {
        if (!Valid(value.Exchange) || !Valid(value.Status)) throw new ArgumentException("Failure simulation values are out of range.");
        lock (settingsLock) failures = value;
    }

    public object GetUiState() => new
    {
        users = users.Values.OrderBy(user => user.ExternalUserId),
        failures = GetFailures(),
        sapioFrontendOrigin = SapioFrontendOrigin
    };

    public bool UpdateUser(string id, UserUpdateRequest request)
    {
        if (!users.TryGetValue(id, out var current)) return false;
        if (request.Plan is not ("Free" or "Plus" or "Pro") || request.Status is not ("Active" or "Suspended") || request.Revision < 0)
            throw new ArgumentException("Invalid synthetic subscription.");
        users[id] = current with
        {
            Plan = request.Plan,
            IsActive = request.IsActive,
            Status = request.Status,
            EffectiveFrom = request.EffectiveFrom.ToUniversalTime(),
            ExpiresAt = request.ExpiresAt?.ToUniversalTime(),
            Revision = request.Revision
        };
        return true;
    }

    public string Issue(SyntheticUser user, LaunchSimulation simulation)
    {
        var code = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(code);
        var entry = new LaunchCodeEntry(hash, user.ExternalUserId,
            simulation == LaunchSimulation.Expired ? DateTimeOffset.UtcNow.AddSeconds(-1) : DateTimeOffset.UtcNow.AddSeconds(60),
            simulation == LaunchSimulation.Reused);
        if (simulation != LaunchSimulation.Invalid) codes[hash] = entry;
        return code;
    }

    public SyntheticUser? Consume(string code)
    {
        var hash = Hash(code);
        while (codes.TryGetValue(hash, out var entry))
        {
            if (entry.Consumed || entry.ExpiresAt <= DateTimeOffset.UtcNow) return null;
            if (codes.TryUpdate(hash, entry with { Consumed = true }, entry))
                return users.TryGetValue(entry.ExternalUserId, out var user) ? user : null;
        }
        return null;
    }

    public object ExchangeResponse(SyntheticUser user) => new
    {
        user = new { user.ExternalUserId, user.FirstName, user.LastName, user.NationalId, user.Mobile, user.Status },
        subscription = new { plan = user.Plan, isActive = user.IsActive, effectiveFrom = Iso(user.EffectiveFrom), expiresAt = user.ExpiresAt is null ? null : Iso(user.ExpiresAt.Value), revision = user.Revision },
        permissions = Array.Empty<string>()
    };

    public object StatusResponse(SyntheticUser user) => new
    {
        user = new { user.ExternalUserId, user.Status },
        subscription = new { plan = user.Plan, isActive = user.IsActive, effectiveFrom = Iso(user.EffectiveFrom), expiresAt = user.ExpiresAt is null ? null : Iso(user.ExpiresAt.Value), revision = user.Revision }
    };

    private static bool Valid(FailureBehavior b) => b.StatusCode is 200 or 429 or 500 or 502 or 503 && b.DelayMilliseconds is >= 0 and <= 30000;
    private static SyntheticUser Default(string id, string first, string last) => new(id, first, last, "0000000000", "+989120000000", "Active", "Free", false, DateTimeOffset.UtcNow, null, 1);
    private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class MockUi
{
    public const string Html = """
<!doctype html><html lang="fa" dir="rtl"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>محیط آزمایشی ورود SapioAI</title>
<style>body{font:16px Tahoma,Arial;background:#f3f5f9;color:#1b2430;margin:0}.wrap{max-width:800px;margin:36px auto;padding:24px}.card{background:white;border:1px solid #dfe5ef;border-radius:16px;padding:24px;margin:16px 0;box-shadow:0 8px 28px #17233a0c}h1{font-size:25px}h2{font-size:18px}label{display:block;font-size:14px;margin:12px 0 6px}input,select,button{font:inherit;border:1px solid #ccd5e2;border-radius:8px;padding:10px;background:white;width:100%;box-sizing:border-box}button{background:#146bce;color:#fff;border:0;cursor:pointer;margin-top:18px;font-weight:bold}button:hover{background:#0b57ad}.grid{display:grid;grid-template-columns:1fr 1fr;gap:12px}.hint{color:#637083;font-size:13px}.danger{color:#a23424}pre{direction:ltr;text-align:left;white-space:pre-wrap;word-break:break-word;background:#f6f8fb;padding:12px;border-radius:8px}</style>
<div class="wrap"><h1>محیط توسعه ورود به SapioAI</h1><p class="hint">فقط برای توسعه محلی. همه کاربران و داده‌ها ساختگی هستند.</p>
<section class="card"><h2>کاربر آزمایشی و اشتراک</h2><label>کاربر</label><select id="user"></select><div class="grid"><div><label>طرح</label><select id="plan"><option>Free</option><option>Plus</option><option>Pro</option></select></div><div><label>وضعیت حساب</label><select id="status"><option>Active</option><option>Suspended</option></select></div><div><label>اشتراک فعال است</label><select id="active"><option value="true">بله</option><option value="false">خیر</option></select></div><div><label>Revision</label><input id="revision" type="number" min="0"></div><div><label>effectiveFrom (UTC)</label><input id="from" type="datetime-local"></div><div><label>expiresAt (UTC، خالی برای بدون انقضا)</label><input id="expires" type="datetime-local"></div></div>
<label>شبیه‌سازی LaunchCode</label><select id="simulation"><option value="Normal">کد معتبر (یک‌بار مصرف)</option><option value="Invalid">کد نامعتبر</option><option value="Expired">کد منقضی</option><option value="Reused">کد قبلاً مصرف‌شده</option></select>
<button id="launch">ورود به SapioAI</button><pre id="result" hidden></pre></section>
<section class="card"><h2>اختلال سرویس برای آزمون</h2><p class="hint">خطاها فقط پاسخ همین mock را تغییر می‌دهند.</p><div class="grid"><div><label>Exchange HTTP</label><select id="exStatus"><option>200</option><option>429</option><option>500</option><option>502</option><option>503</option></select></div><div><label>Exchange تأخیر (ms؛ بیش از ۵۰۰۰ برای timeout)</label><input id="exDelay" type="number" value="0" min="0" max="30000"></div><div><label>Status HTTP</label><select id="stStatus"><option>200</option><option>429</option><option>500</option><option>502</option><option>503</option></select></div><div><label>Status تأخیر (ms)</label><input id="stDelay" type="number" value="0" min="0" max="30000"></div></div><button id="save">ذخیره شبیه‌سازی</button><div id="saved" class="hint"></div></section></div>
<script>
const $=id=>document.getElementById(id), result=$('result');let users=[];
function localTime(d){return new Date(d).toISOString().slice(0,16)}
async function refresh(){const s=await fetch('/dev/state').then(r=>r.json());users=s.users;$('user').innerHTML=users.map(u=>`<option value="${u.externalUserId}">${u.firstName} ${u.lastName} (${u.externalUserId})</option>`).join('');const f=s.failures;$('exStatus').value=f.exchange.statusCode;$('exDelay').value=f.exchange.delayMilliseconds;$('stStatus').value=f.status.statusCode;$('stDelay').value=f.status.delayMilliseconds;loadUser()}
function loadUser(){const u=users.find(x=>x.externalUserId===$('user').value);if(!u)return;$('plan').value=u.plan;$('status').value=u.status;$('active').value=String(u.isActive);$('revision').value=u.revision;$('from').value=localTime(u.effectiveFrom);$('expires').value=u.expiresAt?localTime(u.expiresAt):''}
$('user').onchange=loadUser;
$('launch').onclick=async()=>{const ext=$('user').value;const data={plan:$('plan').value,isActive:$('active').value==='true',status:$('status').value,effectiveFrom:new Date($('from').value+'Z').toISOString(),expiresAt:$('expires').value?new Date($('expires').value+'Z').toISOString():null,revision:Number($('revision').value)};const saved=await fetch('/dev/users/'+encodeURIComponent(ext),{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});if(!saved.ok){result.hidden=false;result.textContent='ذخیره اشتراک ناموفق بود';return}const r=await fetch('/api/financial-copilot/launch',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({externalUserId:ext,simulation:$('simulation').value})});const payload=await r.json();if(!r.ok){result.hidden=false;result.textContent=JSON.stringify(payload,null,2);return}location.assign(payload.launchUrl)};
$('save').onclick=async()=>{const data={exchange:{statusCode:Number($('exStatus').value),delayMilliseconds:Number($('exDelay').value)},status:{statusCode:Number($('stStatus').value),delayMilliseconds:Number($('stDelay').value)}};const r=await fetch('/dev/failures',{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)});$('saved').textContent=r.ok?'تنظیمات ذخیره شد':'تنظیم نامعتبر است'};
refresh();
</script></html>
""";
}
