using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;

namespace FinancialCopilot.Infrastructure.Administration;

/// <summary>
/// Records that an administrator read the full content of a conversation turn (Features 149 and 164). The record
/// carries identifiers only, never question, answer or payload text. It is saved before content is read; a failed save
/// propagates so the caller returns no content (fail closed).
/// </summary>
internal static class AdminContentReadAudit
{
    internal const string ChatHistoryTurnTargetType = "chat_history_turn";

    internal static async Task RecordTurnReadAsync(
        AuthDbContext auth,
        TimeProvider timeProvider,
        Guid tenantId,
        Guid administratorId,
        string administratorType,
        string correlationId,
        string actionCode,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        auth.SecurityAdminAudits.Add(new SecurityAdminAuditRow
        {
            Id = Guid.NewGuid(),
            OccurredAt = timeProvider.GetUtcNow(),
            TenantId = tenantId,
            ActorId = administratorId,
            ActorType = administratorType,
            PermissionCode = FinancialCopilotPermissions.AdminUsersRead,
            ActionCode = actionCode,
            TargetType = ChatHistoryTurnTargetType,
            TargetId = turnId.ToString("D"),
            CorrelationId = correlationId
        });
        await auth.SaveChangesAsync(cancellationToken);
    }
}
