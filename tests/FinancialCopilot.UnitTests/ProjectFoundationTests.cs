using FinancialCopilot.Domain;

namespace FinancialCopilot.UnitTests;

public sealed class ProjectFoundationTests
{
    [Fact]
    public void DomainAssembly_IsAvailableForBusinessRules()
    {
        Assert.Equal("FinancialCopilot.Domain", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
