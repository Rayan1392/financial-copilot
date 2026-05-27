using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;

namespace FinancialCopilot.Infrastructure.Memory;

public sealed class EfCoreMemoryContextProvider(
    EfCoreMemoryRecordRepository recordRepository,
    IMemoryConsentService consentService,
    IMemoryProtectionPolicy protectionPolicy,
    IMessageRepository messageRepository,
    TimeProvider timeProvider) : IMemoryContextProvider
{
    public async Task<AuthorizedMemoryContext> GetAuthorizedContextAsync(
        MemoryContextRequest request,
        CancellationToken cancellationToken)
    {
        var items = new List<OptionalMemoryRecord>();
        var disclosures = new List<MemoryUseDisclosure>();

        // Short-term conversation memory derives from recent messages — no separate storage or consent required.
        if (request.Purpose == MemoryPurpose.CurrentConversationContinuity && request.ConversationId is not null)
        {
            var messages = await messageRepository.ListByConversationAsync(
                request.ConversationId.Value, cancellationToken);

            if (messages.Count > 0)
            {
                var recent = messages.TakeLast(10).ToList();
                var summary = BuildConversationSummary(recent);
                var shortTermItem = new OptionalMemoryRecord(
                    Guid.NewGuid(),
                    request.Subject,
                    MemoryType.ShortTermConversationMemory,
                    MemoryPurpose.CurrentConversationContinuity,
                    MemorySensitivity.General,
                    MemoryVersion: "1",
                    PolicyVersion: "v1",
                    Summary: summary,
                    new MemoryProvenance("ConversationHistory", null, timeProvider.GetUtcNow()),
                    new MemoryRetentionPolicy(null, InspectableBySubject: true, DeletableBySubject: false));

                var stDecision = protectionPolicy.Authorize(request, shortTermItem, consent: null, timeProvider.GetUtcNow());
                if (stDecision.Authorized)
                {
                    items.Add(shortTermItem);
                    if (stDecision.Disclosure is not null)
                        disclosures.Add(stDecision.Disclosure);
                }
            }
        }

        // Durable optional memory: retrieve from persistence, require active consent.
        var storedRecords = await recordRepository.GetRecordsAsync(request.Subject, cancellationToken);
        var now = timeProvider.GetUtcNow();

        foreach (var record in storedRecords)
        {
            var consent = await consentService.GetConsentAsync(
                request.Subject,
                record.Type,
                record.Purpose,
                cancellationToken);

            var decision = protectionPolicy.Authorize(request, record, consent, now);
            if (decision.Authorized)
            {
                items.Add(record);
                if (decision.Disclosure is not null)
                    disclosures.Add(decision.Disclosure);
            }
        }

        return new AuthorizedMemoryContext(items, disclosures, OptionalMemoryEnabled: items.Count > 0);
    }

    private static string BuildConversationSummary(IReadOnlyList<MessageRecord> messages)
    {
        var lines = messages.Select(m => $"{m.Role}: {TruncateAt200(m.Content)}");
        return string.Join("\n", lines);
    }

    private static string TruncateAt200(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";
}
