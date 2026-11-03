using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.AspNetCore.DataProtection;

namespace FinancialCopilot.UnitTests;

/// <summary>Profile PII survives a restart when the key ring is persisted outside the process (Design §17.1, AC-78).</summary>
public sealed class TahlilAppProfileProtectionTests : IDisposable
{
    private readonly string _keyRing = Directory.CreateTempSubdirectory("tahlilapp-ring-").FullName;

    [Fact]
    public void Ciphertext_DecryptsWithAFreshProviderOverTheSameKeyRing()
    {
        var before = Provider();
        var ciphertext = new TahlilAppProfileProtector(before)
            .Protect(TahlilAppProfileProtector.NationalIdPurpose, "0012345678");

        // A new provider instance simulates a process restart or redeployment over the same persisted ring.
        var after = new TahlilAppProfileProtector(Provider());

        Assert.NotEqual("0012345678", ciphertext);
        Assert.Equal("0012345678", after.TryUnprotect(TahlilAppProfileProtector.NationalIdPurpose, ciphertext));
    }

    [Fact]
    public void Ciphertext_IsBoundToItsPurpose()
    {
        var protector = new TahlilAppProfileProtector(Provider());
        var ciphertext = protector.Protect(TahlilAppProfileProtector.NationalIdPurpose, "0012345678");

        Assert.Null(protector.TryUnprotect(TahlilAppProfileProtector.MobilePurpose, ciphertext));
    }

    [Fact]
    public void UnreadableCiphertext_ReturnsNullInsteadOfThrowing()
    {
        var protector = new TahlilAppProfileProtector(Provider());

        Assert.Null(protector.TryUnprotect(TahlilAppProfileProtector.NationalIdPurpose, "not-a-valid-ciphertext"));
    }

    private IDataProtectionProvider Provider() =>
        DataProtectionProvider.Create(new DirectoryInfo(_keyRing), builder => builder.SetApplicationName("FinancialCopilot"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_keyRing, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temporary key ring.
        }
    }
}
