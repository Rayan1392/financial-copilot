namespace FinancialCopilot.Application.Authentication;

public sealed record TelegramLinkChallenge(
    string DeepLink,
    DateTimeOffset ExpiresAtUtc,
    string CorrelationId);

public sealed record TelegramWebConfirmationChallenge(
    string ConfirmationUrl,
    DateTimeOffset ExpiresAtUtc,
    string CorrelationId);

public sealed record TelegramIdentity(
    long TelegramUserId,
    long TelegramChatId,
    string? Username);

public sealed record TelegramLinkView(
    long TelegramUserId,
    long TelegramChatId,
    string? Username,
    DateTimeOffset LinkedAtUtc,
    DateTimeOffset LastVerifiedAtUtc);

public enum TelegramLinkOutcome
{
    Linked,
    AlreadyLinked,
    InvalidOrExpired,
    Conflict
}

public sealed record TelegramLinkResult(TelegramLinkOutcome Outcome, TelegramLinkView? Link);

public sealed record TelegramLinkPreview(string MaskedTelegramUserId, string? Username, DateTimeOffset ExpiresAtUtc);

public interface ITelegramIdentityLinkReader
{
    Task<TelegramLinkView?> GetCurrentAsync(CurrentActor actor, CancellationToken cancellationToken);
    Task<CurrentActor?> ResolveActorAsync(long telegramUserId, CancellationToken cancellationToken);
}

public interface ITelegramLinkService : ITelegramIdentityLinkReader
{
    Task<TelegramLinkChallenge> CreateWebChallengeAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken);

    Task<TelegramWebConfirmationChallenge> CreateTelegramChallengeAsync(
        CurrentActor adapterActor,
        TelegramIdentity telegramIdentity,
        long telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<TelegramLinkResult> ConfirmFromTelegramAsync(
        CurrentActor adapterActor,
        string token,
        TelegramIdentity telegramIdentity,
        long telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken);

    Task<TelegramLinkResult> ConfirmFromWebAsync(
        CurrentActor actor,
        string token,
        string correlationId,
        CancellationToken cancellationToken);

    Task<TelegramLinkPreview?> PreviewFromWebAsync(
        CurrentActor actor,
        string token,
        CancellationToken cancellationToken);

    Task<bool> UnlinkAsync(CurrentActor actor, string correlationId, CancellationToken cancellationToken);
    Task<bool> UnlinkFromTelegramAsync(
        CurrentActor adapterActor,
        long telegramUserId,
        long telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken);
}
