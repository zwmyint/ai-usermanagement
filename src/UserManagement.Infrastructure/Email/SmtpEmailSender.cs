using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using UserManagement.Application.Common.Settings;
using UserManagement.Application.Interfaces;

namespace UserManagement.Infrastructure.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        if (_settings.PickupDirectoryOnly)
        {
            await WriteToPickupDirectoryAsync(message, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            _logger.LogWarning("SMTP host is not configured; email to {Recipient} was not sent.", toEmail);
            return;
        }

        using var client = new SmtpClient();
        var socketOptions = _settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;

        await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, ct);

        if (!string.IsNullOrWhiteSpace(_settings.UserName))
            await client.AuthenticateAsync(_settings.UserName, _settings.Password ?? string.Empty, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Sent email '{Subject}' to {Recipient}.", subject, toEmail);
    }

    /// <summary>Development fallback: persists the message to disk instead of contacting an SMTP server.</summary>
    private async Task WriteToPickupDirectoryAsync(MimeMessage message, CancellationToken ct)
    {
        var directory = string.IsNullOrWhiteSpace(_settings.PickupDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "mail-pickup")
            : _settings.PickupDirectory;

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");

        await using var stream = File.Create(path);
        await message.WriteToAsync(stream, ct);

        _logger.LogInformation("Email written to pickup directory: {Path}", path);
    }
}
