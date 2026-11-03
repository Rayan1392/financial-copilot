using FinancialCopilot.Domain.Identity.TahlilApp;

namespace FinancialCopilot.UnitTests;

public sealed class TahlilAppDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, string> PlanMap = new Dictionary<string, string>
    {
        ["Free"] = "Free",
        ["Plus"] = "Plus",
        ["Pro"] = "Pro"
    };

    [Theory]
    [InlineData("0012345678")]
    [InlineData("۰۰۱۲۳۴۵۶۷۸")]
    [InlineData("٠٠١٢٣٤٥٦٧٨")]
    public void NationalId_AcceptsTenDigitsAfterDigitNormalisation(string raw)
    {
        Assert.True(IranianNationalId.TryCreate(raw, out var id));
        Assert.Equal("0012345678", id!.Value);
        Assert.Equal("5678", id.Last4);
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("12345abcde")]
    [InlineData("")]
    public void NationalId_RejectsNonTenDigitValues(string raw) =>
        Assert.False(IranianNationalId.TryCreate(raw, out _));

    [Theory]
    [InlineData("09121234567", "+989121234567")]
    [InlineData("+989121234567", "+989121234567")]
    [InlineData("989121234567", "+989121234567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷", "+989121234567")]
    [InlineData("0912 123 4567", "+989121234567")]
    public void Mobile_NormalisesToE164(string raw, string expected)
    {
        Assert.True(IranianMobile.TryCreate(raw, out var mobile));
        Assert.Equal(expected, mobile!.Value);
    }

    [Theory]
    [InlineData("02112345678")]
    [InlineData("+14155550100")]
    [InlineData("0912123456")]
    public void Mobile_RejectsNonIranianMobileNumbers(string raw) =>
        Assert.False(IranianMobile.TryCreate(raw, out _));

    [Fact]
    public void PersonName_TrimsAndRejectsControlCharactersAndLength()
    {
        Assert.True(PersonName.TryCreate("  Ali ", out var name));
        Assert.Equal("Ali", name!.Value);
        Assert.False(PersonName.TryCreate("Ali\u0007", out _));
        Assert.False(PersonName.TryCreate(new string('a', 101), out _));
        Assert.False(PersonName.TryCreate("   ", out _));
    }

    [Fact]
    public void ExternalUserId_RejectsSeparatorsAndOverlongValues()
    {
        Assert.True(ExternalUserId.TryCreate("user-123.abc:9", out var id));
        Assert.Equal("user-123.abc:9", id!.Value);
        Assert.False(ExternalUserId.TryCreate("user 123", out _));
        Assert.False(ExternalUserId.TryCreate("u/../x", out _));
        Assert.False(ExternalUserId.TryCreate(new string('a', 129), out _));
    }

    [Fact]
    public void PersonalIdentifiers_AreRedactedWhenConvertedToString()
    {
        IranianNationalId.TryCreate("0012345678", out var nationalId);
        IranianMobile.TryCreate("09121234567", out var mobile);
        PersonName.TryCreate("Ali", out var name);

        Assert.Equal("[redacted]", nationalId!.ToString());
        Assert.Equal("[redacted]", mobile!.ToString());
        Assert.Equal("[redacted]", name!.ToString());
    }

    [Fact]
    public void Subscription_ActivePaidWithFutureExpiry_MapsToPlanWithWindowEnd()
    {
        var expiry = Now.AddDays(30);
        var activation = Now.AddDays(-5);
        var decision = TahlilAppSubscriptionPolicy.Decide("Plus", true, activation, expiry, Now, PlanMap);

        Assert.Equal("Plus", decision.PlanCode);
        Assert.Equal(expiry, decision.EffectiveTo);
        Assert.Equal("active-paid", decision.Reason);
        Assert.Equal(activation, decision.EffectiveFrom);
    }

    [Fact]
    public void Subscription_MissingActivationTimestampKeepsLegacyEligibilityButDoesNotCreateCreditAnchor()
    {
        var decision = TahlilAppSubscriptionPolicy.Decide("Plus", true, null, Now.AddDays(30), Now, PlanMap);
        Assert.Equal("Plus", decision.PlanCode);
        Assert.Null(decision.EffectiveFrom);
        Assert.Equal("active-paid-missing-effective-from", decision.Reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    public void Subscription_RejectsFutureOrAfterExpiryActivation(int activationDays)
    {
        var decision = TahlilAppSubscriptionPolicy.Decide(
            "Pro", true, Now.AddDays(activationDays), Now.AddDays(30), Now, PlanMap);
        Assert.Equal("Free", decision.PlanCode);
        Assert.Equal("invalid-effective-from", decision.Reason);
    }

    [Theory]
    [InlineData("Plus", false, 30, "inactive")]
    [InlineData("Pro", true, -1, "expired")]
    [InlineData("Pro", true, null, "missing-expiry")]
    [InlineData("Enterprise", true, 30, "unknown-plan")]
    [InlineData(null, true, 30, "unknown-plan")]
    [InlineData("Free", true, 30, "free")]
    public void Subscription_FailsClosedToFree(string? plan, bool active, int? expiryDays, string reason)
    {
        var expiry = expiryDays is null ? (DateTimeOffset?)null : Now.AddDays(expiryDays.Value);
        var decision = TahlilAppSubscriptionPolicy.Decide(plan, active, expiry, Now, PlanMap);

        Assert.Equal("Free", decision.PlanCode);
        Assert.Null(decision.EffectiveTo);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void Subscription_PlanCodeIsOnlyWhatThePolicyMapped_NeverTheRawExternalName()
    {
        var mapped = new Dictionary<string, string> { ["Gold"] = "Pro" };
        var decision = TahlilAppSubscriptionPolicy.Decide("Gold", true, Now.AddDays(5), Now, mapped);

        Assert.Equal("Pro", decision.PlanCode);
    }

    [Fact]
    public void ProfileMerge_ValidChangedValueOverwrites()
    {
        Assert.Equal(ProfileFieldAction.Overwrite,
            ProfileFieldMergePolicy.Decide("old", IncomingProfileField.Valid("new")));
    }

    [Fact]
    public void ProfileMerge_ValidIdenticalValueIsUnchanged()
    {
        Assert.Equal(ProfileFieldAction.Unchanged,
            ProfileFieldMergePolicy.Decide("same", IncomingProfileField.Valid("same")));
    }

    [Fact]
    public void ProfileMerge_ValidValueWithoutStoredValueOverwrites()
    {
        Assert.Equal(ProfileFieldAction.Overwrite,
            ProfileFieldMergePolicy.Decide(null, IncomingProfileField.Valid("first")));
    }

    [Theory]
    [MemberData(nameof(NonValidIncomingValues))]
    public void ProfileMerge_AbsentNullOrInvalidKeepsStoredValue(IncomingProfileField incoming)
    {
        Assert.Equal(ProfileFieldAction.KeepStored, ProfileFieldMergePolicy.Decide("stored", incoming));
        Assert.Equal(ProfileFieldAction.KeepStored, ProfileFieldMergePolicy.Decide(null, incoming));
    }

    public static IEnumerable<object[]> NonValidIncomingValues()
    {
        yield return [IncomingProfileField.Absent];
        yield return [IncomingProfileField.Null];
        yield return [IncomingProfileField.Invalid];
    }
}
