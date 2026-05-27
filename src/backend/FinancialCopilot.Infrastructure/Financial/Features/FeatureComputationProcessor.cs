using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Domain.Financial.Features;
using FinancialCopilot.Domain.Financial.Periods;

namespace FinancialCopilot.Infrastructure.Financial.Features;

public sealed class FeatureRecalculationScheduler(
    IFeatureComputationJobRepository jobRepository,
    IFeatureRecalculationPublisher publisher) : IFeatureRecalculationScheduler
{
    public async Task<FeatureComputationJob> ScheduleAsync(
        FeatureRecalculationRequested request,
        CancellationToken cancellationToken)
    {
        var existing = await jobRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var job = new FeatureComputationJob(
            request.JobId,
            request.FeatureCode,
            request.FeatureVersion,
            request.SymbolId,
            request.Period.ToFiscalPeriod(),
            request.IdempotencyKey,
            FeatureComputationStatus.Requested,
            request.RequestedAt);
        await jobRepository.StoreAsync(job, cancellationToken);
        await publisher.PublishRequestedAsync(request, cancellationToken);
        return job;
    }
}

public sealed class FeatureComputationProcessor(
    IFeatureComputationJobRepository jobRepository,
    IFeatureDefinitionRegistry definitionRegistry,
    IFeatureInputReader inputReader,
    IDerivedFeatureCalculationService calculationService,
    IFeatureRecalculationPublisher publisher,
    TimeProvider timeProvider) : IFeatureComputationProcessor
{
    public async Task ProcessAsync(
        FeatureRecalculationRequested request,
        CancellationToken cancellationToken)
    {
        var existing = await jobRepository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken);
        if (existing?.Status == FeatureComputationStatus.Completed)
        {
            return;
        }

        var startedAt = timeProvider.GetUtcNow();
        var job = new FeatureComputationJob(
            existing?.Id ?? request.JobId,
            request.FeatureCode,
            request.FeatureVersion,
            request.SymbolId,
            request.Period.ToFiscalPeriod(),
            request.IdempotencyKey,
            FeatureComputationStatus.Running,
            existing?.RequestedAt ?? request.RequestedAt,
            existing?.StartedAt ?? startedAt);
        await jobRepository.StoreAsync(job, cancellationToken);

        try
        {
            var symbolId = request.SymbolId ??
                throw new InvalidOperationException("Feature calculation requests must identify a symbol.");
            var definition = await definitionRegistry.ResolveAsync(
                request.FeatureCode,
                request.FeatureVersion,
                cancellationToken);
            var period = request.Period.ToFiscalPeriod();
            var inputs = await inputReader.LoadAsync(definition, symbolId, period, cancellationToken);
            var snapshot = await calculationService.CalculateAsync(
                new CalculateDerivedFeatureCommand(
                    symbolId,
                    request.FeatureCode,
                    request.FeatureVersion,
                    period,
                    inputs),
                cancellationToken);
            var completedAt = timeProvider.GetUtcNow();
            await jobRepository.StoreAsync(job with
            {
                Status = FeatureComputationStatus.Completed,
                CompletedAt = completedAt
            }, cancellationToken);
            await publisher.PublishCompletedAsync(
                new FeatureRecalculationCompleted(job.Id, job.IdempotencyKey, snapshot.Id, completedAt),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failedAt = timeProvider.GetUtcNow();
            var message = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
            await jobRepository.StoreAsync(job with
            {
                Status = FeatureComputationStatus.Failed,
                CompletedAt = failedAt,
                ErrorMessage = message
            }, cancellationToken);
            await publisher.PublishFailedAsync(
                new FeatureRecalculationFailed(job.Id, job.IdempotencyKey, message, failedAt),
                cancellationToken);
            throw;
        }
    }
}

// Concrete input sourcing is added alongside a promoted feature formula.
public sealed class NoOpFeatureInputReader : IFeatureInputReader
{
    public Task<IReadOnlyCollection<FeatureInputObservation>> LoadAsync(
        FeatureDefinition definition,
        Guid symbolId,
        FiscalPeriod period,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<FeatureInputObservation>>([]);
}
