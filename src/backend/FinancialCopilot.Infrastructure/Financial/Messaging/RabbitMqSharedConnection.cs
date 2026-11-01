using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace FinancialCopilot.Infrastructure.Financial.Messaging;

/// <summary>
/// One long-lived connection per process and role, opened on first use. Concurrent callers share a
/// single connection attempt, failed attempts are paced, and an established connection is left to
/// the client's automatic recovery until the grace period shows recovery is not happening.
/// </summary>
internal sealed class RabbitMqSharedConnection(
    IConnectionFactory factory,
    string clientProvidedName,
    RabbitMqConnectionPolicy policy,
    TimeProvider timeProvider,
    ILogger logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _healthLock = new();
    private IConnection? _connection;
    private DateTimeOffset? _unhealthySince;
    private DateTimeOffset _retryNotBefore = DateTimeOffset.MinValue;
    private Exception? _lastFailure;
    private int _consecutiveFailures;
    private bool _disposed;

    public string Name => clientProvidedName;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _connection) is { IsOpen: true } current)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_connection is { IsOpen: true } open)
            {
                return open;
            }

            var now = timeProvider.GetUtcNow();
            if (_connection is not null)
            {
                DateTimeOffset unhealthySince;
                lock (_healthLock)
                {
                    unhealthySince = _unhealthySince ??= now;
                }

                if (now - unhealthySince < policy.RecoveryGracePeriod)
                {
                    // Replacing the connection while the client is still recovering it would leave two.
                    throw new RabbitMqUnavailableException(
                        $"RabbitMQ connection '{clientProvidedName}' is recovering.",
                        _lastFailure);
                }

                logger.LogWarning(
                    "RabbitMQ connection {ConnectionName} did not recover within {GracePeriod}; replacing it.",
                    clientProvidedName,
                    policy.RecoveryGracePeriod);
                var abandoned = _connection;
                _connection = null;
                await DisposeConnectionAsync(abandoned);
            }

            if (now < _retryNotBefore)
            {
                throw new RabbitMqUnavailableException(
                    $"RabbitMQ connection '{clientProvidedName}' is unavailable until {_retryNotBefore:O} after a failed attempt.",
                    _lastFailure);
            }

            return await OpenAsync(now, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IConnection> OpenAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await factory.CreateConnectionAsync(clientProvidedName, cancellationToken);
            connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
            connection.RecoverySucceededAsync += OnRecoverySucceededAsync;
            lock (_healthLock)
            {
                _unhealthySince = null;
            }

            _consecutiveFailures = 0;
            _lastFailure = null;
            Volatile.Write(ref _connection, connection);
            logger.LogInformation("Opened RabbitMQ connection {ConnectionName}.", clientProvidedName);
            return connection;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _consecutiveFailures++;
            _lastFailure = exception;
            var permanent = RabbitMqFailure.IsPermanent(exception);
            var delay = permanent ? policy.PermanentFailureCooldown : policy.RetryDelay(_consecutiveFailures);
            _retryNotBefore = now + delay;
            logger.Log(
                permanent ? LogLevel.Error : LogLevel.Warning,
                exception,
                "Could not open RabbitMQ connection {ConnectionName} (consecutive failures {ConsecutiveFailures}, permanent {Permanent}); next attempt in {RetryDelay}.",
                clientProvidedName,
                _consecutiveFailures,
                permanent,
                delay);
            throw;
        }
    }

    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs args)
    {
        if (args.Initiator != ShutdownInitiator.Application)
        {
            lock (_healthLock)
            {
                _unhealthySince ??= timeProvider.GetUtcNow();
            }

            logger.LogWarning(
                "RabbitMQ connection {ConnectionName} was shut down by {Initiator} ({ReplyCode} {ReplyText}).",
                clientProvidedName,
                args.Initiator,
                args.ReplyCode,
                args.ReplyText);
        }

        return Task.CompletedTask;
    }

    private Task OnRecoverySucceededAsync(object sender, AsyncEventArgs args)
    {
        lock (_healthLock)
        {
            _unhealthySince = null;
        }

        logger.LogInformation("RabbitMQ connection {ConnectionName} recovered.", clientProvidedName);
        return Task.CompletedTask;
    }

    private async Task DisposeConnectionAsync(IConnection connection)
    {
        connection.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
        connection.RecoverySucceededAsync -= OnRecoverySucceededAsync;
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Closing RabbitMQ connection {ConnectionName} failed.", clientProvidedName);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_connection is { } connection)
            {
                _connection = null;
                await DisposeConnectionAsync(connection);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
