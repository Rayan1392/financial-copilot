using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests;

public sealed class CanonicalQueryEntityResolverTests
{
    [Fact]
    public async Task ExactTickerAndCompanyName_ResolveToTheSameCanonicalCompany()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "فولاد", "شرکت فولاد مبارکه", "steel");
        await db.SaveChangesAsync();
        var resolver = CreateResolver(db);

        var ticker = await resolver.ResolveMentionAsync("فولاد");
        var name = await resolver.ResolveMentionAsync("شرکت فولاد مبارکه");

        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(ticker).Entity.CanonicalId);
        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(name).Entity.CanonicalId);
    }

    [Fact]
    public async Task PresentationTerms_AreMissingAndNeverEntities()
    {
        await using var db = CreateDb();
        db.Companies.Add(AddCompany(db, "چارت", "شرکت چارت", "chart"));
        await db.SaveChangesAsync();
        var resolver = CreateResolver(db);

        var result = await resolver.ResolveMentionAsync("چارت");

        Assert.IsType<EntityResolutionResult.Missing>(result);
    }

    [Fact]
    public async Task AmbiguousAlias_ReturnsOrderedCandidatesWithoutResolution()
    {
        await using var db = CreateDb();
        AddCompany(db, "AAA", "Alpha One", "same");
        AddCompany(db, "BBB", "Beta Two", "same");
        await db.SaveChangesAsync();
        var resolver = CreateResolver(db);

        var result = Assert.IsType<EntityResolutionResult.Ambiguous>(await resolver.ResolveMentionAsync("same"));

        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(["AAA", "BBB"], result.Candidates.Select(candidate => candidate.Entity.DisplaySymbol));
    }

    [Fact]
    public async Task Typo_IsAnAmbiguousFuzzyCandidateAndNeverAutoResolved()
    {
        await using var db = CreateDb();
        AddCompany(db, "فولاد", "فولاد مبارکه", "foolad");
        await db.SaveChangesAsync();
        var resolver = CreateResolver(db);

        var result = await resolver.ResolveMentionAsync("فولادد");

        Assert.IsType<EntityResolutionResult.Ambiguous>(result);
    }

    [Fact]
    public async Task InterpretationWithChartAndFoolad_ResolvesFoolad()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "فولاد", "فولاد مبارکه", "foolad");
        await db.SaveChangesAsync();
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret("چارت روند فروش فولاد");

        var result = await CreateResolver(db).ResolveFromInterpretationAsync(interpretation);

        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public async Task ExactTickerWinsOverAnEarlierAmbiguousGenericMention()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "کچاد", "معدنی و صنعتی چادرملو", "kchad");
        AddCompany(db, "الف", "محصولات معدنی الف", "alpha");
        AddCompany(db, "ب", "محصولات معدنی ب", "beta");
        await db.SaveChangesAsync();
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret("محصولات کچاد") with
        {
            EntityMentions =
            [
                new EntityMention("محصولات", 0, 7),
                new EntityMention("کچاد", 8, 4)
            ]
        };

        var result = await CreateResolver(db).ResolveFromInterpretationAsync(interpretation);

        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public void ProductMixSemanticWordsAndKnownTypo_NeverBecomeEntityMentions()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry)
            .Interpret("رکیب فروش محصولات کچاد؟");

        Assert.Equal(["کچاد"], interpretation.EntityMentions.Select(item => item.Text));
    }

    [Theory]
    [InlineData("فولاد")]
    [InlineData("فولاد،")]
    [InlineData("فو‌لاد")]
    [InlineData("فولاد؟")]
    public async Task TickerCharacterVariants_ZwnjAndPunctuationResolveExactly(string mention)
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "فولاد", "فولاد مبارکه", "foolad");
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveMentionAsync(mention);

        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public async Task ArabicCharacterVariant_ResolvesToCanonicalTicker()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "کگل", "گل گهر", "kgol");
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveMentionAsync("كگل");

        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(result).Entity.CanonicalId);
    }

    [Fact]
    public async Task UnknownEntity_IsNotFoundRatherThanMissingOrNoData()
    {
        await using var db = CreateDb();
        db.Companies.Add(AddCompany(db, "فولاد", "فولاد مبارکه", "foolad"));
        await db.SaveChangesAsync();

        var result = await CreateResolver(db).ResolveMentionAsync("ناشناخته");

        Assert.Equal("ناشناخته", Assert.IsType<EntityResolutionResult.NotFound>(result).NormalizedMention);
    }

    [Fact]
    public void SemanticDistractors_NeverBecomeEntityMentions()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry)
            .Interpret("لطفاً چارت نمودار روند فروش ماه قبل مشابه سال گذشته P/E فولاد را نشان بده");

        var mentions = interpretation.EntityMentions.Select(item => item.Text).ToArray();
        Assert.Contains("فولاد", mentions);
        Assert.DoesNotContain(mentions, QueryNormalization.IsEntityDistractor);
    }

    [Fact]
    public void FundamentalAnalysisQualifier_NeverBecomesEntityMention()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry)
            .Interpret("تحلیل بنیادی فولاژ؟");

        var mention = Assert.Single(interpretation.EntityMentions);
        Assert.Equal("فولاژ", mention.Text);
    }

    [Fact]
    public async Task MultipleCanonicalEntities_AreResolvedWithoutTreatingConnectorsAsSymbols()
    {
        await using var db = CreateDb();
        AddCompany(db, "فملی", "ملی صنایع مس ایران", "femeli");
        AddCompany(db, "حفاری", "حفاری شمال", "hafari");
        await db.SaveChangesAsync();
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret("PE و ROE فملی و حفاری");

        var resolved = await CreateResolver(db).ResolveAllFromInterpretationAsync(interpretation);

        Assert.Equal(["فملی", "حفاری"], resolved.Select(item => item.Entity.DisplaySymbol));
    }

    [Fact]
    public async Task V1AndV2RouteAdapters_ResolveTheSameCanonicalEntity()
    {
        await using var db = CreateDb();
        var company = AddCompany(db, "فولاد", "فولاد مبارکه", "foolad");
        await db.SaveChangesAsync();
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret("چارت روند فروش فولاد");
        var resolver = CreateResolver(db);
        var v1Adapter = new CanonicalCompanyRouteAdapter(resolver, new CanonicalEntityResolutionOptions(Enabled: true));
        var v2Adapter = new CanonicalCompanyRouteAdapter(resolver, new CanonicalEntityResolutionOptions(Enabled: true));

        var v1 = await v1Adapter.ResolveForRouteAsync("monthly_activity_trend", interpretation);
        var v2 = await v2Adapter.ResolveForRouteAsync("monthly_activity_trend", interpretation);

        Assert.Equal(
            Assert.IsType<EntityResolutionResult.Resolved>(v1).Entity.CanonicalId,
            Assert.IsType<EntityResolutionResult.Resolved>(v2).Entity.CanonicalId);
        Assert.Equal(company.Id, Assert.IsType<EntityResolutionResult.Resolved>(v1).Entity.CanonicalId);
    }

    [Fact]
    public void SlotValidator_PrioritizesAmbiguousSymbolBeforeOtherMissingSlots()
    {
        var registry = new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create());
        var interpretation = new DeterministicCapabilityInterpreter(registry).Interpret("P/E");
        var ambiguity = new EntityResolutionResult.Ambiguous([]);

        var result = new CapabilitySlotValidator(registry).Validate("symbol_metric_lookup", interpretation, ambiguity);

        Assert.Equal(QuerySlotType.CompanyOrSymbol, result.NextClarificationSlot?.Type);
        Assert.Equal(QuerySlotValidationState.Ambiguous, result.NextClarificationSlot?.ValidationState);
    }

    [Theory]
    [InlineData(typeof(EntityResolutionResult.Missing), DialogueOutcome.ClarificationNeeded, DialogueOutcomeReasonCodes.RequiredInputMissing)]
    [InlineData(typeof(EntityResolutionResult.NotFound), DialogueOutcome.DisambiguationNeeded, DialogueOutcomeReasonCodes.EntityNotFound)]
    public void OutcomeMapper_DistinguishesMissingAndNotFound(Type resolutionType, DialogueOutcome expected, string reason)
    {
        EntityResolutionResult resolution = resolutionType == typeof(EntityResolutionResult.Missing)
            ? new EntityResolutionResult.Missing("CompanyOrSymbol")
            : new EntityResolutionResult.NotFound("unknown");

        var outcome = EntityResolutionOutcomeMapper.ToOutcome("فروش فولاد", resolution);

        Assert.Equal(expected, outcome.Outcome);
        Assert.Equal(reason, outcome.ReasonCode);
    }

    [Fact]
    public async Task CanonicalIndustryResolver_ReturnsResolvedAmbiguousAndNotFoundOutcomes()
    {
        await using var db = CreateDb();
        db.Industries.AddRange(
            new NormalizedIndustryRow { Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalId = "steel", Name = "Steel" },
            new NormalizedIndustryRow { Id = Guid.NewGuid(), ProviderName = "NoavaranCurrentApi", ExternalId = "steel-2", Name = "Steel" });
        await db.SaveChangesAsync();
        var resolver = CreateResolver(db);

        Assert.IsType<IndustryResolutionResult.Ambiguous>(await resolver.ResolveIndustryMentionAsync("Steel"));
        Assert.IsType<IndustryResolutionResult.NotFound>(await resolver.ResolveIndustryMentionAsync("Unknown industry"));
    }

    private static FinancialIngestionDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CanonicalQueryEntityResolver CreateResolver(FinancialIngestionDbContext db) =>
        new(db, Options.Create(new CanonicalEntityResolutionOptions(Enabled: true)));

    private static NormalizedCompanyRow AddCompany(FinancialIngestionDbContext db, string symbol, string name, string alias) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "canonical-test",
            ExternalCompanyId = symbol,
            Ticker = symbol,
            CompanySymbol = alias,
            Name = name,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        }).Entity;
}
