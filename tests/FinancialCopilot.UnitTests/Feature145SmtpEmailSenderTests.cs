using FinancialCopilot.Infrastructure.Authentication;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FinancialCopilot.UnitTests;

public sealed class Feature145SmtpEmailSenderTests
{
    private const string GmailMailbox = "kamran.abdi@gmail.com";
    private const string FakePassword = "smtp-test-password-sentinel";

    [Fact]
    public void Shipped_appsettings_bind_gmail_sender_identity_without_a_password()
    {
        var options = BindShippedOptions();

        Assert.Equal("smtp.gmail.com", options.Host);
        Assert.Equal(587, options.Port);
        Assert.Equal("StartTls", options.Security);
        Assert.Equal(GmailMailbox, options.UserName);
        Assert.Equal(GmailMailbox, options.SenderAddress);
        Assert.Equal("Sapio AI", options.SenderName);
        Assert.Equal("", options.Password);
    }

    [Fact]
    public void Environment_values_override_shipped_defaults()
    {
        var options = BindShippedOptions(new Dictionary<string, string?>
        {
            ["Authentication:Smtp:Host"] = "smtp.zoho.eu",
            ["Authentication:Smtp:Port"] = "465",
            ["Authentication:Smtp:Security"] = "SslOnConnect",
            ["Authentication:Smtp:Password"] = FakePassword
        });

        Assert.Equal("smtp.zoho.eu", options.Host);
        Assert.Equal(465, options.Port);
        Assert.Equal("SslOnConnect", options.Security);
        Assert.Equal(FakePassword, options.Password);
        Assert.True(options.IsValid(emailEnabled: true));
    }

    [Fact]
    public void Missing_password_fails_validation_only_when_email_is_enabled()
    {
        var options = BindShippedOptions();

        Assert.False(options.IsValid(emailEnabled: true));
        Assert.True(options.IsValid(emailEnabled: false));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Auto")]
    [InlineData("StartTlsWhenAvailable")]
    [InlineData("")]
    public void Plaintext_or_opportunistic_security_modes_are_rejected(string security)
    {
        var options = ZohoOptions(security: security);

        Assert.False(options.IsValid(emailEnabled: true));
        Assert.False(SmtpEmailOptions.TryResolveSocketOptions(security, out _));
    }

    [Fact]
    public async Task Sends_through_starttls_authenticated_as_the_sender_mailbox()
    {
        var transport = new RecordingSmtpTransport();
        var sender = CreateSender(ZohoOptions(), transport, new CapturingLogger());
        var message = new EmailMessage("user@example.test", "تایید نشانی ایمیل", "<p>html</p>", "plain https://sapioai.ir/#token=abc");

        await sender.SendAsync(message, CancellationToken.None);

        Assert.Equal(["Connect", "Authenticate", "Send", "Disconnect"], transport.Calls);
        Assert.Equal(("smtppro.zoho.com", 587, SecureSocketOptions.StartTls), transport.Connection);
        Assert.Equal((GmailMailbox, FakePassword), transport.Credentials);
        var sent = Assert.IsType<MimeMessage>(transport.Sent);
        var from = Assert.Single(sent.From.Mailboxes);
        Assert.Equal(GmailMailbox, from.Address);
        Assert.Equal("Sapio AI", from.Name);
        Assert.Equal("user@example.test", Assert.Single(sent.To.Mailboxes).Address);
        Assert.Equal(message.Subject, sent.Subject);
        Assert.Equal(message.HtmlBody, sent.HtmlBody);
        Assert.Equal(message.TextBody, sent.TextBody);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task Port_465_uses_implicit_tls()
    {
        var transport = new RecordingSmtpTransport();
        var sender = CreateSender(ZohoOptions(port: 465, security: "SslOnConnect"), transport, new CapturingLogger());

        await sender.SendAsync(new EmailMessage("user@example.test", "s", "h", "t"), CancellationToken.None);

        Assert.Equal(("smtppro.zoho.com", 465, SecureSocketOptions.SslOnConnect), transport.Connection);
    }

    [Fact]
    public async Task Unsupported_security_mode_never_opens_a_connection()
    {
        var transport = new RecordingSmtpTransport();
        var sender = CreateSender(ZohoOptions(security: "None"), transport, new CapturingLogger());

        await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            sender.SendAsync(new EmailMessage("user@example.test", "s", "h", "t"), CancellationToken.None));

        Assert.Empty(transport.Calls);
    }

    [Fact]
    public async Task Authentication_failure_is_reported_by_stage_without_leaking_credentials_or_content()
    {
        var transport = new RecordingSmtpTransport
        {
            FailOn = "Authenticate",
            Failure = new AuthenticationException($"535 rejected {GmailMailbox} {FakePassword}")
        };
        var logger = new CapturingLogger();
        var sender = CreateSender(ZohoOptions(), transport, logger);
        var message = new EmailMessage("user@example.test", "subject", "<p>html</p>", "plain #token=secret-token");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(() => sender.SendAsync(message, CancellationToken.None));

        Assert.Equal(["Connect", "Authenticate"], transport.Calls);
        Assert.True(transport.Disposed);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("Stage: Authenticate", entry);
        Assert.Contains(nameof(AuthenticationException), entry);
        foreach (var sensitive in new[] { FakePassword, GmailMailbox, "user@example.test", "secret-token", "535" })
        {
            Assert.DoesNotContain(sensitive, entry);
            Assert.DoesNotContain(sensitive, exception.Message);
        }
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_a_delivery_failure_log()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var transport = new RecordingSmtpTransport { FailOn = "Connect", Failure = new OperationCanceledException() };
        var logger = new CapturingLogger();
        var sender = CreateSender(ZohoOptions(), transport, logger);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sender.SendAsync(new EmailMessage("user@example.test", "s", "h", "t"), cancellation.Token));

        Assert.Empty(logger.Entries);
    }

    private static SmtpEmailOptions BindShippedOptions(IDictionary<string, string?>? overrides = null)
    {
        var appSettings = Path.Combine(FindRepositoryRoot(), "src", "backend", "FinancialCopilot.API", "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettings, optional: false)
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
            .Build();
        return configuration.GetSection(SmtpEmailOptions.SectionName).Get<SmtpEmailOptions>()
            ?? throw new InvalidOperationException("Authentication:Smtp is missing from appsettings.json.");
    }

    private static SmtpEmailOptions ZohoOptions(int port = 587, string security = "StartTls") => new()
    {
        Host = "smtppro.zoho.com",
        Port = port,
        Security = security,
        UserName = GmailMailbox,
        Password = FakePassword,
        SenderAddress = GmailMailbox,
        SenderName = "Sapio AI",
        TimeoutSeconds = 15
    };

    private static SmtpEmailSender CreateSender(SmtpEmailOptions options, RecordingSmtpTransport transport, CapturingLogger logger) =>
        new(Options.Create(options), logger, _ => transport);

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "README.md"))) return current.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class RecordingSmtpTransport : ISmtpTransport
    {
        public List<string> Calls { get; } = [];
        public (string Host, int Port, SecureSocketOptions Security)? Connection { get; private set; }
        public (string UserName, string Password)? Credentials { get; private set; }
        public MimeMessage? Sent { get; private set; }
        public bool Disposed { get; private set; }
        public string? FailOn { get; init; }
        public Exception? Failure { get; init; }

        public Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken)
        {
            Record("Connect");
            Connection = (host, port, security);
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            Record("Authenticate");
            Credentials = (userName, password);
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            Record("Send");
            Sent = message;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Record("Disconnect");
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        private void Record(string call)
        {
            Calls.Add(call);
            if (call == FailOn) throw Failure!;
        }
    }

    private sealed class CapturingLogger : ILogger<SmtpEmailSender>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}
