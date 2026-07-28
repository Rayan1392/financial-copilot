namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesTokenCache
{
    private readonly object _gate = new();
    private string? _token;
    private DateTimeOffset _expiresAt;

    public bool TryGetToken(DateTimeOffset now, out string token)
    {
        lock (_gate)
        {
            if (_token is not null && _expiresAt > now)
            {
                token = _token;
                return true;
            }

            token = string.Empty;
            return false;
        }
    }

    public void SetToken(string token, DateTimeOffset expiresAt)
    {
        lock (_gate)
        {
            _token = token;
            _expiresAt = expiresAt;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _token = null;
            _expiresAt = DateTimeOffset.MinValue;
        }
    }
}
