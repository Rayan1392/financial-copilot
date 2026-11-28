using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;

namespace FinancialCopilot.UnitTests;

/// <summary>Records the gauge refreshes a Telegram channel handler asks for.</summary>
public sealed class RecordingCodalGaugeRefreshTrigger : ICodalAnnouncementGaugeRefreshTrigger
{
    public List<CodalAnnouncementGaugeRefreshRequest> Requests { get; } = [];

    public Task RequestAsync(CodalAnnouncementGaugeRefreshRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.CompletedTask;
    }
}
