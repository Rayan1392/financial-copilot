using FinancialCopilot.Application.AI.Orchestration;

namespace FinancialCopilot.Application.Administration;

/// <summary>Feature 164: the single owner of the conversation-log filter limits, search normalization and status rules.</summary>
public static class AdminConversationLogQueryRules
{
    public const int DefaultPageSize = 20;
    public const int MaximumTextLength = 200;
    public const int MaximumUserSearchLength = 200;
    public const int MaximumMatchedUsers = 200;
    public const int QuestionPreviewLength = 240;
    public const int AnswerPreviewLength = 320;
    public const char LikeEscape = '\\';

    public static IReadOnlyList<int> AllowedPageSizes { get; } = [20, 50, 100];

    /// <summary>Returns field → message pairs for every invalid value; empty when the query is valid.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Validate(AdminConversationLogQuery query)
    {
        var errors = new List<KeyValuePair<string, string>>();
        if (query.Page < 1 || query.Page > int.MaxValue / AllowedPageSizes[^1])
            errors.Add(new("page", "Page must be a positive value within the supported range."));
        if (!AllowedPageSizes.Contains(query.PageSize))
            errors.Add(new("pageSize", "Page size must be 20, 50 or 100."));
        if (query.Text is not null && Trimmed(query.Text) is { Length: 0 or > MaximumTextLength })
            errors.Add(new("q", $"Search text must be 1 to {MaximumTextLength} characters."));
        if (query.User is not null && Trimmed(query.User) is { Length: 0 or > MaximumUserSearchLength })
            errors.Add(new("user", $"User search must be 1 to {MaximumUserSearchLength} characters."));
        if (query.FromUtc is { } from && query.ToUtc is { } to && from >= to)
            errors.Add(new("fromUtc", "The start of the date range must be before its end."));
        return errors;
    }

    /// <summary>Parses a status query value (case-insensitive name); null when it is not a known status.</summary>
    public static AdminConversationLogStatus? ParseStatus(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !int.TryParse(value, out _) &&
        Enum.TryParse<AdminConversationLogStatus>(value.Trim(), ignoreCase: true, out var status) &&
        Enum.IsDefined(status)
            ? status
            : null;

    /// <summary>
    /// Persian and Arabic forms of «ی» and «ک» match each other. The same mapping is applied to the stored question in
    /// SQL (<see cref="ArabicLetters"/> → <see cref="PersianLetters"/>).
    /// </summary>
    public static string NormalizeSearchText(string value) =>
        Trimmed(value).Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک');

    public const string ArabicLetters = "يىك";
    public const string PersianLetters = "ییک";

    /// <summary>A case-insensitive substring LIKE pattern with <c>%</c>, <c>_</c> and the escape character literal.</summary>
    public static string ContainsPattern(string value)
    {
        var normalized = NormalizeSearchText(value)
            .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}")
            .Replace("%", $"{LikeEscape}%")
            .Replace("_", $"{LikeEscape}_");
        return $"%{normalized}%";
    }

    /// <summary>
    /// Status of a stored answer. A missing payload or outcome means the answer was written before outcomes existed
    /// and counts as answered.
    /// </summary>
    public static AdminConversationLogStatus StatusOf(bool hasAnswer, int? outcome)
    {
        if (!hasAnswer) return AdminConversationLogStatus.NoResponse;
        return outcome is { } value && Enum.IsDefined(typeof(DialogueOutcome), value)
            ? FromOutcome((DialogueOutcome)value)
            : AdminConversationLogStatus.Answered;
    }

    public static AdminConversationLogStatus FromOutcome(DialogueOutcome outcome) => outcome switch
    {
        DialogueOutcome.Answered => AdminConversationLogStatus.Answered,
        DialogueOutcome.PartialAnswer => AdminConversationLogStatus.PartialAnswer,
        DialogueOutcome.ClarificationNeeded => AdminConversationLogStatus.ClarificationNeeded,
        DialogueOutcome.DisambiguationNeeded => AdminConversationLogStatus.DisambiguationNeeded,
        DialogueOutcome.NoData => AdminConversationLogStatus.NoData,
        DialogueOutcome.Unsupported => AdminConversationLogStatus.Unsupported,
        DialogueOutcome.TemporarilyUnavailable => AdminConversationLogStatus.TemporarilyUnavailable,
        DialogueOutcome.Failed => AdminConversationLogStatus.Failed,
        _ => AdminConversationLogStatus.Answered
    };

    /// <summary>The stored outcome value a status filter matches; null for <see cref="AdminConversationLogStatus.NoResponse"/>.</summary>
    public static int? StoredOutcomeOf(AdminConversationLogStatus status) => status switch
    {
        AdminConversationLogStatus.Answered => (int)DialogueOutcome.Answered,
        AdminConversationLogStatus.PartialAnswer => (int)DialogueOutcome.PartialAnswer,
        AdminConversationLogStatus.ClarificationNeeded => (int)DialogueOutcome.ClarificationNeeded,
        AdminConversationLogStatus.DisambiguationNeeded => (int)DialogueOutcome.DisambiguationNeeded,
        AdminConversationLogStatus.NoData => (int)DialogueOutcome.NoData,
        AdminConversationLogStatus.Unsupported => (int)DialogueOutcome.Unsupported,
        AdminConversationLogStatus.TemporarilyUnavailable => (int)DialogueOutcome.TemporarilyUnavailable,
        AdminConversationLogStatus.Failed => (int)DialogueOutcome.Failed,
        _ => null
    };

    public static int TotalPages(int totalCount, int pageSize) =>
        totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

    private static string Trimmed(string value) => value.Trim();
}
