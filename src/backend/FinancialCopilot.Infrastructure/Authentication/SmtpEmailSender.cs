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
    public string SenderName { get; init; } = "Financial Copilot";
    public int TimeoutSeconds { get; init; } = 15;
}

public sealed class SmtpEmailSender(
    IOptions<SmtpEmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(settings.SenderName, settings.SenderAddress));
        email.To.Add(MailboxAddress.Parse(message.To));
        email.Subject = message.Subject;
        email.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        }.ToMessageBody();

        using var client = new SmtpClient { Timeout = checked(settings.TimeoutSeconds * 1000) };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            var socketSecurity = settings.Security switch
            {
                "StartTls" => SecureSocketOptions.StartTls,
                "SslOnConnect" => SecureSocketOptions.SslOnConnect,
                _ => throw new InvalidOperationException("Unsupported SMTP security mode.")
            };
            await client.ConnectAsync(settings.Host, settings.Port, socketSecurity, timeout.Token);
            if (!string.IsNullOrWhiteSpace(settings.UserName))
            {
                await client.AuthenticateAsync(settings.UserName, settings.Password, timeout.Token);
            }
            await client.SendAsync(email, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Deliberately omit address, message body, server response, and credentials.
            logger.LogWarning("Authentication email delivery failed. FailureType: {FailureType}", exception.GetType().Name);
            throw new EmailDeliveryException("Authentication email delivery failed.");
        }
    }
}

public sealed class EmailDeliveryException(string message) : Exception(message);
