namespace FinancialCopilot.Domain.Identity;

public sealed class User
{
    public User(Guid id, Guid tenantId, string externalSubject, string email)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(externalSubject))
        {
            throw new ArgumentException("External subject is required.", nameof(externalSubject));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        Id = id;
        TenantId = tenantId;
        ExternalSubject = externalSubject.Trim();
        Email = email.Trim();
    }

    public Guid Id { get; }

    public Guid TenantId { get; }

    public string ExternalSubject { get; }

    public string Email { get; }
}
