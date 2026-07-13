using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Identity;
using FinancialCopilot.Domain.Identity.Telegram;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class TelegramLinkService(
    AuthDbContext dbContext,
    IOptions<TelegramLinkOptions> options,
    TimeProvider timeProvider,
    ILogger<TelegramLinkService> logger) : ITelegramLinkService
{
    public async Task<TelegramLinkChallenge> CreateWebChallengeAsync(
        CurrentActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        var (rawToken, tokenHash) = CreateToken();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.Value.TokenLifetimeMinutes);

        await RevokePendingAsync(actor.ActorId, null, now, correlationId, cancellationToken);
        dbContext.TelegramLinkTokens.Add(new TelegramLinkTokenRow
        {
            Id = Guid.NewGuid(),
            ActorId = actor.ActorId,
            TenantId = actor.TenantId,
            TokenHash = tokenHash,
            Purpose = TelegramLinkTokenPurpose.WebToTelegram.ToString(),
            Status = TelegramLinkTokenStatus.Pending.ToString(),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            CorrelationId = correlationId
        });
        AddAudit(actor.ActorId, actor.TenantId, null, TelegramLinkAuditAction.TokenIssued, now, correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Telegram web link challenge issued for actor {ActorId} with correlation {CorrelationId}.", actor.ActorId, correlationId);

        var botUsername = options.Value.BotUsername.Trim().TrimStart('@');
        return new TelegramLinkChallenge(
            $"https://t.me/{botUsername}?start=link_{rawToken}",
            expiresAt,
            correlationId);
    }

    public async Task<TelegramWebConfirmationChallenge> CreateTelegramChallengeAsync(
        CurrentActor adapterActor,
        TelegramIdentity telegramIdentity,
        long telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireApiClient(adapterActor);
        ValidateIdentity(telegramIdentity);
        if (telegramUpdateId <= 0)
        {
            throw new ArgumentException("A valid Telegram update id is required.");
        }
        if (await dbContext.TelegramLinkTokens.AnyAsync(
            row => row.TelegramUpdateId == telegramUpdateId,
            cancellationToken))
        {
            throw new ArgumentException("This Telegram update has already been processed.");
        }
        var (rawToken, tokenHash) = CreateToken();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.Value.TokenLifetimeMinutes);

        await RevokePendingAsync(null, telegramIdentity.TelegramUserId, now, correlationId, cancellationToken);
        dbContext.TelegramLinkTokens.Add(new TelegramLinkTokenRow
        {
            Id = Guid.NewGuid(),
            TenantId = adapterActor.TenantId,
            TelegramUserId = telegramIdentity.TelegramUserId,
            TelegramChatId = telegramIdentity.TelegramChatId,
            Username = NormalizeUsername(telegramIdentity.Username),
            TokenHash = tokenHash,
            Purpose = TelegramLinkTokenPurpose.TelegramToWeb.ToString(),
            Status = TelegramLinkTokenStatus.Pending.ToString(),
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAt,
            TelegramUpdateId = telegramUpdateId,
            CorrelationId = correlationId
        });
        AddAudit(null, adapterActor.TenantId, telegramIdentity.TelegramUserId, TelegramLinkAuditAction.TokenIssued, now, correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Telegram-first link challenge issued with correlation {CorrelationId}.", correlationId);

        var separator = options.Value.WebConfirmationBaseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return new TelegramWebConfirmationChallenge(
            $"{options.Value.WebConfirmationBaseUrl}{separator}token={rawToken}",
            expiresAt,
            correlationId);
    }

    public Task<TelegramLinkResult> ConfirmFromTelegramAsync(
        CurrentActor adapterActor,
        string token,
        TelegramIdentity telegramIdentity,
        long telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireApiClient(adapterActor);
        ValidateIdentity(telegramIdentity);
        return ConfirmAsync(
            token,
            TelegramLinkTokenPurpose.WebToTelegram,
            null,
            adapterActor.TenantId,
            telegramIdentity,
            telegramUpdateId,
            correlationId,
            cancellationToken);
    }

    public Task<TelegramLinkResult> ConfirmFromWebAsync(
        CurrentActor actor,
        string token,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        return ConfirmAsync(
            token,
            TelegramLinkTokenPurpose.TelegramToWeb,
            actor.ActorId,
            actor.TenantId,
            null,
            null,
            correlationId,
            cancellationToken);
    }

    public async Task<TelegramLinkPreview?> PreviewFromWebAsync(
        CurrentActor actor,
        string token,
        CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var hash = Hash(token);
        var row = await dbContext.TelegramLinkTokens.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.TokenHash == hash &&
                candidate.TenantId == actor.TenantId &&
                candidate.Purpose == TelegramLinkTokenPurpose.TelegramToWeb.ToString() &&
                candidate.Status == TelegramLinkTokenStatus.Pending.ToString() &&
                candidate.ExpiresAtUtc > now,
            cancellationToken);
        if (row?.TelegramUserId is null)
        {
            return null;
        }

        var value = row.TelegramUserId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var suffix = value.Length <= 4 ? value : value[^4..];
        return new TelegramLinkPreview($"***{suffix}", row.Username, row.ExpiresAtUtc);
    }

    public async Task<bool> UnlinkAsync(CurrentActor actor, string correlationId, CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        var now = timeProvider.GetUtcNow();
        var link = await dbContext.TelegramAccountLinks.SingleOrDefaultAsync(
            row => row.ActorId == actor.ActorId && row.TenantId == actor.TenantId && row.RevokedAtUtc == null,
            cancellationToken);

        await RevokePendingAsync(actor.ActorId, null, now, correlationId, cancellationToken);
        if (link is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        link.RevokedAtUtc = now;
        link.LastVerifiedAtUtc = now;
        AddAudit(actor.ActorId, actor.TenantId, link.TelegramUserId, TelegramLinkAuditAction.Unlinked, now, correlationId);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Telegram link revoked for actor {ActorId} with correlation {CorrelationId}.", actor.ActorId, correlationId);
        return true;
    }

    public async Task<bool> UnlinkFromTelegramAsync(
        CurrentActor adapterActor,
        long telegramUserId,
        long telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        RequireApiClient(adapterActor);
        if (telegramUserId <= 0 || telegramUpdateId <= 0)
        {
            throw new ArgumentException("A valid Telegram user and update id is required.");
        }

        var now = timeProvider.GetUtcNow();
        var alreadyProcessed = await dbContext.TelegramLinkAudits.AnyAsync(
            row => row.CorrelationId == $"telegram-update:{telegramUpdateId}" &&
                row.TelegramUserId == telegramUserId,
            cancellationToken);
        if (alreadyProcessed)
        {
            return false;
        }

        var link = await dbContext.TelegramAccountLinks.SingleOrDefaultAsync(
            row => row.TelegramUserId == telegramUserId &&
                row.TenantId == adapterActor.TenantId &&
                row.RevokedAtUtc == null,
            cancellationToken);
        await RevokePendingAsync(null, telegramUserId, now, correlationId, cancellationToken);
        if (link is null)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        link.RevokedAtUtc = now;
        link.LastVerifiedAtUtc = now;
        AddAudit(
            link.ActorId,
            link.TenantId,
            telegramUserId,
            TelegramLinkAuditAction.Unlinked,
            now,
            $"telegram-update:{telegramUpdateId}",
            "Unlinked from Telegram.");
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Telegram-originated unlink completed with correlation {CorrelationId}.", correlationId);
        return true;
    }

    public async Task<TelegramLinkView?> GetCurrentAsync(CurrentActor actor, CancellationToken cancellationToken)
    {
        RequireWebUser(actor);
        var row = await dbContext.TelegramAccountLinks.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ActorId == actor.ActorId &&
                candidate.TenantId == actor.TenantId &&
                candidate.RevokedAtUtc == null,
            cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<CurrentActor?> ResolveActorAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var row = await dbContext.TelegramAccountLinks.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.TelegramUserId == telegramUserId && candidate.RevokedAtUtc == null,
            cancellationToken);
        return row is null
            ? null
            : new CurrentActor(
                ActorType.User,
                row.ActorId,
                row.TenantId,
                AuthenticationMode.WebAppUser,
                UserId: row.ActorId);
    }

    private async Task<TelegramLinkResult> ConfirmAsync(
        string rawToken,
        TelegramLinkTokenPurpose requiredPurpose,
        Guid? webActorId,
        Guid tenantId,
        TelegramIdentity? suppliedIdentity,
        long? telegramUpdateId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 128)
        {
            return new TelegramLinkResult(TelegramLinkOutcome.InvalidOrExpired, null);
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var hash = Hash(rawToken);
        var challenge = await dbContext.TelegramLinkTokens.SingleOrDefaultAsync(
            row => row.TokenHash == hash,
            cancellationToken);
        if (challenge is null ||
            challenge.Status != TelegramLinkTokenStatus.Pending.ToString() ||
            challenge.Purpose != requiredPurpose.ToString() ||
            challenge.TenantId != tenantId ||
            challenge.ExpiresAtUtc <= now)
        {
            if (challenge is not null && challenge.Status == TelegramLinkTokenStatus.Pending.ToString() && challenge.ExpiresAtUtc <= now)
            {
                challenge.Status = TelegramLinkTokenStatus.Expired.ToString();
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return new TelegramLinkResult(TelegramLinkOutcome.InvalidOrExpired, null);
        }

        var actorId = webActorId ?? challenge.ActorId;
        var identity = suppliedIdentity ?? new TelegramIdentity(
            challenge.TelegramUserId!.Value,
            challenge.TelegramChatId!.Value,
            challenge.Username);
        if (actorId is null || (challenge.ActorId is not null && webActorId is not null && challenge.ActorId != webActorId))
        {
            return new TelegramLinkResult(TelegramLinkOutcome.InvalidOrExpired, null);
        }

        var actorLink = await dbContext.TelegramAccountLinks.SingleOrDefaultAsync(
            row => row.ActorId == actorId && row.TenantId == tenantId && row.RevokedAtUtc == null,
            cancellationToken);
        var telegramLink = await dbContext.TelegramAccountLinks.SingleOrDefaultAsync(
            row => row.TelegramUserId == identity.TelegramUserId && row.RevokedAtUtc == null,
            cancellationToken);
        if (actorLink is not null || telegramLink is not null)
        {
            var same = actorLink is not null && telegramLink is not null && actorLink.Id == telegramLink.Id;
            challenge.Status = same ? TelegramLinkTokenStatus.Consumed.ToString() : TelegramLinkTokenStatus.Revoked.ToString();
            challenge.ConsumedAtUtc = same ? now : null;
            challenge.RevokedAtUtc = same ? null : now;
            AddAudit(actorId, tenantId, identity.TelegramUserId,
                same ? TelegramLinkAuditAction.ReplayRejected : TelegramLinkAuditAction.ConflictRejected,
                now,
                correlationId);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Telegram link confirmation rejected due to an identity conflict. Correlation {CorrelationId}.", correlationId);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new TelegramLinkResult(same ? TelegramLinkOutcome.AlreadyLinked : TelegramLinkOutcome.Conflict, same ? Map(actorLink!) : null);
        }

        var link = new TelegramAccountLinkRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId.Value,
            TenantId = tenantId,
            TelegramUserId = identity.TelegramUserId,
            TelegramChatId = identity.TelegramChatId,
            Username = NormalizeUsername(identity.Username),
            LinkedAtUtc = now,
            LastVerifiedAtUtc = now
        };
        dbContext.TelegramAccountLinks.Add(link);
        challenge.Status = TelegramLinkTokenStatus.Consumed.ToString();
        challenge.ConsumedAtUtc = now;
        challenge.ConsumedByTelegramUserId = identity.TelegramUserId;
        challenge.TelegramUpdateId ??= telegramUpdateId;
        AddAudit(actorId, tenantId, identity.TelegramUserId, TelegramLinkAuditAction.Linked, now, correlationId);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Telegram account link created for actor {ActorId}. Correlation {CorrelationId}.", actorId, correlationId);
            return new TelegramLinkResult(TelegramLinkOutcome.Linked, Map(link));
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return new TelegramLinkResult(TelegramLinkOutcome.Conflict, null);
        }
    }

    private async Task RevokePendingAsync(
        Guid? actorId,
        long? telegramUserId,
        DateTimeOffset now,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var pending = await dbContext.TelegramLinkTokens.Where(row =>
                row.Status == TelegramLinkTokenStatus.Pending.ToString() &&
                ((actorId != null && row.ActorId == actorId) ||
                 (telegramUserId != null && row.TelegramUserId == telegramUserId)))
            .ToArrayAsync(cancellationToken);
        foreach (var row in pending)
        {
            row.Status = TelegramLinkTokenStatus.Revoked.ToString();
            row.RevokedAtUtc = now;
            AddAudit(row.ActorId, row.TenantId, row.TelegramUserId, TelegramLinkAuditAction.TokenRevoked, now, correlationId);
        }
    }

    private void AddAudit(
        Guid? actorId,
        Guid tenantId,
        long? telegramUserId,
        TelegramLinkAuditAction action,
        DateTimeOffset now,
        string correlationId,
        string? reason = null) =>
        dbContext.TelegramLinkAudits.Add(new TelegramLinkAuditRow
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            TenantId = tenantId,
            TelegramUserId = telegramUserId,
            Action = action.ToString(),
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            Reason = reason
        });

    private static (string Raw, string Hash) CreateToken()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (raw, Hash(raw));
    }

    private static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string? NormalizeUsername(string? username) =>
        string.IsNullOrWhiteSpace(username) ? null : username.Trim().TrimStart('@');

    private static void ValidateIdentity(TelegramIdentity identity)
    {
        if (identity.TelegramUserId <= 0 || identity.TelegramChatId == 0)
        {
            throw new ArgumentException("A valid numeric Telegram user and chat identity is required.");
        }
    }

    private static void RequireWebUser(CurrentActor actor)
    {
        if (actor.ActorType != ActorType.User || actor.AuthenticationMode != AuthenticationMode.WebAppUser)
        {
            throw new UnauthorizedAccessException("A canonical web user is required.");
        }
    }

    private static void RequireApiClient(CurrentActor actor)
    {
        if (actor.ActorType != ActorType.ApiClient || actor.AuthenticationMode != AuthenticationMode.ApiClient)
        {
            throw new UnauthorizedAccessException("An authenticated Telegram adapter API client is required.");
        }
    }

    private static TelegramLinkView Map(TelegramAccountLinkRow row) =>
        new(row.TelegramUserId, row.TelegramChatId, row.Username, row.LinkedAtUtc, row.LastVerifiedAtUtc);
}
