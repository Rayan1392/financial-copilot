using System.Linq.Expressions;
using System.Text.Json;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Administration;

public sealed class EfCoreAdminChatHistoryQueries(
    ConversationDbContext conversations,
    AuthDbContext auth,
    TimeProvider timeProvider) : IAdminChatHistoryQueries
{
    private const int PageSize = 20;
    private const int QuestionSummaryLength = 240;
    private const int AnswerSummaryLength = 320;
    private const string UserRole = nameof(FinancialCopilot.Application.Conversations.MessageRole.User);
    private const string AssistantRole = nameof(FinancialCopilot.Application.Conversations.MessageRole.Assistant);
    private static readonly HashSet<string> PublicResponsePayloadFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextAnswer", "ScannerTable", "SymbolLookupTable", "ExplainableAnswer", "ConfidenceScore",
        "ComprehensiveAnalysisResult", "FinancialStatementAnalysisResult", "FinancialStatementTableResult",
        "ProductRevenueMixResult", "MonthlyActivityTrendResult", "MonthlySalesQualityRankingResult",
        "DisclosureListingResult", "PsVisualizationResult", "MonthlyProductComparisonResult",
        "FinancialStatementValueSearchResult", "MonthlyProductTrendResult"
    };

    public async Task<AdminChatHistoryPage> GetPageAsync(
        Guid tenantId,
        Guid userId,
        int page,
        CancellationToken cancellationToken)
    {
        if (page < 1 || page > int.MaxValue / PageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        var ownedConversations = conversations.Conversations.AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId && conversation.ActorId == userId);
        var questions = from message in conversations.Messages.AsNoTracking()
                        join conversation in ownedConversations on message.ConversationId equals conversation.Id
                        where message.Role == UserRole
                        select new
                        {
                            message.Id,
                            message.ConversationId,
                            conversation.Title,
                            message.Content,
                            message.CreatedAt
                        };

        var totalCount = await questions.CountAsync(cancellationToken);
        var anchors = await questions
            .OrderByDescending(question => question.CreatedAt)
            .ThenByDescending(question => question.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(question => new QuestionAnchor(
                question.Id,
                question.ConversationId,
                question.Title,
                question.Content.Length > QuestionSummaryLength
                    ? question.Content.Substring(0, QuestionSummaryLength)
                    : question.Content,
                question.CreatedAt))
            .ToArrayAsync(cancellationToken);

        if (anchors.Length == 0)
        {
            return new AdminChatHistoryPage(Array.Empty<AdminChatHistoryItem>(), page, PageSize, totalCount,
                (int)Math.Ceiling(totalCount / (double)PageSize));
        }

        var answerPredicate = ForTurns(anchors);
        var answerRows = await conversations.Messages.AsNoTracking()
            .Where(answerPredicate)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => new AnswerPreview(
                message.Id,
                message.ConversationId,
                message.CreatedAt,
                message.Content.Length > AnswerSummaryLength
                    ? message.Content.Substring(0, AnswerSummaryLength)
                    : message.Content))
            .ToArrayAsync(cancellationToken);

        var answersByTurn = GroupAnswers(anchors, answerRows);
        var items = anchors.Select(anchor =>
        {
            var answers = answersByTurn[anchor.Id];
            var answer = answers.FirstOrDefault();
            return new AdminChatHistoryItem(
                anchor.Id,
                anchor.ConversationId,
                NormalizeTitle(anchor.Title),
                anchor.Content,
                answer?.Content,
                anchor.CreatedAt,
                answer?.CreatedAt,
                answers.Count != 0);
        }).ToArray();

        return new AdminChatHistoryPage(items, page, PageSize, totalCount,
            (int)Math.Ceiling(totalCount / (double)PageSize));
    }

    public async Task<AdminChatHistoryTurn?> GetTurnAsync(
        Guid tenantId,
        Guid userId,
        Guid administratorId,
        string administratorType,
        string correlationId,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        var anchor = await (from message in conversations.Messages.AsNoTracking()
                            join conversation in conversations.Conversations.AsNoTracking()
                                on message.ConversationId equals conversation.Id
                            where message.Id == turnId && message.Role == UserRole &&
                                  conversation.TenantId == tenantId && conversation.ActorId == userId
                            select new QuestionAnchor(
                                message.Id,
                                message.ConversationId,
                                conversation.Title,
                                message.Content,
                                message.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (anchor is null)
        {
            return null;
        }

        auth.SecurityAdminAudits.Add(new SecurityAdminAuditRow
        {
            Id = Guid.NewGuid(),
            OccurredAt = timeProvider.GetUtcNow(),
            TenantId = tenantId,
            ActorId = administratorId,
            ActorType = administratorType,
            PermissionCode = FinancialCopilot.Application.Authentication.FinancialCopilotPermissions.AdminUsersRead,
            ActionCode = "admin.customer-chat-history.turn-read",
            TargetType = "chat_history_turn",
            TargetId = turnId.ToString("D"),
            CorrelationId = correlationId
        });
        await auth.SaveChangesAsync(cancellationToken);

        var answerRows = await conversations.Messages.AsNoTracking()
            .Where(ForTurns([anchor]))
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Select(message => new AnswerFull(
                message.Id,
                message.Content,
                message.CreatedAt,
                message.AssistantPayloadJson))
            .ToArrayAsync(cancellationToken);

        var answers = answerRows.Select(row => new AdminChatHistoryAnswer(
            row.Id,
            row.Content,
            row.CreatedAt,
            ParsePayload(row.AssistantPayloadJson))).ToArray();

        return new AdminChatHistoryTurn(
            anchor.Id,
            anchor.ConversationId,
            NormalizeTitle(anchor.Title),
            anchor.Content,
            anchor.CreatedAt,
            answers);
    }

    private Expression<Func<MessageRow, bool>> ForTurns(IReadOnlyCollection<QuestionAnchor> anchors)
    {
        var predicate = False<MessageRow>();
        foreach (var anchor in anchors)
        {
            predicate = Or(predicate, ForTurn(anchor));
        }
        return predicate;

        Expression<Func<MessageRow, bool>> ForTurn(QuestionAnchor question) => answer =>
            answer.ConversationId == question.ConversationId &&
            answer.Role == AssistantRole &&
            (answer.CreatedAt > question.CreatedAt ||
             (answer.CreatedAt == question.CreatedAt && answer.Id.CompareTo(question.Id) > 0)) &&
            !conversations.Messages.AsNoTracking().Any(nextQuestion =>
                nextQuestion.ConversationId == question.ConversationId &&
                nextQuestion.Role == UserRole &&
                (nextQuestion.CreatedAt > question.CreatedAt ||
                 (nextQuestion.CreatedAt == question.CreatedAt && nextQuestion.Id.CompareTo(question.Id) > 0)) &&
                (nextQuestion.CreatedAt < answer.CreatedAt ||
                 (nextQuestion.CreatedAt == answer.CreatedAt && nextQuestion.Id.CompareTo(answer.Id) < 0)));
    }

    private static Expression<Func<T, bool>> False<T>() => _ => false;

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ParameterReplaceVisitor(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rightBody), parameter);
    }

    private static Dictionary<Guid, List<AnswerPreview>> GroupAnswers(
        IReadOnlyCollection<QuestionAnchor> anchors,
        IEnumerable<AnswerPreview> answerRows)
    {
        var result = anchors.ToDictionary(anchor => anchor.Id, _ => new List<AnswerPreview>());
        var orderedQuestions = anchors.GroupBy(anchor => anchor.ConversationId)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(anchor => anchor.CreatedAt).ThenBy(anchor => anchor.Id).ToArray());

        foreach (var answer in answerRows)
        {
            var questions = orderedQuestions[answer.ConversationId];
            for (var index = questions.Length - 1; index >= 0; index--)
            {
                var question = questions[index];
                if (answer.CreatedAt > question.CreatedAt ||
                    (answer.CreatedAt == question.CreatedAt && answer.Id.CompareTo(question.Id) > 0))
                {
                    result[question.Id].Add(answer);
                    break;
                }
            }
        }

        foreach (var answers in result.Values)
        {
            answers.Sort((left, right) =>
            {
                var timestamp = left.CreatedAt.CompareTo(right.CreatedAt);
                return timestamp != 0 ? timestamp : left.Id.CompareTo(right.Id);
            });
        }
        return result;
    }

    private static string? NormalizeTitle(string title) =>
        string.IsNullOrWhiteSpace(title) || string.Equals(title, "New conversation", StringComparison.Ordinal)
            ? null
            : title;

    private static JsonElement? ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            using var safePayload = new MemoryStream();
            using (var writer = new Utf8JsonWriter(safePayload))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!PublicResponsePayloadFields.Contains(property.Name)) continue;
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            using var safeDocument = JsonDocument.Parse(safePayload.ToArray());
            return safeDocument.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record QuestionAnchor(
        Guid Id,
        Guid ConversationId,
        string Title,
        string Content,
        DateTimeOffset CreatedAt);

    private sealed record AnswerPreview(Guid Id, Guid ConversationId, DateTimeOffset CreatedAt, string Content);
    private sealed record AnswerFull(Guid Id, string Content, DateTimeOffset CreatedAt, string? AssistantPayloadJson);

    private sealed class ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : node;
    }
}
