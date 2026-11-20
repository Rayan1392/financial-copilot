using System.Diagnostics;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Domain.Identity.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Authentication.Profile;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace FinancialCopilot.Infrastructure.Administration;

/// <summary>
/// Feature 164: cross-user, read-only conversation logs for administrators.
/// <para>
/// A turn is a User message and the Assistant messages that follow it in the same conversation until the next User
/// message. One exchange stores both rows with the same <c>CreatedAt</c>, so an Assistant row at the question's time
/// belongs to that question; Users are ordered by <c>(CreatedAt, Id)</c> among themselves (BUG-037).
/// </para>
/// <para>
/// The list runs as parameterized PostgreSQL in a read-only transaction with a statement timeout; user identities come
/// from <see cref="AuthDbContext"/> in one query per page, so no query runs per row.
/// </para>
/// </summary>
public sealed class EfCoreAdminConversationLogQueries(
    ConversationDbContext conversations,
    AuthDbContext auth,
    TahlilAppProfileProtector profileProtector,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    ILogger<EfCoreAdminConversationLogQueries> logger) : IAdminConversationLogQueries
{
    internal const string TurnReadActionCode = "admin.ai-conversation-log.turn-read";
    private const string StatementTimeout = "5s";
    private const int MaximumMobileCandidates = 2_000;
    private const string DefaultConversationTitle = "New conversation";
    private static readonly string UserRole = nameof(MessageRole.User);

    // The turn's first answer. The payload is parsed only when it is valid JSON, so one malformed row cannot fail the
    // page; an outcome that is not an integer is treated as missing. EF Core formats raw SQL with string.Format when
    // parameters are passed, so this SQL must not contain braces.
    private const string FirstAnswerJoin = """
        LEFT JOIN LATERAL (
            SELECT n."CreatedAt"
            FROM "Messages" AS n
            WHERE n."ConversationId" = q."ConversationId"
              AND n."Role" = 'User'
              AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
            ORDER BY n."CreatedAt", n."Id"
            LIMIT 1
        ) AS n ON TRUE
        LEFT JOIN LATERAL (
            SELECT m."Id" AS "AnswerId",
                   left(m."Content", @answerPreviewLength) AS "AnswerPreview",
                   m."CreatedAt" AS "AnswerCreatedAt",
                   CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
                       CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                                 AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                                 AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                            THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
                       END
                   END AS "Outcome"
            FROM "Messages" AS m
            WHERE m."ConversationId" = q."ConversationId"
              AND m."Role" = 'Assistant'
              AND m."CreatedAt" >= q."CreatedAt"
              AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
            ORDER BY m."CreatedAt", m."Id"
            LIMIT 1
        ) AS a ON TRUE
        """;

    private const string TurnSource = """
        FROM "Messages" AS q
        JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
        """;

    public async Task<AdminConversationLogPage> GetPageAsync(
        Guid tenantId,
        AdminConversationLogQuery query,
        CancellationToken cancellationToken)
    {
        if (AdminConversationLogQueryRules.Validate(query).Count > 0)
            throw new ArgumentException("The conversation log query is invalid.", nameof(query));
        EnsurePostgreSql();

        var started = Stopwatch.GetTimestamp();
        var actorIds = await ResolveUsersAsync(tenantId, query.User, cancellationToken);
        if (actorIds is { Length: 0 })
            return new AdminConversationLogPage([], query.Page, query.PageSize, 0, 0);

        var filter = TurnFilter.Create(tenantId, query, actorIds);
        int totalCount;
        TurnRow[] rows;
        await using (var transaction = await conversations.Database.BeginTransactionAsync(cancellationToken))
        {
            await conversations.Database.ExecuteSqlRawAsync(
                $"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '{StatementTimeout}'", cancellationToken);

            // The SQL text is composed only from the constants above and TurnFilter's constant conditions; every value
            // is an NpgsqlParameter.
            var countSource = filter.NeedsAnswer ? TurnSource + "\n" + FirstAnswerJoin : TurnSource;
            var countSql = $"""SELECT count(*)::int AS "Value" {countSource} WHERE {filter.Where}""";
            totalCount = await conversations.Database
                .SqlQueryRaw<int>(countSql, filter.CreateParameters(includeAnswerPreview: filter.NeedsAnswer))
                .SingleAsync(cancellationToken);

            // Without a status filter the answer does not decide which turns match, so the page of turns is chosen
            // first and answers are looked up for those rows only (measured in evidence/index-decision.md: the lookup
            // otherwise runs for every matching turn before the sort). A status filter needs the answer of every
            // candidate turn.
            var pageSql = filter.NeedsAnswer
                ? $"""
                    SELECT q."Id" AS "TurnId",
                           q."ConversationId",
                           c."Title" AS "ConversationTitle",
                           c."ActorId" AS "UserId",
                           left(q."Content", @questionPreviewLength) AS "QuestionPreview",
                           q."CreatedAt" AS "QuestionCreatedAt",
                           a."AnswerId",
                           a."AnswerPreview",
                           a."AnswerCreatedAt",
                           a."Outcome"
                    {TurnSource}
                    {FirstAnswerJoin}
                    WHERE {filter.Where}
                    ORDER BY q."CreatedAt" DESC, q."Id" DESC
                    LIMIT @take OFFSET @skip
                    """
                : $"""
                    SELECT q."Id" AS "TurnId",
                           q."ConversationId",
                           q."Title" AS "ConversationTitle",
                           q."ActorId" AS "UserId",
                           left(q."Content", @questionPreviewLength) AS "QuestionPreview",
                           q."CreatedAt" AS "QuestionCreatedAt",
                           a."AnswerId",
                           a."AnswerPreview",
                           a."AnswerCreatedAt",
                           a."Outcome"
                    FROM (
                        SELECT q."Id", q."ConversationId", q."Content", q."CreatedAt", c."Title", c."ActorId"
                        {TurnSource}
                        WHERE {filter.Where}
                        ORDER BY q."CreatedAt" DESC, q."Id" DESC
                        LIMIT @take OFFSET @skip
                    ) AS q
                    {FirstAnswerJoin}
                    ORDER BY q."CreatedAt" DESC, q."Id" DESC
                    """;
            rows = totalCount <= (query.Page - 1) * query.PageSize
                ? []
                : await conversations.Database
                    .SqlQueryRaw<TurnRow>(
                        pageSql,
                        [
                            .. filter.CreateParameters(includeAnswerPreview: true),
                            new NpgsqlParameter("questionPreviewLength", NpgsqlDbType.Integer) { Value = AdminConversationLogQueryRules.QuestionPreviewLength },
                            new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = query.PageSize },
                            new NpgsqlParameter("skip", NpgsqlDbType.Integer) { Value = (query.Page - 1) * query.PageSize }
                        ])
                    .ToArrayAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var users = await LoadUsersAsync(rows.Select(row => row.UserId), cancellationToken);
        var telegram = await LoadTelegramConversationsAsync(tenantId, rows.Select(row => row.ConversationId), cancellationToken);
        var items = rows.Select(row => new AdminConversationLogItem(
            row.TurnId,
            row.ConversationId,
            NormalizeTitle(row.ConversationTitle),
            users.GetValueOrDefault(row.UserId) ?? new AdminConversationLogUser(row.UserId, null, null, null),
            row.QuestionPreview,
            row.AnswerPreview,
            row.QuestionCreatedAt,
            row.AnswerCreatedAt,
            AdminConversationLogQueryRules.StatusOf(row.AnswerId is not null, row.Outcome),
            telegram.Contains(row.ConversationId) ? AdminConversationLogChannel.Telegram : AdminConversationLogChannel.Web))
            .ToArray();

        logger.LogInformation(
            "Admin conversation log page read. Page={Page} PageSize={PageSize} TotalCount={TotalCount} Returned={Returned} " +
            "HasText={HasText} HasUser={HasUser} HasRange={HasRange} Status={Status} ElapsedMs={ElapsedMs}",
            query.Page, query.PageSize, totalCount, items.Length, query.Text is not null, query.User is not null,
            query.FromUtc is not null || query.ToUtc is not null, query.Status,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);

        return new AdminConversationLogPage(items, query.Page, query.PageSize, totalCount,
            AdminConversationLogQueryRules.TotalPages(totalCount, query.PageSize));
    }

    public async Task<AdminConversationLogTurn?> GetTurnAsync(
        Guid tenantId,
        Guid administratorId,
        string administratorType,
        string correlationId,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        EnsurePostgreSql();

        // Identify the turn without reading any content; content is read only after the audit is saved.
        var anchor = await (from message in conversations.Messages.AsNoTracking()
                            join conversation in conversations.Conversations.AsNoTracking()
                                on message.ConversationId equals conversation.Id
                            where message.Id == turnId && message.Role == UserRole && conversation.TenantId == tenantId
                            select new
                            {
                                message.Id,
                                message.ConversationId,
                                message.CreatedAt,
                                conversation.Title,
                                conversation.ActorId
                            })
            .SingleOrDefaultAsync(cancellationToken);
        if (anchor is null) return null;

        await AdminContentReadAudit.RecordTurnReadAsync(
            auth, timeProvider, tenantId, administratorId, administratorType, correlationId,
            TurnReadActionCode, turnId, cancellationToken);

        var question = await conversations.Messages.AsNoTracking()
            .Where(message => message.Id == anchor.Id)
            .Select(message => message.Content)
            .SingleAsync(cancellationToken);

        var answerRows = await conversations.Database
            .SqlQueryRaw<AnswerRow>(
                """
                SELECT m."Id" AS "MessageId", m."Content", m."CreatedAt", m."AssistantPayloadJson" AS "PayloadJson"
                FROM "Messages" AS m
                WHERE m."ConversationId" = @conversationId
                  AND m."Role" = 'Assistant'
                  AND m."CreatedAt" >= @questionCreatedAt
                  AND m."CreatedAt" < COALESCE((
                      SELECT n."CreatedAt"
                      FROM "Messages" AS n
                      WHERE n."ConversationId" = @conversationId
                        AND n."Role" = 'User'
                        AND (n."CreatedAt", n."Id") > (@questionCreatedAt, @questionId)
                      ORDER BY n."CreatedAt", n."Id"
                      LIMIT 1), 'infinity'::timestamptz)
                ORDER BY m."CreatedAt", m."Id"
                """,
                new NpgsqlParameter("conversationId", NpgsqlDbType.Uuid) { Value = anchor.ConversationId },
                new NpgsqlParameter("questionCreatedAt", NpgsqlDbType.TimestampTz) { Value = anchor.CreatedAt.ToUniversalTime() },
                new NpgsqlParameter("questionId", NpgsqlDbType.Uuid) { Value = anchor.Id })
            .ToArrayAsync(cancellationToken);

        var answers = answerRows.Select(row =>
        {
            var payload = DeserializePayload(row.MessageId, row.PayloadJson);
            return new AdminConversationLogAnswer(
                row.MessageId,
                row.Content,
                row.CreatedAt,
                AdminConversationLogQueryRules.StatusOf(true, payload is null ? null : (int)payload.Outcome),
                payload?.OutcomeReasonCode,
                MapUsage(payload?.Usage),
                payload);
        }).ToArray();

        var users = await LoadUsersAsync([anchor.ActorId], cancellationToken);
        var telegram = await LoadTelegramConversationsAsync(tenantId, [anchor.ConversationId], cancellationToken);
        return new AdminConversationLogTurn(
            anchor.Id,
            anchor.ConversationId,
            NormalizeTitle(anchor.Title),
            telegram.Contains(anchor.ConversationId) ? AdminConversationLogChannel.Telegram : AdminConversationLogChannel.Web,
            users.GetValueOrDefault(anchor.ActorId) ?? new AdminConversationLogUser(anchor.ActorId, null, null, null),
            question,
            anchor.CreatedAt,
            answers.Length == 0 ? AdminConversationLogStatus.NoResponse : answers[0].Status,
            answers);
    }

    /// <summary>
    /// Null when there is no user filter; otherwise the matching user ids of the tenant (possibly empty).
    /// </summary>
    private async Task<Guid[]?> ResolveUsersAsync(Guid tenantId, string? search, CancellationToken cancellationToken)
    {
        if (search is null) return null;
        var term = search.Trim();
        var tenantUsers = auth.UserTenants.AsNoTracking()
            .Where(membership => membership.TenantId == tenantId)
            .Select(membership => membership.UserId);

        if (Guid.TryParse(term, out var userId))
            return await tenantUsers.Where(id => id == userId).Distinct().ToArrayAsync(cancellationToken);

        Guid[] matches;
        if (IranianMobile.TryCreate(term, out var mobile) && mobile is not null)
        {
            // Mobiles are stored encrypted. Narrow by the stored last four digits, then compare the decrypted numbers.
            var last4 = mobile.Last4;
            var candidates = await auth.UserProfiles.AsNoTracking()
                .Where(profile => tenantUsers.Contains(profile.UserId) &&
                                  (profile.MobileLast4 == last4 || profile.ContactMobileLast4 == last4))
                .Select(profile => new { profile.UserId, profile.MobileEncrypted, profile.ContactMobileEncrypted })
                .Take(MaximumMobileCandidates + 1)
                .ToArrayAsync(cancellationToken);
            if (candidates.Length > MaximumMobileCandidates)
                logger.LogWarning("Admin conversation log mobile search narrowed to more than {Limit} candidates; extra candidates ignored.",
                    MaximumMobileCandidates);

            var contactProtector = ContactMobileProtection.CreateProtector(dataProtection);
            matches = candidates.Take(MaximumMobileCandidates)
                .Where(candidate =>
                    SameMobile(mobile, candidate.MobileEncrypted is null
                        ? null
                        : profileProtector.TryUnprotect(TahlilAppProfileProtector.MobilePurpose, candidate.MobileEncrypted)) ||
                    SameMobile(mobile, candidate.ContactMobileEncrypted is null
                        ? null
                        : TryUnprotect(contactProtector, candidate.ContactMobileEncrypted)))
                .Select(candidate => candidate.UserId)
                .Distinct()
                .ToArray();
        }
        else
        {
            var upper = term.ToUpperInvariant();
            matches = await (from user in auth.Users.AsNoTracking()
                             join profileRow in auth.UserProfiles.AsNoTracking() on user.Id equals profileRow.UserId into profileRows
                             from profile in profileRows.DefaultIfEmpty()
                             where tenantUsers.Contains(user.Id) &&
                                   ((user.NormalizedEmail != null && user.NormalizedEmail.Contains(upper)) ||
                                    (profile != null && (
                                        (profile.DisplayName != null && profile.DisplayName.ToUpper().Contains(upper)) ||
                                        (profile.FirstName != null && profile.FirstName.ToUpper().Contains(upper)) ||
                                        (profile.LastName != null && profile.LastName.ToUpper().Contains(upper)) ||
                                        (profile.LocalFirstName != null && profile.LocalFirstName.ToUpper().Contains(upper)) ||
                                        (profile.LocalLastName != null && profile.LocalLastName.ToUpper().Contains(upper)))))
                             select user.Id)
                .Distinct()
                .Take(AdminConversationLogQueryRules.MaximumMatchedUsers + 1)
                .ToArrayAsync(cancellationToken);
        }

        if (matches.Length > AdminConversationLogQueryRules.MaximumMatchedUsers)
            throw new AdminConversationLogUserSearchTooBroadException();
        return matches;
    }

    private async Task<Dictionary<Guid, AdminConversationLogUser>> LoadUsersAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await (from user in auth.Users.AsNoTracking()
                          join profileRow in auth.UserProfiles.AsNoTracking() on user.Id equals profileRow.UserId into profileRows
                          from profile in profileRows.DefaultIfEmpty()
                          where ids.Contains(user.Id)
                          select new
                          {
                              user.Id,
                              user.Email,
                              DisplayName = profile == null ? null : profile.DisplayName,
                              FirstName = profile == null ? null : profile.LocalFirstName ?? profile.FirstName,
                              LastName = profile == null ? null : profile.LocalLastName ?? profile.LastName,
                              MobileLast4 = profile == null ? null : profile.ContactMobileLast4 ?? profile.MobileLast4
                          })
            .ToArrayAsync(cancellationToken);
        return rows.ToDictionary(
            row => row.Id,
            row => new AdminConversationLogUser(
                row.Id,
                EfCoreAdminUserQueries.ResolveDisplayName(row.DisplayName, row.FirstName, row.LastName),
                row.Email,
                string.IsNullOrEmpty(row.MobileLast4) ? null : "•••" + row.MobileLast4));
    }

    private async Task<HashSet<Guid>> LoadTelegramConversationsAsync(
        Guid tenantId,
        IEnumerable<Guid> conversationIds,
        CancellationToken cancellationToken)
    {
        var ids = conversationIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var bound = await auth.TelegramConversationBindings.AsNoTracking()
            .Where(binding => binding.TenantId == tenantId && ids.Contains(binding.ConversationId))
            .Select(binding => binding.ConversationId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return [.. bound];
    }

    private AssistantMessagePayload? DeserializePayload(Guid messageId, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AssistantMessagePayload>(json);
        }
        catch (JsonException)
        {
            logger.LogWarning("Stored assistant payload could not be read. MessageId={MessageId}", messageId);
            return null;
        }
    }

    private static AdminConversationLogUsage? MapUsage(UsageAccountingResult? usage) =>
        usage is null
            ? null
            : new AdminConversationLogUsage(
                usage.ProviderName,
                usage.ModelName,
                usage.PromptTokens,
                usage.CompletionTokens,
                usage.TotalTokens,
                usage.CreditsCharged,
                usage.CompletionStatus,
                usage.ExecutionRoute);

    private static bool SameMobile(IranianMobile searched, string? stored) =>
        IranianMobile.TryCreate(stored, out var candidate) && candidate is not null &&
        string.Equals(candidate.Value, searched.Value, StringComparison.Ordinal);

    private static string? TryUnprotect(IDataProtector protector, string ciphertext)
    {
        try { return protector.Unprotect(ciphertext); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    private static string? NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) || string.Equals(title, DefaultConversationTitle, StringComparison.Ordinal)
            ? null
            : title;

    private void EnsurePostgreSql()
    {
        if (!conversations.Database.IsNpgsql())
            throw new NotSupportedException("Admin conversation logs require PostgreSQL.");
    }

    /// <summary>The WHERE clause of the list and the parameters it uses; parameters are created per command.</summary>
    private sealed class TurnFilter
    {
        private readonly List<Func<NpgsqlParameter>> parameters = [];
        private readonly List<string> conditions = ["q.\"Role\" = 'User'", "c.\"TenantId\" = @tenantId"];

        public bool NeedsAnswer { get; private set; }

        public string Where => string.Join("\n  AND ", conditions);

        public static TurnFilter Create(Guid tenantId, AdminConversationLogQuery query, Guid[]? actorIds)
        {
            var filter = new TurnFilter();
            filter.Add("tenantId", NpgsqlDbType.Uuid, tenantId);
            if (actorIds is not null)
            {
                filter.conditions.Add("c.\"ActorId\" = ANY(@actorIds)");
                filter.Add("actorIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid, actorIds);
            }
            if (query.FromUtc is { } from)
            {
                filter.conditions.Add("q.\"CreatedAt\" >= @fromUtc");
                filter.Add("fromUtc", NpgsqlDbType.TimestampTz, from.ToUniversalTime());
            }
            if (query.ToUtc is { } to)
            {
                filter.conditions.Add("q.\"CreatedAt\" < @toUtc");
                filter.Add("toUtc", NpgsqlDbType.TimestampTz, to.ToUniversalTime());
            }
            if (query.Text is not null)
            {
                filter.conditions.Add(
                    $"translate(q.\"Content\", '{AdminConversationLogQueryRules.ArabicLetters}', '{AdminConversationLogQueryRules.PersianLetters}') " +
                    $"ILIKE @textPattern ESCAPE '{AdminConversationLogQueryRules.LikeEscape}'");
                filter.Add("textPattern", NpgsqlDbType.Text, AdminConversationLogQueryRules.ContainsPattern(query.Text));
            }
            if (query.Status is { } status)
            {
                filter.NeedsAnswer = true;
                if (status == AdminConversationLogStatus.NoResponse)
                {
                    filter.conditions.Add("a.\"AnswerId\" IS NULL");
                }
                else if (status == AdminConversationLogStatus.Answered)
                {
                    // Matches StatusOf: no outcome, or one that is not another known outcome, counts as answered.
                    filter.conditions.Add("a.\"AnswerId\" IS NOT NULL AND (a.\"Outcome\" IS NULL OR NOT (a.\"Outcome\" = ANY(@otherOutcomes)))");
                    filter.Add("otherOutcomes", NpgsqlDbType.Array | NpgsqlDbType.Integer,
                        Enum.GetValues<DialogueOutcome>().Where(outcome => outcome != DialogueOutcome.Answered)
                            .Select(outcome => (int)outcome).ToArray());
                }
                else
                {
                    filter.conditions.Add("a.\"Outcome\" = @outcome");
                    filter.Add("outcome", NpgsqlDbType.Integer, AdminConversationLogQueryRules.StoredOutcomeOf(status)!.Value);
                }
            }
            return filter;
        }

        public object[] CreateParameters(bool includeAnswerPreview)
        {
            var created = parameters.Select(factory => (object)factory()).ToList();
            if (includeAnswerPreview)
                created.Add(new NpgsqlParameter("answerPreviewLength", NpgsqlDbType.Integer)
                {
                    Value = AdminConversationLogQueryRules.AnswerPreviewLength
                });
            return [.. created];
        }

        private void Add(string name, NpgsqlDbType type, object value) =>
            parameters.Add(() => new NpgsqlParameter(name, type) { Value = value });
    }

    private sealed class TurnRow
    {
        public Guid TurnId { get; set; }
        public Guid ConversationId { get; set; }
        public string? ConversationTitle { get; set; }
        public Guid UserId { get; set; }
        public string QuestionPreview { get; set; } = string.Empty;
        public DateTimeOffset QuestionCreatedAt { get; set; }
        public Guid? AnswerId { get; set; }
        public string? AnswerPreview { get; set; }
        public DateTimeOffset? AnswerCreatedAt { get; set; }
        public int? Outcome { get; set; }
    }

    private sealed class AnswerRow
    {
        public Guid MessageId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string? PayloadJson { get; set; }
    }
}
