using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FinancialCopilot.Infrastructure.Authentication;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Authentication:Smtp";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    public string Security { get; init; } = "StartTls";
    public string UserName { get; init; } = "";
    public string Password { get; init; } = "";
    public string SenderAddress { get; init; } = "";
    public string SenderName { get; init; } = "Sapio AI";
    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// Settings are only enforced while authentication email is enabled. A configured user name
    /// without its password (for example, a missing provider app password) fails startup.
    /// </summary>
    public bool IsValid(bool emailEnabled) =>
        !emailEnabled ||
        !string.IsNullOrWhiteSpace(Host) &&
        Port is > 0 and <= 65535 &&
        TimeoutSeconds is >= 1 and <= 60 &&
        TryResolveSocketOptions(Security, out _) &&
        System.Net.Mail.MailAddress.TryCreate(SenderAddress, out _) &&
        string.IsNullOrWhiteSpace(UserName) == string.IsNullOrEmpty(Password);

    // Only encrypted modes are accepted; StartTls fails rather than continuing in plaintext.
    internal static bool TryResolveSocketOptions(string security, out SecureSocketOptions socketOptions)
    {
        socketOptions = security switch
        {
            "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.None
        };
        return socketOptions != SecureSocketOptions.None;
    }
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptions<SmtpEmailOptions> _options;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly Func<SmtpEmailOptions, ISmtpTransport> _transportFactory;

    public SmtpEmailSender(IOptions<SmtpEmailOptions> options, ILogger<SmtpEmailSender> logger)
        : this(options, logger, settings => new MailKitSmtpTransport(checked(settings.TimeoutSeconds * 1000)))
    {
    }

    internal SmtpEmailSender(
        IOptions<SmtpEmailOptions> options,
        ILogger<SmtpEmailSender> logger,
        Func<SmtpEmailOptions, ISmtpTransport> transportFactory)
    {
        _options = options;
        _logger = logger;
        _transportFactory = transportFactory;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(settings.SenderName, settings.SenderAddress));
        email.To.Add(MailboxAddress.Parse(message.To));
        email.Subject = message.Subject;
        email.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        }.ToMessageBody();

        using var transport = _transportFactory(settings);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var stage = "Connect";
        try
        {
            if (!SmtpEmailOptions.TryResolveSocketOptions(settings.Security, out var socketSecurity))
            {
                throw new InvalidOperationException("Unsupported SMTP security mode.");
            }
            await transport.ConnectAsync(settings.Host, settings.Port, socketSecurity, timeout.Token);
            if (!string.IsNullOrWhiteSpace(settings.UserName))
            {
                stage = "Authenticate";
                await transport.AuthenticateAsync(settings.UserName, settings.Password, timeout.Token);
            }
            stage = "Send";
            await transport.SendAsync(email, timeout.Token);
            stage = "Disconnect";
            await transport.DisconnectAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Deliberately omit address, message body, server response, and credentials.
            _logger.LogWarning(
                "Authentication email delivery failed. Stage: {SmtpStage}; FailureType: {FailureType}",
                stage,
                exception.GetType().Name);
            throw new EmailDeliveryException("Authentication email delivery failed.");
        }
    }
}

/// <summary>SMTP session operations used by <see cref="SmtpEmailSender"/>; the seam keeps tests off the network.</summary>
internal interface ISmtpTransport : IDisposable
{
    Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken);
    Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

// MailKit validates the server certificate by default; no custom callback may weaken it.
internal sealed class MailKitSmtpTransport(int timeoutMilliseconds) : ISmtpTransport
{
    private readonly SmtpClient _client = new() { Timeout = timeoutMilliseconds };

    public Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken) =>
        _client.ConnectAsync(host, port, security, cancellationToken);

    public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
        _client.AuthenticateAsync(userName, password, cancellationToken);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken) =>
        _client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _client.DisconnectAsync(true, cancellationToken);

    public void Dispose() => _client.Dispose();
}

public sealed class EmailDeliveryException(string message) : Exception(message);
