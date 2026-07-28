namespace FinancialCopilot.Domain.Identity;

public sealed class ApiClient
{
    public ApiClient(Guid id, Guid tenantId, string name, string keyHash, bool isActive = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("API client id is required.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("API client name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(keyHash))
        {
            throw new ArgumentException("API client key hash is required.", nameof(keyHash));
        }

        Id = id;
        TenantId = tenantId;
        Name = name.Trim();
        KeyHash = keyHash.Trim();
        IsActive = isActive;
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string Name { get; }

    public string KeyHash { get; }

    public bool IsActive { get; }
}
