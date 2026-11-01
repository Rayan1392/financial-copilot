using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace FinancialCopilot.Infrastructure.Financial.Messaging;

/// <summary>
/// Limits and pacing for the long-lived RabbitMQ connections owned by one messaging adapter.
/// </summary>
internal sealed record RabbitMqConnectionPolicy(
    TimeSpan InitialRetryDelay,
    TimeSpan MaxRetryDelay,
    TimeSpan PermanentFailureCooldown,
    TimeSpan RecoveryGracePeriod,
    TimeSpan PublishTimeout,
    int PublisherChannelLimit)
{
    public static readonly TimeSpan NetworkRecoveryInterval = TimeSpan.FromSeconds(5);

    public static RabbitMqConnectionPolicy Create(int publisherChannelLimit, int publishTimeoutSeconds) => new(
        InitialRetryDelay: TimeSpan.FromSeconds(1),
        MaxRetryDelay: TimeSpan.FromSeconds(60),
        PermanentFailureCooldown: TimeSpan.FromMinutes(5),
        // Gives the client's own automatic recovery several attempts before the connection is replaced.
        RecoveryGracePeriod: NetworkRecoveryInterval * 6,
        PublishTimeout: TimeSpan.FromSeconds(Math.Max(1, publishTimeoutSeconds)),
        PublisherChannelLimit: Math.Max(1, publisherChannelLimit));

    /// <summary>
    /// Capped exponential backoff with equal jitter, so retries never collapse to zero delay and
    /// competing consumers do not reconnect in lockstep.
    /// </summary>
    public TimeSpan RetryDelay(int consecutiveFailures)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 16);
        var ceiling = Math.Min(
            MaxRetryDelay.TotalMilliseconds,
            InitialRetryDelay.TotalMilliseconds * Math.Pow(2, exponent));
        return TimeSpan.FromMilliseconds(ceiling / 2 + Random.Shared.NextDouble() * ceiling / 2);
    }

    public static ConnectionFactory CreateConnectionFactory(
        string hostName,
        int port,
        string virtualHost,
        string userName,
        string password) => new()
    {
        HostName = hostName,
        Port = port,
        VirtualHost = virtualHost,
        UserName = userName,
        Password = password,
        // The client recovers the TCP/AMQP connection; the adapters own channels, queue declarations
        // and consumer registration, so topology recovery is off to avoid duplicate consumers.
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = false,
        NetworkRecoveryInterval = NetworkRecoveryInterval
    };
}

internal static class RabbitMqFailure
{
    /// <summary>
    /// True when retrying with unchanged configuration cannot succeed: rejected credentials, a refused
    /// virtual host, or a queue already declared with different arguments.
    /// </summary>
    public static bool IsPermanent(Exception exception) => exception switch
    {
        AuthenticationFailureException => true,
        OperationInterruptedException { ShutdownReason.ReplyCode: Constants.AccessRefused or Constants.NotAllowed or Constants.PreconditionFailed } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsPermanent),
        { InnerException: { } inner } => IsPermanent(inner),
        _ => false
    };
}

/// <summary>
/// Raised without contacting the broker while a connection is recovering or a failed connection
/// attempt is still cooling down.
/// </summary>
internal sealed class RabbitMqUnavailableException(string message, Exception? innerException)
    : Exception(message, innerException);
